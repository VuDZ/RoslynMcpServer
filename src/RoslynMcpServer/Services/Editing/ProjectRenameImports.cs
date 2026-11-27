using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;

namespace RoslynMcpServer.Services.Editing;

/// <summary>
/// Rejects a rename when an imported MSBuild file declares a <c>ProjectReference</c> operation on the renamed project.
/// </summary>
/// <remarks>
/// The planner edits literal references in project and solution files only. A shared import such as
/// <c>Directory.Build.targets</c> can serve projects in different directories, so no single rewritten path fits every
/// importer, and a path built from <c>$(MSBuildThisFileDirectory)</c> is known only after evaluation. Without this check
/// the rename reported success and left a reference to the old path that broke the next build.
/// Projects are evaluated with default global properties, so a declaration that applies only under another
/// configuration is still checked through its item specification, but not through other conditional imports.
/// </remarks>
internal static class ProjectRenameImports
{
    internal static void RejectImportedReferences(IEnumerable<string> projectPaths, ProjectMove move, List<string> warnings)
    {
        using var collection = new ProjectCollection();
        var declarations = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var path in projectPaths)
        {
            Project project;
            try
            {
                project = collection.LoadProject(path);
            }
            catch (InvalidProjectFileException ex)
            {
                warnings.Add($"Could not evaluate `{path}` to check imported ProjectReference items: {ex.BaseMessage}");
                continue;
            }

            foreach (var import in project.Imports)
            {
                var items = import.ImportedProject.AllChildren.OfType<ProjectItemElement>()
                    .Where(item => item.ItemType.Equals("ProjectReference", StringComparison.OrdinalIgnoreCase));
                foreach (var item in items)
                {
                    if (ResolvesToRenamedProject(project, import.ImportedProject, move, item.Include, item.Update, item.Remove, item.Exclude))
                    {
                        var declaration = $"`{import.ImportedProject.FullPath}` line {item.Location.Line}";
                        if (!declarations.TryGetValue(declaration, out var importers))
                        {
                            declarations[declaration] = importers = new SortedSet<string>(StringComparer.Ordinal);
                        }

                        importers.Add(Path.GetFileName(path));
                    }
                }
            }
        }

        if (declarations.Count > 0)
        {
            var list = string.Join("; ", declarations.Select(pair => $"{pair.Key} (imported by {string.Join(", ", pair.Value)})"));
            throw new InvalidOperationException(
                $"Imported MSBuild files declare ProjectReference items for `{move.OldProjectPath}`: {list}. "
                + "rename_project edits only project and solution files. Remove or comment out these declarations, "
                + $"run the rename, then restore them with the new path `{move.NewProjectPath}`.");
        }
    }

    private static bool ResolvesToRenamedProject(
        Project project,
        ProjectRootElement import,
        ProjectMove move,
        params string[] specifications)
    {
        // A specification built from an item list or metadata re-shapes items declared elsewhere; the SDK uses such
        // transforms of @(ProjectReference). The original declaration is checked where it is written.
        foreach (var specification in specifications.Where(value => !string.IsNullOrWhiteSpace(value)
                     && !value.Contains("@(", StringComparison.Ordinal) && !value.Contains("%(", StringComparison.Ordinal)))
        {
            foreach (var part in project.ExpandString(BindThisFileProperties(specification, import))
                         .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                // MSBuild matches literal item specifications without case on every platform.
                if (string.Equals(ProjectMove.ResolveReference(project.FullPath, part), move.OldProjectPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string BindThisFileProperties(string specification, ProjectRootElement import)
    {
        // MSBuild defines MSBuildThisFile* for the file that contains the expression. Project-level expansion
        // would resolve them against the importing project instead.
        var file = import.FullPath;
        return specification
            .Replace("$(MSBuildThisFileDirectory)", import.DirectoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            .Replace("$(MSBuildThisFileFullPath)", file, StringComparison.OrdinalIgnoreCase)
            .Replace("$(MSBuildThisFileName)", Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase)
            .Replace("$(MSBuildThisFileExtension)", Path.GetExtension(file), StringComparison.OrdinalIgnoreCase)
            .Replace("$(MSBuildThisFile)", Path.GetFileName(file), StringComparison.OrdinalIgnoreCase);
    }
}
