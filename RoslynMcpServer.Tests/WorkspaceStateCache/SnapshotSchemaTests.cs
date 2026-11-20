using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Workspace.StateCache.Models;
using Xunit;

namespace RoslynMcpServer.Tests.WorkspaceStateCache;

public sealed class SnapshotSchemaTests
{
    public SnapshotSchemaTests() => MsBuildBootstrapper.Register();

    [Fact]
    public async Task Analyzer_original_identity_requires_pre_overlay_source_witness()
    {
        using var host = new AdhocWorkspace();
        var result = await RealControlSchemaCapture.CaptureAsync(host.CurrentSolution, [], "control.csproj",
            new(new(null, null, null), new(null, null, null)), false, default);
        Assert.Null(result.Snapshot);
        Assert.Equal("original-analyzer-paths-unproven", result.UnsupportedReason);
    }

    [Fact]
    public void Duplicate_loaded_project_path_is_explicitly_unsupported()
    {
        var path = Path.GetFullPath("control.csproj");
        var exception = Assert.Throws<InvalidOperationException>(() => RealControlSchemaCapture.RequireUniqueProjects([path, path]));
        Assert.Equal("ambiguous-project-instance", exception.Message);
    }

    [Fact]
    public void Missing_option_factory_witness_is_explicitly_unsupported()
    {
        using var host = new AdhocWorkspace();
        var project = host.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(),
            "UnknownOptions", "UnknownOptions", LanguageNames.CSharp, filePath: Path.GetFullPath("control.csproj"),
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)));
        var exception = Assert.Throws<InvalidOperationException>(() => SnapshotOptionsCapture.Compilation(project));
        Assert.Equal("metadata-resolver-factory-witness-unavailable", exception.Message);
    }

    [Fact]
    public void Requested_absence_loader_globals_and_evaluated_defaults_are_separate_facts()
    {
        var properties = new SnapshotLoadProperties(new(null, null, null), new("Release", "AnyCPU", "net10.0"));
        var evaluated = new SnapshotEvaluatedProperties(new("Release", "AnyCPU", "net10.0"), "public-evaluation", []);
        Assert.Null(properties.RawRequested.Configuration);
        Assert.Equal("Release", properties.LoaderGlobals.Configuration);
        Assert.Equal("public-evaluation", evaluated.Source);
        Assert.NotEqual(new SnapshotProjectKey("control.csproj", "Debug", "AnyCPU", "net9.0"),
            new SnapshotProjectKey("control.csproj", "Debug", "AnyCPU", "net10.0"));
    }

    [Fact]
    public void Unwitnessed_parser_owned_state_refuses_instead_of_substituting_debug_defaults()
    {
        using var host = new AdhocWorkspace();
        var directory = Path.GetFullPath(".");
        var factory = CommandLineProject.CreateProjectInfo("OtherProfile", LanguageNames.CSharp,
            Array.Empty<string>(), directory, host);
        var options = CSharpCommandLineParser.Default.Parse(["/debug-"], directory, null).CompilationOptions
            .WithMetadataReferenceResolver(factory.CompilationOptions!.MetadataReferenceResolver);
        var project = host.AddProject(factory.WithFilePath(Path.Combine(directory, "control.csproj"))
            .WithCompilationOptions(options));
        var exception = Assert.Throws<InvalidOperationException>(() => SnapshotOptionsCapture.Compilation(project, options));
        Assert.Equal("compilation-option-witness-unavailable", exception.Message);
    }

    [Fact]
    public void Descriptor_tree_provider_without_a_public_factory_source_is_unsupported()
    {
        using var host = new AdhocWorkspace();
        var factory = CommandLineProject.CreateProjectInfo("UnknownTreeProvider", LanguageNames.CSharp,
            Array.Empty<string>(), Path.GetFullPath("."), host);
        var project = host.AddProject(factory.WithFilePath(Path.GetFullPath("control.csproj")));
        Assert.NotNull(project.CompilationOptions!.SyntaxTreeOptionsProvider);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SnapshotOptionsCapture.Compilation(project, project.CompilationOptions));
        Assert.Equal("custom-descriptor-tree-options-unsupported", exception.Message);
    }
}
