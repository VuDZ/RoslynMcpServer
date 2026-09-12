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
        var mapping = Mapping(projectId, original.FullPath!, @"C:\Shadow\Generator.dll");
        var context = WorkspaceWriteOperationContext.Verified(
            Guid.NewGuid(),
            @"C:\Repro\App.sln",
            mapping,
            baseSolution,
            baseSolution,
            rawWorkspaceRevision: 1,
            shadowCopyEnabled: true);

        var preflight = WorkspaceWriteBoundary.Preflight(
            baseSolution,
            baseSolution,
            context,
            Freshness(currentSession, mapping, baseSolution),
            new InProcessAnalyzerAssemblyLoader());
        Assert.False(preflight.Accepted);
        Assert.Equal(WorkspaceWriteBoundary.ReasonStaleSession, preflight.Reason);
    }

    [Fact]
    public void Stale_raw_revision_is_rejected_before_inverse()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var original = new FakeAnalyzerReference("original", @"C:\Repro\Generator.dll");
        var baseSolution = workspace.CurrentSolution.AddProject(
            ProjectInfo.Create(projectId, VersionStamp.Create(), "Consumer", "Consumer", LanguageNames.CSharp)
                .WithAnalyzerReferences(new AnalyzerReference[] { original }));
        var session = Guid.NewGuid();
        var mapping = Mapping(projectId, original.FullPath!, @"C:\Shadow\Generator.dll");
        var context = WorkspaceWriteOperationContext.Verified(
            session,
            @"C:\Repro\App.sln",
            mapping,
            baseSolution,
            baseSolution,
            rawWorkspaceRevision: 1,
            shadowCopyEnabled: true);

        var preflight = WorkspaceWriteBoundary.Preflight(
            baseSolution,
            baseSolution,
            context,
            Freshness(session, mapping, baseSolution, revision: 2),
            new InProcessAnalyzerAssemblyLoader());
        Assert.False(preflight.Accepted);
        Assert.Equal(WorkspaceWriteBoundary.ReasonStaleBase, preflight.Reason);
    }

    [Fact]
    public void Stale_raw_snapshot_identity_is_rejected_even_when_revision_matches()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var original = new FakeAnalyzerReference("original", @"C:\Repro\Generator.dll");
        var first = workspace.CurrentSolution.AddProject(
            ProjectInfo.Create(projectId, VersionStamp.Create(), "Consumer", "Consumer", LanguageNames.CSharp)
                .WithAnalyzerReferences(new AnalyzerReference[] { original }));
        var later = first.WithProjectName(projectId, "Consumer2");
        var session = Guid.NewGuid();
        var mapping = Mapping(projectId, original.FullPath!, @"C:\Shadow\Generator.dll");
        var context = WorkspaceWriteOperationContext.Verified(
            session,
            @"C:\Repro\App.sln",
            mapping,
            first,
            first,
            rawWorkspaceRevision: 1,
            shadowCopyEnabled: true);

        var preflight = WorkspaceWriteBoundary.Preflight(
            later,
            later,
            context,
            Freshness(session, mapping, later, revision: 1),
            new InProcessAnalyzerAssemblyLoader());
        Assert.False(preflight.Accepted);
        Assert.Equal(WorkspaceWriteBoundary.ReasonStaleBase, preflight.Reason);
    }

    [Fact]
    public void Held_base_that_is_neither_current_published_nor_raw_is_rejected()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var original = new FakeAnalyzerReference("original", @"C:\Repro\Generator.dll");
        var raw = workspace.CurrentSolution.AddProject(
            ProjectInfo.Create(projectId, VersionStamp.Create(), "Consumer", "Consumer", LanguageNames.CSharp)
                .WithAnalyzerReferences(new AnalyzerReference[] { original }));
        var published = raw.WithProjectName(projectId, "Published");
        var held = raw.WithProjectName(projectId, "Held");
        var session = Guid.NewGuid();
        var mapping = Mapping(projectId, original.FullPath!, @"C:\Shadow\Generator.dll");
        var context = WorkspaceWriteOperationContext.Verified(
            session,
            @"C:\Repro\App.sln",
            mapping,
            published,
            raw,
            rawWorkspaceRevision: 2,
            shadowCopyEnabled: true);

        var preflight = WorkspaceWriteBoundary.Preflight(
            held,
            raw,
            context,
            new WorkspaceWriteFreshnessState(
                session,
                @"C:\Repro\App.sln",
                mapping,
                ShadowCopyEnabled: true,
                RawWorkspaceRevision: 2,
                RawWorkspaceSnapshot: raw,
                PublishedSnapshot: published,
                PublicationAdmission: SemanticPublicationAdmission.AllowedMapping),
            new InProcessAnalyzerAssemblyLoader(),
            heldBase: held);
        Assert.False(preflight.Accepted);
        Assert.Equal(WorkspaceWriteBoundary.ReasonStaleBase, preflight.Reason);
    }

    [Fact]
    public void Publication_admission_change_without_raw_mutation_is_rejected()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var original = new FakeAnalyzerReference("original", @"C:\Repro\Generator.dll");
        var baseSolution = workspace.CurrentSolution.AddProject(
            ProjectInfo.Create(projectId, VersionStamp.Create(), "Consumer", "Consumer", LanguageNames.CSharp)
                .WithAnalyzerReferences(new AnalyzerReference[] { original }));
        var session = Guid.NewGuid();
        var context = WorkspaceWriteOperationContext.Verified(
            session,
            @"C:\Repro\App.sln",
            mapping: null,
            baseSolution,
            baseSolution,
            rawWorkspaceRevision: 1,
            shadowCopyEnabled: false,
            publicationAdmission: SemanticPublicationAdmission.NoOverlay);

        var preflight = WorkspaceWriteBoundary.Preflight(
            baseSolution,
            baseSolution,
            context,
            new WorkspaceWriteFreshnessState(
                session,
                @"C:\Repro\App.sln",
                Mapping: null,
                ShadowCopyEnabled: false,
                RawWorkspaceRevision: 1,
                RawWorkspaceSnapshot: baseSolution,
                PublicationAdmission: SemanticPublicationAdmission.Banned),
            new InProcessAnalyzerAssemblyLoader());
        Assert.False(preflight.Accepted);
        Assert.Equal(WorkspaceWriteBoundary.ReasonStalePublication, preflight.Reason);
    }

    [Fact]
    public void Mapping_change_without_raw_mutation_is_rejected()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var original = new FakeAnalyzerReference("original", @"C:\Repro\Generator.dll");
        var baseSolution = workspace.CurrentSolution.AddProject(
            ProjectInfo.Create(projectId, VersionStamp.Create(), "Consumer", "Consumer", LanguageNames.CSharp)
                .WithAnalyzerReferences(new AnalyzerReference[] { original }));
        var session = Guid.NewGuid();
        var heldMapping = Mapping(projectId, original.FullPath!, @"C:\Shadow\Generator.dll");
        var currentMapping = Mapping(projectId, original.FullPath!, @"C:\Shadow\Generator-v2.dll");
        var context = WorkspaceWriteOperationContext.Verified(
            session,
            @"C:\Repro\App.sln",
            heldMapping,
            baseSolution,
            baseSolution,
            rawWorkspaceRevision: 1,
            shadowCopyEnabled: true);

        var preflight = WorkspaceWriteBoundary.Preflight(
            baseSolution,
            baseSolution,
            context,
            Freshness(session, currentMapping, baseSolution),
            new InProcessAnalyzerAssemblyLoader());
        Assert.False(preflight.Accepted);
        Assert.Equal(WorkspaceWriteBoundary.ReasonStalePublication, preflight.Reason);
    }

    [Fact]
    public void Unknown_operation_context_is_rejected_before_inverse()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var baseSolution = workspace.CurrentSolution.AddProject(
            ProjectInfo.Create(projectId, VersionStamp.Create(), "Consumer", "Consumer", LanguageNames.CSharp));
        var session = Guid.NewGuid();

        var missing = WorkspaceWriteBoundary.Preflight(
            baseSolution,
            baseSolution,
            operationContext: null,
            Freshness(session, mapping: null, baseSolution, shadowCopyEnabled: false),
            new InProcessAnalyzerAssemblyLoader());
        Assert.False(missing.Accepted);
        Assert.Equal(WorkspaceWriteBoundary.ReasonUnknownOperationContext, missing.Reason);

        var unverified = WorkspaceWriteBoundary.Preflight(
            baseSolution,
            baseSolution,
            WorkspaceWriteOperationContext.Unverified,
            Freshness(session, mapping: null, baseSolution, shadowCopyEnabled: false),
            new InProcessAnalyzerAssemblyLoader());
        Assert.False(unverified.Accepted);
        Assert.Equal(WorkspaceWriteBoundary.ReasonUnknownOperationContext, unverified.Reason);
    }

    [Fact]
    public void Fresh_context_is_accepted_with_and_without_overlay()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var original = new FakeAnalyzerReference("original", @"C:\Repro\Generator.dll");
        var shadow = new FakeAnalyzerReference("shadow", @"C:\Shadow\Generator.dll");
        var raw = workspace.CurrentSolution.AddProject(
            ProjectInfo.Create(projectId, VersionStamp.Create(), "Consumer", "Consumer", LanguageNames.CSharp)
                .WithAnalyzerReferences(new AnalyzerReference[] { original }));
        var session = Guid.NewGuid();

        var noOverlay = WorkspaceWriteOperationContext.Verified(
            session,
            @"C:\Repro\App.sln",
            mapping: null,
            raw,
            raw,
            rawWorkspaceRevision: 1,
            shadowCopyEnabled: false);
        var noOverlayPreflight = WorkspaceWriteBoundary.Preflight(
            raw,
            raw,
            noOverlay,
            Freshness(session, mapping: null, raw, shadowCopyEnabled: false),
            new InProcessAnalyzerAssemblyLoader());
        Assert.True(noOverlayPreflight.Accepted);
        Assert.Same(raw, noOverlayPreflight.CleanedCandidate);

        var mapping = Mapping(projectId, original.FullPath!, shadow.FullPath!);
        var overlayCandidate = raw.WithProjectAnalyzerReferences(projectId, new AnalyzerReference[] { shadow });
        var overlay = WorkspaceWriteOperationContext.Verified(
            session,
            @"C:\Repro\App.sln",
            mapping,
            overlayCandidate,
            raw,
            rawWorkspaceRevision: 1,
            shadowCopyEnabled: true);
        var overlayPreflight = WorkspaceWriteBoundary.Preflight(
            overlayCandidate,
            raw,
            overlay,
            Freshness(session, mapping, raw),
            new InProcessAnalyzerAssemblyLoader());
        Assert.True(overlayPreflight.Accepted);
        Assert.True(
            WorkspaceWriteBoundary.AnalyzerReferencesEquivalent(
                overlayPreflight.CleanedCandidate!.GetProject(projectId)!.AnalyzerReferences,
                raw.GetProject(projectId)!.AnalyzerReferences));
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

    private static WorkspaceWriteFreshnessState Freshness(
        Guid sessionId,
        AnalyzerShadowMapping? mapping,
        Solution raw,
        bool shadowCopyEnabled = true,
        long revision = 1)
    {
        return new WorkspaceWriteFreshnessState(
            sessionId,
            @"C:\Repro\App.sln",
            mapping,
            shadowCopyEnabled,
            revision,
            raw,
            PublicationAdmission: shadowCopyEnabled
                ? SemanticPublicationAdmission.AllowedMapping
                : SemanticPublicationAdmission.NoOverlay);
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
