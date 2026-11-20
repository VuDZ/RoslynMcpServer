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
            await RoundtripObservation.CaptureAsync(fixture.ProjectPath, directory, timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(directory, "shared.path"), fixture.SharedPath, timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(directory, "encoding.name"), encoding, timeout.Token);
            await RoundtripProcess.RunAsync(
                directory,
                typeof(CrossProcessRoundtripTests),
                nameof(Linked_edit_roundtrip_in_a_new_pid),
                timeout.Token);
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
            fixture.ProjectPath, RoundtripObservation.Properties(), originalAnalyzerPathsProven: true, timeout.Token);
        Assert.Null(capture.Snapshot);
        Assert.Equal(reason, capture.UnsupportedReason);
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
        await RoundtripProcess.WriteReceiptAsync(directory, cancellationToken);
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

    private static int Constant(CSharpCompilation compilation) =>
        (int)Assert.IsAssignableFrom<IFieldSymbol>(compilation.GetTypeByMetadataName("Shared")!.GetMembers("Value").Single()).ConstantValue!;

    private static string EvidenceDirectory(string name)
    {
        var directory = Path.Combine(RepositoryRoot.Find(), "TestResults", "task-06", name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
