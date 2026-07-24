using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RoslynMcpServer.Services;

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

    private static readonly Regex SlnProjectLine = new(
        @"^Project\(""(?<type>\{[^""]+\})""\)\s*=\s*""(?<name>[^""]+)""\s*,\s*""(?<path>[^""]+)""\s*,\s*""(?<guid>\{[^""]+\})""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public sealed record TextEdit(string Path, string NewContent, string Description);

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
        IReadOnlyList<string> Warnings);

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
        if (trimmedNewName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || trimmedNewName.Contains('/')
            || trimmedNewName.Contains('\\'))
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

        var csprojText = File.ReadAllText(oldProjectPath);
        if (!IsSdkStyle(csprojText))
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

        if (Directory.Exists(newDirectory) || File.Exists(newProjectPath))
        {
            throw new InvalidOperationException($"Destination already exists: `{newDirectory}`.");
        }

        var (hasAssemblyName, assemblyNameValue) = TryReadProperty(csprojText, "AssemblyName");
        var (hasRootNamespace, rootNamespaceValue) = TryReadProperty(csprojText, "RootNamespace");
        var updateAssemblyName = hasAssemblyName
            && string.Equals(assemblyNameValue, oldNameWithoutExt, StringComparison.Ordinal);
        var updateRootNamespace = hasRootNamespace
            && string.Equals(rootNamespaceValue, oldNameWithoutExt, StringComparison.Ordinal);

        string? newCsprojContent = null;
        if (updateAssemblyName || updateRootNamespace)
        {
            newCsprojContent = csprojText;
            if (updateAssemblyName)
            {
                newCsprojContent = ReplaceXmlPropertyValue(newCsprojContent, "AssemblyName", oldNameWithoutExt, trimmedNewName);
            }

            if (updateRootNamespace)
            {
                newCsprojContent = ReplaceXmlPropertyValue(newCsprojContent, "RootNamespace", oldNameWithoutExt, trimmedNewName);
            }
        }

        var root = ResolveSearchRoot(searchRoot, parent);
        var warnings = new List<string>();
        var textEdits = new List<TextEdit>();

        foreach (var host in EnumerateFiles(root, "*.csproj"))
        {
            if (string.Equals(host, oldProjectPath, PathComparison))
            {
                continue;
            }

            if (!TryBuildProjectReferenceEdit(host, oldProjectPath, newProjectPath, out var edit))
            {
                continue;
            }

            textEdits.Add(edit);
        }

        var solutionFiles = EnumerateFiles(root, "*.sln")
            .Concat(EnumerateFiles(root, "*.slnx"))
            .ToList();

        if (solutionFiles.Count == 0)
        {
            warnings.Add("No `.sln`/`.slnx` found under search root — project refs will still be updated.");
        }

        foreach (var solution in solutionFiles)
        {
            if (TryBuildSolutionEdit(solution, oldProjectPath, newProjectPath, oldNameWithoutExt, trimmedNewName, out var edit))
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
            warnings);
    }

    public static string FormatPlan(RenamePlan plan, bool dryRun)
    {
        var sb = new StringBuilder();
        sb.AppendLine(dryRun ? "## rename_project preview (dryRun=true) — no changes written" : "## rename_project apply");
        sb.AppendLine($"- Directory: `{plan.OldDirectory}` → `{plan.NewDirectory}`");
        sb.AppendLine($"- Project file: `{plan.OldProjectFileName}` → `{plan.NewProjectFileName}`");
        sb.AppendLine($"- Update AssemblyName: {(plan.UpdateAssemblyName ? "yes (matched old project name)" : "no")}");
        sb.AppendLine($"- Update RootNamespace: {(plan.UpdateRootNamespace ? "yes (matched old project name)" : "no")}");
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
        var completed = new List<string>();
        try
        {
            // 1) Move directory + rename csproj while old absolute paths still valid for nothing else
            Directory.Move(plan.OldDirectory, plan.NewDirectory);
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
                File.WriteAllText(plan.NewProjectPath, plan.NewCsprojContent);
                completed.Add("Wrote updated AssemblyName/RootNamespace in csproj");
            }

            var failed = new List<string>();
            foreach (var edit in plan.TextEdits)
            {
                try
                {
                    // Path may still be absolute (sibling projects / solutions outside moved dir)
                    File.WriteAllText(edit.Path, edit.NewContent);
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

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
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

    internal static bool IsSdkStyle(string csprojText)
    {
        using var reader = new StringReader(csprojText);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("<?", StringComparison.Ordinal))
            {
                continue;
            }

            return trimmed.StartsWith("<Project", StringComparison.OrdinalIgnoreCase)
                && trimmed.Contains("Sdk=", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool TryBuildProjectReferenceEdit(
        string hostProjectPath,
        string oldTarget,
        string newTarget,
        out TextEdit edit)
    {
        edit = null!;
        string text;
        try
        {
            text = File.ReadAllText(hostProjectPath);
        }
        catch
        {
            return false;
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(text);
        }
        catch
        {
            return false;
        }

        var hostDir = Path.GetDirectoryName(hostProjectPath)!;
        var changed = false;
        foreach (var pref in doc.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
        {
            var includeAttr = pref.Attribute("Include");
            if (includeAttr is null || string.IsNullOrWhiteSpace(includeAttr.Value))
            {
                continue;
            }

            var resolved = Path.GetFullPath(Path.Combine(hostDir, includeAttr.Value.Replace('\\', Path.DirectorySeparatorChar)));
            if (!string.Equals(resolved, oldTarget, PathComparison))
            {
                continue;
            }

            includeAttr.Value = Path.GetRelativePath(hostDir, newTarget).Replace(Path.DirectorySeparatorChar, '\\');
            changed = true;
        }

        if (!changed)
        {
            return false;
        }

        var newContent = doc.Declaration is null
            ? doc.ToString() + Environment.NewLine
            : doc.Declaration + Environment.NewLine + doc.ToString() + Environment.NewLine;

        // Prefer surgical string replace to preserve formatting when Include is unique
        var oldRel = Path.GetRelativePath(hostDir, oldTarget).Replace(Path.DirectorySeparatorChar, '\\');
        var newRel = Path.GetRelativePath(hostDir, newTarget).Replace(Path.DirectorySeparatorChar, '\\');
        var surgical = text.Replace(oldRel, newRel, PathComparison);
        if (!string.Equals(surgical, text, StringComparison.Ordinal)
            && surgical.Contains(newRel, PathComparison)
            && !surgical.Contains(oldRel, PathComparison))
        {
            newContent = surgical;
        }
        else
        {
            // Also try forward-slash form
            var oldFwd = oldRel.Replace('\\', '/');
            var newFwd = newRel.Replace('\\', '/');
            var surgicalFwd = text.Replace(oldFwd, newFwd, PathComparison);
            if (!string.Equals(surgicalFwd, text, StringComparison.Ordinal))
            {
                newContent = surgicalFwd;
            }
        }

        edit = new TextEdit(hostProjectPath, newContent, "Update ProjectReference");
        return true;
    }

    private static bool TryBuildSolutionEdit(
        string solutionPath,
        string oldProjectPath,
        string newProjectPath,
        string oldName,
        string newName,
        out TextEdit edit)
    {
        edit = null!;
        var ext = Path.GetExtension(solutionPath);
        if (ext.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            return TryBuildSlnxEdit(solutionPath, oldProjectPath, newProjectPath, out edit);
        }

        return TryBuildClassicSlnEdit(solutionPath, oldProjectPath, newProjectPath, oldName, newName, out edit);
    }

    private static bool TryBuildClassicSlnEdit(
        string solutionPath,
        string oldProjectPath,
        string newProjectPath,
        string oldName,
        string newName,
        out TextEdit edit)
    {
        edit = null!;
        var solutionDir = Path.GetDirectoryName(solutionPath)!;
        var lines = File.ReadAllLines(solutionPath);
        var changed = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var match = SlnProjectLine.Match(lines[i]);
            if (!match.Success)
            {
                continue;
            }

            var pathInSln = match.Groups["path"].Value.Replace('/', '\\');
            var resolved = Path.GetFullPath(Path.Combine(solutionDir, pathInSln));
            if (!string.Equals(resolved, oldProjectPath, PathComparison))
            {
                continue;
            }

            var newRel = Path.GetRelativePath(solutionDir, newProjectPath).Replace(Path.DirectorySeparatorChar, '\\');
            var displayName = match.Groups["name"].Value;
            if (string.Equals(displayName, oldName, PathComparison))
            {
                displayName = newName;
            }

            lines[i] =
                $"Project(\"{match.Groups["type"].Value}\") = \"{displayName}\", \"{newRel}\", \"{match.Groups["guid"].Value}\"";
            changed = true;
        }

        if (!changed)
        {
            return false;
        }

        edit = new TextEdit(
            solutionPath,
            string.Join(Environment.NewLine, lines) + Environment.NewLine,
            "Update .sln project entry");
        return true;
    }

    private static bool TryBuildSlnxEdit(
        string solutionPath,
        string oldProjectPath,
        string newProjectPath,
        out TextEdit edit)
    {
        edit = null!;
        var solutionDir = Path.GetDirectoryName(solutionPath)!;
        var text = File.ReadAllText(solutionPath);
        XDocument doc;
        try
        {
            doc = XDocument.Parse(text);
        }
        catch
        {
            return false;
        }

        var changed = false;
        foreach (var projectEl in doc.Descendants().Where(e => e.Name.LocalName == "Project"))
        {
            var pathAttr = projectEl.Attribute("Path") ?? projectEl.Attribute("path");
            if (pathAttr is null || string.IsNullOrWhiteSpace(pathAttr.Value))
            {
                continue;
            }

            var resolved = Path.GetFullPath(
                Path.Combine(solutionDir, pathAttr.Value.Replace('/', Path.DirectorySeparatorChar)));
            if (!string.Equals(resolved, oldProjectPath, PathComparison))
            {
                continue;
            }

            pathAttr.Value = Path.GetRelativePath(solutionDir, newProjectPath).Replace(Path.DirectorySeparatorChar, '/');
            changed = true;
        }

        if (!changed)
        {
            return false;
        }

        var oldRel = Path.GetRelativePath(solutionDir, oldProjectPath).Replace(Path.DirectorySeparatorChar, '/');
        var newRel = Path.GetRelativePath(solutionDir, newProjectPath).Replace(Path.DirectorySeparatorChar, '/');
        var surgical = text.Replace(oldRel, newRel, PathComparison);
        if (string.Equals(surgical, text, StringComparison.Ordinal))
        {
            surgical = text.Replace(oldRel.Replace('/', '\\'), newRel.Replace('/', '\\'), PathComparison);
        }

        var newContent = !string.Equals(surgical, text, StringComparison.Ordinal)
            ? surgical
            : (doc.Declaration is null
                ? doc.ToString() + Environment.NewLine
                : doc.Declaration + Environment.NewLine + doc.ToString() + Environment.NewLine);

        edit = new TextEdit(solutionPath, newContent, "Update .slnx project entry");
        return true;
    }

    private static (bool Present, string? Value) TryReadProperty(string csprojText, string propertyName)
    {
        try
        {
            var doc = XDocument.Parse(csprojText);
            var el = doc.Descendants().FirstOrDefault(e =>
                e.Name.LocalName.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
            if (el is null)
            {
                return (false, null);
            }

            return (true, el.Value.Trim());
        }
        catch
        {
            return (false, null);
        }
    }

    private static string ReplaceXmlPropertyValue(string csprojText, string propertyName, string oldValue, string newValue)
    {
        // Surgical replace preserves formatting for the common single-line property case.
        var pattern = $@"<{propertyName}>\s*{Regex.Escape(oldValue)}\s*</{propertyName}>";
        var replaced = Regex.Replace(
            csprojText,
            pattern,
            $"<{propertyName}>{newValue}</{propertyName}>",
            RegexOptions.CultureInvariant);
        if (!string.Equals(replaced, csprojText, StringComparison.Ordinal))
        {
            return replaced;
        }

        var doc = XDocument.Parse(csprojText, LoadOptions.PreserveWhitespace);
        var el = doc.Descendants().FirstOrDefault(e =>
            e.Name.LocalName.Equals(propertyName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.Value.Trim(), oldValue, StringComparison.Ordinal));
        if (el is not null)
        {
            el.Value = newValue;
        }

        return doc.ToString();
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

    private static IEnumerable<string> EnumerateFiles(string root, string pattern)
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
                if (ExcludedDirectoryNames.Contains(Path.GetFileName(sub)))
                {
                    continue;
                }

                stack.Push(sub);
            }
        }
    }
}
