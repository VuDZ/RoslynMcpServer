using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host.Mef;

namespace ParallelLoad;

internal static class MergeChecks
{
    public static int Run()
    {
        var libraryPath = Path.GetFullPath("Library.csproj");
        var parentPath = Path.GetFullPath("Parent.csproj");
        var fallbackPath = Path.GetFullPath("Fallback.csproj");
        var discovered = CreateProject(libraryPath, "Library(net10.0)");
        var requested = CreateProject(libraryPath, "Library(net10.0)");
        var otherFramework = CreateProject(libraryPath, "Library(net9.0)");
        var firstFallback = CreateProject(fallbackPath, "Fallback");
        var laterFallback = CreateProject(fallbackPath, "Fallback");
        var nativeId = ProjectId.CreateNewId("unsupported.vcxproj");
        var aliases = ImmutableArray.Create("libraryAlias");
        var parent = CreateProject(parentPath, "Parent").WithProjectReferences(new[]
        {
            new ProjectReference(discovered.Id, aliases, embedInteropTypes: true),
            new ProjectReference(otherFramework.Id),
            new ProjectReference(nativeId, aliases),
        });
        var solutions = new[]
        {
            SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Create(), projects: new[]
            {
                parent, discovered, otherFramework, firstFallback,
            }),
            SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Create(), projects: new[]
            {
                requested, laterFallback,
            }),
        };
        var merged = ProjectMerger.Merge(solutions, new[] { new[] { parentPath }, new[] { libraryPath } }, "test.slnx");
        var projects = merged.Projects.ToDictionary(project => project.Id);
        Require(projects.Count == 4, "TFM variants must remain separate, and duplicate keys must collapse.");
        Require(projects.ContainsKey(requested.Id), "The requested copy must win over the earlier discovered copy.");
        Require(projects.ContainsKey(firstFallback.Id), "The first discovered copy must win when no group requests the path.");
        Require(projects[requested.Id].Documents.Single().Id == requested.Documents.Single().Id, "Document IDs must survive merging.");
        var references = projects[parent.Id].ProjectReferences;
        Require(references[0].ProjectId == requested.Id, "References must point to the selected canonical copy.");
        Require(references[0].Aliases.SequenceEqual(aliases) && references[0].EmbedInteropTypes, "Reference properties must survive rewriting.");
        Require(references[1].ProjectId == otherFramework.Id, "The reference to the other TFM must retain its target.");
        Require(references[2].ProjectId == nativeId, "Opaque references to unloaded native projects must retain their ID.");
        using var workspace = new AdhocWorkspace(MSBuildMefHostServices.DefaultServices);
        var solution = workspace.AddSolution(merged);
        Require(solution.GetProject(parent.Id)!.ProjectReferences.Count() == 2, "Public references must omit the absent native target.");
        Require(solution.GetProject(parent.Id)!.AllProjectReferences.Count == 3, "Raw references must retain the absent native target.");
        Console.WriteLine("Merge checks passed: canonical ownership, TFM keys, fallback, IDs, aliases, EmbedInteropTypes, absent native targets.");
        return 0;
    }

    private static ProjectInfo CreateProject(string path, string name)
    {
        var id = ProjectId.CreateNewId(name);
        return ProjectInfo.Create(
            id,
            VersionStamp.Create(),
            name,
            name,
            LanguageNames.CSharp,
            filePath: path,
            documents: new[] { DocumentInfo.Create(DocumentId.CreateNewId(id), "Source.cs", filePath: path + ".cs") });
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
