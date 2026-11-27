using System.Text;
using System.Text.Json;
using System.Xml;
using Microsoft.Build.Locator;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Models;
using RoslynMcpServer.Services.Workspace;

namespace RoslynMcpServer.Services.Editing;

/// <summary>
/// Renames an SDK-style project directory + .csproj and updates ProjectReference / .sln / .slnx graph entries.
/// Does not rename C# namespaces or types — use <c>rename_symbol</c> after reload.
/// </summary>
public static class ProjectRenameHelper
{
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        ".idea",
        "bin",
        "obj",
        "node_modules",
        "packages",
    };

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public sealed record TextEdit(string Path, string NewContent, string Description)
    {
        internal ProjectRenameFile? Source { get; init; }
    }

    public sealed record RenamePlan(
        string OldProjectPath,
        string NewProjectPath,
        string OldDirectory,
        string NewDirectory,
        string OldProjectFileName,
        string NewProjectFileName,
        bool UpdateAssemblyName,
        bool UpdateRootNamespace,
        string? NewCsprojContent,
        IReadOnlyList<TextEdit> TextEdits,
        IReadOnlyList<string> Warnings)
    {
        internal ProjectRenameFile? SourceProject { get; init; }
    }

    public static RenamePlan CreatePlan(string projectPath, string newProjectName, string? searchRoot = null)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            throw new ArgumentException("projectPath is empty.", nameof(projectPath));
        }

        if (string.IsNullOrWhiteSpace(newProjectName))
        {
            throw new ArgumentException("newProjectName is empty.", nameof(newProjectName));
        }

        var trimmedNewName = newProjectName.Trim();
        if (trimmedNewName is "." or ".."
            || trimmedNewName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || trimmedNewName.Contains('/')
            || trimmedNewName.Contains('\\')
            || trimmedNewName.IndexOfAny(['"', '\r', '\n']) >= 0
            || !IsLiteralProjectReference(trimmedNewName))
        {
            throw new InvalidOperationException(
                $"`newProjectName` must be a single path segment (project name), got `{trimmedNewName}`.");
        }

        var oldProjectPath = Path.GetFullPath(projectPath.Trim());
        if (!File.Exists(oldProjectPath))
        {
            throw new FileNotFoundException("Project file not found.", oldProjectPath);
        }

        if (!oldProjectPath.EndsWith(".csproj", PathComparison))
        {
            throw new InvalidOperationException("Only `.csproj` projects are supported.");
        }

        var project = ProjectRenameFile.Read(oldProjectPath);
        if (!IsSdkStyle(project))
        {
            throw new InvalidOperationException(
                "Only SDK-style projects (`<Project Sdk=\"...\">`) are supported. Old-style csproj is unsupported.");
        }

        var oldDirectory = Path.GetDirectoryName(oldProjectPath)
            ?? throw new InvalidOperationException("Cannot resolve project directory.");
        var oldProjectFileName = Path.GetFileName(oldProjectPath);
        var oldNameWithoutExt = Path.GetFileNameWithoutExtension(oldProjectFileName);
        var folderName = Path.GetFileName(oldDirectory);

        if (!string.Equals(folderName, oldNameWithoutExt, PathComparison))
        {
            throw new InvalidOperationException(
                $"Unsupported layout: project folder `{folderName}` does not match csproj name `{oldNameWithoutExt}`. " +
                "Rename only when the project lives in its own identically named folder.");
        }

        var parent = Path.GetDirectoryName(oldDirectory)
            ?? throw new InvalidOperationException("Cannot resolve parent directory.");
        var newDirectory = Path.Combine(parent, trimmedNewName);
        var newProjectFileName = trimmedNewName + ".csproj";
        var newProjectPath = Path.Combine(newDirectory, newProjectFileName);

        if (string.Equals(oldProjectPath, newProjectPath, PathComparison))
        {
            throw new InvalidOperationException("New project name equals the current name — nothing to rename.");
        }

        var move = new ProjectMove(oldProjectPath, newProjectPath);
        ValidateMove(move);

        if (!MSBuildLocator.IsRegistered)
        {
            MsBuildBootstrapper.Register();
        }

        var (newCsprojContent, updateAssemblyName, updateRootNamespace) = ProjectRenameProject.Rewrite(
            project, move, renameProperties: true);

        var root = ResolveSearchRoot(searchRoot, parent);
        var warnings = new List<string>();
        var textEdits = new List<TextEdit>();

        IEnumerable<string> FindOwners(string pattern) =>
            EnumerateFiles(oldDirectory, pattern).Concat(move.Contains(root) ? [] : EnumerateFiles(root, pattern, oldDirectory));

        string[] projectPatterns = ["*.csproj", "*.fsproj", "*.vbproj"];
        var hosts = projectPatterns.SelectMany(FindOwners)
            .Where(host => !string.Equals(host, oldProjectPath, PathComparison))
            .ToList();
        foreach (var host in hosts)
        {
            var edit = CreateProjectReferenceEdit(host, move);
            if (edit is not null)
            {
                textEdits.Add(edit);
            }
        }

        ProjectRenameImports.RejectImportedReferences(hosts, move, warnings);

        var solutionFiles = FindOwners("*.sln").Concat(FindOwners("*.slnx")).ToList();

        if (solutionFiles.Count == 0)
        {
            warnings.Add("No `.sln`/`.slnx` found under search root — project refs will still be updated.");
        }

        foreach (var solution in solutionFiles)
        {
            var edit = CreateSolutionEdit(solution, move);
            if (edit is not null)
            {
                textEdits.Add(edit);
            }
        }

        foreach (var filter in FindOwners("*.slnf"))
        {
            var edit = CreateSolutionFilterEdit(filter, move, warnings);
            if (edit is not null)
            {
                textEdits.Add(edit);
            }
        }

        return new RenamePlan(
            oldProjectPath,
            newProjectPath,
            oldDirectory,
            newDirectory,
            oldProjectFileName,
            newProjectFileName,
            updateAssemblyName,
            updateRootNamespace,
            newCsprojContent,
            textEdits,
            warnings)
        {
            SourceProject = project,
        };
    }

    public static string FormatPlan(RenamePlan plan, bool dryRun)
    {
        var sb = new StringBuilder();
        sb.AppendLine(dryRun ? "## rename_project preview (dryRun=true) — no changes written" : "## rename_project apply");
        sb.AppendLine($"- Directory: `{plan.OldDirectory}` → `{plan.NewDirectory}`");
        sb.AppendLine($"- Project file: `{plan.OldProjectFileName}` → `{plan.NewProjectFileName}`");
        sb.AppendLine($"- Update AssemblyName: {(plan.UpdateAssemblyName ? "yes (matched old project name)" : "no")}");
        sb.AppendLine($"- Update RootNamespace: {(plan.UpdateRootNamespace ? "yes (matched old project name)" : "no")}");
        if (plan.NewCsprojContent is not null)
        {
            sb.AppendLine($"- Renamed project updates: `{plan.NewProjectPath}`");
        }

        sb.AppendLine($"- Text edits: {plan.TextEdits.Count}");
        foreach (var edit in plan.TextEdits)
        {
            sb.AppendLine($"  - {edit.Description}: `{edit.Path}`");
        }

        foreach (var w in plan.Warnings)
        {
            sb.AppendLine($"- Warning: {w}");
        }

        sb.AppendLine();
        sb.AppendLine("After apply: `reset_workspace` → `load_workspace` → optional `rename_symbol` → `run_dotnet_build` / `run_dotnet_test`.");
        return sb.ToString().TrimEnd();
    }

    public static string Apply(RenamePlan plan)
    {
        return Apply(plan, inputSession: null, writeText: null).Message;
    }

    internal static (string Message, bool Success) Apply(
        RenamePlan plan,
        WorkspaceInputSession? inputSession,
        Action<string, string, Encoding>? writeText)
    {
        ValidateMove(new ProjectMove(plan.OldProjectPath, plan.NewProjectPath));
        plan.SourceProject?.VerifyCurrent(plan.OldProjectPath);
        foreach (var edit in plan.TextEdits)
        {
            if (edit.Source is { } source)
            {
                source.VerifyCurrent(source.Path);
            }
        }

        var completed = new List<string>();
        try
        {
            // Edits already target the paths that will exist after the move.
            Directory.Move(plan.OldDirectory, plan.NewDirectory);
            inputSession?.NoteDirectoryRename(plan.OldDirectory, plan.NewDirectory, affectsMembership: true);
            completed.Add($"Moved directory to `{plan.NewDirectory}`");

            var movedCsproj = Path.Combine(plan.NewDirectory, plan.OldProjectFileName);
            if (!string.Equals(plan.OldProjectFileName, plan.NewProjectFileName, PathComparison))
            {
                if (!File.Exists(movedCsproj))
                {
                    throw new FileNotFoundException("Moved project file not found after directory move.", movedCsproj);
                }

                File.Move(movedCsproj, plan.NewProjectPath);
                completed.Add($"Renamed project file to `{plan.NewProjectFileName}`");
            }

            if (plan.NewCsprojContent is not null)
            {
                WriteText(plan.NewProjectPath, plan.NewCsprojContent, plan.SourceProject, writeText);
                completed.Add("Wrote renamed csproj updates");
            }

            var failed = new List<string>();
            foreach (var edit in plan.TextEdits)
            {
                try
                {
                    WriteText(edit.Path, edit.NewContent, edit.Source, writeText);
                    completed.Add($"{edit.Description}: `{edit.Path}`");
                }
                catch (Exception ex)
                {
                    failed.Add($"`{edit.Path}`: {ex.Message}");
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine(FormatPlan(plan, dryRun: false));
            sb.AppendLine();
            sb.AppendLine("### Completed");
            foreach (var c in completed)
            {
                sb.AppendLine($"- {c}");
            }

            if (failed.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("### Partial failure — fix manually or restore from VCS");
                foreach (var f in failed)
                {
                    sb.AppendLine($"- {f}");
                }
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("Rename applied successfully.");
            }

            return (sb.ToString().TrimEnd(), failed.Count == 0);
        }
        catch (Exception ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine("### Partial failure — rename aborted mid-apply");
            sb.AppendLine(ex.Message);
            sb.AppendLine();
            sb.AppendLine("### Completed before failure");
            if (completed.Count == 0)
            {
                sb.AppendLine("- (none)");
            }
            else
            {
                foreach (var c in completed)
                {
                    sb.AppendLine($"- {c}");
                }
            }

            throw new InvalidOperationException(sb.ToString().TrimEnd(), ex);
        }
    }

    internal static bool IsLiteralProjectReference(string path) =>
        path.IndexOfAny(['*', '?', ';', '%']) < 0
        && !path.Contains("$(", StringComparison.Ordinal)
        && !path.Contains("@(", StringComparison.Ordinal);

    private static void WriteText(string path, string text, ProjectRenameFile? source, Action<string, string, Encoding>? writeText)
    {
        source?.VerifyCurrent(path);
        var encoding = source?.Encoding ?? SourceTextEncoding.ForDiskPath(path);
        if (writeText is not null)
        {
            writeText(path, text, encoding);
            return;
        }

        File.WriteAllText(path, text, encoding);
    }

    private static bool IsSdkStyle(ProjectRenameFile file)
    {
        using var reader = file.CreateXmlReader();
        return reader.MoveToContent() == XmlNodeType.Element && reader.Name == "Project"
            && !string.IsNullOrWhiteSpace(reader.GetAttribute("Sdk"));
    }

    private static void ValidateMove(ProjectMove move)
    {
        var gitMetadata = Path.Combine(move.OldDirectory, ".git");
        if (File.Exists(gitMetadata) || Directory.Exists(gitMetadata))
        {
            throw new InvalidOperationException("Unsupported layout: the project directory is a Git checkout root.");
        }

        if (!File.Exists(move.OldProjectPath))
        {
            throw new FileNotFoundException("Project file not found.", move.OldProjectPath);
        }

        if (Directory.Exists(move.NewDirectory) || File.Exists(move.NewDirectory))
        {
            throw new InvalidOperationException($"Destination already exists: `{move.NewDirectory}`.");
        }

        var occupiedPath = Path.Combine(move.OldDirectory, Path.GetFileName(move.NewProjectPath));
        if (File.Exists(occupiedPath) || Directory.Exists(occupiedPath))
        {
            throw new InvalidOperationException($"Destination project path already exists inside the moving directory: `{occupiedPath}`.");
        }
    }

    private static TextEdit? CreateProjectReferenceEdit(string projectPath, ProjectMove move)
    {
        ProjectRenameFile file;
        try
        {
            file = ProjectRenameFile.Read(projectPath);
        }
        catch (Exception ex) when (!move.Contains(projectPath) && ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var (updated, _, _) = ProjectRenameProject.Rewrite(file, move, renameProperties: false);
        return updated is null ? null : new TextEdit(move.FinalPath(projectPath), updated, "Update ProjectReference")
        {
            Source = file,
        };
    }

    private static TextEdit? CreateSolutionFilterEdit(string filterPath, ProjectMove move, List<string> warnings)
    {
        var file = ProjectRenameFile.Read(filterPath);
        string? updated;
        try
        {
            updated = ProjectRenameSolutionFilter.Rewrite(file, move);
        }
        catch (JsonException ex)
        {
            // MSBuild cannot open such a filter either; the rename itself does not depend on it.
            warnings.Add($"Skipped unreadable solution filter `{filterPath}`: {ex.Message}");
            return null;
        }

        return updated is null ? null : new TextEdit(move.FinalPath(filterPath), updated, "Update .slnf project entries")
        {
            Source = file,
        };
    }

    private static TextEdit? CreateSolutionEdit(string solutionPath, ProjectMove move)
    {
        var file = ProjectRenameFile.Read(solutionPath);
        var updated = ProjectRenameSolution.Rewrite(file, move);
        return updated is null ? null : new TextEdit(
            move.FinalPath(solutionPath), updated, $"Update {Path.GetExtension(solutionPath)} project entries")
        {
            Source = file,
        };
    }

    private static string ResolveSearchRoot(string? searchRoot, string projectParent)
    {
        if (!string.IsNullOrWhiteSpace(searchRoot))
        {
            return Path.GetFullPath(searchRoot.Trim());
        }

        var env = Environment.GetEnvironmentVariable("ROSLYN_MCP_WORKSPACE");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
        {
            return Path.GetFullPath(env);
        }

        var candidate = projectParent;
        for (var i = 0; i < 5; i++)
        {
            if (Directory.EnumerateFiles(candidate, "*.sln").Any()
                || Directory.EnumerateFiles(candidate, "*.slnx").Any())
            {
                return candidate;
            }

            var parent = Path.GetDirectoryName(candidate);
            if (parent is null)
            {
                break;
            }

            candidate = parent;
        }

        return projectParent;
    }

    private static IEnumerable<string> EnumerateFiles(string root, string pattern, string? excludedRoot = null)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, pattern);
            }
            catch
            {
                continue;
            }

            foreach (var f in files)
            {
                yield return Path.GetFullPath(f);
            }

            IEnumerable<string> subDirs;
            try
            {
                subDirs = Directory.EnumerateDirectories(dir);
            }
            catch
            {
                continue;
            }

            foreach (var sub in subDirs)
            {
                if (ExcludedDirectoryNames.Contains(Path.GetFileName(sub))
                    || string.Equals(sub, excludedRoot, PathComparison))
                {
                    continue;
                }

                stack.Push(sub);
            }
        }
    }
}
