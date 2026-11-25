using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
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
        if (trimmedNewName is "." or ".."
            || trimmedNewName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || trimmedNewName.Contains('/')
            || trimmedNewName.Contains('\\')
            || trimmedNewName.Contains('"')
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

        var move = new ProjectMove(oldProjectPath, newProjectPath);
        ValidateMove(move);

        var (hasAssemblyName, assemblyNameValue) = TryReadProperty(csprojText, "AssemblyName");
        var (hasRootNamespace, rootNamespaceValue) = TryReadProperty(csprojText, "RootNamespace");
        var updateAssemblyName = hasAssemblyName
            && string.Equals(assemblyNameValue, oldNameWithoutExt, StringComparison.Ordinal);
        var updateRootNamespace = hasRootNamespace
            && string.Equals(rootNamespaceValue, oldNameWithoutExt, StringComparison.Ordinal);

        var newCsprojContent = RewriteXmlReferences(csprojText, oldProjectPath, move, isProject: true);
        if (updateAssemblyName || updateRootNamespace)
        {
            if (newCsprojContent is null)
            {
                ValidateXmlEncoding(oldProjectPath, XDocument.Parse(csprojText).Declaration?.Encoding);
                newCsprojContent = csprojText;
            }

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

        IEnumerable<string> FindOwners(string pattern) =>
            EnumerateFiles(oldDirectory, pattern).Concat(move.Contains(root) ? [] : EnumerateFiles(root, pattern, oldDirectory));

        string[] projectPatterns = ["*.csproj", "*.fsproj", "*.vbproj"];
        foreach (var host in projectPatterns.SelectMany(FindOwners))
        {
            if (string.Equals(host, oldProjectPath, PathComparison))
            {
                continue;
            }

            var edit = CreateProjectReferenceEdit(host, move);
            if (edit is not null)
            {
                textEdits.Add(edit);
            }
        }

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
                WriteText(plan.NewProjectPath, plan.NewCsprojContent, writeText);
                completed.Add("Wrote renamed csproj updates");
            }

            var failed = new List<string>();
            foreach (var edit in plan.TextEdits)
            {
                try
                {
                    WriteText(edit.Path, edit.NewContent, writeText);
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

    private static void WriteText(string path, string text, Action<string, string, Encoding>? writeText)
    {
        var encoding = SourceTextEncoding.ForDiskPath(path);
        if (writeText is not null)
        {
            writeText(path, text, encoding);
            return;
        }

        File.WriteAllText(path, text, encoding);
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
        string text;
        try
        {
            text = File.ReadAllText(projectPath);
        }
        catch (Exception ex) when (!move.Contains(projectPath) && ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var updated = RewriteXmlReferences(text, projectPath, move, isProject: true);
        return updated is null ? null : new TextEdit(move.FinalPath(projectPath), updated, "Update ProjectReference");
    }

    private static TextEdit? CreateSolutionEdit(string solutionPath, ProjectMove move)
    {
        if (Path.GetExtension(solutionPath).Equals(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            var text = File.ReadAllText(solutionPath);
            var updated = RewriteXmlReferences(text, solutionPath, move, isProject: false);
            return updated is null ? null : new TextEdit(move.FinalPath(solutionPath), updated, "Update .slnx project entries");
        }

        return CreateClassicSolutionEdit(solutionPath, move);
    }

    private static TextEdit? CreateClassicSolutionEdit(string solutionPath, ProjectMove move)
    {
        const string SolutionFolderType = "{2150E333-8FDC-42A3-9474-1A3956D46DE8}";
        var lines = File.ReadAllLines(solutionPath);
        var changed = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var match = SlnProjectLine.Match(lines[i]);
            if (!match.Success || match.Groups["type"].Value.Equals(SolutionFolderType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = match.Groups["path"];
            var updated = move.RewriteReference(solutionPath, path.Value, '\\', out var renamedProject);
            if (updated is null)
            {
                continue;
            }

            var name = match.Groups["name"];
            var line = lines[i].Remove(path.Index, path.Length).Insert(path.Index, updated);
            if (renamedProject && string.Equals(name.Value, Path.GetFileNameWithoutExtension(move.OldProjectPath), PathComparison))
            {
                line = line.Remove(name.Index, name.Length).Insert(name.Index, Path.GetFileNameWithoutExtension(move.NewProjectPath));
            }

            lines[i] = line;
            changed = true;
        }

        return changed
            ? new TextEdit(move.FinalPath(solutionPath), string.Join(Environment.NewLine, lines) + Environment.NewLine, "Update .sln project entries")
            : null;
    }

    private static IEnumerable<XmlAttribute?> GetSolutionProjectReferences(XmlDocument document)
    {
        if (document.DocumentElement is not { } solution || !solution.Name.Equals("Solution", StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        // SLNX compares qualified element names without case, even in a default namespace. Projects
        // are direct children of Solution or a top-level Folder; extensions may reuse the same names.
        // See docs/project-review-2026-11-25/task-43-p2-slnx-extension-project-metadata-med.md.
        var containers = ChildrenNamed(solution, "Folder").Prepend(solution);
        foreach (var project in containers.SelectMany(container => ChildrenNamed(container, "Project")))
        {
            yield return project.GetAttributeNode("Path");
            foreach (var dependency in ChildrenNamed(project, "BuildDependency"))
            {
                yield return dependency.GetAttributeNode("Project");
            }
        }

        static IEnumerable<XmlElement> ChildrenNamed(XmlElement parent, string name) =>
            parent.ChildNodes.OfType<XmlElement>()
                .Where(child => child.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static string? RewriteXmlReferences(string text, string ownerPath, ProjectMove move, bool isProject)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        try
        {
            using var input = new StringReader(text);
            using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse });
            doc.Load(reader);
        }
        catch (XmlException) when (!move.Contains(ownerPath))
        {
            return null;
        }

        var attributes = isProject
            ? doc.GetElementsByTagName("ProjectReference", "*").OfType<XmlElement>()
                .Select(element => element.GetAttributeNode("Include"))
            : GetSolutionProjectReferences(doc);
        var changed = false;
        foreach (var attribute in attributes)
        {
            if (attribute is null || string.IsNullOrWhiteSpace(attribute.Value))
            {
                continue;
            }

            if (isProject && !IsLiteralProjectReference(attribute.Value))
            {
                throw new InvalidOperationException(
                    $"Unsupported ProjectReference path in `{ownerPath}`: `{attribute.Value}`. Use a literal project path before moving the directory.");
            }

            var updated = move.RewriteReference(ownerPath, attribute.Value, isProject ? '\\' : '/', out _);
            if (updated is not null)
            {
                attribute.Value = updated;
                changed = true;
            }
        }

        if (!changed)
        {
            return null;
        }

        var declaration = doc.FirstChild as XmlDeclaration;
        ValidateXmlEncoding(ownerPath, declaration?.Encoding);
        // Literal attribute whitespace was normalized by the reader. Character references that
        // decoded to tabs or newlines must stay escaped when another XML reader opens the result.
        var buffer = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true, NewLineHandling = NewLineHandling.Entitize };
        using (var writer = XmlWriter.Create(buffer, settings))
        {
            doc.WriteTo(writer);
        }

        var content = buffer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        if (text.Contains("\r\n", StringComparison.Ordinal))
        {
            content = content.Replace("\n", "\r\n", StringComparison.Ordinal);
        }

        return declaration is null ? content : text[..(text.IndexOf("?>", StringComparison.Ordinal) + 2)] + content;
    }

    private static void ValidateXmlEncoding(string path, string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var encoding = SourceTextEncoding.ForDiskPath(path);
        var matches = name.ToLowerInvariant() switch
        {
            "utf-8" => encoding.CodePage == Encoding.UTF8.CodePage,
            "utf-16" => encoding is UnicodeEncoding,
            "utf-16le" => encoding.CodePage == Encoding.Unicode.CodePage,
            "utf-16be" => encoding.CodePage == Encoding.BigEndianUnicode.CodePage,
            // XmlReader interprets a generic UTF-32 declaration as little-endian.
            "utf-32" or "utf-32le" => encoding.CodePage == Encoding.UTF32.CodePage,
            "utf-32be" => encoding is UTF32Encoding && encoding.CodePage != Encoding.UTF32.CodePage,
            _ => false,
        };
        if (!matches)
        {
            throw new InvalidOperationException(
                $"Unsupported XML encoding `{name}` in `{path}`. A rewrite requires UTF-8 or a matching UTF-16 or UTF-32 BOM.");
        }
    }

    private static bool IsLiteralProjectReference(string path) =>
        path.IndexOfAny(['*', '?', ';', '%']) < 0
        && !path.Contains("$(", StringComparison.Ordinal)
        && !path.Contains("@(", StringComparison.Ordinal);

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
        var replacement = new XElement(propertyName, newValue).ToString(SaveOptions.DisableFormatting);
        var replaced = Regex.Replace(
            csprojText,
            pattern,
            _ => replacement,
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

    private sealed record ProjectMove(string OldProjectPath, string NewProjectPath)
    {
        public string OldDirectory { get; } = Path.GetDirectoryName(OldProjectPath)!;
        public string NewDirectory { get; } = Path.GetDirectoryName(NewProjectPath)!;

        public bool Contains(string path) =>
            WorkspaceDiskPathFilter.IsPathUnderDirectory(path, OldDirectory, PathComparison);

        public string FinalPath(string path)
        {
            if (string.Equals(path, OldProjectPath, PathComparison))
            {
                return NewProjectPath;
            }

            return Contains(path) ? Path.Combine(NewDirectory, Path.GetRelativePath(OldDirectory, path)) : path;
        }

        public string? RewriteReference(string ownerPath, string reference, char separator, out bool renamedProject)
        {
            // Resolve in the original graph before calculating what the moved owner's path will resolve to.
            var normalized = reference.Replace('\\', '/');
            var oldTarget = Path.GetFullPath(normalized, Path.GetDirectoryName(ownerPath)!);
            var newTarget = FinalPath(oldTarget);
            var newOwnerDirectory = Path.GetDirectoryName(FinalPath(ownerPath))!;
            renamedProject = string.Equals(oldTarget, OldProjectPath, PathComparison);
            if (string.Equals(Path.GetFullPath(normalized, newOwnerDirectory), newTarget, PathComparison))
            {
                return null;
            }

            if (reference.Contains('/') != reference.Contains('\\'))
            {
                separator = reference.Contains('/') ? '/' : '\\';
            }

            return Path.GetRelativePath(newOwnerDirectory, newTarget).Replace(Path.DirectorySeparatorChar, separator);
        }
    }
}
