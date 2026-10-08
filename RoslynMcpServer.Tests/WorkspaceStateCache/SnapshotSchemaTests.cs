using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Workspace.StateCache.Models;
using Xunit;

namespace RoslynMcpServer.Tests.WorkspaceStateCache;

public sealed class SnapshotSchemaTests
{
    public SnapshotSchemaTests() => MsBuildBootstrapper.Register();

    [Fact]
    public async Task Real_ordinary_descriptors_fill_portable_schema_without_source_text_or_host_ids()
    {
        var root = FindRoot();
        var path = Path.Combine(root, "samples", "RoslynMcpPlugin", "RoslynMcpPlugin.csproj");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var host = new AdhocWorkspace();
        var descriptors = await new MSBuildProjectLoader(host).LoadProjectInfoAsync(path, cancellationToken: timeout.Token);
        host.AddProjects(descriptors);
        var properties = new SnapshotLoadProperties(new(null, null, null), new(null, null, null));
        var result = await RealControlSchemaCapture.CaptureAsync(host.CurrentSolution, descriptors, path,
            properties, originalAnalyzerPathsProven: true, timeout.Token);
        Assert.True(result.UnsupportedReason is null, result.UnsupportedReason);
        var snapshot = Assert.IsType<WorkspaceSnapshot>(result.Snapshot);
        Assert.Equal(1, snapshot.SchemaVersion);
        Assert.Equal(2, snapshot.Projects.Length);
        Assert.Null(snapshot.Properties.RawRequested.Configuration);
        Assert.Null(snapshot.Properties.LoaderGlobals.Configuration);
        Assert.All(snapshot.Projects, project =>
        {
            Assert.Equal("Debug", project.EvaluatedProperties.Values.Configuration);
            Assert.Equal("net10.0", project.Key.TargetFramework);
            Assert.Empty(project.EvaluatedProperties.UnknownProperties);
            Assert.NotEmpty(project.MetadataReferences);
            Assert.NotEmpty(project.AnalyzerReferences);
            Assert.All(project.MetadataReferences, reference => Assert.Equal("H-001-external-xml-excluded", reference.BinaryDocumentationPolicy));
            Assert.All(project.AnalyzerReferences, reference => Assert.Equal("ordinary-pre-overlay-public-loader-descriptor", reference.IdentitySource));
        });
        var plugin = snapshot.Projects.Single(project => project.Name == "RoslynMcpPlugin");
        var server = snapshot.Projects.Single(project => project.Name == "RoslynMcpServer");
        Assert.Equal(server.Key, Assert.Single(plugin.ProjectReferences).Target);
        Assert.Empty(server.ProjectReferences);
        Assert.Equal(descriptors.Sum(info => info.Documents.Count), snapshot.Projects.Sum(project => project.Documents.Count(document => document.Kind == "source")));
        Assert.Equal(descriptors.Sum(info => info.MetadataReferences.Count), snapshot.Projects.Sum(project => project.MetadataReferences.Length));
        Assert.Contains(snapshot.InputPaths, input => input.Occurrences.Any(occurrence => occurrence.Role == "Unknown"));
        foreach (var project in snapshot.Projects)
        {
            foreach (var document in project.Documents)
            {
                Assert.NotEmpty(document.TextMetadata.Checksum);
                var occurrence = Assert.Single(snapshot.InputPaths.Single(input => input.Path == document.Path).Occurrences,
                    occurrence => occurrence.Owner == project.Key && occurrence.DocumentKind == document.Kind);
                Assert.Equal(document.IsGenerated, occurrence.Role == "Generated");
            }
        }
        Assert.Contains(snapshot.InputPaths, input => input.Occurrences.Count(occurrence => occurrence.DocumentKind == "analyzer-config") > 1);
        CheckLinkedRolesAndProducerAmbiguity(snapshot, root);
        var json = JsonSerializer.Serialize(snapshot);
        foreach (var info in descriptors)
        {
            Assert.DoesNotContain(info.Id.Id.ToString(), json);
            Assert.All(info.Documents, document => Assert.DoesNotContain(document.Id.Id.ToString(), json));
        }
        Assert.DoesNotContain("public sealed class SamplePlugin", json);
        var directory = Path.Combine(root, "TestResults", "task-02");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "schema-observation.json"),
            JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }), timeout.Token);
    }

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

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RoslynMcpServer.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static void CheckLinkedRolesAndProducerAmbiguity(WorkspaceSnapshot snapshot, string root)
    {
        var first = snapshot.Projects[0];
        var second = snapshot.Projects[1];
        var linked = first.Documents.First(document => document.Kind == "source") with
        {
            Path = Path.Combine(root, "shared-input.cs"),
            IsGenerated = false,
        };
        var binaryPath = Path.Combine(root, "shared-output.dll");
        first = first with
        {
            OutputFilePath = binaryPath,
            OutputRefFilePath = null,
            Documents = [linked, linked with { Kind = "additional" }],
            MetadataReferences = [new(binaryPath, "Assembly", ["alias"], true, "H-001-external-xml-excluded")],
            ProjectReferences = [new(second.Key, [], false)],
        };
        second = second with { OutputFilePath = binaryPath, OutputRefFilePath = null, Documents = [linked] };
        var paths = RealControlSchemaCapture.CaptureInputPaths([first, second]);
        var occurrences = paths.Single(input => input.Path == linked.Path).Occurrences;
        Assert.Equal(3, occurrences.Length);
        Assert.Equal(2, occurrences.Select(occurrence => occurrence.Owner).Distinct().Count());
        Assert.Contains(occurrences, occurrence => occurrence.Owner == first.Key && occurrence.Role == "AdditionalFile");
        Assert.All(paths.Single(input => input.Path == binaryPath).Occurrences
            .Where(occurrence => occurrence.Role == "MetadataDependency"), occurrence => Assert.Null(occurrence.Producer));

        // A unique matching output is still insufficient without the actual directed binding.
        first = first with { OutputFilePath = null, ProjectReferences = [] };
        Assert.Null(Assert.Single(RealControlSchemaCapture.CaptureInputPaths([first, second])
            .Single(input => input.Path == binaryPath).Occurrences,
            occurrence => occurrence.Role == "MetadataDependency").Producer);
        first = first with { ProjectReferences = [new(second.Key, [], false)] };
        Assert.Equal(second.Key, Assert.Single(RealControlSchemaCapture.CaptureInputPaths([first, second])
            .Single(input => input.Path == binaryPath).Occurrences,
            occurrence => occurrence.Role == "MetadataDependency").Producer);
    }
}
