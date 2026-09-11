using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class WorkspaceWriteBoundaryTests
{
    [Fact]
    public void Exact_inverse_restores_only_mapped_shadow_and_keeps_unrelated_order_and_multiplicity()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var nugetA = new FakeAnalyzerReference("nuget-a", @"C:\NuGet\A.dll");
        var original = new FakeAnalyzerReference("original", @"C:\Repro\Generator.dll");
        var nugetB = new FakeAnalyzerReference("nuget-b", @"C:\NuGet\B.dll");
        var nugetA2 = new FakeAnalyzerReference("nuget-a-again", @"C:\NuGet\A.dll");
        var shadow = new FakeAnalyzerReference("shadow", @"C:\Shadow\v2-main-only\hash\Generator.dll");

        var baseSolution = workspace.CurrentSolution.AddProject(
            ProjectInfo.Create(projectId, VersionStamp.Create(), "Consumer", "Consumer", LanguageNames.CSharp)
                .WithAnalyzerReferences(new AnalyzerReference[] { nugetA, original, nugetB, nugetA2 }));
        var candidate = baseSolution.WithProjectAnalyzerReferences(
            projectId,
            new AnalyzerReference[] { nugetA, shadow, nugetB, nugetA2 });

        var mapping = new AnalyzerShadowMapping(
            Guid.NewGuid(),
            @"C:\Repro\App.sln",
            [
                new AnalyzerShadowReferenceEntry(
                    projectId,
                    "Consumer",
                    "original",
                    original.FullPath,
                    "Generator",
                    shadow.FullPath,
                    "gen-1",
                    Applied: true,
                    SkipReason: null,
                    StaleGeneration: false),
            ]);

        var inverted = mapping.InvertKnownReplacements(candidate, baseSolution, new InProcessAnalyzerAssemblyLoader());
        var refs = inverted.GetProject(projectId)!.AnalyzerReferences;
        Assert.Equal(4, refs.Count);
        Assert.Same(nugetA, refs[0]);
        Assert.Same(original, refs[1]);
        Assert.Same(nugetB, refs[2]);
        Assert.Same(nugetA2, refs[3]);
    }

    [Fact]
    public void Whole_list_wipe_is_not_accepted_as_inverse_when_unrelated_ref_was_dropped()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var nuget = new FakeAnalyzerReference("nuget", @"C:\NuGet\A.dll");
        var original = new FakeAnalyzerReference("original", @"C:\Repro\Generator.dll");
        var shadow = new FakeAnalyzerReference("shadow", @"C:\Shadow\Generator.dll");

        var baseSolution = workspace.CurrentSolution.AddProject(
            ProjectInfo.Create(projectId, VersionStamp.Create(), "Consumer", "Consumer", LanguageNames.CSharp)
                .WithAnalyzerReferences(new AnalyzerReference[] { nuget, original }));
        var candidate = baseSolution.WithProjectAnalyzerReferences(projectId, new AnalyzerReference[] { shadow });

        var mapping = Mapping(projectId, original.FullPath!, shadow.FullPath!);
        var preflight = WorkspaceWriteBoundary.ClassifyAndInvert(
            candidate,
            baseSolution,
            mapping,
            new InProcessAnalyzerAssemblyLoader());
        Assert.False(preflight.Accepted);
        Assert.Equal("unknown-analyzer-diff", preflight.Reason);
    }

    [Fact]
    public void No_overlay_is_noop_when_analyzer_lists_match()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var same = new FakeAnalyzerReference("same", @"C:\NuGet\SomeAnalyzers.dll");
        var baseSolution = workspace.CurrentSolution.AddProject(
            ProjectInfo.Create(projectId, VersionStamp.Create(), "Consumer", "Consumer", LanguageNames.CSharp)
                .WithAnalyzerReferences(new AnalyzerReference[] { same }));

        var preflight = WorkspaceWriteBoundary.ClassifyAndInvert(
            baseSolution,
            baseSolution,
            mapping: null,
            new InProcessAnalyzerAssemblyLoader());
        Assert.True(preflight.Accepted);
        Assert.Same(baseSolution, preflight.CleanedCandidate);
    }

    [Fact]
    public void Unknown_analyzer_diff_is_rejected_without_temp_prefix_inference()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var original = new FakeAnalyzerReference("original", @"C:\Repro\Generator.dll");
        var other = new FakeAnalyzerReference("other", @"C:\Temp\NotFromMapping.dll");
        var baseSolution = workspace.CurrentSolution.AddProject(
            ProjectInfo.Create(projectId, VersionStamp.Create(), "Consumer", "Consumer", LanguageNames.CSharp)
                .WithAnalyzerReferences(new AnalyzerReference[] { original }));
        var candidate = baseSolution.WithProjectAnalyzerReferences(projectId, new AnalyzerReference[] { other });

        var mapping = Mapping(projectId, original.FullPath!, @"C:\Shadow\Generator.dll");
        var preflight = WorkspaceWriteBoundary.ClassifyAndInvert(
            candidate,
            baseSolution,
            mapping,
            new InProcessAnalyzerAssemblyLoader());
        Assert.False(preflight.Accepted);
        Assert.Equal("unknown-analyzer-diff", preflight.Reason);
    }

    [Fact]
    public void Stale_session_is_rejected_before_inverse()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var original = new FakeAnalyzerReference("original", @"C:\Repro\Generator.dll");
        var baseSolution = workspace.CurrentSolution.AddProject(
            ProjectInfo.Create(projectId, VersionStamp.Create(), "Consumer", "Consumer", LanguageNames.CSharp)
                .WithAnalyzerReferences(new AnalyzerReference[] { original }));
        var currentSession = Guid.NewGuid();
        var context = new WorkspaceWriteOperationContext(
            Guid.NewGuid(),
            @"C:\Repro\App.sln",
            Mapping(projectId, original.FullPath!, @"C:\Shadow\Generator.dll"),
            baseSolution);

        var preflight = WorkspaceWriteBoundary.Preflight(
            baseSolution,
            baseSolution,
            context,
            currentSession,
            @"C:\Repro\App.sln",
            new InProcessAnalyzerAssemblyLoader());
        Assert.False(preflight.Accepted);
        Assert.Equal("stale-session", preflight.Reason);
    }

    [Fact]
    public void Added_project_with_analyzer_refs_is_rejected()
    {
        using var workspace = new AdhocWorkspace();
        var existingId = ProjectId.CreateNewId();
        var addedId = ProjectId.CreateNewId();
        var nuget = new FakeAnalyzerReference("nuget", @"C:\NuGet\A.dll");
        var baseSolution = workspace.CurrentSolution.AddProject(
            ProjectInfo.Create(existingId, VersionStamp.Create(), "Consumer", "Consumer", LanguageNames.CSharp));
        var candidate = baseSolution.AddProject(
            ProjectInfo.Create(addedId, VersionStamp.Create(), "NewProj", "NewProj", LanguageNames.CSharp)
                .WithAnalyzerReferences(new AnalyzerReference[] { nuget }));

        var preflight = WorkspaceWriteBoundary.ClassifyAndInvert(
            candidate,
            baseSolution,
            mapping: null,
            new InProcessAnalyzerAssemblyLoader());
        Assert.False(preflight.Accepted);
        Assert.Equal("unknown-analyzer-diff: added-project", preflight.Reason);
    }

    [Fact]
    public void Added_project_without_analyzer_refs_is_allowed()
    {
        using var workspace = new AdhocWorkspace();
        var existingId = ProjectId.CreateNewId();
        var addedId = ProjectId.CreateNewId();
        var baseSolution = workspace.CurrentSolution.AddProject(
            ProjectInfo.Create(existingId, VersionStamp.Create(), "Consumer", "Consumer", LanguageNames.CSharp));
        var candidate = baseSolution.AddProject(
            ProjectInfo.Create(addedId, VersionStamp.Create(), "NewProj", "NewProj", LanguageNames.CSharp));

        var preflight = WorkspaceWriteBoundary.ClassifyAndInvert(
            candidate,
            baseSolution,
            mapping: null,
            new InProcessAnalyzerAssemblyLoader());
        Assert.True(preflight.Accepted);
        Assert.NotNull(preflight.CleanedCandidate?.GetProject(addedId));
    }

    [Fact]
    public void Removed_project_is_not_recreated_by_mapping()
    {
        using var workspace = new AdhocWorkspace();
        var keptId = ProjectId.CreateNewId();
        var removedId = ProjectId.CreateNewId();
        var original = new FakeAnalyzerReference("original", @"C:\Repro\Generator.dll");
        var shadow = new FakeAnalyzerReference("shadow", @"C:\Shadow\Generator.dll");
        var baseSolution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(keptId, VersionStamp.Create(), "Kept", "Kept", LanguageNames.CSharp)
                .WithAnalyzerReferences(new AnalyzerReference[] { original }))
            .AddProject(ProjectInfo.Create(removedId, VersionStamp.Create(), "Gone", "Gone", LanguageNames.CSharp)
                .WithAnalyzerReferences(new AnalyzerReference[] { original }));
        var candidate = baseSolution.RemoveProject(removedId);

        var mapping = new AnalyzerShadowMapping(
            Guid.NewGuid(),
            @"C:\Repro\App.sln",
            [
                new AnalyzerShadowReferenceEntry(
                    removedId,
                    "Gone",
                    "original",
                    original.FullPath,
                    "Generator",
                    shadow.FullPath,
                    "gen-1",
                    Applied: true,
                    SkipReason: null,
                    StaleGeneration: false),
            ]);

        var inverted = mapping.InvertKnownReplacements(candidate, baseSolution, new InProcessAnalyzerAssemblyLoader());
        Assert.Null(inverted.GetProject(removedId));
        var preflight = WorkspaceWriteBoundary.ClassifyAndInvert(
            candidate,
            baseSolution,
            mapping,
            new InProcessAnalyzerAssemblyLoader());
        Assert.True(preflight.Accepted);
        Assert.Null(preflight.CleanedCandidate!.GetProject(removedId));
    }

    [Fact]
    public void Production_TryApplyChanges_has_single_SolutionManager_call_site()
    {
        var path = Path.Combine(FindRepoRoot(), "Services", "SolutionManager.cs");
        var text = File.ReadAllText(path);
        var calls = 0;
        var searchFrom = 0;
        while (true)
        {
            var index = text.IndexOf("workspace.TryApplyChanges(", searchFrom, StringComparison.Ordinal);
            if (index < 0)
            {
                break;
            }

            calls++;
            searchFrom = index + 1;
        }

        Assert.Equal(1, calls);
        Assert.Contains("private bool TryApplyWorkspaceChanges", text, StringComparison.Ordinal);
        Assert.DoesNotContain("RevertAnalyzerReferenceOverlayForApply", text, StringComparison.Ordinal);
    }

    private static AnalyzerShadowMapping Mapping(ProjectId projectId, string original, string shadow)
    {
        return new AnalyzerShadowMapping(
            Guid.NewGuid(),
            @"C:\Repro\App.sln",
            [
                new AnalyzerShadowReferenceEntry(
                    projectId,
                    "Consumer",
                    "original",
                    original,
                    "Generator",
                    shadow,
                    "gen-1",
                    Applied: true,
                    SkipReason: null,
                    StaleGeneration: false),
            ]);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RoslynMcpServer.csproj")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed class FakeAnalyzerReference : AnalyzerReference
    {
        public FakeAnalyzerReference(string display, string? fullPath)
        {
            Display = display;
            FullPath = fullPath;
        }

        public override string Display { get; }

        public override string? FullPath { get; }

        public override object Id => Display;

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages() => ImmutableArray<DiagnosticAnalyzer>.Empty;

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language) => ImmutableArray<DiagnosticAnalyzer>.Empty;
    }
}
