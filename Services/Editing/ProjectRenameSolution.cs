using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.VisualStudio.SolutionPersistence;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;
using RoslynMcpServer.Services.Workspace;

namespace RoslynMcpServer.Services.Editing;

internal static class ProjectRenameSolution
{
    private const string SolutionFolderType = "{2150E333-8FDC-42A3-9474-1A3956D46DE8}";

    internal static string? Rewrite(ProjectRenameFile file, ProjectMove move) =>
        Path.GetExtension(file.Path).Equals(".slnx", StringComparison.OrdinalIgnoreCase)
            ? RewriteSlnx(file, move)
            : RewriteClassic(file, move);

    private static string? RewriteClassic(ProjectRenameFile file, ProjectMove move)
    {
        var model = ReadModel(file, SolutionSerializers.SlnFileV12);
        var expected = FindAffectedProjects(model, file, move);
        if (expected.Count == 0)
        {
            return null;
        }

        var byId = expected.ToLookup(project => project.Id);
        var edits = new ProjectRenameTextEdits(file);
        foreach (Match header in _projectHeader.Matches(file.Text))
        {
            if (header.Groups["type"].Value.Equals(SolutionFolderType, StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParse(header.Groups["id"].Value, out var id) || !byId.Contains(id))
            {
                continue;
            }

            var path = header.Groups["path"];
            var resolvedPath = ProjectMove.ResolveReference(file.Path, path.Value);
            var project = FindUnique(byId[id].Where(candidate =>
                _pathComparer.Equals(ProjectMove.ResolveReference(file.Path, candidate.FilePath), resolvedPath)));
            if (project is null)
            {
                continue;
            }

            var replacement = move.RewriteReference(file.Path, path.Value, '\\', out var renamedProject);
            if (replacement is null || !expected.Remove(project))
            {
                throw Unmatched(file, $"project entry '{path.Value}' with GUID {id:B}");
            }

            if (replacement.IndexOfAny(['\r', '\n', '"']) >= 0)
            {
                throw new InvalidOperationException($"Cannot write the project path '{replacement}' in classic solution '{file.Path}'.");
            }

            edits.Replace(path.Index, path.Length, replacement);
            var name = header.Groups["name"];
            if (renamedProject && _pathComparer.Equals(name.Value, Path.GetFileNameWithoutExtension(move.OldProjectPath)))
            {
                edits.Replace(name.Index, name.Length, Path.GetFileNameWithoutExtension(move.NewProjectPath));
            }
        }

        VerifyProjectsMatched(file, expected);
        return VerifyUpdatedSolution(file, edits, SolutionSerializers.SlnFileV12);
    }

    private static string? RewriteSlnx(ProjectRenameFile file, ProjectMove move)
    {
        List<SlnxReference>? references;
        try
        {
            references = ReadSlnxReferences(file);
        }
        catch (XmlException) when (!move.Contains(file.Path))
        {
            return null;
        }

        if (references is null)
        {
            return null;
        }

        if (references.Any(reference => move.RewriteReference(file.Path, reference.Value, '/', out _) is not null))
        {
            file.ValidateXmlEncoding();
        }

        var model = ReadModel(file, SolutionSerializers.SlnXml);
        var edits = new ProjectRenameTextEdits(file);
        var rewrittenPaths = RewriteSlnxProjects(file, move, model, references, edits);
        RewriteSlnxDependencies(file, model, references, rewrittenPaths, edits);
        return VerifyUpdatedSolution(file, edits, SolutionSerializers.SlnXml);
    }

    private static Dictionary<SolutionProjectModel, string> RewriteSlnxProjects(
        ProjectRenameFile file,
        ProjectMove move,
        SolutionModel model,
        List<SlnxReference> references,
        ProjectRenameTextEdits edits)
    {
        var expected = FindAffectedProjects(model, file, move);
        var byPath = model.SolutionProjects
            .ToLookup(project => ProjectMove.ResolveReference(file.Path, project.FilePath), _pathComparer);
        var rewrittenPaths = new Dictionary<SolutionProjectModel, string>();
        foreach (var reference in references.Where(reference => reference.OwnerPath is null))
        {
            var replacement = move.RewriteReference(file.Path, reference.Value, '/', out _);
            if (replacement is null)
            {
                continue;
            }

            var project = FindUnique(byPath[ProjectMove.ResolveReference(file.Path, reference.Value)]);
            if (project is null || !expected.Remove(project))
            {
                throw Unmatched(file, $"project path '{reference.Value}'");
            }

            rewrittenPaths.Add(project, replacement);
            edits.ReplaceAttribute(reference.Line, reference.Column, "Path", reference.Value, replacement);
        }

        VerifyProjectsMatched(file, expected);
        return rewrittenPaths;
    }

    private static void RewriteSlnxDependencies(
        ProjectRenameFile file,
        SolutionModel model,
        List<SlnxReference> references,
        Dictionary<SolutionProjectModel, string> rewrittenPaths,
        ProjectRenameTextEdits edits)
    {
        var expected = model.SolutionProjects
            .SelectMany(owner => (owner.Dependencies ?? [])
                .Where(rewrittenPaths.ContainsKey)
                .Select(target => (Owner: owner, Target: target)))
            .ToHashSet();
        foreach (var reference in references.Where(reference => reference.OwnerPath is not null))
        {
            // BuildDependency binds to the SDK's project path identity, which admits case aliases even on Linux.
            // Use the new path of that declaration rather than interpreting a case alias as a disk path.
            var target = model.FindProject(ToModelPath(reference.Value));
            if (target is null || !rewrittenPaths.TryGetValue(target, out var replacement))
            {
                continue;
            }

            var owner = model.FindProject(ToModelPath(reference.OwnerPath!));
            if (owner is null || !expected.Remove((owner, target)))
            {
                throw Unmatched(file, $"build dependency '{reference.Value}' of '{reference.OwnerPath}'");
            }

            if (StringComparer.OrdinalIgnoreCase.Equals(ToModelPath(reference.Value), ToModelPath(replacement)))
            {
                continue;
            }

            var separator = reference.Value.Contains('\\') && !reference.Value.Contains('/') ? '\\' : '/';
            replacement = replacement.Replace('\\', separator).Replace('/', separator);
            edits.ReplaceAttribute(reference.Line, reference.Column, "Project", reference.Value, replacement);
        }

        if (expected.Count != 0)
        {
            var (owner, target) = expected.First();
            throw Unmatched(file, $"build dependency '{target.FilePath}' of '{owner.FilePath}'");
        }
    }

    private static List<SlnxReference>? ReadSlnxReferences(ProjectRenameFile file)
    {
        using var reader = file.CreateXmlReader();
        if (reader.MoveToContent() != XmlNodeType.Element || !IsElement(reader, "Solution"))
        {
            return null;
        }

        var references = new List<SlnxReference>();
        var inSolutionFolder = false;
        var projectDepth = -1;
        string? projectPath = null;
        // SolutionPersistence compares qualified names without case and reads folders only at the root.
        // A prefixed element with the same namespace URI can still be extension data (P2-43).
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            var depth = reader.Depth;
            if (depth == 1)
            {
                inSolutionFolder = IsElement(reader, "Folder");
            }

            if (depth <= projectDepth)
            {
                projectPath = null;
                projectDepth = -1;
            }

            if ((depth == 1 || depth == 2 && inSolutionFolder) && IsElement(reader, "Project"))
            {
                projectDepth = depth;
                projectPath = CaptureReference(reader, "Path", ownerPath: null, references);
            }
            else if (projectPath is not null && depth == projectDepth + 1 && IsElement(reader, "BuildDependency"))
            {
                CaptureReference(reader, "Project", projectPath, references);
            }
        }

        return references;
    }

    private static string? CaptureReference(XmlReader reader, string attribute, string? ownerPath, List<SlnxReference> references)
    {
        if (!reader.MoveToAttribute(attribute))
        {
            return null;
        }

        var value = reader.Value;
        if (!string.IsNullOrWhiteSpace(value))
        {
            var location = (IXmlLineInfo)reader;
            references.Add(new SlnxReference(value, ownerPath, location.LineNumber, location.LinePosition));
        }

        reader.MoveToElement();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static HashSet<SolutionProjectModel> FindAffectedProjects(SolutionModel model, ProjectRenameFile file, ProjectMove move)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var unchangedTargets = new HashSet<string>(_pathComparer);
        var movedTargets = new List<string>();
        var affected = new HashSet<SolutionProjectModel>();
        foreach (var project in model.SolutionProjects)
        {
            var original = ProjectMove.ResolveReference(file.Path, project.FilePath);
            var destination = move.FinalPath(original);
            if (_pathComparer.Equals(original, destination))
            {
                unchangedTargets.Add(destination);
            }
            else
            {
                movedTargets.Add(destination);
            }

            if (move.FormatReferenceAfterMove(file.Path, project.FilePath, destination, '/', comparison) is not null)
            {
                affected.Add(project);
            }
        }

        foreach (var destination in movedTargets)
        {
            if (!unchangedTargets.Add(destination))
            {
                throw new InvalidOperationException(
                    $"Renaming would duplicate the resolved project path '{destination}' in '{file.Path}'.");
            }
        }

        return affected;
    }

    private static string? VerifyUpdatedSolution<TSettings>(
        ProjectRenameFile file, ProjectRenameTextEdits edits, ISolutionSingleFileSerializer<TSettings> serializer)
    {
        var updated = edits.GetUpdatedText();
        if (updated is not null)
        {
            // Distinct disk paths can acquire the same name in a solution folder after the rename.
            using var source = new MemoryStream(PersistedPayload.FromText(updated, file.Encoding), writable: false);
            _ = ReadModel(file.Path, source, serializer);
        }

        return updated;
    }

    private static SolutionModel ReadModel<TSettings>(ProjectRenameFile file, ISolutionSingleFileSerializer<TSettings> serializer)
    {
        using var source = file.OpenBytes();
        return ReadModel(file.Path, source, serializer);
    }

    private static SolutionModel ReadModel<TSettings>(
        string path, Stream source, ISolutionSingleFileSerializer<TSettings> serializer)
    {
        try
        {
            return serializer.OpenAsync(source, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (SolutionException ex)
        {
            throw new InvalidOperationException($"Cannot read solution model for '{path}': {ex.Message}", ex);
        }
    }

    private static SolutionProjectModel? FindUnique(IEnumerable<SolutionProjectModel> projects)
    {
        using var candidates = projects.GetEnumerator();
        if (!candidates.MoveNext())
        {
            return null;
        }

        var project = candidates.Current;
        return candidates.MoveNext() ? null : project;
    }

    private static void VerifyProjectsMatched(ProjectRenameFile file, HashSet<SolutionProjectModel> expected)
    {
        if (expected.FirstOrDefault() is { } project)
        {
            throw Unmatched(file, $"project entry '{project.FilePath}'");
        }
    }

    private static InvalidOperationException Unmatched(ProjectRenameFile file, string reference) =>
        new($"Cannot match {reference} in '{file.Path}' to one source location and one solution model entry.");

    private static bool IsElement(XmlReader reader, string name) => reader.Name.Equals(name, StringComparison.OrdinalIgnoreCase);

    private static string ToModelPath(string path) =>
        path.Contains("://", StringComparison.Ordinal)
            ? path
            : path.Replace(Path.DirectorySeparatorChar == '\\' ? '/' : '\\', Path.DirectorySeparatorChar);

    private sealed record SlnxReference(string Value, string? OwnerPath, int Line, int Column);

    private static readonly Regex _projectHeader = new(
        @"(?:\A|(?<=[\r\n]))[ \t]*Project\(""(?<type>\{[^}""\r\n]+\})""\)[ \t]*=[ \t]*""(?<name>[^""\r\n]+)""[ \t]*,[ \t]*""(?<path>[^""\r\n]+)""[ \t]*,[ \t]*""(?<id>\{[^}""\r\n]+\})""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly StringComparer _pathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
