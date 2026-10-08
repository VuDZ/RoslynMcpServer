using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Workspace.StateCache;
using RoslynMcpServer.Services.Workspace.StateCache.Models;
using RoslynMcpServer.Tests.SourceStructure;
using Xunit;

namespace RoslynMcpServer.Tests.WorkspaceStateCache;

public sealed class IsolatedWorkspaceHydratorTests
{
    public IsolatedWorkspaceHydratorTests() => MsBuildBootstrapper.Register();

    [Fact]
    public async Task Real_ordinary_capture_codec_and_hydrate_preserve_the_selected_base_query()
    {
        var root = RepositoryRoot.Find();
        var path = Path.Combine(root, "samples", "RoslynMcpPlugin", "RoslynMcpPlugin.csproj");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        using var ordinary = new AdhocWorkspace();
        var descriptors = await new MSBuildProjectLoader(ordinary).LoadProjectInfoAsync(
            path, cancellationToken: timeout.Token);
        ordinary.AddProjects(descriptors);
        var capture = await OrdinaryWorkspaceCapture.CaptureAsync(
            ordinary.CurrentSolution,
            descriptors,
            path,
            new SnapshotLoadProperties(new(null, null, null), new(null, null, null)),
            originalAnalyzerPathsProven: true,
            timeout.Token);
        Assert.Null(capture.UnsupportedReason);
        var snapshot = WorkspaceSnapshotCodec.Decode(WorkspaceSnapshotCodec.Encode(capture.Snapshot!));
        using var session = await new IsolatedWorkspaceHydrator().HydrateAsync(snapshot, timeout.Token);
        Assert.Empty(session.Workspace.CurrentSolution.ProjectIds);
        Assert.DoesNotContain(session.Solution.ProjectIds, id => ordinary.CurrentSolution.ProjectIds.Contains(id));
        var pluginId = session.Projects.Single(pair => pair.Key.ProjectPath == path).Value;
        var plugin = session.BaseCompilations[pluginId];
        var sample = Assert.IsAssignableFrom<INamedTypeSymbol>(plugin.GetTypeByMetadataName("RoslynMcpPlugin.SamplePlugin"));
        Assert.Contains(sample.AllInterfaces, symbol =>
            symbol.ToDisplayString() == "RoslynMcpServer.Plugins.IRoslynMcpPlugin"
            && symbol.ContainingAssembly.Name == "RoslynMcpServer");
        Assert.NotNull(plugin.GetTypeByMetadataName("Newtonsoft.Json.Linq.JToken"));
        Assert.DoesNotContain(plugin.GetDiagnostics(timeout.Token), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var serverId = session.Projects.Single(pair => pair.Key.ProjectPath == Path.Combine(root, "RoslynMcpServer.csproj")).Value;
        var sourceComment = session.BaseCompilations[serverId]
            .GetTypeByMetadataName("RoslynMcpServer.Services.Workspace.StateCache.Models.WorkspaceSnapshot")!
            .GetDocumentationCommentXml(cancellationToken: timeout.Token);
        Assert.Contains("Portable graph facts only", sourceComment, StringComparison.Ordinal);
        foreach (var project in snapshot.Projects)
        {
            var live = ordinary.CurrentSolution.Projects.Single(item => item.FilePath == project.Key.ProjectPath);
            var restored = session.Solution.GetProject(session.Projects[project.Key])!;
            var options = Assert.IsType<CSharpCompilationOptions>(restored.CompilationOptions);
            var originalOptions = Assert.IsType<CSharpCompilationOptions>(live.CompilationOptions);
            Assert.True(options.WithStrongNameProvider(originalOptions.StrongNameProvider)
                .WithMetadataReferenceResolver(originalOptions.MetadataReferenceResolver)
                .WithSyntaxTreeOptionsProvider(originalOptions.SyntaxTreeOptionsProvider).Equals(originalOptions));
            Assert.NotSame(originalOptions.MetadataReferenceResolver, options.MetadataReferenceResolver);
            Assert.NotSame(originalOptions.SyntaxTreeOptionsProvider, options.SyntaxTreeOptionsProvider);
            Assert.Equal(live.ParseOptions, restored.ParseOptions);
            Assert.Equal(live.MetadataReferences.Count(), restored.MetadataReferences.Count());
            Assert.Equal(live.AnalyzerReferences.Count, restored.AnalyzerReferences.Count);
            foreach (var document in live.Documents.Cast<TextDocument>()
                .Concat(live.AdditionalDocuments).Concat(live.AnalyzerConfigDocuments))
            {
                var actualDocument = restored.Documents.Cast<TextDocument>()
                    .Concat(restored.AdditionalDocuments).Concat(restored.AnalyzerConfigDocuments)
                    .Single(item => item.FilePath == document.FilePath);
                Assert.NotEqual(document.Id, actualDocument.Id);
                AssertText(await document.GetTextAsync(timeout.Token), await actualDocument.GetTextAsync(timeout.Token));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Project_order_preserves_edges_memberships_roles_and_confirmed_producers(bool reverse)
    {
        using var fixture = Fixture.Create();
        var snapshot = reverse ? fixture.Snapshot with { Projects = fixture.Snapshot.Projects.Reverse().ToArray() } : fixture.Snapshot;
        using var first = await new IsolatedWorkspaceHydrator().HydrateAsync(snapshot, default);
        using var second = await new IsolatedWorkspaceHydrator().HydrateAsync(snapshot, default);
        Assert.DoesNotContain(first.Solution.ProjectIds, id => second.Solution.ProjectIds.Contains(id));
        Assert.Equal(3, first.Solution.GetDocumentIdsWithFilePath(fixture.SharedPath).Length);
        var app = first.Solution.GetProject(first.Projects[fixture.AppKey])!;
        var edge = Assert.Single(app.ProjectReferences);
        Assert.Equal(first.Projects[fixture.LibraryKey], edge.ProjectId);
        Assert.Equal(new[] { "global", "LibraryAlias" }, edge.Aliases.ToArray());
        Assert.True(edge.EmbedInteropTypes);
        Assert.Single(app.AdditionalDocuments);
        Assert.Single(app.AnalyzerConfigDocuments);
        Assert.NotNull(first.BaseCompilations[app.Id].GetTypeByMetadataName("LibraryMarker"));
        var shared = first.InputMap[fixture.SharedPath];
        Assert.Equal(3, shared.Length);
        Assert.Equal(2, shared.Count(occurrence => occurrence.Role == "Unknown"));
        Assert.All(shared.Where(occurrence => occurrence.Role == "Unknown"), occurrence =>
        {
            Assert.Null(occurrence.Producer);
            Assert.Null(occurrence.ProducerSource);
        });
        Assert.All(shared, occurrence => Assert.NotNull(occurrence.DocumentId));
        var generated = Assert.Single(first.InputMap[fixture.GeneratedPath]);
        Assert.Equal("Generated", generated.Role);
        Assert.Equal(first.Projects[fixture.LibraryKey], generated.Producer);
        Assert.Equal(SnapshotOccurrenceBinder.GeneratedProducerSource, generated.ProducerSource);
        Assert.True(RoslynDocumentBuildSideEffect.TryGetIsGenerated(
            first.Solution.GetDocument(generated.DocumentId!)!) == true);
        foreach (var input in snapshot.InputPaths)
        {
            var rebound = first.InputMap[input.Path];
            Assert.Equal(input.Occurrences.Length, rebound.Length);
            for (var index = 0; index < rebound.Length; index++)
            {
                var portable = input.Occurrences[index];
                Assert.Equal(first.Projects[portable.Owner], rebound[index].Owner);
                Assert.Equal(portable.Role, rebound[index].Role);
                Assert.Equal(portable.RoleSource, rebound[index].RoleSource);
                Assert.Equal(portable.ProducerSource, rebound[index].ProducerSource);
                Assert.Equal(portable.Producer is null ? null : first.Projects[portable.Producer], rebound[index].Producer);
            }
        }
        var properties = Assert.Single(app.MetadataReferences).Properties;
        Assert.Equal(new[] { "global", "CoreAlias" }, properties.Aliases.ToArray());
        Assert.False(properties.EmbedInteropTypes);
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf8-bom")]
    [InlineData("utf16-bom")]
    [InlineData("null")]
    public async Task Linked_path_is_read_once_and_current_bytes_and_text_metadata_reach_every_membership(string encodingName)
    {
        using var fixture = Fixture.Create(encodingName);
        const string current = "/// <summary>Grüße 世界</summary>\npublic static class Shared { public const int Value = 19; }";
        File.WriteAllBytes(fixture.SharedPath, Bytes(current, fixture.Encoding));
        var reads = new Dictionary<string, int>(InputPathCanon.Comparer);
        var hydrator = new IsolatedWorkspaceHydrator(readBytes: (path, token) =>
        {
            reads[path] = reads.GetValueOrDefault(path) + 1;
            return File.ReadAllBytesAsync(path, token);
        });
        using var session = await hydrator.HydrateAsync(fixture.Snapshot, default);
        Assert.Equal(1, reads[fixture.SharedPath]);
        Assert.All(reads, pair => Assert.Equal(1, pair.Value));
        foreach (var occurrence in session.InputMap[fixture.SharedPath])
        {
            var project = session.Solution.GetProject(occurrence.Owner)!;
            var document = project.Documents.Cast<TextDocument>().Concat(project.AdditionalDocuments)
                .Single(item => item.Id == occurrence.DocumentId);
            var text = await document.GetTextAsync();
            Assert.Equal(current, text.ToString());
            Assert.Equal(fixture.Encoding?.CodePage, text.Encoding?.CodePage);
            Assert.Equal(fixture.Encoding?.GetPreamble() ?? [], text.Encoding?.GetPreamble() ?? []);
            Assert.Equal(SourceHashAlgorithm.Sha256, text.ChecksumAlgorithm);
            Assert.NotEqual(fixture.Snapshot.Projects[0].Documents[0].TextMetadata.Checksum, text.GetChecksum().ToArray());
        }
        var compilation = session.BaseCompilations[session.Projects[fixture.LibraryKey]];
        var field = Assert.IsAssignableFrom<IFieldSymbol>(compilation.GetTypeByMetadataName("Shared")!.GetMembers("Value").Single());
        Assert.Equal(19, field.ConstantValue);
    }

    [Fact]
    public async Task Fresh_metadata_image_observes_replaced_binary_and_dispose_releases_owned_resources()
    {
        using var fixture = Fixture.Create();
        File.Copy(typeof(Console).Assembly.Location, fixture.MetadataPath, overwrite: true);
        var owned = new List<Metadata>();
        var disposedWorkspaces = new List<AdhocWorkspace>();
        var session = await new IsolatedWorkspaceHydrator(
            metadataCreated: owned.Add, workspaceDisposed: disposedWorkspaces.Add).HydrateAsync(fixture.Snapshot, default);
        var project = session.Solution.GetProject(session.Projects[fixture.LibraryKey])!;
        var reference = Assert.Single(project.MetadataReferences);
        var symbol = Assert.IsAssignableFrom<IAssemblySymbol>(
            session.BaseCompilations[project.Id].GetAssemblyOrModuleSymbol(reference));
        Assert.Equal(typeof(Console).Assembly.GetName().Name, symbol.Identity.Name);
        Assert.Single(owned);
        session.Dispose();
        session.Dispose();
        Assert.True(session.IsDisposed);
        AssertDisposed(owned);
        Assert.Same(session.Workspace, Assert.Single(disposedWorkspaces));
    }

    [Theory]
    [InlineData("System.Reflection.AssemblyKeyFile(\"missing.snk\")", "signing-dependent")]
    [InlineData("MissingAttribute", "signing-state-unknown")]
    public async Task Current_source_signing_dependency_or_unknown_state_refuses_before_return(
        string attribute,
        string expectedReason)
    {
        using var fixture = Fixture.Create();
        File.WriteAllText(fixture.GeneratedPath, "[assembly: " + attribute + "]\npublic class GeneratedInput { }");
        var owned = new List<Metadata>();
        var disposedWorkspaces = new List<AdhocWorkspace>();
        var hydrator = new IsolatedWorkspaceHydrator(
            workspaceDisposed: disposedWorkspaces.Add, metadataCreated: owned.Add);
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => hydrator.HydrateAsync(fixture.Snapshot, default));
        Assert.Contains("stage=hydrate", failure.Message, StringComparison.Ordinal);
        Assert.Contains("configuration=Debug tfm=net10.0", failure.Message, StringComparison.Ordinal);
        Assert.Contains("reason=" + expectedReason + " route=refusal", failure.Message, StringComparison.Ordinal);
        AssertDisposed(owned);
        Assert.Single(disposedWorkspaces);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_or_cancellation_after_metadata_allocation_disposes_host_and_every_image(bool cancel)
    {
        using var fixture = Fixture.Create();
        using var cancellation = new CancellationTokenSource();
        var owned = new List<Metadata>();
        var disposedWorkspaces = new List<AdhocWorkspace>();
        var hydrator = new IsolatedWorkspaceHydrator(
            workspaceDisposed: disposedWorkspaces.Add,
            metadataCreated: image =>
            {
                owned.Add(image);
                if (cancel)
                {
                    cancellation.Cancel();
                }
                else
                {
                    throw new IOException("Injected failure after metadata ownership transfer.");
                }
            });
        if (cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hydrator.HydrateAsync(fixture.Snapshot, cancellation.Token));
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => hydrator.HydrateAsync(fixture.Snapshot, cancellation.Token));
        }
        Assert.Single(owned);
        AssertDisposed(owned);
        Assert.Single(disposedWorkspaces);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("metadata")]
    public async Task Missing_current_input_refuses_and_disposes_candidate(string kind)
    {
        using var fixture = Fixture.Create();
        File.Delete(kind == "source" ? fixture.SharedPath : fixture.MetadataPath);
        var disposedWorkspaces = new List<AdhocWorkspace>();
        var hydrator = new IsolatedWorkspaceHydrator(workspaceDisposed: disposedWorkspaces.Add);
        await Assert.ThrowsAsync<FileNotFoundException>(() => hydrator.HydrateAsync(fixture.Snapshot, default));
        Assert.Single(disposedWorkspaces);
    }

    [Fact]
    public async Task Analyzer_identity_does_not_authorize_assembly_loading()
    {
        using var fixture = Fixture.Create();
        using var session = await new IsolatedWorkspaceHydrator().HydrateAsync(fixture.Snapshot, default);
        var reference = Assert.IsType<AnalyzerFileReference>(
            Assert.Single(session.Solution.GetProject(session.Projects[fixture.LibraryKey])!.AnalyzerReferences));
        Assert.Equal(typeof(IsolatedWorkspaceHydrator).Assembly.Location, reference.FullPath);
        var failure = Assert.Throws<InvalidOperationException>(() => reference.GetAssembly());
        Assert.Contains("does not authorize", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Hydrate_owned_calls_have_no_open_loader_process_or_apply_with_exact_callback_limits()
    {
        var analysis = ProductionAnalysis.Instance;
        Assert.True(analysis.IsUsable, string.Join("\n", analysis.InputDiagnostics));
        var diagnostics = new List<string>();
        var type = analysis.FindType("RoslynMcpServer.Services.Workspace.StateCache", "IsolatedWorkspaceHydrator", diagnostics);
        Assert.NotNull(type);
        var hydrate = analysis.FindDeclaredMethod(type, "HydrateAsync", diagnostics);
        Assert.NotNull(hydrate);
        var expected = new Dictionary<string, string>
        {
            ["_workspaceDisposed"] = "workspaceDisposed",
            ["_metadataCreated"] = "metadataCreated",
            ["_readBytes"] = "readBytes",
        };
        var sites = type.DeclaringSyntaxReferences.Single().GetSyntax().DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => analysis.ModelFor(invocation.SyntaxTree).GetSymbolInfo(invocation).Symbol
                is IMethodSymbol { MethodKind: MethodKind.DelegateInvoke })
            .ToArray();
        Assert.Equal(3, sites.Length);
        var limitTexts = sites.Select(site =>
        {
            var receiver = site.Expression is IdentifierNameSyntax direct
                ? (ExpressionSyntax)direct
                : site.Ancestors().OfType<ConditionalAccessExpressionSyntax>().First().Expression;
            var identifier = Assert.IsAssignableFrom<IFieldSymbol>(
                analysis.ModelFor(site.SyntaxTree).GetSymbolInfo(receiver).Symbol).Name;
            return "`" + expected[identifier] + "`, reached from the invoked delegate `" + identifier + "` at "
                + SourceSetAnalysis.Describe(site)
                + ", has no assignment inside the declared scope; targets registered outside the scope are not followed";
        }).Order().ToArray();
        Assert.Empty(diagnostics);
        foreach (var target in new[]
        {
            ("OpenProjectAsync", "Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace"),
            ("OpenSolutionAsync", "Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace"),
            ("LoadProjectInfoAsync", "Microsoft.CodeAnalysis.MSBuild.MSBuildProjectLoader"),
            ("Start", "System.Diagnostics.Process"),
            ("TryApplyChanges", "Microsoft.CodeAnalysis.Workspace"),
        })
        {
            var result = ReachabilityAnalysis.FindPath(analysis, hydrate, target.Item1, symbol =>
                symbol.Name == target.Item1 && symbol.ContainingType.ToDisplayString() == target.Item2);
            Assert.False(result.IsReachable, result.DescribePath());
            Assert.Empty(result.Diagnostics);
            Assert.All(result.Limits, limit => Assert.Equal(ReachabilityLimitKind.TargetOutsideTheScope, limit.Kind));
            Assert.Equal(limitTexts, result.Limits.Select(limit => limit.Text).Order().ToArray());
        }
        // The exact external resource-hook limits are intentional. Arbitrary supplied callbacks
        // are not a no-DTB proof; the owned default file/workspace path has no such external effects.
    }

    [Fact]
    public void Workspace_dispose_is_called_before_the_observed_disposal_notification_on_both_owned_paths()
    {
        var analysis = ProductionAnalysis.Instance;
        Assert.True(analysis.IsUsable, string.Join("\n", analysis.InputDiagnostics));
        AssertDisposePath("IsolatedWorkspaceHydrator", "HydrateAsync");
        AssertDisposePath("IsolatedHydratedSession", "Dispose");

        void AssertDisposePath(string typeName, string methodName)
        {
            var diagnostics = new List<string>();
            var type = analysis.FindType("RoslynMcpServer.Services.Workspace.StateCache", typeName, diagnostics);
            Assert.NotNull(type);
            var method = analysis.FindDeclaredMethod(type, methodName, diagnostics);
            Assert.NotNull(method);
            Assert.Empty(diagnostics);
            var declaration = Assert.IsType<MethodDeclarationSyntax>(method.DeclaringSyntaxReferences.Single().GetSyntax());
            var invocations = declaration.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
            var dispose = Assert.Single(invocations, invocation =>
                analysis.ModelFor(invocation.SyntaxTree).GetSymbolInfo(invocation).Symbol is IMethodSymbol symbol
                && symbol.Name == "Dispose"
                && symbol.ContainingType.ToDisplayString() == "Microsoft.CodeAnalysis.Workspace");
            var notification = Assert.Single(invocations, invocation =>
                analysis.ModelFor(invocation.SyntaxTree).GetSymbolInfo(invocation).Symbol is IMethodSymbol
                { MethodKind: MethodKind.DelegateInvoke });
            var guardedDispose = dispose.Ancestors().OfType<TryStatementSyntax>().First();
            var notificationStatement = notification.Ancestors().OfType<StatementSyntax>().First();
            var scope = Assert.IsType<BlockSyntax>(guardedDispose.Parent);
            Assert.Same(scope, notificationStatement.Parent);
            Assert.True(scope.Statements.IndexOf(guardedDispose) < scope.Statements.IndexOf(notificationStatement));
            Assert.NotNull(guardedDispose.Finally);
            var metadataDisposal = Assert.Single(guardedDispose.Finally!.DescendantNodes().OfType<InvocationExpressionSyntax>(),
                invocation => analysis.ModelFor(invocation.SyntaxTree).GetSymbolInfo(invocation).Symbol is IMethodSymbol symbol
                    && symbol.Name == "Dispose" && symbol.ContainingType.ToDisplayString() == "Microsoft.CodeAnalysis.Metadata");
            Assert.Single(metadataDisposal.Ancestors().OfType<ForEachStatementSyntax>());
        }
    }

    [Fact]
    public async Task Failure_on_a_second_metadata_image_disposes_the_first_image()
    {
        using var fixture = Fixture.Create();
        var secondPath = Path.Combine(fixture.Root, "BrokenMetadata.dll");
        File.WriteAllBytes(secondPath, [1, 2, 3]);
        var snapshot = fixture.WithExtraMetadata(secondPath);
        var owned = new List<Metadata>();
        var disposedWorkspaces = new List<AdhocWorkspace>();
        var hydrator = new IsolatedWorkspaceHydrator(
            metadataCreated: owned.Add, workspaceDisposed: disposedWorkspaces.Add);
        await Assert.ThrowsAsync<BadImageFormatException>(() => hydrator.HydrateAsync(snapshot, default));
        Assert.Equal(2, owned.Count);
        AssertDisposed(owned);
        Assert.Single(disposedWorkspaces);
    }

    [Fact]
    public async Task Multiple_owned_images_are_disposed_on_success_and_late_cancellation()
    {
        using var fixture = Fixture.Create();
        var secondPath = Path.Combine(fixture.Root, "SecondMetadata.dll");
        File.Copy(typeof(Console).Assembly.Location, secondPath);
        var snapshot = fixture.WithExtraMetadata(secondPath);
        var owned = new List<Metadata>();
        using (var session = await new IsolatedWorkspaceHydrator(metadataCreated: owned.Add).HydrateAsync(snapshot, default))
        {
            Assert.Equal(2, owned.Count);
        }
        AssertDisposed(owned);
        owned.Clear();
        using var cancellation = new CancellationTokenSource();
        var disposedWorkspaces = new List<AdhocWorkspace>();
        var hydrator = new IsolatedWorkspaceHydrator(workspaceDisposed: disposedWorkspaces.Add, metadataCreated: image =>
        {
            owned.Add(image);
            if (owned.Count == 2)
            {
                cancellation.Cancel();
            }
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hydrator.HydrateAsync(snapshot, cancellation.Token));
        Assert.Equal(2, owned.Count);
        AssertDisposed(owned);
        Assert.Single(disposedWorkspaces);
    }

    [Fact]
    public async Task Confirmed_metadata_producer_is_rebound_to_the_fresh_project_id()
    {
        using var fixture = Fixture.Create();
        var projects = fixture.Snapshot.Projects.ToArray();
        projects[0] = projects[0] with { OutputFilePath = fixture.MetadataPath };
        var snapshot = fixture.WithProjects(projects);
        using var session = await new IsolatedWorkspaceHydrator().HydrateAsync(snapshot, default);
        var occurrence = Assert.Single(session.InputMap[fixture.MetadataPath], item =>
            item.Role == "MetadataDependency" && item.Owner == session.Projects[fixture.AppKey]);
        Assert.Equal(session.Projects[fixture.LibraryKey], occurrence.Producer);
        Assert.Equal(SnapshotOccurrenceBinder.MetadataProducerSource, occurrence.ProducerSource);
    }

    [Fact]
    public async Task Precancelled_or_invalid_snapshot_allocates_no_owned_resource()
    {
        using var fixture = Fixture.Create();
        var owned = new List<Metadata>();
        var disposedWorkspaces = new List<AdhocWorkspace>();
        var reads = 0;
        var hydrator = new IsolatedWorkspaceHydrator(
            readBytes: (path, token) =>
            {
                reads++;
                return File.ReadAllBytesAsync(path, token);
            },
            metadataCreated: owned.Add,
            workspaceDisposed: disposedWorkspaces.Add);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            hydrator.HydrateAsync(fixture.Snapshot, new CancellationToken(canceled: true)));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            hydrator.HydrateAsync(fixture.Snapshot with { InputPaths = [] }, default));
        Assert.Empty(owned);
        Assert.Empty(disposedWorkspaces);
        Assert.Equal(0, reads);
    }

    [Theory]
    [InlineData("target", false)]
    [InlineData("target", true)]
    [InlineData("owner", false)]
    [InlineData("owner", true)]
    [InlineData("producer", false)]
    [InlineData("producer", true)]
    public async Task Codec_project_path_identity_is_preserved_for_references_occurrences_and_session_lookup(
        string binding,
        bool reverse)
    {
        using var fixture = Fixture.Create();
        var projects = fixture.Snapshot.Projects.ToArray();
        projects[0] = projects[0] with { OutputFilePath = fixture.MetadataPath };
        var snapshot = fixture.WithProjects(projects);
        if (binding == "target")
        {
            projects[1] = projects[1] with
            {
                ProjectReferences = projects[1].ProjectReferences.Select(edge =>
                    edge with { Target = CasingVariant(edge.Target) }).ToArray(),
            };
            snapshot = snapshot with { Projects = projects };
        }
        else
        {
            snapshot = snapshot with
            {
                InputPaths = snapshot.InputPaths.Select(input => input with
                {
                    Occurrences = input.Occurrences.Select(occurrence => binding == "owner"
                        ? occurrence with { Owner = CasingVariant(occurrence.Owner) }
                        : occurrence with
                        {
                            Producer = occurrence.Producer is null ? null : CasingVariant(occurrence.Producer),
                        }).ToArray(),
                }).ToArray(),
            };
        }
        if (reverse)
        {
            snapshot = snapshot with { Projects = snapshot.Projects.Reverse().ToArray() };
        }
        if (!OperatingSystem.IsWindows())
        {
            Assert.Throws<InvalidDataException>(() => WorkspaceSnapshotCodec.Encode(snapshot));
            using var exact = await new IsolatedWorkspaceHydrator().HydrateAsync(fixture.Snapshot, default);
            Assert.False(exact.Projects.ContainsKey(CasingVariant(fixture.LibraryKey)));
            return;
        }

        // Prove this spelling is accepted by the actual codec before testing its consumer.
        var decoded = WorkspaceSnapshotCodec.Decode(WorkspaceSnapshotCodec.Encode(snapshot));
        using var session = await new IsolatedWorkspaceHydrator().HydrateAsync(decoded, default);
        var libraryId = session.Projects[fixture.LibraryKey];
        var appId = session.Projects[fixture.AppKey];
        Assert.Equal(libraryId, session.Projects[CasingVariant(fixture.LibraryKey)]);
        Assert.Equal(appId, session.Projects[CasingVariant(fixture.AppKey)]);
        Assert.False(session.Projects.ContainsKey(fixture.LibraryKey with { Configuration = "DEBUG" }));
        Assert.False(session.Projects.ContainsKey(fixture.LibraryKey with { Platform = "anycpu" }));
        Assert.False(session.Projects.ContainsKey(fixture.LibraryKey with { TargetFramework = "NET10.0" }));
        Assert.Equal(libraryId, Assert.Single(session.Solution.GetProject(appId)!.ProjectReferences).ProjectId);
        var metadata = Assert.Single(session.InputMap[fixture.MetadataPath], occurrence =>
            occurrence.Role == "MetadataDependency" && occurrence.Owner == appId);
        Assert.Equal(libraryId, metadata.Producer);
        var generated = Assert.Single(session.InputMap[fixture.GeneratedPath]);
        Assert.Equal(libraryId, generated.Owner);
        Assert.Equal(libraryId, generated.Producer);
        Assert.Equal(libraryId, generated.DocumentId!.ProjectId);
        Assert.Equal(new[] { libraryId, appId }, session.InputMap[fixture.SharedPath]
            .Where(occurrence => occurrence.Role == "Unknown").Select(occurrence => occurrence.Owner).ToArray());
    }

    private static SnapshotProjectKey CasingVariant(SnapshotProjectKey key) => key with
    {
        ProjectPath = Path.Combine(Path.GetDirectoryName(key.ProjectPath)!, Path.GetFileName(key.ProjectPath).ToUpperInvariant()),
    };

    private static void AssertText(SourceText expected, SourceText actual)
    {
        Assert.True(expected.ContentEquals(actual));
        Assert.Equal(expected.Encoding?.CodePage, actual.Encoding?.CodePage);
        Assert.Equal(expected.Encoding?.GetPreamble() ?? [], actual.Encoding?.GetPreamble() ?? []);
        Assert.Equal(expected.ChecksumAlgorithm, actual.ChecksumAlgorithm);
        Assert.Equal(expected.GetChecksum().ToArray(), actual.GetChecksum().ToArray());
    }

    private static void AssertDisposed(IEnumerable<Metadata> metadata)
    {
        foreach (var image in metadata)
        {
            var assembly = Assert.IsType<AssemblyMetadata>(image);
            Assert.Throws<ObjectDisposedException>(() => assembly.GetModules()[0].GetMetadataReader());
        }
    }

    private static byte[] Bytes(string text, Encoding? encoding)
    {
        var actual = encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        return [..actual.GetPreamble(), ..actual.GetBytes(text)];
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; }
        public string SharedPath { get; }
        public string GeneratedPath { get; }
        public string MetadataPath { get; }
        public Encoding? Encoding { get; }
        public SnapshotProjectKey LibraryKey { get; }
        public SnapshotProjectKey AppKey { get; }
        public WorkspaceSnapshot Snapshot { get; }

        private Fixture(string root, Encoding? encoding)
        {
            Root = root;
            Encoding = encoding;
            SharedPath = Path.Combine(root, "Shared.cs");
            GeneratedPath = Path.Combine(root, "Generated.cs");
            MetadataPath = Path.Combine(root, "CurrentMetadata.dll");
            LibraryKey = new SnapshotProjectKey(Path.Combine(root, "Library.csproj"), "Debug", "AnyCPU", "net10.0");
            AppKey = new SnapshotProjectKey(Path.Combine(root, "App.csproj"), "Debug", "AnyCPU", "net10.0");
            File.WriteAllBytes(SharedPath, Bytes("public static class Shared { public const int Value = 7; }", encoding));
            File.WriteAllText(GeneratedPath, "public class GeneratedInput { }");
            File.WriteAllText(Path.Combine(root, "LibraryMarker.cs"), "public class LibraryMarker { }");
            File.WriteAllText(Path.Combine(root, ".editorconfig"), "root = true\n[*.cs]\ndotnet_diagnostic.CS0168.severity = none");
            File.Copy(typeof(object).Assembly.Location, MetadataPath);
            File.WriteAllText(LibraryKey.ProjectPath, "<Project />");
            File.WriteAllText(AppKey.ProjectPath, "<Project />");
            using var optionWorkspace = new AdhocWorkspace();
            var projects = new[]
            {
                Project(optionWorkspace, LibraryKey, "Library", [
                    Document(SharedPath, "source", isGenerated: false),
                    Document(GeneratedPath, "source", isGenerated: true),
                    Document(Path.Combine(root, "LibraryMarker.cs"), "source", isGenerated: false),
                    Document(Path.Combine(root, ".editorconfig"), "analyzer-config", isGenerated: false),
                ], []),
                Project(optionWorkspace, AppKey, "App", [
                    Document(SharedPath, "source", isGenerated: false),
                    Document(SharedPath, "additional", isGenerated: false),
                    Document(Path.Combine(root, ".editorconfig"), "analyzer-config", isGenerated: false),
                ], [new SnapshotProjectReference(LibraryKey, ["global", "LibraryAlias"], true)]),
            };
            Snapshot = new WorkspaceSnapshot(
                OrdinaryWorkspaceCapture.SchemaVersion,
                OrdinaryWorkspaceCapture.Profile,
                typeof(Compilation).Assembly.GetName().Version!.ToString(),
                root,
                new SnapshotLoadProperties(new(null, null, null), new(null, null, null)),
                projects,
                InputPaths(projects));
        }

        public static Fixture Create(string encodingName = "utf8")
        {
            var root = Path.Combine(Path.GetTempPath(), "isolated-hydrate-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Encoding? encoding = encodingName switch
            {
                "utf8" => new UTF8Encoding(false),
                "utf8-bom" => new UTF8Encoding(true),
                "utf16-bom" => new UnicodeEncoding(false, true),
                "null" => null,
                _ => throw new ArgumentException("Unknown fixture encoding.", nameof(encodingName)),
            };
            return new Fixture(root, encoding);
        }

        public void Dispose()
        {
            var canonical = Path.GetFullPath(Root);
            var temporary = Path.GetFullPath(Path.GetTempPath());
            if (!canonical.StartsWith(temporary, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(canonical).StartsWith("isolated-hydrate-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Refusing to delete a path outside the owned fixture directory.");
            }
            Directory.Delete(canonical, recursive: true);
        }

        public WorkspaceSnapshot WithExtraMetadata(string path)
        {
            var projects = Snapshot.Projects.ToArray();
            projects[0] = projects[0] with
            {
                MetadataReferences = [..projects[0].MetadataReferences,
                    new SnapshotMetadataReference(path, "Assembly", ["global"], false,
                        OrdinaryWorkspaceCapture.BinaryDocumentationPolicy)],
            };
            return WithProjects(projects);
        }

        public WorkspaceSnapshot WithProjects(SnapshotProject[] projects) =>
            Snapshot with { Projects = projects, InputPaths = InputPaths(projects) };

        private SnapshotProject Project(
            AdhocWorkspace workspace,
            SnapshotProjectKey key,
            string name,
            SnapshotDocument[] documents,
            SnapshotProjectReference[] references)
        {
            var factory = CommandLineProject.CreateProjectInfo(name, LanguageNames.CSharp, [], Root, workspace);
            var options = CSharpCommandLineParser.Default.Parse(["/debug+"], Root, null).CompilationOptions
                .WithOutputKind(OutputKind.DynamicallyLinkedLibrary)
                .WithMetadataReferenceResolver(factory.CompilationOptions!.MetadataReferenceResolver)
                .WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default)
                .WithStrongNameProvider(new DesktopStrongNameProvider(ImmutableArray<string>.Empty));
            var parse = new CSharpParseOptions(LanguageVersion.CSharp14, DocumentationMode.Diagnose,
                preprocessorSymbols: ["HYDRATE_FIXTURE"]);
            var info = ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), name, name, LanguageNames.CSharp,
                filePath: key.ProjectPath, compilationOptions: options, parseOptions: parse);
            var live = workspace.CurrentSolution.AddProject(info).GetProject(info.Id)!;
            return new SnapshotProject(
                key,
                new SnapshotEvaluatedProperties(new(key.Configuration, key.Platform, key.TargetFramework),
                    OrdinaryWorkspaceCapture.EvaluatedPropertySource, []),
                name,
                name,
                LanguageNames.CSharp,
                Path.Combine(Root, name + ".dll"),
                Path.Combine(Root, name + ".ref.dll"),
                name,
                Path.Combine(Root, name + ".dll"),
                Path.Combine(Root, name + "-generated"),
                SnapshotOptionFacts.Parse(parse),
                SnapshotOptionFacts.Compilation(live, info.CompilationOptions, SnapshotSigningProfile.IndependentPolicy),
                documents,
                references,
                [new SnapshotMetadataReference(MetadataPath, "Assembly", ["global", "CoreAlias"], false,
                    OrdinaryWorkspaceCapture.BinaryDocumentationPolicy)],
                [new SnapshotAnalyzerReference(typeof(IsolatedWorkspaceHydrator).Assembly.Location,
                    OrdinaryWorkspaceCapture.AnalyzerIdentitySource)]);
        }

        private SnapshotDocument Document(string path, string kind, bool isGenerated)
        {
            var encoding = InputPathCanon.Comparer.Equals(path, SharedPath) ? Encoding : new UTF8Encoding(false);
            using var stream = File.OpenRead(path);
            var disk = SourceText.From(stream, encoding, SourceHashAlgorithm.Sha256);
            var text = encoding is null ? SourceText.From(disk.ToString(), encoding: null, SourceHashAlgorithm.Sha256) : disk;
            return new SnapshotDocument(kind, path, Path.GetFileName(path), ["Fixture"], "Regular", isGenerated,
                new SnapshotTextMetadata(text.Encoding?.CodePage, text.Encoding?.GetPreamble() ?? [],
                    text.ChecksumAlgorithm.ToString(), text.GetChecksum().ToArray()));
        }

        private static SnapshotInputPath[] InputPaths(SnapshotProject[] projects)
        {
            var paths = new Dictionary<string, List<SnapshotInputOccurrence>>(InputPathCanon.Comparer);
            foreach (var project in projects)
            {
                Add(project.Key.ProjectPath, project.Key, null, "EvaluationInput", SnapshotOccurrenceBinder.ProjectFileRoleSource);
                Add(project.OutputFilePath!, project.Key, null, "Output", SnapshotOccurrenceBinder.OutputRoleSource,
                    project.Key, SnapshotOccurrenceBinder.OutputRoleSource);
                Add(project.OutputRefFilePath!, project.Key, null, "Output", SnapshotOccurrenceBinder.OutputRoleSource,
                    project.Key, SnapshotOccurrenceBinder.OutputRoleSource);
                foreach (var document in project.Documents)
                {
                    var role = document.Kind switch
                    {
                        "additional" => "AdditionalFile",
                        "analyzer-config" => "AnalyzerConfig",
                        _ => document.IsGenerated ? "Generated" : "Unknown",
                    };
                    Add(document.Path, project.Key, document.Kind, role, SnapshotOccurrenceBinder.DocumentRoleSource,
                        document.IsGenerated ? project.Key : null,
                        document.IsGenerated ? SnapshotOccurrenceBinder.GeneratedProducerSource : null);
                }
                foreach (var reference in project.MetadataReferences)
                {
                    var producers = projects.Where(candidate =>
                        InputPathCanon.Comparer.Equals(candidate.OutputFilePath, reference.Path)
                        || InputPathCanon.Comparer.Equals(candidate.OutputRefFilePath, reference.Path)).ToArray();
                    var producer = producers.Length == 1 && project.ProjectReferences.Any(edge => edge.Target == producers[0].Key)
                        ? producers[0].Key : null;
                    Add(reference.Path, project.Key, null, "MetadataDependency", SnapshotOccurrenceBinder.MetadataRoleSource,
                        producer, producer is null ? null : SnapshotOccurrenceBinder.MetadataProducerSource);
                }
                foreach (var analyzer in project.AnalyzerReferences)
                {
                    Add(analyzer.OriginalPath, project.Key, null, "AnalyzerDependency", analyzer.IdentitySource);
                }
            }
            return paths.Select(pair => new SnapshotInputPath(pair.Key, pair.Value.ToArray())).ToArray();

            void Add(string path, SnapshotProjectKey owner, string? kind, string role, string source,
                SnapshotProjectKey? producer = null, string? producerSource = null)
            {
                if (!paths.TryGetValue(path, out var occurrences))
                {
                    occurrences = [];
                    paths.Add(path, occurrences);
                }
                occurrences.Add(new SnapshotInputOccurrence(owner, kind, role, source, producer, producerSource, File.Exists(path)));
            }
        }
    }
}
