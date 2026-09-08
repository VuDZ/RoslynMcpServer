using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RoslynMcpServer.Services;

/// <summary>
/// Resolves the MSBuild solution target for a project from <c>.sln</c> / <c>.slnx</c>
/// solution-folder hierarchy (Solution Explorer path, not the filesystem path).
/// </summary>
public static class SolutionProjectTargetResolver
{
    public const string SolutionFolderTypeGuid = "{2150E333-8FDC-42A3-9474-1A3956D46DE8}";

    private static readonly HashSet<string> BuildableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csproj",
        ".vbproj",
        ".fsproj",
    };

    private static readonly char[] TargetNameInvalidChars = ['%', '$', '@', ';', '.', '(', ')', '\''];

    private static readonly Regex SlnProjectLine = new(
        @"^Project\(""(?<type>\{[^""]+\})""\)\s*=\s*""(?<name>[^""]+)""\s*,\s*""(?<path>[^""]+)""\s*,\s*""(?<guid>\{[^""]+\})""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex NestedProjectLine = new(
        @"^\s*(?<child>\{[0-9A-Fa-f-]+\})\s*=\s*(?<parent>\{[0-9A-Fa-f-]+\})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    public sealed record ProjectEntry(
        string DisplayName,
        string FileNameWithoutExtension,
        string VirtualPath,
        string TargetName);

    public sealed record ResolveResult(
        bool Success,
        string? TargetName,
        string? DisplayName,
        string? VirtualPath,
        string? ErrorMessage)
    {
        public static ResolveResult Ok(ProjectEntry entry) =>
            new(true, entry.TargetName, entry.DisplayName, entry.VirtualPath, null);

        public static ResolveResult Fail(string message) =>
            new(false, null, null, null, message);
    }

    public static ResolveResult TryResolve(string solutionPath, string projectName)
    {
        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            return ResolveResult.Fail("Error: solution path is empty.");
        }

        if (string.IsNullOrWhiteSpace(projectName))
        {
            return ResolveResult.Fail("Error: `projectName` is empty.");
        }

        if (!File.Exists(solutionPath))
        {
            return ResolveResult.Fail($"Error: solution file not found: `{solutionPath}`.");
        }

        var ext = Path.GetExtension(solutionPath);
        IReadOnlyList<ProjectEntry> projects;
        try
        {
            var text = File.ReadAllText(solutionPath);
            if (ext.Equals(".sln", StringComparison.OrdinalIgnoreCase))
            {
                projects = ListFromSln(text);
            }
            else if (ext.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                projects = ListFromSlnx(text);
            }
            else
            {
                return ResolveResult.Fail(
                    "Error: `projectName` requires `workspacePath` to be a `.sln` or `.slnx` so the project can be built as a solution target.");
            }
        }
        catch (Exception ex)
        {
            return ResolveResult.Fail($"Error: could not read solution `{solutionPath}`: {ex.Message}");
        }

        return Match(projects, projectName.Trim(), solutionPath);
    }

    internal static IReadOnlyList<ProjectEntry> ListFromSln(string slnText)
    {
        if (string.IsNullOrWhiteSpace(slnText))
        {
            return [];
        }

        var items = new List<(string Guid, string Type, string Name, string Path)>();
        foreach (Match match in SlnProjectLine.Matches(slnText))
        {
            items.Add((
                NormalizeGuid(match.Groups["guid"].Value),
                NormalizeGuid(match.Groups["type"].Value),
                match.Groups["name"].Value,
                match.Groups["path"].Value));
        }

        var folders = items
            .Where(i => i.Type.Equals(NormalizeGuid(SolutionFolderTypeGuid), StringComparison.Ordinal))
            .ToDictionary(i => i.Guid, i => i.Name, StringComparer.Ordinal);

        var parents = ParseNestedProjects(slnText);
        var result = new List<ProjectEntry>();
        foreach (var item in items)
        {
            if (folders.ContainsKey(item.Guid) || !IsBuildableProjectPath(item.Path))
            {
                continue;
            }

            var folderPath = BuildFolderPath(item.Guid, parents, folders);
            var virtualPath = CombineVirtual(folderPath, item.Name);
            result.Add(new ProjectEntry(
                item.Name,
                Path.GetFileNameWithoutExtension(item.Path.Replace('/', Path.DirectorySeparatorChar)),
                virtualPath,
                CombineVirtual(folderPath, SanitizeProjectNameForTarget(item.Name))));
        }

        return result;
    }

    internal static IReadOnlyList<ProjectEntry> ListFromSlnx(string slnxText)
    {
        if (string.IsNullOrWhiteSpace(slnxText))
        {
            return [];
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(slnxText);
        }
        catch
        {
            return [];
        }

        var result = new List<ProjectEntry>();
        var root = doc.Root;
        if (root is null)
        {
            return result;
        }

        CollectSlnxProjects(root, parentFolderPath: null, result);
        return result;
    }

    internal static string SanitizeProjectNameForTarget(string projectName)
    {
        if (string.IsNullOrEmpty(projectName))
        {
            return projectName;
        }

        var chars = projectName.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(TargetNameInvalidChars, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    internal static string NormalizeVirtualPath(string path) =>
        path.Replace('/', '\\').Trim().Trim('\\');

    private static ResolveResult Match(
        IReadOnlyList<ProjectEntry> projects,
        string projectName,
        string solutionPath)
    {
        if (projects.Count == 0)
        {
            return ResolveResult.Fail(
                $"Error: no buildable projects found in `{Path.GetFileName(solutionPath)}`.");
        }

        var needleVirtual = NormalizeVirtualPath(projectName);
        var matches = projects
            .Where(p =>
                p.DisplayName.Equals(projectName, StringComparison.OrdinalIgnoreCase)
                || p.FileNameWithoutExtension.Equals(projectName, StringComparison.OrdinalIgnoreCase)
                || NormalizeVirtualPath(p.VirtualPath).Equals(needleVirtual, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(p => NormalizeVirtualPath(p.VirtualPath), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matches.Count == 1)
        {
            return ResolveResult.Ok(matches[0]);
        }

        var fileName = Path.GetFileName(solutionPath);
        var list = FormatProjectList(projects);
        if (matches.Count == 0)
        {
            return ResolveResult.Fail(
                $"Error: `projectName` `{projectName}` was not found in `{fileName}`. Projects:{Environment.NewLine}{list}");
        }

        return ResolveResult.Fail(
            $"Error: `projectName` `{projectName}` matches {matches.Count} projects in `{fileName}`. Pass a unique name or virtual path:{Environment.NewLine}{FormatProjectList(matches)}");
    }

    private static string FormatProjectList(IEnumerable<ProjectEntry> projects)
    {
        var sb = new StringBuilder();
        foreach (var project in projects.OrderBy(p => p.VirtualPath, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("- ");
            sb.Append(project.DisplayName);
            sb.Append(" → ");
            sb.Append(project.VirtualPath);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static Dictionary<string, string> ParseNestedProjects(string slnText)
    {
        var parents = new Dictionary<string, string>(StringComparer.Ordinal);
        const string header = "NestedProjects";
        var start = slnText.IndexOf(header, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return parents;
        }

        var end = slnText.IndexOf("EndGlobalSection", start, StringComparison.OrdinalIgnoreCase);
        var block = end > start ? slnText[start..end] : slnText[start..];
        foreach (Match match in NestedProjectLine.Matches(block))
        {
            parents[NormalizeGuid(match.Groups["child"].Value)] = NormalizeGuid(match.Groups["parent"].Value);
        }

        return parents;
    }

    private static string? BuildFolderPath(
        string projectGuid,
        IReadOnlyDictionary<string, string> parents,
        IReadOnlyDictionary<string, string> folders)
    {
        if (!parents.TryGetValue(projectGuid, out var parentGuid))
        {
            return null;
        }

        var segments = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = parentGuid;
        while (true)
        {
            if (!seen.Add(current))
            {
                break;
            }

            if (folders.TryGetValue(current, out var folderName) && !string.IsNullOrWhiteSpace(folderName))
            {
                segments.Add(folderName);
            }

            if (!parents.TryGetValue(current, out current))
            {
                break;
            }
        }

        if (segments.Count == 0)
        {
            return null;
        }

        segments.Reverse();
        return string.Join('\\', segments);
    }

    private static void CollectSlnxProjects(XElement element, string? parentFolderPath, List<ProjectEntry> result)
    {
        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName.Equals("Folder", StringComparison.OrdinalIgnoreCase))
            {
                CollectSlnxProjects(child, ResolveSlnxFolderPath(child, parentFolderPath), result);
                continue;
            }

            if (!child.Name.LocalName.Equals("Project", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = (string?)child.Attribute("Path") ?? (string?)child.Attribute("path");
            if (string.IsNullOrWhiteSpace(path) || !IsBuildableProjectPath(path))
            {
                continue;
            }

            var displayName = (string?)child.Attribute("Name") ?? (string?)child.Attribute("name");
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = Path.GetFileNameWithoutExtension(path.Replace('/', Path.DirectorySeparatorChar));
            }

            var virtualPath = CombineVirtual(parentFolderPath, displayName);
            result.Add(new ProjectEntry(
                displayName,
                Path.GetFileNameWithoutExtension(path.Replace('/', Path.DirectorySeparatorChar)),
                virtualPath,
                CombineVirtual(parentFolderPath, SanitizeProjectNameForTarget(displayName))));
        }
    }

    private static string? ResolveSlnxFolderPath(XElement folder, string? parentFolderPath)
    {
        var name = ((string?)folder.Attribute("Name") ?? (string?)folder.Attribute("name") ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return parentFolderPath;
        }

        if (name.StartsWith('/'))
        {
            var absolute = name.Trim('/').Replace('/', '\\');
            return string.IsNullOrWhiteSpace(absolute) ? parentFolderPath : absolute;
        }

        var segment = name.Replace('/', '\\').Trim('\\');
        return CombineVirtual(parentFolderPath, segment);
    }

    private static string CombineVirtual(string? folderPath, string name)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return name;
        }

        return string.IsNullOrWhiteSpace(name) ? folderPath : folderPath + '\\' + name;
    }

    private static bool IsBuildableProjectPath(string path)
    {
        var ext = Path.GetExtension(path.Replace('/', Path.DirectorySeparatorChar));
        return BuildableExtensions.Contains(ext);
    }

    private static string NormalizeGuid(string guid)
    {
        var trimmed = guid.Trim();
        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        if (trimmed[0] != '{')
        {
            trimmed = "{" + trimmed;
        }

        if (trimmed[^1] != '}')
        {
            trimmed += "}";
        }

        return trimmed.ToUpperInvariant();
    }
}
