using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Workspace;
using RoslynMcpServer.Tests.SourceStructure;
using Xunit;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

/// <summary>
/// Synthetic evidence demonstrates the isolated host and writer capabilities. It does not establish
/// the production SDK dependency closure; that requires the separate observed-evidence experiment.
/// </summary>
public sealed class HydrateHostExperimentTests
{
    public HydrateHostExperimentTests() => MsBuildBootstrapper.Register();

    [Fact]
    public async Task Two_project_value_round_trip_preserves_options_references_and_semantics()
    {
        using var fixture = await Fixture.CreateAsync();
        var host = new HydrateHostExperiment();
        var snapshot = await fixture.CaptureAsync(host);
        var payload = SemanticSnapshotCodec.Encode(snapshot);
        var decoded = SemanticSnapshotCodec.Decode(payload);
        Assert.Equal(payload, SemanticSnapshotCodec.Encode(decoded));
        using var hydrated = await host.HydrateAsync(decoded, fixture.Evidence, fixture.Artifacts, default);
        Assert.True(hydrated.AnalyzerAdmissionReady);
        Assert.Equal(2, hydrated.Solution.ProjectIds.Count);
        Assert.Single(hydrated.Solution.Projects.SelectMany(project => project.ProjectReferences));
        Assert.DoesNotContain(hydrated.Solution.ProjectIds, id => fixture.OriginalIds.Contains(id));
        Assert.Equal(2, hydrated.Solution.GetDocumentIdsWithFilePath(fixture.SharedPath).Length);
        foreach (var project in hydrated.Solution.Projects)
        {
            var parse = Assert.IsType<CSharpParseOptions>(project.ParseOptions);
            Assert.Equal(LanguageVersion.CSharp14, parse.SpecifiedLanguageVersion);
            Assert.Equal(new[] { "FIXTURE" }, parse.PreprocessorSymbolNames);
            var options = Assert.IsType<CSharpCompilationOptions>(project.CompilationOptions);
            Assert.Equal(NullableContextOptions.Enable, options.NullableContextOptions);
            Assert.True(options.Deterministic);
            Assert.True(options.AllowUnsafe);
            Assert.Equal(typeof(object).Assembly.Location,
                Assert.IsAssignableFrom<PortableExecutableReference>(Assert.Single(project.MetadataReferences)).FilePath);
        }
        var secondSnapshot = await host.CaptureAsync(hydrated, fixture.Evidence, default);
        Assert.Equal(payload, SemanticSnapshotCodec.Encode(secondSnapshot));
        await AssertSemanticsAsync(hydrated.Solution, 7);
        Assert.True(hydrated.Dtb.Complete);
        Assert.Equal(0, hydrated.Dtb.OpenInvocations);
        Assert.Equal(0, hydrated.Dtb.ProjectStarts);
        Assert.Equal(0, hydrated.Dtb.CoreCompileTargets);
        Assert.Equal(0, hydrated.Dtb.DesignTimeContexts);
        fixture.AssertProjectBytesUnchanged();
    }

    [Fact]
    public async Task Sdk_glob_edit_add_rename_remove_build_and_keep_project_bytes()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.BuildAsync();
        var host = new HydrateHostExperiment();
        var snapshot = await fixture.CaptureAsync(host);
        using var session = await host.HydrateAsync(snapshot, fixture.Evidence, fixture.Artifacts, default);
        var current = session.Solution;
        var candidate = WithTextAtPath(current, fixture.LibraryPath, LibrarySource(11), fixture.Encoding);
        await AssertSuccessAsync(host, session, current, candidate, fixture.LibraryPath);
        await AssertSemanticsAsync(session.Solution, 11);
        await fixture.BuildAsync();

        var newPath = Path.Combine(fixture.LibraryDirectory, "Added.cs");
        current = session.Solution;
        var library = current.Projects.Single(project => project.Name == "Library");
        var newId = DocumentId.CreateNewId(library.Id);
        candidate = current.AddDocument(newId, "Added.cs", SourceText.From("public class Added { }", fixture.Encoding), filePath: newPath);
        await AssertSuccessAsync(host, session, current, candidate, newPath);
        Assert.True(File.Exists(newPath));
        await fixture.BuildAsync();

        var renamedPath = Path.Combine(fixture.LibraryDirectory, "Renamed.cs");
        current = session.Solution;
        var renamedId = DocumentId.CreateNewId(library.Id);
        candidate = current.RemoveDocument(newId).AddDocument(
            renamedId, "Renamed.cs", SourceText.From("public class Added { }", fixture.Encoding), filePath: renamedPath);
        var rename = await host.ApplyAsync(session, current, candidate, session.WriteContext, default);
        Assert.Equal(WorkspaceWriteStatus.FullSuccess, rename.Status);
        Assert.Equal(new[] { newPath, renamedPath }.Order(), rename.SavedPaths.Order());
        Assert.False(File.Exists(newPath));
        Assert.True(File.Exists(renamedPath));
        await fixture.BuildAsync();

        current = session.Solution;
        await AssertSuccessAsync(host, session, current, current.RemoveDocument(renamedId), renamedPath);
        Assert.False(File.Exists(renamedPath));
        await fixture.BuildAsync();
        fixture.AssertProjectBytesUnchanged();
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf8-bom")]
    [InlineData("utf16-bom")]
    public async Task Linked_edit_preserves_exact_encoding_bytes_and_every_membership(string encodingName)
    {
        using var fixture = await Fixture.CreateAsync(encodingName);
        var host = new HydrateHostExperiment();
        var snapshot = await fixture.CaptureAsync(host);
        using var session = await host.HydrateAsync(snapshot, fixture.Evidence, fixture.Artifacts, default);
        const string changed = "namespace Shared; public static class Link { public const string Text = \"Grüße 世界\"; }";
        var heldBase = session.Solution;
        var candidate = WithTextAtPath(heldBase, fixture.SharedPath, changed, fixture.Encoding);
        await AssertSuccessAsync(host, session, heldBase, candidate, fixture.SharedPath);
        Assert.Equal(Bytes(changed, fixture.Encoding), await File.ReadAllBytesAsync(fixture.SharedPath));
        foreach (var id in session.Solution.GetDocumentIdsWithFilePath(fixture.SharedPath))
        {
            Assert.Equal(changed, (await session.Solution.GetDocument(id)!.GetTextAsync()).ToString());
        }
        await AssertSemanticsAsync(session.Solution, 7);
        await fixture.BuildAsync();
        fixture.AssertProjectBytesUnchanged();
    }

    [Fact]
    public async Task Null_stale_session_stale_base_and_unknown_analyzer_refuse_without_bytes()
    {
        using var fixture = await Fixture.CreateAsync();
        var host = new HydrateHostExperiment();
        var snapshot = await fixture.CaptureAsync(host);
        using var session = await host.HydrateAsync(snapshot, fixture.Evidence, fixture.Artifacts, default);
        var originalBytes = await File.ReadAllBytesAsync(fixture.LibraryPath);
        var heldBase = session.Solution;
        var candidate = WithTextAtPath(heldBase, fixture.LibraryPath, LibrarySource(13), fixture.Encoding);
        await AssertRefusedAsync(host, session, heldBase, candidate, null);
        var foreign = WorkspaceWriteOperationContext.Verified(
            Guid.NewGuid(), session.Request.WorkspacePath, null, heldBase, heldBase, 0, false);
        await AssertRefusedAsync(host, session, heldBase, candidate, foreign);
        var arbitraryBase = heldBase.WithProjectAssemblyName(heldBase.ProjectIds[0], "Different");
        await AssertRefusedAsync(host, session, arbitraryBase, candidate, session.WriteContext);
        var unknownAnalyzer = candidate.AddAnalyzerReference(
            heldBase.ProjectIds[0], new UnknownAnalyzerReference(Path.Combine(fixture.Root, "unknown.dll")));
        await AssertRefusedAsync(host, session, heldBase, unknownAnalyzer, session.WriteContext);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(fixture.LibraryPath));
        Assert.Same(heldBase, session.Solution);
        fixture.AssertProjectBytesUnchanged();
    }

    [Fact]
    public async Task Reusing_context_after_publication_refuses_before_writing()
    {
        using var fixture = await Fixture.CreateAsync();
        var host = new HydrateHostExperiment();
        var snapshot = await fixture.CaptureAsync(host);
        using var session = await host.HydrateAsync(snapshot, fixture.Evidence, fixture.Artifacts, default);
        var original = session.Solution;
        var oldContext = session.WriteContext;
        await AssertSuccessAsync(host, session, original,
            WithTextAtPath(original, fixture.LibraryPath, LibrarySource(17), fixture.Encoding), fixture.LibraryPath);
        var saved = await File.ReadAllBytesAsync(fixture.LibraryPath);
        await AssertRefusedAsync(host, session, original,
            WithTextAtPath(original, fixture.LibraryPath, LibrarySource(23), fixture.Encoding), oldContext);
        Assert.Equal(saved, await File.ReadAllBytesAsync(fixture.LibraryPath));
    }

    [Fact]
    public async Task Explicit_compile_without_default_items_refuses_before_write()
    {
        using var fixture = await Fixture.CreateAsync(explicitCompile: true);
        var host = new HydrateHostExperiment();
        var snapshot = await fixture.CaptureAsync(host);
        using var session = await host.HydrateAsync(snapshot, fixture.Evidence, fixture.Artifacts, default);
        var bytes = await File.ReadAllBytesAsync(fixture.LibraryPath);
        var heldBase = session.Solution;
        await AssertRefusedAsync(host, session, heldBase,
            WithTextAtPath(heldBase, fixture.LibraryPath, LibrarySource(19), fixture.Encoding), session.WriteContext);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(fixture.LibraryPath));
        fixture.AssertProjectBytesUnchanged();
    }

    [Fact]
    public async Task Second_file_io_failure_reports_only_committed_first_path()
    {
        using var fixture = await Fixture.CreateAsync();
        var host = new HydrateHostExperiment();
        var snapshot = await fixture.CaptureAsync(host);
        using var session = await host.HydrateAsync(snapshot, fixture.Evidence, fixture.Artifacts, default);
        var heldBase = session.Solution;
        var paths = new[] { fixture.LibraryPath, fixture.SharedPath }.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var candidate = WithTextAtPath(heldBase, paths[0], "public class FirstChanged { }", fixture.Encoding);
        candidate = WithTextAtPath(candidate, paths[1], "public class SecondChanged { }", fixture.Encoding);
        var secondBytes = await File.ReadAllBytesAsync(paths[1]);
        using var blocked = new FileStream(paths[1], FileMode.Open, FileAccess.Read, FileShare.Read);
        var result = await host.ApplyAsync(session, heldBase, candidate, session.WriteContext, default);
        Assert.Equal(WorkspaceWriteStatus.PartialPersistence, result.Status);
        Assert.Equal(new[] { paths[0] }, result.SavedPaths);
        Assert.Equal(Bytes("public class FirstChanged { }", fixture.Encoding), await File.ReadAllBytesAsync(paths[0]));
        Assert.Equal(secondBytes, await File.ReadAllBytesAsync(paths[1]));
        fixture.AssertProjectBytesUnchanged();
    }

    [Fact]
    public async Task Cancellation_before_first_write_reports_no_paths()
    {
        using var fixture = await Fixture.CreateAsync();
        var host = new HydrateHostExperiment();
        var snapshot = await fixture.CaptureAsync(host);
        using var session = await host.HydrateAsync(snapshot, fixture.Evidence, fixture.Artifacts, default);
        var before = await File.ReadAllBytesAsync(fixture.LibraryPath);
        var heldBase = session.Solution;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await host.ApplyAsync(session, heldBase,
            WithTextAtPath(heldBase, fixture.LibraryPath, LibrarySource(31), fixture.Encoding),
            session.WriteContext, cancellation.Token);
        Assert.Equal(WorkspaceWriteStatus.Cancelled, result.Status);
        Assert.Empty(result.SavedPaths);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.LibraryPath));
    }

    [Fact]
    public async Task Cancellation_after_first_write_reports_committed_path_and_unapplied_state()
    {
        using var fixture = await Fixture.CreateAsync();
        var host = new HydrateHostExperiment();
        var snapshot = await fixture.CaptureAsync(host);
        using var session = await host.HydrateAsync(snapshot, fixture.Evidence, fixture.Artifacts, default);
        var heldBase = session.Solution;
        var firstText = LibrarySource(37);
        var candidate = WithTextAtPath(heldBase, fixture.LibraryPath, firstText, fixture.Encoding);
        // The large second payload leaves a bounded observation window after the first file closes.
        // Cancellation is tied to actual committed bytes rather than to an arbitrary wall-clock delay.
        candidate = WithTextAtPath(candidate, fixture.SharedPath,
            "/*" + new string('x', 64 * 1024 * 1024) + "*/ public class SecondPayload { }", fixture.Encoding);
        using var cancellation = new CancellationTokenSource();
        using var observationStop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var expectedBytes = Bytes(firstText, fixture.Encoding);
        var observed = ObserveCommittedWriteAsync(fixture.LibraryPath, expectedBytes, cancellation, observationStop.Token);
        var result = await host.ApplyAsync(session, heldBase, candidate, session.WriteContext, cancellation.Token);
        observationStop.Cancel();
        await observed;
        Assert.True(cancellation.IsCancellationRequested, "The committed-write observer did not cancel the operation.");
        Assert.Equal(WorkspaceWriteStatus.Cancelled, result.Status);
        Assert.Contains(fixture.LibraryPath, result.SavedPaths);
        Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(fixture.LibraryPath));
        Assert.True(result.UnappliedProjectState);
        fixture.AssertProjectBytesUnchanged();
    }

    [Fact]
    public async Task Unspecified_encoding_reads_but_refuses_writer_guessing()
    {
        using var fixture = await Fixture.CreateAsync();
        var host = new HydrateHostExperiment();
        var snapshot = await fixture.CaptureAsync(host);
        var policy = new EncodingPolicy(null, [], "unspecified", null, "unspecified", null);
        snapshot = snapshot with
        {
            Projects = snapshot.Projects.Select(project => project with
            {
                Documents = project.Documents.Select(document => document.Path == fixture.LibraryPath
                    ? document with { Encoding = policy } : document).ToImmutableArray(),
            }).ToImmutableArray(),
        };
        using var session = await host.HydrateAsync(snapshot, fixture.Evidence, fixture.Artifacts, default);
        var id = session.Solution.GetDocumentIdsWithFilePath(fixture.LibraryPath).Single();
        Assert.Null((await session.Solution.GetDocument(id)!.GetTextAsync()).Encoding);
        var bytes = await File.ReadAllBytesAsync(fixture.LibraryPath);
        var heldBase = session.Solution;
        await AssertRefusedAsync(host, session, heldBase,
            heldBase.WithDocumentText(id, SourceText.From(LibrarySource(41))), session.WriteContext);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(fixture.LibraryPath));
    }

    [Fact]
    public async Task Reconciliation_updates_all_roles_from_disk_without_persistence_or_dtb()
    {
        using var fixture = await Fixture.CreateAsync(includeOtherRoles: true);
        var host = new HydrateHostExperiment();
        var snapshot = await fixture.CaptureAsync(host);
        using var session = await host.HydrateAsync(snapshot, fixture.Evidence, fixture.Artifacts, default);
        var before = session.Solution;
        const string source = "namespace Shared; public static class Link { public const string Text = \"external\"; }";
        const string additional = "external payload 世界";
        const string config = "root = true\n[*.cs]\ndotnet_diagnostic.CS0219.severity = error\n";
        const string generated = "public static class GeneratedOnDisk { public const int Number = 29; }";
        await File.WriteAllBytesAsync(fixture.SharedPath, Bytes(source, fixture.Encoding));
        await File.WriteAllBytesAsync(fixture.AdditionalPath, Bytes(additional, fixture.Encoding));
        await File.WriteAllBytesAsync(fixture.ConfigPath, Bytes(config, fixture.Encoding));
        await File.WriteAllBytesAsync(fixture.GeneratedPath, Bytes(generated, fixture.Encoding));
        var paths = ImmutableArray.Create(fixture.SharedPath, fixture.AdditionalPath, fixture.ConfigPath, fixture.GeneratedPath);
        var hashes = paths.ToDictionary(path => path, path => Hash(File.ReadAllBytes(path)));
        await host.ReconcileAsync(session, paths, default);
        Assert.NotSame(before, session.Solution);
        Assert.Equal(2, session.Solution.GetDocumentIdsWithFilePath(fixture.SharedPath).Length);
        foreach (var project in session.Solution.Projects)
        {
            Assert.Equal(additional, (await project.AdditionalDocuments.Single().GetTextAsync()).ToString());
            Assert.Equal(config, (await project.AnalyzerConfigDocuments.Single().GetTextAsync()).ToString());
        }
        Assert.Equal(source, (await session.Solution.GetDocument(
            session.Solution.GetDocumentIdsWithFilePath(fixture.SharedPath)[0])!.GetTextAsync()).ToString());
        Assert.Equal(generated, (await session.Solution.GetDocument(
            session.Solution.GetDocumentIdsWithFilePath(fixture.GeneratedPath).Single())!.GetTextAsync()).ToString());
        await AssertSemanticsAsync(session.Solution, 7);
        var compilation = await session.Solution.Projects.Single(project => project.Name == "Library").GetCompilationAsync();
        Assert.Equal(29, Assert.IsAssignableFrom<IFieldSymbol>(
            compilation!.GetTypeByMetadataName("GeneratedOnDisk")!.GetMembers("Number").Single()).ConstantValue);
        foreach (var path in paths)
        {
            Assert.Equal(hashes[path], Hash(File.ReadAllBytes(path)));
        }
        Assert.Equal(0, session.Dtb.OpenInvocations);
        Assert.Equal(0, session.Dtb.ProjectStarts);
        await fixture.BuildAsync();
        fixture.AssertProjectBytesUnchanged();
    }

    [Fact]
    public async Task Failed_reconciliation_keeps_old_published_snapshot()
    {
        using var fixture = await Fixture.CreateAsync();
        var host = new HydrateHostExperiment();
        var snapshot = await fixture.CaptureAsync(host);
        using var session = await host.HydrateAsync(snapshot, fixture.Evidence, fixture.Artifacts, default);
        var before = session.Solution;
        using var blocked = new FileStream(fixture.LibraryPath, FileMode.Open, FileAccess.Read, FileShare.None);
        await Assert.ThrowsAnyAsync<IOException>(() => host.ReconcileAsync(session, [fixture.LibraryPath], default));
        Assert.Same(before, session.Solution);
        Assert.True(File.Exists(fixture.LibraryPath));
    }

    [Fact]
    public async Task Deleted_linked_source_reconciliation_retains_old_snapshot_without_recreating_disk_file()
    {
        using var fixture = await Fixture.CreateAsync();
        var host = new HydrateHostExperiment();
        var snapshot = await fixture.CaptureAsync(host);
        using var session = await host.HydrateAsync(snapshot, fixture.Evidence, fixture.Artifacts, default);
        var before = session.Solution;
        File.Delete(fixture.SharedPath);
        await Assert.ThrowsAsync<FileNotFoundException>(() => host.ReconcileAsync(session, [fixture.SharedPath], default));
        Assert.Same(before, session.Solution);
        Assert.Equal(2, session.Solution.GetDocumentIdsWithFilePath(fixture.SharedPath).Length);
        Assert.False(File.Exists(fixture.SharedPath));
        await AssertSemanticsAsync(session.Solution, 7);
        fixture.AssertProjectBytesUnchanged();
    }

    [Theory]
    [InlineData("empty-dto")]
    [InlineData("removed-dependency")]
    [InlineData("extra-analyzer")]
    [InlineData("incomplete-inventory")]
    public async Task Independent_analyzer_inventory_tampering_refuses_before_delegate(string mutation)
    {
        using var fixture = await Fixture.CreateAsync();
        var host = new HydrateHostExperiment();
        var snapshot = await fixture.CaptureAsync(host);
        var owner = snapshot.Projects[0].Key;
        var analyzerPath = Path.Combine(fixture.Root, "synthetic-analyzer.dll");
        var dependencyPath = Path.Combine(fixture.Root, "synthetic-dependency.dll");
        await File.WriteAllBytesAsync(analyzerPath, "not executable fixture bytes"u8.ToArray());
        await File.WriteAllBytesAsync(dependencyPath, "dependency fixture bytes"u8.ToArray());
        var analyzer = new AnalyzerEntry(owner, analyzerPath, Hash(File.ReadAllBytes(analyzerPath)), fixture.Root, [dependencyPath]);
        snapshot = snapshot with { Projects = snapshot.Projects.SetItem(0, snapshot.Projects[0] with { Analyzers = [analyzer] }) };
        var evidence = fixture.Evidence with
        {
            OverlayReady = true,
            Entries = fixture.Evidence.Entries.Add(Fixture.EvidenceFor("analyzer", owner, analyzerPath))
                .Add(Fixture.EvidenceFor("analyzer-dependency", owner, dependencyPath)),
        };
        snapshot = mutation switch
        {
            "empty-dto" => snapshot with { Projects = snapshot.Projects.SetItem(0, snapshot.Projects[0] with { Analyzers = [] }) },
            "removed-dependency" => snapshot with
            {
                Projects = snapshot.Projects.SetItem(0, snapshot.Projects[0] with { Analyzers = [analyzer with { DependencyPaths = [] }] }),
            },
            "extra-analyzer" => snapshot with
            {
                Projects = snapshot.Projects.SetItem(1, snapshot.Projects[1] with
                {
                    Analyzers = [analyzer with { Consumer = snapshot.Projects[1].Key }],
                }),
            },
            _ => snapshot,
        };
        if (mutation == "incomplete-inventory")
        {
            evidence = evidence with { AnalyzerInventoryComplete = false };
        }
        var calls = 0;
        var admissionHost = new HydrateHostExperiment((_, _, _, _, _) =>
        {
            calls++;
            throw new InvalidOperationException("An unverified inventory must never reach analyzer admission.");
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => admissionHost.HydrateAsync(
            SemanticSnapshotCodec.Decode(SemanticSnapshotCodec.Encode(snapshot)), evidence, fixture.Artifacts, default));
        Assert.Equal(0, calls);
        fixture.AssertProjectBytesUnchanged();
    }

    [Theory]
    [InlineData("unknown-schema")]
    [InlineData("extra-key")]
    [InlineData("missing-key")]
    [InlineData("duplicate-key")]
    [InlineData("invalid-enum")]
    [InlineData("relative-path")]
    [InlineData("unknown-option")]
    [InlineData("duplicate-membership")]
    public async Task Codec_rejects_malformed_value_contract(string mutation)
    {
        using var fixture = await Fixture.CreateAsync();
        var snapshot = await fixture.CaptureAsync(new HydrateHostExperiment());
        var payload = SemanticSnapshotCodec.Encode(snapshot);
        var json = Encoding.UTF8.GetString(payload);
        if (mutation == "unknown-schema")
        {
            json = json.Replace("e0-semantic-1", "future-semantic", StringComparison.Ordinal);
        }
        else if (mutation is "extra-key" or "duplicate-key")
        {
            var property = mutation == "extra-key" ? "\"Extra\":true," : "\"Schema\":\"e0-semantic-1\",";
            json = "{" + property + json[1..];
        }
        else if (mutation == "missing-key")
        {
            using var document = JsonDocument.Parse(json);
            json = "{" + string.Join(",", document.RootElement.EnumerateObject()
                .Where(property => !property.Name.Equals("Schema", StringComparison.OrdinalIgnoreCase))
                .Select(property => JsonSerializer.Serialize(property.Name) + ":" + property.Value.GetRawText())) + "}";
        }
        else
        {
            var project = snapshot.Projects[0];
            snapshot = mutation switch
            {
                "invalid-enum" => snapshot with
                {
                    Projects = snapshot.Projects.SetItem(0, project with
                    {
                        Documents = project.Documents.SetItem(0, project.Documents[0] with { SourceKind = (SourceCodeKind)99 }),
                    }),
                },
                "relative-path" => snapshot with { Request = snapshot.Request with { WorkspacePath = "relative.csproj" } },
                "unknown-option" => snapshot with
                {
                    Projects = snapshot.Projects.SetItem(0, project with { ParseOptions = project.ParseOptions.Add("Unknown", "true") }),
                },
                "duplicate-membership" => snapshot with
                {
                    Projects = snapshot.Projects.SetItem(0, project with { Documents = project.Documents.Add(project.Documents[0]) }),
                },
                _ => snapshot,
            };
            Assert.Throws<InvalidDataException>(() => SemanticSnapshotCodec.Encode(snapshot));
            return;
        }
        Assert.Throws<InvalidDataException>(() => SemanticSnapshotCodec.Decode(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void Codec_rejects_payload_exceeding_experiment_budget()
    {
        Assert.Throws<InvalidDataException>(() => SemanticSnapshotCodec.Decode(new byte[16 * 1024 * 1024 + 1]));
    }

    [Fact]
    public async Task Codec_encode_stops_at_payload_budget_for_valid_individual_values()
    {
        using var fixture = await Fixture.CreateAsync();
        var snapshot = await fixture.CaptureAsync(new HydrateHostExperiment());
        var project = snapshot.Projects[0];
        var document = project.Documents[0];
        var memberships = Enumerable.Range(0, 520).Select(index => document with
        {
            Folders = [new string('x', 32700) + index.ToString(System.Globalization.CultureInfo.InvariantCulture)],
        }).ToImmutableArray();
        snapshot = snapshot with { Projects = snapshot.Projects.SetItem(0, project with { Documents = memberships }) };
        Assert.Throws<InvalidDataException>(() => SemanticSnapshotCodec.Encode(snapshot));
    }

    [Fact]
    public async Task Capture_refuses_stale_source_text_against_current_independent_bytes()
    {
        using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.CaptureAsync(new HydrateHostExperiment(), staleSource: true));
    }

    [Theory]
    [InlineData("metadata-documentation-provider", "metadata documentation provider is not publicly observable")]
    [InlineData("strong-name-provider", "DesktopStrongNameProvider search paths have no public getter")]
    public async Task Unobservable_public_api_option_values_are_explicit_capture_refusals(string policy, string reason)
    {
        using var fixture = await Fixture.CreateAsync();
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => fixture.CaptureAsync(
            new HydrateHostExperiment(), unobservablePolicy: policy));
        Assert.Contains(reason, exception.Message, StringComparison.Ordinal);
        fixture.AssertProjectBytesUnchanged();
    }

    [Fact]
    public async Task External_linked_add_remove_refuse_before_disk_or_project_changes()
    {
        using var fixture = await Fixture.CreateAsync();
        var host = new HydrateHostExperiment();
        var snapshot = await fixture.CaptureAsync(host);
        using var session = await host.HydrateAsync(snapshot, fixture.Evidence, fixture.Artifacts, default);
        var heldBase = session.Solution;
        var app = heldBase.Projects.Single(project => project.Name == "App");
        var externalPath = Path.Combine(fixture.Root, "External.cs");
        var candidate = heldBase.AddDocument(DocumentId.CreateNewId(app.Id), "External.cs",
            SourceText.From("class External { }", fixture.Encoding), filePath: externalPath);
        await AssertRefusedAsync(host, session, heldBase, candidate, session.WriteContext);
        Assert.False(File.Exists(externalPath));
        var original = await File.ReadAllBytesAsync(fixture.SharedPath);
        var linked = app.Documents.Single(document => document.FilePath == fixture.SharedPath);
        await AssertRefusedAsync(host, session, heldBase, heldBase.RemoveDocument(linked.Id), session.WriteContext);
        Assert.Equal(original, await File.ReadAllBytesAsync(fixture.SharedPath));
        Assert.Same(heldBase, session.Solution);
        fixture.AssertProjectBytesUnchanged();
    }

    [Fact]
    public async Task Lossy_encoder_fallback_refuses_before_first_byte()
    {
        using var fixture = await Fixture.CreateAsync("ascii");
        var host = new HydrateHostExperiment();
        var snapshot = await fixture.CaptureAsync(host);
        using var session = await host.HydrateAsync(snapshot, fixture.Evidence, fixture.Artifacts, default);
        var bytes = await File.ReadAllBytesAsync(fixture.LibraryPath);
        var heldBase = session.Solution;
        var candidate = WithTextAtPath(heldBase, fixture.LibraryPath,
            "namespace Library; public static class Value { public const string Text = \"世界\"; }", fixture.Encoding);
        await AssertRefusedAsync(host, session, heldBase, candidate, session.WriteContext);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(fixture.LibraryPath));
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("category")]
    [InlineData("absence")]
    [InlineData("owner")]
    [InlineData("empty-region-grammar")]
    [InlineData("empty-full-graph")]
    public async Task Malformed_admission_contract_refuses_before_materialization(string mutation)
    {
        using var fixture = await Fixture.CreateAsync();
        var host = new HydrateHostExperiment();
        var snapshot = await fixture.CaptureAsync(host);
        var evidence = fixture.Evidence;
        evidence = mutation switch
        {
            "profile" => evidence with { ProfileVersion = "unrecognized-profile" },
            "category" => evidence with
            {
                Entries = evidence.Entries.SetItem(0, evidence.Entries[0] with { Category = "unrecognized-category" }),
            },
            "absence" => evidence with
            {
                Entries = evidence.Entries.SetItem(0, evidence.Entries[0] with { IsAbsent = true, Sha256 = null }),
            },
            "owner" => evidence with
            {
                Entries = evidence.Entries.SetItem(0, evidence.Entries[0] with
                {
                    Owner = snapshot.Projects[0].Key with { ProjectPath = Path.Combine(fixture.Root, "Unknown.csproj") },
                }),
            },
            "empty-region-grammar" => evidence with
            {
                Regions = evidence.Regions.Add(new RegionEntry(
                    snapshot.Projects[0].Key, Path.GetDirectoryName(snapshot.Projects[0].Key.ProjectPath)!, "negative",
                    ["missing/**bad/*.txt"], [], [], Hash("e0-region-1\n"u8.ToArray()), fixture.InventoryPath)),
            },
            "empty-full-graph" => evidence with
            {
                Entries = [], Regions = [], ExpectedInstances = [], ExpectedEdges = [], ExpectedCompilerInputPaths = [],
            },
            _ => evidence,
        };
        if (mutation == "empty-full-graph")
        {
            snapshot = snapshot with { Projects = [], ProjectReferences = [] };
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => host.HydrateAsync(snapshot, evidence, fixture.Artifacts, default));
    }

    [Theory]
    [InlineData("changed-bytes")]
    [InlineData("new-member")]
    [InlineData("escaping-glob")]
    [InlineData("unresolved-glob")]
    [InlineData("absent-now-present")]
    [InlineData("depth-bound")]
    public async Task Disk_and_membership_evidence_changes_refuse_hydration(string mutation)
    {
        using var fixture = await Fixture.CreateAsync();
        var host = new HydrateHostExperiment();
        var snapshot = await fixture.CaptureAsync(host);
        var evidence = fixture.Evidence;
        if (mutation == "changed-bytes")
        {
            await File.WriteAllTextAsync(fixture.LibraryPath, LibrarySource(97), fixture.Encoding);
        }
        else if (mutation == "new-member")
        {
            await File.WriteAllTextAsync(Path.Combine(fixture.LibraryDirectory, "New.cs"), "class New { }", fixture.Encoding);
        }
        else if (mutation is "escaping-glob" or "unresolved-glob")
        {
            evidence = evidence with
            {
                Regions = evidence.Regions.SetItem(0, evidence.Regions[0] with
                {
                    Includes = [mutation == "escaping-glob" ? "../**/*.cs" : "$(Unknown)/**/*.cs"],
                }),
            };
        }
        else if (mutation == "depth-bound")
        {
            var directory = fixture.LibraryDirectory;
            for (var level = 0; level < 65; level++)
            {
                directory = Path.Combine(directory, "d");
            }
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "Deep.cs"), "class Deep { }", fixture.Encoding);
        }
        else
        {
            var absentPath = Path.Combine(fixture.Root, "Local.props");
            evidence = evidence with
            {
                Entries = evidence.Entries.Add(new EvidenceEntry(
                    "known-absent", null, absentPath, null, true, "synthetic-fixture", "e0-1", fixture.InventoryPath)),
            };
            await File.WriteAllTextAsync(absentPath, "<Project />", fixture.Encoding);
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => host.HydrateAsync(snapshot, evidence, fixture.Artifacts, default));
    }

    [Fact]
    public async Task Ordinary_open_negative_witness_observes_open_and_raw_logger_artifact()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.BuildAsync();
        var host = new HydrateHostExperiment();
        using var ordinary = await host.OpenFreshAsync(fixture.Request, fixture.Artifacts, default);
        Assert.Equal(1, ordinary.Dtb.OpenInvocations);
        Assert.NotEmpty(ordinary.Solution.ProjectIds);
        Assert.True(ordinary.Dtb.EvidencePaths.Length > 0,
            "Ordinary open logger has no raw artifact. Counter: " + JsonSerializer.Serialize(ordinary.Dtb)
            + "\nWorkspace diagnostics:\n" + string.Join("\n", ordinary.LoadDiagnostics.Select(diagnostic => diagnostic.ToString())));
        Assert.All(ordinary.Dtb.EvidencePaths, path => Assert.True(File.Exists(path), path));
        Assert.True(ordinary.Dtb.ProjectStarts > 0, "The negative witness must observe build events, not only the runner invocation.");
        Assert.True(ordinary.Dtb.CoreCompileTargets > 0, "The ordinary design-time open must observe CoreCompile target events.");
        Assert.True(ordinary.Dtb.Complete, JsonSerializer.Serialize(ordinary.Dtb));
        Assert.True(ordinary.Dtb.DesignTimeContexts > 0, "The complete ordinary stage must retain observed project context identities.");
        var originalPaths = OriginalBinlogs(ordinary.Dtb);
        Assert.NotEmpty(originalPaths);
        Assert.Equal(originalPaths.Length,
            ordinary.Dtb.EvidencePaths.Count(path => Path.GetFileName(path).StartsWith("counter-replay-", StringComparison.Ordinal)));
        var originalHashes = originalPaths.ToDictionary(path => path, path => Hash(File.ReadAllBytes(path)));
        var counter = ReplayCounter(originalPaths[0]);
        try
        {
            // This stage re-observes the raw logs from the actual owned Open above. The invocation
            // association is a witnessed fact; replay itself does not invoke Open or count another build.
            counter.RecordOpenInvocation();
            counter.ReplayRawBinlogs(originalPaths, default);
            var replayed = counter.Snapshot();
            Assert.True(replayed.Complete, JsonSerializer.Serialize(replayed));
            Assert.Equal(ordinary.Dtb.ProjectStarts, replayed.ProjectStarts);
            Assert.Equal(ordinary.Dtb.CoreCompileTargets, replayed.CoreCompileTargets);
            Assert.Equal(ordinary.Dtb.DesignTimeContexts, replayed.DesignTimeContexts);
            Assert.Equal(originalPaths.Length * 2, replayed.EvidencePaths.Length);
            foreach (var original in originalPaths)
            {
                Assert.Equal(originalHashes[original], Hash(File.ReadAllBytes(original)));
                Assert.Contains(original, replayed.EvidencePaths);
            }
            Assert.All(replayed.EvidencePaths.Except(originalPaths), path =>
            {
                Assert.True(File.Exists(path), path);
                Assert.DoesNotContain(path, ordinary.Dtb.EvidencePaths);
            });
            var countsBeforeDuplicate = replayed;
            Assert.Throws<InvalidOperationException>(() => counter.ReplayRawBinlogs(originalPaths, default));
            var duplicate = counter.Snapshot();
            Assert.False(duplicate.Complete);
            Assert.Null(duplicate.DesignTimeContexts);
            Assert.Equal(countsBeforeDuplicate.ProjectStarts, duplicate.ProjectStarts);
            Assert.Equal(countsBeforeDuplicate.CoreCompileTargets, duplicate.CoreCompileTargets);
            counter.Shutdown();
            Assert.False(counter.Snapshot().Complete);
        }
        finally
        {
            counter.Shutdown();
        }
    }

    [Fact]
    public void Unattached_counter_never_reports_complete_zero_design_time_contexts()
    {
        var counter = new DtbEventCounter();
        var unattached = counter.Snapshot();
        Assert.False(unattached.Complete);
        Assert.Null(unattached.DesignTimeContexts);
        Assert.Empty(unattached.EvidencePaths);
        counter.RecordOpenInvocation();
        counter.Shutdown();
        var missingCoverage = counter.Snapshot();
        Assert.Equal(1, missingCoverage.OpenInvocations);
        Assert.False(missingCoverage.Complete);
        Assert.Null(missingCoverage.DesignTimeContexts);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("corrupt")]
    [InlineData("cancelled")]
    [InlineData("duplicate-input")]
    public async Task Failed_raw_binlog_replay_keeps_coverage_unknown_and_cancellation_token(string failure)
    {
        using var fixture = await Fixture.CreateAsync();
        var rawPath = Path.Combine(fixture.Artifacts, "corrupt-original.binlog");
        await File.WriteAllBytesAsync(rawPath, "This is deliberately corrupt binary log fixture data."u8.ToArray());
        var rawHash = Hash(File.ReadAllBytes(rawPath));
        var counter = ReplayCounter(rawPath);
        counter.RecordOpenInvocation();
        using var cancellation = new CancellationTokenSource();
        if (failure == "cancelled")
        {
            cancellation.Cancel();
        }
        ImmutableArray<string> inputs = failure switch
        {
            "empty" => [],
            "duplicate-input" => [rawPath, rawPath],
            _ => [rawPath],
        };
        try
        {
            var exception = Record.Exception(() => counter.ReplayRawBinlogs(inputs, cancellation.Token));
            Assert.NotNull(exception);
            if (failure == "cancelled")
            {
                Assert.Equal(cancellation.Token, Assert.IsAssignableFrom<OperationCanceledException>(exception).CancellationToken);
            }
            else
            {
                Assert.False(exception is OperationCanceledException,
                    "A non-cancellation replay failure must not manufacture cancellation.");
            }
            var failed = counter.Snapshot();
            Assert.Equal(1, failed.OpenInvocations);
            Assert.False(failed.Complete);
            Assert.Null(failed.DesignTimeContexts);
            Assert.Equal(rawHash, Hash(File.ReadAllBytes(rawPath)));
            Assert.Throws<InvalidOperationException>(() => counter.ReplayRawBinlogs([rawPath], default));
            Assert.Throws<InvalidOperationException>(() => counter.MarkHydrateClosedPath());
            counter.Shutdown();
            var afterShutdown = counter.Snapshot();
            Assert.False(afterShutdown.Complete);
            Assert.Null(afterShutdown.DesignTimeContexts);
        }
        finally
        {
            counter.Shutdown();
        }
    }

    [Fact]
    public void Hydrate_transitive_calls_have_no_open_or_process_with_exact_external_admission_limit()
    {
        var repository = RepositoryRoot.Find();
        var scope = SourceSetScope.ProductionRepository(repository);
        var experimentDirectory = Path.Combine(repository, "RoslynMcpServer.Tests", "WorkspaceLoadCache");
        foreach (var path in Directory.EnumerateFiles(experimentDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) != nameof(HydrateHostExperimentTests) + ".cs"))
        {
            scope = scope.WithFile(SourceFile.ReadFromDisk(path));
        }
        var analysis = SourceSetAnalysis.Create(scope);
        Assert.True(analysis.IsUsable, string.Join("\n", analysis.InputDiagnostics));
        var diagnostics = new List<string>();
        var host = analysis.FindType("RoslynMcpServer.Tests.WorkspaceLoadCache", "HydrateHostExperiment", diagnostics);
        Assert.NotNull(host);
        var hydrate = analysis.FindDeclaredMethod(host, "HydrateAsync", diagnostics);
        Assert.NotNull(hydrate);
        var createBase = analysis.FindDeclaredMethod(host, "CreateBaseSessionAsync", diagnostics);
        Assert.NotNull(createBase);
        var admit = analysis.FindDeclaredMethod(host, "AdmitAnalyzersAsync", diagnostics);
        Assert.NotNull(admit);
        var admitSyntax = admit.DeclaringSyntaxReferences.Single().GetSyntax();
        var delegateCall = admitSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Single(invocation =>
            analysis.ModelFor(invocation.SyntaxTree).GetSymbolInfo(invocation).Symbol is IMethodSymbol
            { MethodKind: MethodKind.DelegateInvoke });
        var expectedLimit = "`analyzerAdmission`, reached from the invoked delegate `_analyzerAdmission` at "
            + SourceSetAnalysis.Describe(delegateCall)
            + ", has no assignment inside the declared scope; targets registered outside the scope are not followed";
        Assert.Empty(diagnostics);
        foreach (var name in new[] { "OpenProjectAsync", "OpenSolutionAsync", "Start" })
        {
            var result = ReachabilityAnalysis.FindPath(analysis, hydrate, name, symbol =>
                symbol.Name == name && symbol.ContainingType.ToDisplayString() is
                    "Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace" or "System.Diagnostics.Process");
            Assert.False(result.IsReachable, result.DescribePath());
            Assert.Empty(result.Diagnostics);
            // The injected delegate is deliberately outside this scope. The empty-inventory
            // behavioral witness closes that branch; arbitrary injected implementations remain unproved.
            var limit = Assert.Single(result.Limits);
            Assert.Equal(ReachabilityLimitKind.TargetOutsideTheScope, limit.Kind);
            Assert.Equal(expectedLimit, limit.Text);
            var closedResult = ReachabilityAnalysis.FindPath(analysis, createBase, name, symbol =>
                symbol.Name == name && symbol.ContainingType.ToDisplayString() is
                    "Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace" or "System.Diagnostics.Process");
            Assert.True(closedResult.IsDecided,
                string.Join("\n", closedResult.Diagnostics.Concat(closedResult.Limits.Select(limitValue => limitValue.Text))));
            Assert.False(closedResult.IsReachable, closedResult.DescribePath());
        }
    }

    private static async Task AssertSuccessAsync(
        HydrateHostExperiment host, ExperimentSession session, Solution heldBase, Solution candidate, string expectedPath)
    {
        var result = await host.ApplyAsync(session, heldBase, candidate, session.WriteContext, default);
        Assert.Equal(WorkspaceWriteStatus.FullSuccess, result.Status);
        Assert.Equal(new[] { expectedPath }, result.SavedPaths);
        Assert.True(result.WorkspaceApplied);
    }

    private static ImmutableArray<string> OriginalBinlogs(DtbMeasurement measurement) =>
        measurement.EvidencePaths.Where(path => !Path.GetFileName(path).StartsWith("counter-replay-", StringComparison.Ordinal))
            .ToImmutableArray();

    private static DtbEventCounter ReplayCounter(string rawPath)
    {
        var counter = new DtbEventCounter
        {
            Parameters = Path.Combine(Path.GetDirectoryName(rawPath)!, "replay-stage-owned.binlog") + ";ProjectImports=None",
        };
        Assert.IsType<Microsoft.Build.Logging.BinaryLogger>(counter.CreateRemoteBinaryLogger());
        return counter;
    }

    private static async Task ObserveCommittedWriteAsync(
        string path, byte[] expectedBytes, CancellationTokenSource cancellation, CancellationToken stopToken)
    {
        while (!stopToken.IsCancellationRequested)
        {
            try
            {
                var matches = false;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (stream.Length == expectedBytes.Length)
                    {
                        var actual = new byte[expectedBytes.Length];
                        stream.ReadExactly(actual);
                        matches = actual.AsSpan().SequenceEqual(expectedBytes);
                    }
                }
                if (matches)
                {
                    using (var closedWriterWitness = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        // Successfully opening exclusively proves the first writer released its handle.
                    }
                    cancellation.Cancel();
                    return;
                }
            }
            catch (IOException)
            {
                // Exclusive read is possible only after the writer has closed its committed file.
            }
            await Task.Delay(1);
        }
    }

    private static async Task AssertRefusedAsync(
        HydrateHostExperiment host, ExperimentSession session, Solution heldBase, Solution candidate,
        WorkspaceWriteOperationContext? context)
    {
        var result = await host.ApplyAsync(session, heldBase, candidate, context, default);
        Assert.Equal(WorkspaceWriteStatus.PreflightRejected, result.Status);
        Assert.Empty(result.SavedPaths);
        Assert.False(result.WorkspaceApplied);
    }

    private static async Task AssertSemanticsAsync(Solution solution, int expectedConstant)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            Assert.NotNull(compilation);
            Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
            var type = compilation.GetTypeByMetadataName("Library.Value");
            Assert.NotNull(type);
            Assert.Equal(expectedConstant, Assert.IsAssignableFrom<IFieldSymbol>(type.GetMembers("Number").Single()).ConstantValue);
            Assert.Empty(await project.GetSourceGeneratedDocumentsAsync());
        }
    }

    private static Solution WithTextAtPath(Solution solution, string path, string text, Encoding encoding)
    {
        foreach (var id in solution.GetDocumentIdsWithFilePath(path))
        {
            solution = solution.WithDocumentText(id, SourceText.From(text, encoding));
        }
        return solution;
    }

    private static string LibrarySource(int constant) => $"namespace Library; public static class Value {{ public const int Number = {constant}; }}";

    private static byte[] Bytes(string text, Encoding encoding) => [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed class Fixture : IDisposable
    {
        public string Root { get; }
        public string Artifacts => Path.Combine(Root, "artifacts");
        public string LibraryDirectory => Path.Combine(Root, "Library");
        public string LibraryPath => Path.Combine(LibraryDirectory, "Library.cs");
        public string SharedPath => Path.Combine(LibraryDirectory, "Shared.cs");
        public string AdditionalPath => Path.Combine(LibraryDirectory, "payload.txt");
        public string ConfigPath => Path.Combine(LibraryDirectory, ".editorconfig");
        public string GeneratedPath => Path.Combine(LibraryDirectory, "obj", "Generated.cs");
        public string InventoryPath => Path.Combine(Artifacts, "synthetic-inventory.json");
        public Encoding Encoding { get; }
        public ExperimentRequest Request { get; }
        public AdmissionEvidence Evidence { get; private set; } = null!;
        public ImmutableArray<ProjectId> OriginalIds { get; private set; } = [];

        private Fixture(string root, Encoding encoding)
        {
            Root = root;
            Encoding = encoding;
            Request = new ExperimentRequest(
                Path.Combine(root, "App", "App.csproj"), "Debug", null, "net10.0", [], "full", false);
        }

        public static async Task<Fixture> CreateAsync(
            string encodingName = "utf8", bool explicitCompile = false, bool includeOtherRoles = false)
        {
            var root = Path.Combine(Path.GetTempPath(), "RoslynMcp-E0-Hydrate-" + Guid.NewGuid().ToString("N"));
            var encoding = encodingName switch
            {
                "utf8-bom" => (Encoding)new UTF8Encoding(true),
                "utf16-bom" => new UnicodeEncoding(false, true),
                "ascii" => System.Text.Encoding.ASCII,
                _ => new UTF8Encoding(false),
            };
            var fixture = new Fixture(root, encoding);
            try
            {
                Directory.CreateDirectory(fixture.Artifacts);
                Directory.CreateDirectory(fixture.LibraryDirectory);
                Directory.CreateDirectory(Path.Combine(root, "App"));
                await File.WriteAllTextAsync(Path.Combine(root, "global.json"),
                    "{\"sdk\":{\"version\":\"10.0.300\",\"rollForward\":\"disable\"}}", new UTF8Encoding(false));
                await File.WriteAllTextAsync(Path.Combine(root, "NuGet.Config"),
                    "<configuration><packageSources><clear /></packageSources></configuration>", new UTF8Encoding(false));
                var explicitProperties = explicitCompile ? "<EnableDefaultCompileItems>false</EnableDefaultCompileItems>" : "";
                var explicitItems = explicitCompile ? "<ItemGroup><Compile Include=\"Library.cs\" /><Compile Include=\"Shared.cs\" /></ItemGroup>" : "";
                if (includeOtherRoles)
                {
                    explicitItems += "<ItemGroup><Compile Include=\"obj/Generated.cs\" /><AdditionalFiles Include=\"payload.txt\" /></ItemGroup>";
                }
                var project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework>"
                    + "<EnableNETAnalyzers>false</EnableNETAnalyzers><Nullable>enable</Nullable><DefineConstants>FIXTURE</DefineConstants>"
                    + explicitProperties + "</PropertyGroup>" + explicitItems + "</Project>";
                var libraryProject = Path.Combine(fixture.LibraryDirectory, "Library.csproj");
                await File.WriteAllTextAsync(libraryProject, project, new UTF8Encoding(false));
                await File.WriteAllTextAsync(fixture.Request.WorkspacePath,
                    "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework>"
                    + "<EnableNETAnalyzers>false</EnableNETAnalyzers><Nullable>enable</Nullable><DefineConstants>FIXTURE</DefineConstants>"
                    + "</PropertyGroup><ItemGroup><ProjectReference Include=\"../Library/Library.csproj\" />"
                    + "<Compile Include=\"../Library/Shared.cs\" Link=\"Linked/Shared.cs\" /></ItemGroup></Project>", new UTF8Encoding(false));
                await File.WriteAllBytesAsync(fixture.LibraryPath, Bytes(LibrarySource(7), encoding));
                await File.WriteAllBytesAsync(fixture.SharedPath,
                    Bytes("namespace Shared; public static class Link { public const string Text = \"initial 世界\"; }", encoding));
                await File.WriteAllBytesAsync(Path.Combine(root, "App", "App.cs"),
                    Bytes("namespace App; public static class Consumer { public static int Get() => Library.Value.Number; }", encoding));
                fixture._projectBytes.Add(libraryProject, await File.ReadAllBytesAsync(libraryProject));
                fixture._projectBytes.Add(fixture.Request.WorkspacePath, await File.ReadAllBytesAsync(fixture.Request.WorkspacePath));
                fixture._includeOtherRoles = includeOtherRoles;
                if (includeOtherRoles)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(fixture.GeneratedPath)!);
                    await File.WriteAllBytesAsync(fixture.AdditionalPath, Bytes("initial payload", encoding));
                    await File.WriteAllBytesAsync(fixture.ConfigPath, Bytes("root = true\n[*.cs]\n", encoding));
                    await File.WriteAllBytesAsync(fixture.GeneratedPath,
                        Bytes("public static class GeneratedOnDisk { public const int Number = 1; }", encoding));
                }
                return fixture;
            }
            catch
            {
                fixture.Dispose();
                throw;
            }
        }

        public static EvidenceEntry EvidenceFor(string category, InstanceKey? owner, string path) => new(
            category, owner, path, Hash(File.ReadAllBytes(path)), false,
            "synthetic-fixture-explicit-inventory", "sdk-project-v1/e0-1", path);

        public async Task<SemanticSnapshot> CaptureAsync(
            HydrateHostExperiment host, bool staleSource = false, string? unobservablePolicy = null)
        {
            var workspace = new AdhocWorkspace();
            var libraryId = ProjectId.CreateNewId();
            var appId = ProjectId.CreateNewId();
            OriginalIds = [libraryId, appId];
            var solution = workspace.CurrentSolution;
            foreach (var pair in new[] { (Id: libraryId, Name: "Library"), (Id: appId, Name: "App") })
            {
                var projectPath = Path.Combine(Root, pair.Name, pair.Name + ".csproj");
                solution = solution.AddProject(ProjectInfo.Create(
                    pair.Id, VersionStamp.Create(), pair.Name, pair.Name, LanguageNames.CSharp,
                    filePath: projectPath, outputFilePath: Path.Combine(Root, pair.Name, "bin", pair.Name + ".dll"),
                    parseOptions: new CSharpParseOptions(LanguageVersion.CSharp14, preprocessorSymbols: ["FIXTURE"]),
                    compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                        nullableContextOptions: NullableContextOptions.Enable, deterministic: true, allowUnsafe: true)));
            }
            solution = solution.AddProjectReference(appId, new ProjectReference(libraryId));
            solution = AddSource(solution, libraryId, LibraryPath);
            solution = AddSource(solution, libraryId, SharedPath);
            solution = AddSource(solution, appId, SharedPath, ["Linked"]);
            solution = AddSource(solution, appId, Path.Combine(Root, "App", "App.cs"));
            if (unobservablePolicy == "metadata-documentation-provider")
            {
                solution = solution.AddMetadataReference(libraryId,
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
            }
            else if (unobservablePolicy == "strong-name-provider")
            {
                var options = Assert.IsType<CSharpCompilationOptions>(solution.GetProject(libraryId)!.CompilationOptions);
                solution = solution.WithProjectCompilationOptions(libraryId,
                    options.WithStrongNameProvider(new DesktopStrongNameProvider([Root])));
            }
            if (_includeOtherRoles)
            {
                solution = AddSource(solution, libraryId, GeneratedPath);
                foreach (var id in OriginalIds)
                {
                    solution = solution.AddAdditionalDocument(DocumentId.CreateNewId(id), "payload.txt",
                        SourceText.From(File.ReadAllText(AdditionalPath, Encoding), Encoding), filePath: AdditionalPath);
                    solution = solution.AddAnalyzerConfigDocument(DocumentId.CreateNewId(id), ".editorconfig",
                        SourceText.From(File.ReadAllText(ConfigPath, Encoding), Encoding), filePath: ConfigPath);
                }
            }
            Assert.True(workspace.TryApplyChanges(solution));
            if (staleSource)
            {
                await File.WriteAllBytesAsync(LibraryPath, Bytes(LibrarySource(83), Encoding));
            }
            var instances = OriginalIds.Select(id => new InstanceKey(
                solution.GetProject(id)!.FilePath!, "net10.0",
                ImmutableSortedDictionary<string, string>.Empty.Add("Configuration", "Debug").Add("TargetFramework", "net10.0"))).ToImmutableArray();
            var edge = new ProjectEdge(instances[1], instances[0], [], false);
            var entries = ImmutableArray.CreateBuilder<EvidenceEntry>();
            var regions = ImmutableArray.CreateBuilder<RegionEntry>();
            foreach (var pair in OriginalIds.Zip(instances))
            {
                var project = solution.GetProject(pair.First)!;
                entries.Add(EvidenceFor("project", pair.Second, project.FilePath!));
                entries.Add(EvidenceFor("metadata", pair.Second, typeof(object).Assembly.Location));
                foreach (var document in project.Documents)
                {
                    entries.Add(EvidenceFor("compile", pair.Second, document.FilePath!));
                }
                foreach (var document in project.AdditionalDocuments)
                {
                    entries.Add(EvidenceFor("additional", pair.Second, document.FilePath!));
                }
                foreach (var document in project.AnalyzerConfigDocuments)
                {
                    entries.Add(EvidenceFor("analyzer-config", pair.Second, document.FilePath!));
                }
                regions.Add(CompileRegion(pair.Second));
            }
            await File.WriteAllTextAsync(InventoryPath,
                JsonSerializer.Serialize(new
                {
                    Label = "synthetic-fixture-only; independently explicit empty analyzer list for every instance",
                    Instances = instances,
                    Edges = new[] { edge },
                    AnalyzerInventories = instances.Select(instance => new { Instance = instance, Analyzers = Array.Empty<string>() }),
                }), new UTF8Encoding(false));
            entries.Add(EvidenceFor("expected-graph", null, InventoryPath));
            Evidence = new AdmissionEvidence(
                "sdk-project-v1/e0-1", "supported", entries.ToImmutable(), regions.ToImmutable(), instances, [edge],
                solution.Projects.SelectMany(project => project.Documents).Select(document => document.FilePath!)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToImmutableArray(),
                [], true, false);
            using var session = new ExperimentSession(workspace, Request, new DtbEventCounter(), []);
            var captured = await host.CaptureAsync(session, Evidence, default);
            // The fixture knows this exact reference and documentation policy by construction.
            // Roslyn exposes no public documentation-provider getter for arbitrary loaded references,
            // so this precomputed value is synthetic evidence, never ordinary capture equivalence.
            return captured with
            {
                Projects = captured.Projects.Select(project => project with
                {
                    MetadataReferences = [new ReferenceEntry(
                        typeof(object).Assembly.Location, MetadataImageKind.Assembly, [], false, null)],
                }).ToImmutableArray(),
            };
        }

        public async Task BuildAsync()
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[] { "build", Request.WorkspacePath, "-c", "Debug", "--nologo", "--verbosity", "quiet" })
            {
                start.ArgumentList.Add(argument);
            }
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch
            {
                process.Kill(entireProcessTree: true);
                throw;
            }
            Assert.True(process.ExitCode == 0, "Ordinary fixture build failed:\n" + await output + "\n" + await error);
            AssertProjectBytesUnchanged();
        }

        public void AssertProjectBytesUnchanged()
        {
            foreach (var pair in _projectBytes)
            {
                Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
            }
        }

        public void Dispose()
        {
            var canonical = Path.GetFullPath(Root);
            var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!canonical.StartsWith(parent, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(canonical).StartsWith("RoslynMcp-E0-Hydrate-", StringComparison.Ordinal)
                || canonical.Equals(parent.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Fixture cleanup escaped its owned temporary root.");
            }
            if (Directory.Exists(canonical))
            {
                Directory.Delete(canonical, recursive: true);
            }
        }

        private Solution AddSource(Solution solution, ProjectId projectId, string path, string[]? folders = null) =>
            solution.AddDocument(DocumentId.CreateNewId(projectId), Path.GetFileName(path),
                SourceText.From(File.ReadAllText(path, Encoding), Encoding), folders, path);

        private RegionEntry CompileRegion(InstanceKey owner)
        {
            var root = Path.GetDirectoryName(owner.ProjectPath)!;
            var members = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar)
                    .Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase) || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)))
                .Select(path => new RegionMember(Path.GetRelativePath(root, path).Replace('\\', '/'), Hash(File.ReadAllBytes(path))))
                .OrderBy(member => member.RelativePath, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .ThenBy(member => member.RelativePath, StringComparer.Ordinal).ToImmutableArray();
            var manifest = "e0-region-1\n" + string.Concat(members.Select(member =>
                JsonSerializer.Serialize(new[] { member.RelativePath, member.Sha256 }) + "\n"));
            return new RegionEntry(owner, root, "compile", ["**/*.cs"], ["bin/**", "obj/**"], members,
                Hash(System.Text.Encoding.UTF8.GetBytes(manifest)), InventoryPath);
        }

        private readonly Dictionary<string, byte[]> _projectBytes = new(StringComparer.OrdinalIgnoreCase);
        private bool _includeOtherRoles;
    }

    private sealed class UnknownAnalyzerReference : AnalyzerReference
    {
        public override string FullPath { get; }
        public override object Id => FullPath;

        public UnknownAnalyzerReference(string path) => FullPath = path;

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language) => [];
        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages() => [];
    }
}
