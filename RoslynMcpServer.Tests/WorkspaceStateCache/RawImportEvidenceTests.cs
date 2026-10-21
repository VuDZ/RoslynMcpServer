using System.Text.Json;
using RoslynMcpServer.Diagnostics;
using Xunit;

namespace RoslynMcpServer.Tests.WorkspaceStateCache;

public sealed class RawImportEvidenceTests
{
    public RawImportEvidenceTests() => MsBuildBootstrapper.Register();

    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public void Owned_real_copy_accepts_a_single_configuration_build_tree(string configuration)
    {
        var root = Path.Combine(Path.GetTempPath(), "raw-import-single-configuration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var directory in new[] { "Config", "Diagnostics", "Hosting", "Plugins", "Properties", "Services", "Tools", "samples/RoslynMcpPlugin" })
            {
                Directory.CreateDirectory(Path.Combine(root, directory));
            }
            foreach (var name in new[] { "RoslynMcpServer.csproj", "Directory.Build.props", "Program.cs" })
            {
                File.WriteAllText(Path.Combine(root, name), name);
            }
            var generatedDirectory = Path.Combine(root, "obj", configuration, "net10.0");
            Directory.CreateDirectory(generatedDirectory);
            const string generatedSource = "// Existing generated input.";
            File.WriteAllText(Path.Combine(generatedDirectory, "RoslynMcpServer.AssemblyInfo.cs"), generatedSource);
            File.WriteAllText(Path.Combine(root, "obj", "project.assets.json"), "{}");
            var absentConfiguration = configuration == "Release" ? "Debug" : "Release";
            Assert.False(Directory.Exists(Path.Combine(root, "obj", absentConfiguration)));

            using var fixture = new RawImportEvidenceFixture(root);
            var copiedRoot = Path.Combine(fixture.DirectoryPath, "real");
            Assert.Equal(generatedSource, File.ReadAllText(
                Path.Combine(copiedRoot, "obj", configuration, "net10.0", "RoslynMcpServer.AssemblyInfo.cs")));
            Assert.Equal("{}", File.ReadAllText(Path.Combine(copiedRoot, "obj", "project.assets.json")));
            Assert.False(Directory.Exists(Path.Combine(copiedRoot, "obj", absentConfiguration)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Actual_none_channel_is_inspected_before_owned_fixture_cleanup()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var root = FindRoot();
        var output = Path.Combine(root, "TestResults", "task-11-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        using var fixture = new RawImportEvidenceFixture(root);
        await fixture.PrepareControlledAsync(timeout.Token);
        await RawImportEvidenceCapture.CaptureAsync(fixture.ControlledProjectPath,
            Path.Combine(output, "controlled"), timeout.Token);
        await RawImportEvidenceCapture.CaptureAsync(fixture.RealProjectPath,
            Path.Combine(output, "real"), timeout.Token);
        fixture.AssertRealBytesUnchanged(output);
        File.Copy(fixture.ExpectedInventoryPath, Path.Combine(output, "controlled-expected.json"));
        File.WriteAllText(Path.Combine(output, "owned-root.txt"), fixture.DirectoryPath);
        AssertWorkspaceSuccess(Path.Combine(output, "controlled"), requireSamplePlugin: false);
        AssertWorkspaceSuccess(Path.Combine(output, "real"), requireSamplePlugin: true);
        var controlled = ReadEvents(Path.Combine(output, "controlled"));
        var real = ReadEvents(Path.Combine(output, "real"));
        AssertControlledCategories(controlled, fixture);
        Assert.Equal([fixture.ControlledProjectPath], MissingImportConsumers(controlled));
        Assert.Equal([fixture.RealProjectPath], MissingImportConsumers(real));
        Assert.Equal(2, MissingImportConsumers(controlled.Where(item => Kind(item) != "ProjectImportedEventArgs")).Length);
        Assert.Equal(2, MissingImportConsumers(controlled.Where(item => Kind(item) != "ProjectEvaluationFinishedEventArgs")).Length);
        var rootOnly = controlled.Where(item => Kind(item) != "ProjectStartedEventArgs"
            || item.GetProperty("Values").GetProperty("ProjectFile").GetString() == fixture.ControlledProjectPath);
        Assert.Equal([fixture.ControlledProjectPath], MissingImportConsumers(rootOnly));
        await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
        {
            Channel = "partial", ManifestReadiness = "blocked", RealSupportedManifest = false,
            ControlledMissingImportConsumers = MissingImportConsumers(controlled),
            RealMissingImportConsumers = MissingImportConsumers(real),
            NegativeControls = new[] { "removed-import-events:unsupported", "removed-evaluation-binding:unsupported",
                "root-only-consumer-cannot-inherit-dependent-imports:unsupported" },
            ConsumedGenerationBinding = "unproven:P-004",
        }, new JsonSerializerOptions { WriteIndented = true }), timeout.Token);
    }

    private static void AssertWorkspaceSuccess(string directory, bool requireSamplePlugin)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "workspace.json")));
        Assert.Equal(2, document.RootElement.GetProperty("Projects").GetArrayLength());
        Assert.Empty(document.RootElement.GetProperty("Diagnostics").EnumerateArray());
        Assert.Equal(requireSamplePlugin, document.RootElement.GetProperty("SamplePluginSymbolFound").GetBoolean());
    }

    private static void AssertControlledCategories(JsonElement[] events, RawImportEvidenceFixture fixture)
    {
        var evaluation = Assert.Single(events, item => Kind(item) == "ProjectEvaluationFinishedEventArgs");
        var properties = evaluation.GetProperty("Values").GetProperty("Properties");
        Assert.Equal("Debug", properties.GetProperty("Configuration").GetString());
        Assert.Equal("AnyCPU", properties.GetProperty("Platform").GetString());
        Assert.Equal("net10.0", properties.GetProperty("TargetFramework").GetString());
        Assert.EndsWith(
            Path.Combine("intermediate", "Library", "project.assets.json"),
            properties.GetProperty("ProjectAssetsFile").GetString());
        Assert.EndsWith("locks/Library.lock.json", properties.GetProperty("NuGetLockFilePath").GetString());
        foreach (var suffix in new[] { "Directory.Build.props", "shared.props", "nested.props", "debug.targets", "external.targets" })
        {
            Assert.Contains(events, item => Kind(item) == "ProjectImportedEventArgs"
                && item.GetProperty("Values").GetProperty("ImportedProjectFile").GetString()?.EndsWith(suffix) == true
                && EvaluationKey(item) == EvaluationKey(evaluation));
        }
        Assert.Contains(events, item => Kind(item) == "ProjectImportedEventArgs"
            && item.GetProperty("Values").GetProperty("UnexpandedProject").GetString() == "../imports/absent.targets"
            && item.GetProperty("Values").GetProperty("ImportedProjectFile").ValueKind == JsonValueKind.Null);
        using var expected = JsonDocument.Parse(File.ReadAllText(fixture.ExpectedInventoryPath));
        foreach (var path in expected.RootElement.GetProperty("Assets").EnumerateArray()
            .Concat(expected.RootElement.GetProperty("Locks").EnumerateArray()))
        {
            Assert.True(File.Exists(Path.Combine(fixture.DirectoryPath, "controlled", path.GetString()!)),
                "A declared controlled restore input was not created.");
        }
        Assert.True(File.Exists(expected.RootElement.GetProperty("NuGetConfig").GetString()));
        var assets = events.Where(item => Kind(item) == "TaskParameterEventArgs"
            && item.GetProperty("Values").GetProperty("ItemType").GetString() == "ProjectAssetsFile").ToArray();
        Assert.Equal(2, assets.Length);
        foreach (var asset in assets)
        {
            var context = asset.GetProperty("Context");
            Assert.Contains(events, item => Kind(item) == "ProjectStartedEventArgs"
                && item.GetProperty("Context").GetProperty("ProjectContextId").GetInt32()
                    == context.GetProperty("ProjectContextId").GetInt32()
                && item.GetProperty("Context").GetProperty("SubmissionId").GetInt32()
                    == context.GetProperty("SubmissionId").GetInt32());
        }
        Assert.DoesNotContain(events.Where(item => Kind(item) == "ProjectEvaluationFinishedEventArgs"),
            item => item.GetProperty("Values").GetProperty("Properties").TryGetProperty("RestoreConfigFile", out _));
    }

    // A nested import's ProjectFile is its importer, not its consumer. Bind via the log-local evaluation identity.
    // This is a negative evidence check, not a reusable manifest adapter or a generation proof.
    private static string[] MissingImportConsumers(IEnumerable<JsonElement> events)
    {
        var inventory = events.ToArray();
        var bound = inventory.Where(item => Kind(item) == "ProjectEvaluationFinishedEventArgs"
            && inventory.Any(import => Kind(import) == "ProjectImportedEventArgs" && EvaluationKey(import) == EvaluationKey(item)))
            .Select(item => item.GetProperty("Values").GetProperty("ProjectFile").GetString()).ToHashSet();
        return inventory.Where(item => Kind(item) == "ProjectStartedEventArgs")
            .Select(item => item.GetProperty("Values").GetProperty("ProjectFile").GetString()!)
            .Distinct().Where(path => !bound.Contains(path)).Order().ToArray();
    }

    private static (int Node, int Submission, int Evaluation) EvaluationKey(JsonElement item)
    {
        var context = item.GetProperty("Context");
        return (context.GetProperty("NodeId").GetInt32(), context.GetProperty("SubmissionId").GetInt32(),
            context.GetProperty("EvaluationId").GetInt32());
    }

    private static string? Kind(JsonElement item) => item.GetProperty("Kind").GetString();

    private static JsonElement[] ReadEvents(string directory) => Directory.GetFiles(directory, "*.binlog.json")
        .SelectMany(path =>
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.GetProperty("Events").EnumerateArray().Select(item => item.Clone()).ToArray();
        }).ToArray();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RoslynMcpServer.csproj")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
