using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Workspace.StateCache;
using RoslynMcpServer.Services.Workspace.StateCache.Models;
using RoslynMcpServer.Tests.SourceStructure;
using Xunit;

namespace RoslynMcpServer.Tests.WorkspaceStateCache;

public sealed class CrossProcessRoundtripTests
{
    public CrossProcessRoundtripTests() => MsBuildBootstrapper.Register();

    [Fact]
    public async Task Real_control_roundtrip_in_a_new_pid()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var handoff = Environment.GetEnvironmentVariable(RoundtripProcess.DirectoryVariable);
        if (handoff is not null)
        {
            await RestoreAndCheckAsync(handoff, timeout.Token);
            return;
        }
        var root = RepositoryRoot.Find();
        var directory = Path.Combine(root, "TestResults", "task-06", "real-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(root, "samples", "RoslynMcpPlugin", "RoslynMcpPlugin.csproj");
        await CaptureAsync(path, directory, timeout.Token);
        await RoundtripProcess.RunAsync(directory, nameof(Real_control_roundtrip_in_a_new_pid), timeout.Token);
    }

    [Fact]
    public async Task Linked_edit_roundtrip_in_a_new_pid()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var handoff = Environment.GetEnvironmentVariable(RoundtripProcess.DirectoryVariable);
        if (handoff is not null)
        {
            await EditAndCheckAsync(handoff, timeout.Token);
            return;
        }
        foreach (var encoding in new[] { "utf8", "utf8-bom", "utf16-bom" })
        {
            using var fixture = new RoundtripFixture();
            await fixture.PrepareAsync(encoding, timeout.Token);
            var directory = EvidenceDirectory("edit-" + encoding);
            await CaptureAsync(fixture.ProjectPath, directory, timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(directory, "shared.path"), fixture.SharedPath, timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(directory, "encoding.name"), encoding, timeout.Token);
            await RoundtripProcess.RunAsync(directory, nameof(Linked_edit_roundtrip_in_a_new_pid), timeout.Token);
        }
    }

    [Theory]
    [InlineData("generator", "generator-dependent-scope-unsupported")]
    [InlineData("multitarget", "ambiguous-project-instance")]
    [InlineData("signing", "signing-dependent")]
    [InlineData("unknown", "signing-state-unknown")]
    public async Task Ordinary_unsupported_controls_refuse_without_a_snapshot(string control, string reason)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var fixture = new RoundtripFixture();
        await fixture.PrepareAsync("utf8", timeout.Token);
        if (control == "generator")
        {
            fixture.WriteShared("[System.Text.Json.Serialization.JsonSerializable(typeof(string))]\n"
                + "public partial class GeneratedContext : System.Text.Json.Serialization.JsonSerializerContext { }\n",
                new UTF8Encoding(false));
        }
        else if (control == "multitarget")
        {
            // The selected inner build is still ambiguous for this single-target capture profile.
            var xml = await File.ReadAllTextAsync(fixture.ProjectPath, timeout.Token);
            await File.WriteAllTextAsync(fixture.ProjectPath,
                xml.Replace("<TargetFramework>net10.0</TargetFramework>",
                    "<TargetFrameworks>net10.0;net10.0-windows</TargetFrameworks>", StringComparison.Ordinal), timeout.Token);
        }
        else
        {
            fixture.WriteShared(control == "signing"
                ? "[assembly: System.Reflection.AssemblyKeyFile(\"missing.snk\")]\npublic class Shared { }\n"
                : "[assembly: MissingAttribute]\npublic class Shared { }\n", new UTF8Encoding(false));
        }
        if (control is "generator" or "multitarget")
        {
            await fixture.BuildAsync(timeout.Token);
        }
        MsBuildBootstrapper.Register();
        using var workspace = new AdhocWorkspace();
        var descriptors = await new MSBuildProjectLoader(workspace).LoadProjectInfoAsync(
            fixture.ProjectPath, cancellationToken: timeout.Token);
        workspace.AddProjects(descriptors);
        var capture = await OrdinaryWorkspaceCapture.CaptureAsync(workspace.CurrentSolution, descriptors,
            fixture.ProjectPath, Properties(), originalAnalyzerPathsProven: true, timeout.Token);
        Assert.Null(capture.Snapshot);
        Assert.Equal(reason, capture.UnsupportedReason);
    }

    private static async Task CaptureAsync(string path, string directory, CancellationToken cancellationToken)
    {
        MsBuildBootstrapper.Register();
        using var workspace = new AdhocWorkspace();
        var descriptors = await new MSBuildProjectLoader(workspace).LoadProjectInfoAsync(
            path, cancellationToken: cancellationToken);
        workspace.AddProjects(descriptors);
        var capture = await OrdinaryWorkspaceCapture.CaptureAsync(workspace.CurrentSolution, descriptors,
            path, Properties(), originalAnalyzerPathsProven: true, cancellationToken);
        Assert.Null(capture.UnsupportedReason);
        var snapshot = Assert.IsType<WorkspaceSnapshot>(capture.Snapshot);
        Assert.Equal(Properties(), snapshot.Properties);
        Assert.All(snapshot.Projects, project =>
        {
            Assert.Equal("Debug", project.EvaluatedProperties.Values.Configuration);
            Assert.Equal("net10.0", project.EvaluatedProperties.Values.TargetFramework);
            Assert.Empty(project.EvaluatedProperties.UnknownProperties);
        });
        await File.WriteAllBytesAsync(Path.Combine(directory, "snapshot.bin"),
            WorkspaceSnapshotCodec.Encode(snapshot), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "ordinary.json"),
            await RoundtripObservation.ReadAsync(workspace.CurrentSolution, cancellationToken), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "ordinary.ids"),
            string.Join("\n", workspace.CurrentSolution.ProjectIds.Select(id => id.Id)
                .Concat(workspace.CurrentSolution.Projects.SelectMany(AllDocuments).Select(document => document.Id.Id))),
            cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "parent.pid"), Environment.ProcessId.ToString(), cancellationToken);
    }

    private static async Task RestoreAndCheckAsync(string directory, CancellationToken cancellationToken)
    {
        var snapshot = WorkspaceSnapshotCodec.Decode(await File.ReadAllBytesAsync(
            Path.Combine(directory, "snapshot.bin"), cancellationToken));
        Assert.Equal(Properties(), snapshot.Properties);
        using var session = await new IsolatedWorkspaceHydrator().HydrateAsync(snapshot, cancellationToken);
        Assert.Empty(session.Workspace.CurrentSolution.ProjectIds);
        var actual = await RoundtripObservation.ReadAsync(session.Solution, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "hydrated.json"), actual, cancellationToken);
        Assert.Equal(await File.ReadAllTextAsync(Path.Combine(directory, "ordinary.json"), cancellationToken), actual);
        var oldIds = (await File.ReadAllLinesAsync(Path.Combine(directory, "ordinary.ids"), cancellationToken)).ToHashSet();
        Assert.All(session.Solution.ProjectIds, id => Assert.DoesNotContain(id.Id.ToString(), oldIds));
        Assert.All(session.Solution.Projects.SelectMany(AllDocuments),
            document => Assert.DoesNotContain(document.Id.Id.ToString(), oldIds));
        RoundtripObservation.AssertBindings(snapshot, session);
        var plugin = session.BaseCompilations[session.Projects.Single(pair =>
            pair.Key.ProjectPath.EndsWith("RoslynMcpPlugin.csproj", StringComparison.Ordinal)).Value];
        var sample = Assert.IsAssignableFrom<INamedTypeSymbol>(plugin.GetTypeByMetadataName("RoslynMcpPlugin.SamplePlugin"));
        Assert.Contains(sample.AllInterfaces, symbol => symbol.ToDisplayString() == "RoslynMcpServer.Plugins.IRoslynMcpPlugin"
            && symbol.ContainingAssembly.Name == "RoslynMcpServer");
        Assert.EndsWith(Path.Combine("RoslynMcpPlugin", "SamplePlugin.cs"), Assert.Single(sample.Locations).SourceTree!.FilePath);
        var external = Assert.IsAssignableFrom<INamedTypeSymbol>(plugin.GetTypeByMetadataName("Newtonsoft.Json.Linq.JToken"));
        Assert.Equal("", external.GetDocumentationCommentXml(cancellationToken: cancellationToken));
        Assert.DoesNotContain(plugin.GetDiagnostics(cancellationToken), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var server = session.BaseCompilations[session.Projects.Single(pair =>
            Path.GetFileName(pair.Key.ProjectPath) == "RoslynMcpServer.csproj").Value];
        Assert.Contains("Portable graph facts only", server
            .GetTypeByMetadataName("RoslynMcpServer.Services.Workspace.StateCache.Models.WorkspaceSnapshot")!
            .GetDocumentationCommentXml(cancellationToken: cancellationToken), StringComparison.Ordinal);
        Assert.Equal(NullableContextOptions.Enable, plugin.Options.NullableContextOptions);
        Assert.Equal(OptimizationLevel.Debug, plugin.Options.OptimizationLevel);
        AssertExclusions(plugin);
        await WriteReceiptAsync(directory, cancellationToken);
    }

    private static async Task EditAndCheckAsync(string directory, CancellationToken cancellationToken)
    {
        var snapshot = WorkspaceSnapshotCodec.Decode(await File.ReadAllBytesAsync(
            Path.Combine(directory, "snapshot.bin"), cancellationToken));
        var path = await File.ReadAllTextAsync(Path.Combine(directory, "shared.path"), cancellationToken);
        var encoding = RoundtripFixture.EncodingFor(await File.ReadAllTextAsync(
            Path.Combine(directory, "encoding.name"), cancellationToken));
        using (var session = await new IsolatedWorkspaceHydrator().HydrateAsync(snapshot, cancellationToken))
        {
            Assert.Equal(await File.ReadAllTextAsync(Path.Combine(directory, "ordinary.json"), cancellationToken),
                await RoundtripObservation.ReadAsync(session.Solution, cancellationToken));
            RoundtripObservation.AssertBindings(snapshot, session);
            await ApplyEditAsync(session, path, encoding, cancellationToken);
        }
        using var restored = await new IsolatedWorkspaceHydrator().HydrateAsync(snapshot, cancellationToken);
        RoundtripObservation.AssertBindings(snapshot, restored);
        await AssertEditedAsync(restored, path, encoding, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "edited.json"),
            await RoundtripObservation.ReadAsync(restored.Solution, cancellationToken), cancellationToken);
        await AssertCurrentSourceRefusalsAsync(snapshot, path, encoding, directory, cancellationToken);
        await WriteReceiptAsync(directory, cancellationToken);
    }

    private static async Task ApplyEditAsync(
        IsolatedHydratedSession session,
        string path,
        Encoding encoding,
        CancellationToken cancellationToken)
    {
        var sources = session.InputMap[path].Where(occurrence =>
            session.Solution.GetDocument(occurrence.DocumentId!) is not null).ToArray();
        Assert.Equal(2, sources.Length);
        Assert.Equal(3, session.InputMap[path].Length);
        var original = await session.Solution.GetDocument(sources[0].DocumentId!)!.GetTextAsync(cancellationToken);
        Assert.Equal(encoding.CodePage, original.Encoding!.CodePage);
        Assert.Equal(encoding.GetPreamble(), original.Encoding.GetPreamble());
        var edited = original.WithChanges(new TextChange(
            new TextSpan(original.ToString().IndexOf("Value = 7", StringComparison.Ordinal), "Value = 7".Length), "Value = 19"));
        var candidate = session.Solution;
        foreach (var source in sources)
        {
            candidate = candidate.WithDocumentText(source.DocumentId!, edited);
            var compilation = session.BaseCompilations[source.Owner];
            var oldTree = compilation.SyntaxTrees.Single(tree => tree.FilePath == path);
            var newTree = CSharpSyntaxTree.ParseText(edited, (CSharpParseOptions)oldTree.Options, path);
            var updated = compilation.ReplaceSyntaxTree(oldTree, newTree);
            Assert.Null(SnapshotSigningProfile.UnsupportedReason(updated));
            Assert.Equal(19, Constant(updated));
        }
        foreach (var source in sources)
        {
            Assert.Equal(edited.ToString(), (await candidate.GetDocument(source.DocumentId!)!
                .GetTextAsync(cancellationToken)).ToString());
        }
        // Isolated persistence writes one existing physical file using its loaded encoding policy.
        // It does not invoke or replace the production write boundary.
        await File.WriteAllBytesAsync(path,
            [.. edited.Encoding!.GetPreamble(), .. edited.Encoding.GetBytes(edited.ToString())], cancellationToken);
        Assert.Equal(encoding.GetPreamble().Concat(encoding.GetBytes(edited.ToString())), await File.ReadAllBytesAsync(path, cancellationToken));
    }

    private static async Task AssertEditedAsync(
        IsolatedHydratedSession restored,
        string path,
        Encoding encoding,
        CancellationToken cancellationToken)
    {
        foreach (var compilation in restored.BaseCompilations.Values)
        {
            Assert.True(compilation.Options.CheckOverflow);
            Assert.Equal(19, Constant(compilation));
            Assert.DoesNotContain(compilation.GetDiagnostics(cancellationToken), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }
        foreach (var occurrence in restored.InputMap[path])
        {
            var project = restored.Solution.GetProject(occurrence.Owner)!;
            var document = project.Documents.Cast<TextDocument>().Concat(project.AdditionalDocuments)
                .Single(document => document.Id == occurrence.DocumentId);
            var text = await document.GetTextAsync(cancellationToken);
            Assert.Equal(encoding.CodePage, text.Encoding!.CodePage);
            Assert.Equal(encoding.GetPreamble(), text.Encoding.GetPreamble());
            Assert.Contains("Value = 19", text.ToString(), StringComparison.Ordinal);
            Assert.Contains("Grüße 世界", text.ToString(), StringComparison.Ordinal);
        }
    }

    private static async Task AssertCurrentSourceRefusalsAsync(
        WorkspaceSnapshot snapshot,
        string path,
        Encoding encoding,
        string directory,
        CancellationToken cancellationToken)
    {
        var acceptedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var failures = new List<string>();
        try
        {
            foreach (var (attribute, reason) in new[]
            {
                ("System.Reflection.AssemblyKeyFile(\"missing.snk\")", "signing-dependent"),
                ("MissingAttribute", "signing-state-unknown"),
            })
            {
                var source = "[assembly: " + attribute + "]\npublic class Shared { }\n";
                await File.WriteAllBytesAsync(path, [.. encoding.GetPreamble(), .. encoding.GetBytes(source)], cancellationToken);
                var disposed = 0;
                var images = new List<Metadata>();
                var hydrator = new IsolatedWorkspaceHydrator(
                    workspaceDisposed: _ => disposed++, metadataCreated: images.Add);
                var failure = await Assert.ThrowsAsync<InvalidDataException>(() => hydrator.HydrateAsync(snapshot, cancellationToken));
                Assert.Contains("reason=" + reason + " route=refusal", failure.Message, StringComparison.Ordinal);
                Assert.Contains("stage=hydrate", failure.Message, StringComparison.Ordinal);
                Assert.Contains("configuration=Debug tfm=net10.0", failure.Message, StringComparison.Ordinal);
                Assert.Equal(1, disposed);
                Assert.NotEmpty(images);
                Assert.All(images.OfType<AssemblyMetadata>(), image => Assert.Throws<ObjectDisposedException>(() => image.GetModules()));
                failures.Add(failure.Message);
            }
        }
        finally
        {
            await File.WriteAllBytesAsync(path, acceptedBytes, CancellationToken.None);
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "refusals.json"), JsonSerializer.Serialize(failures), cancellationToken);
    }

    private static void AssertExclusions(CSharpCompilation compilation)
    {
        foreach (var operation in new[] { "metadata-documentation", "documentation-analyzer", "emit", "sign", "unknown-write" })
        {
            var executed = false;
            var logs = new List<string>();
            var reason = CapabilityOperationGate.Execute(compilation, operation, true, () => executed = true, null, logs.Add);
            Assert.False(executed);
            Assert.Contains(operation.StartsWith("metadata", StringComparison.Ordinal) || operation == "documentation-analyzer"
                ? "excluded-metadata-documentation" : operation == "unknown-write" ? "signing-state-unknown" : "excluded-emit-signing", reason);
            Assert.Contains("route=refusal", Assert.Single(logs), StringComparison.Ordinal);
        }
    }

    private static int Constant(CSharpCompilation compilation) =>
        (int)Assert.IsAssignableFrom<IFieldSymbol>(compilation.GetTypeByMetadataName("Shared")!.GetMembers("Value").Single()).ConstantValue!;

    private static SnapshotLoadProperties Properties() => new(new(null, null, null), new(null, null, null));

    private static IEnumerable<TextDocument> AllDocuments(Project project) =>
        project.Documents.Cast<TextDocument>().Concat(project.AdditionalDocuments).Concat(project.AnalyzerConfigDocuments);

    private static string EvidenceDirectory(string name)
    {
        var directory = Path.Combine(RepositoryRoot.Find(), "TestResults", "task-06", name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static Task WriteReceiptAsync(string directory, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(Path.Combine(directory, "child.pid"), Environment.ProcessId.ToString(), cancellationToken);
}
