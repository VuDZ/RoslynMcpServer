using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Analyzers;
using RoslynMcpServer.Services.Workspace;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

/// <summary>Isolated public-API feasibility host; it does not publish through the production manager.</summary>
internal sealed class HydrateHostExperiment
{
    public HydrateHostExperiment(ExperimentAnalyzerAdmission? analyzerAdmission = null)
    {
        _analyzerAdmission = analyzerAdmission;
    }

    public Task<ExperimentSession> OpenFreshAsync(
        ExperimentRequest request,
        string artifactDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MsBuildBootstrapper.Register();
        return OpenFreshRegisteredAsync(request, artifactDirectory, cancellationToken);
    }

    public async Task<SemanticSnapshot> CaptureAsync(
        ExperimentSession ordinary,
        AdmissionEvidence admission,
        CancellationToken cancellationToken)
    {
        await ordinary.OperationLock.WaitAsync(cancellationToken);
        try
        {
            ordinary.ThrowIfDisposed();
            ValidateAdmissionSchema(admission);
            await ValidateEvidenceAsync(admission, cancellationToken);
            var solution = ordinary.Solution;
            var bindings = BindInstances(solution, admission);
            var projects = ImmutableArray.CreateBuilder<ProjectEntry>();
            var edges = ImmutableArray.CreateBuilder<ProjectEdge>();
            foreach (var project in solution.Projects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = bindings[project.Id];
                var documents = ImmutableArray.CreateBuilder<DocumentEntry>();
                foreach (var (document, role) in AllDocuments(project))
                {
                    var text = await document.GetTextAsync(cancellationToken);
                    var path = RequirePath(document.FilePath);
                    var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                    var encoding = CaptureEncoding(text.Encoding, bytes);
                    VerifyConsumedHash(admission, key, role, path, bytes);
                    if (!DecodeText(bytes, encoding).ContentEquals(text))
                    {
                        throw new InvalidDataException("Held document characters differ from admitted source bytes.");
                    }
                    documents.Add(new DocumentEntry(key, role, path, document.Name,
                        document.Folders.ToImmutableArray(),
                        document is Document source ? source.SourceCodeKind : SourceCodeKind.Regular,
                        IsUnder(path, Path.Combine(Path.GetDirectoryName(key.ProjectPath)!, "obj")), encoding));
                }

                var analyzers = project.AnalyzerReferences.Select(reference =>
                {
                    var path = RequirePath(reference.FullPath);
                    var entry = admission.Entries.SingleOrDefault(item => item.Category == "analyzer"
                        && Equals(item.Owner, key) && Paths.Equals(item.PathOrRegion, path));
                    if (entry?.Sha256 is null)
                    {
                        throw new InvalidDataException("Analyzer has no independent inventory evidence.");
                    }

                    var dependencies = admission.Entries.Where(item => item.Category == "analyzer-dependency"
                            && Equals(item.Owner, key)).Select(item => item.PathOrRegion)
                        .OrderBy(pathValue => pathValue, Paths).ToImmutableArray();
                    return new AnalyzerEntry(key, path, entry.Sha256, Path.GetDirectoryName(path)!, dependencies);
                }).ToImmutableArray();
                var references = project.MetadataReferences.Select(reference =>
                {
                    if (reference is not PortableExecutableReference fileReference)
                    {
                        throw new InvalidDataException("Metadata reference has no portable file representation.");
                    }

                    var path = RequirePath(fileReference.FilePath);
                    if (fileReference is not OwnedMetadataReference owned)
                    {
                        throw new InvalidDataException("unsupported-options: metadata documentation provider is not publicly observable");
                    }

                    return new ReferenceEntry(path, reference.Properties.Kind, reference.Properties.Aliases,
                        reference.Properties.EmbedInteropTypes, owned.DocumentationPath);
                }).ToImmutableArray();
                projects.Add(new ProjectEntry(key, project.Name, project.AssemblyName!, project.Language,
                    project.OutputFilePath, project.OutputRefFilePath,
                    CaptureParseOptions(project.ParseOptions), CaptureCompilationOptions(project.CompilationOptions),
                    documents.ToImmutable(), references, analyzers));
                foreach (var reference in project.ProjectReferences)
                {
                    edges.Add(new ProjectEdge(key, bindings[reference.ProjectId], reference.Aliases,
                        reference.EmbedInteropTypes));
                }
            }

            var snapshot = new SemanticSnapshot("e0-semantic-1", ordinary.Request, projects.ToImmutable(), edges.ToImmutable());
            ValidateInventories(snapshot, admission);
            return SemanticSnapshotCodec.Decode(SemanticSnapshotCodec.Encode(snapshot));
        }
        finally
        {
            ordinary.OperationLock.Release();
        }
    }

    public async Task<ExperimentSession> HydrateAsync(
        SemanticSnapshot snapshot,
        AdmissionEvidence admission,
        string artifactDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        snapshot = SemanticSnapshotCodec.Decode(SemanticSnapshotCodec.Encode(snapshot));
        ValidateInventories(snapshot, admission);
        await ValidateEvidenceAsync(admission, cancellationToken);
        var (session, ids) = await CreateBaseSessionAsync(snapshot, admission, cancellationToken);
        try
        {
            await session.OperationLock.WaitAsync(cancellationToken);
            try
            {
                await AdmitAnalyzersAsync(session, snapshot, admission, ids, cancellationToken);
            }
            finally
            {
                session.OperationLock.Release();
            }

            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public async Task<WorkspaceWriteResult> ApplyAsync(
        ExperimentSession session,
        Solution heldBase,
        Solution candidate,
        WorkspaceWriteOperationContext? operationContext,
        CancellationToken cancellationToken)
    {
        try
        {
            await session.OperationLock.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new WorkspaceWriteResult { Status = WorkspaceWriteStatus.Cancelled };
        }

        var saved = new List<string>();
        Solution? cleanedCandidate = null;
        var workspaceApplied = false;
        try
        {
            session.ThrowIfDisposed();
            if (session.Workspace is not AdhocWorkspace || !ReferenceEquals(candidate.Workspace, session.Workspace))
            {
                return WorkspaceWriteResult.PreflightRejected("unsupported-host-write");
            }

            var current = session.Solution;
            var stamp = session.WriteContext;
            var freshness = new WorkspaceWriteFreshnessState(session.SessionId, session.Request.WorkspacePath,
                null, false, stamp.RawWorkspaceRevision, stamp.RawWorkspaceSnapshot, current);
            var preflight = WorkspaceWriteBoundary.Preflight(candidate, current, operationContext,
                freshness, session.AnalyzerLoader, heldBase);
            if (!preflight.Accepted)
            {
                return WorkspaceWriteResult.PreflightRejected(preflight.Reason!);
            }

            var cleaned = await NormalizeLinkedEditsAsync(current, preflight.CleanedCandidate!, cancellationToken);
            cleanedCandidate = cleaned;
            var plan = await BuildWritePlanAsync(session, current, cleaned, cancellationToken);
            foreach (var item in plan.Where(item => item.Text is not null).Concat(plan.Where(item => item.Text is null)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.Text is null)
                {
                    File.Delete(item.Path);
                }
                else
                {
                    await WorkspaceFilePersistence.WriteTextAsync(null, item.Path, item.Text, item.Encoding!, cancellationToken);
                }

                saved.Add(item.Path);
            }

            cancellationToken.ThrowIfCancellationRequested();
            foreach (var item in plan.Where(item => item.Text is not null))
            {
                var text = SourceText.From(item.Text!, item.Encoding);
                foreach (var id in cleaned.GetDocumentIdsWithFilePath(item.Path))
                {
                    cleaned = cleaned.WithDocumentText(id, text);
                }
            }

            if (!session.Workspace.TryApplyChanges(cleaned))
            {
                throw new IOException("Adhoc workspace refused persisted changes.");
            }

            session.Publish(session.Workspace.CurrentSolution);
            workspaceApplied = true;
            await ReconcileUnderLockAsync(session, saved.ToImmutableArray(), cancellationToken);
            return new WorkspaceWriteResult
            {
                Status = WorkspaceWriteStatus.FullSuccess,
                SavedPaths = saved.ToArray(),
                WorkspaceApplied = true,
            };
        }
        catch (InvalidDataException exception) when (saved.Count == 0)
        {
            return WorkspaceWriteResult.PreflightRejected(exception.Message);
        }
        catch (OperationCanceledException)
        {
            await ReconcileCommittedAsync(session, saved, cleanedCandidate);
            return new WorkspaceWriteResult
            {
                Status = WorkspaceWriteStatus.Cancelled,
                SavedPaths = saved.ToArray(),
                WorkspaceApplied = workspaceApplied,
                UnappliedProjectState = !workspaceApplied && saved.Count != 0,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await ReconcileCommittedAsync(session, saved, cleanedCandidate);
            return new WorkspaceWriteResult
            {
                Status = WorkspaceWriteStatus.PartialPersistence,
                Reason = exception.Message,
                SavedPaths = saved.ToArray(),
                WorkspaceApplied = workspaceApplied,
                UnappliedProjectState = !workspaceApplied,
            };
        }
        finally
        {
            session.OperationLock.Release();
        }
    }

    public async Task ReconcileAsync(
        ExperimentSession session,
        ImmutableArray<string> changedPaths,
        CancellationToken cancellationToken)
    {
        if (changedPaths.IsDefault)
        {
            throw new InvalidDataException("Reconciliation paths must be initialized.");
        }

        foreach (var path in changedPaths)
        {
            RequireCanonicalPath(path);
        }

        await session.OperationLock.WaitAsync(cancellationToken);
        try
        {
            session.ThrowIfDisposed();
            await ReconcileUnderLockAsync(session, changedPaths, cancellationToken);
        }
        finally
        {
            session.OperationLock.Release();
        }
    }

    private async Task<ExperimentSession> OpenFreshRegisteredAsync(
        ExperimentRequest request,
        string artifactDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddProperty("Configuration", request.Configuration);
        AddProperty("Platform", request.Platform);
        AddProperty("TargetFramework", request.TargetFramework);
        var stageDirectory = Path.Combine(artifactDirectory, $"ordinary-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stageDirectory);
        var counter = new DtbEventCounter
        {
            Parameters = Path.Combine(stageDirectory, "ordinary.binlog"),
        };
        var remoteLogger = counter.CreateRemoteBinaryLogger();
        var workspace = MSBuildWorkspace.Create(properties);
        workspace.LoadMetadataForReferencedProjects = request.MetadataMode;
        var diagnostics = new List<WorkspaceDiagnostic>();
        var diagnosticRegistration = workspace.RegisterWorkspaceFailedHandler(args =>
        {
            lock (diagnostics)
            {
                diagnostics.Add(args.Diagnostic);
            }
        });
        try
        {
            counter.RecordOpenInvocation();
            if (Path.GetExtension(request.WorkspacePath).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                await workspace.OpenProjectAsync(request.WorkspacePath, remoteLogger, cancellationToken: cancellationToken);
            }
            else
            {
                await workspace.OpenSolutionAsync(request.WorkspacePath, remoteLogger, cancellationToken: cancellationToken);
            }

            var rawPaths = Directory.GetFiles(stageDirectory, "*.binlog", SearchOption.TopDirectoryOnly).ToImmutableArray();
            counter.ReplayRawBinlogs(rawPaths, cancellationToken);
            ImmutableArray<WorkspaceDiagnostic> capturedDiagnostics;
            lock (diagnostics)
            {
                capturedDiagnostics = diagnostics.ToImmutableArray();
            }

            var session = new ExperimentSession(workspace, request, counter, capturedDiagnostics);
            session.Own(diagnosticRegistration);
            return session;
        }
        catch
        {
            workspace.Dispose();
            diagnosticRegistration.Dispose();
            counter.Shutdown();
            throw;
        }

        void AddProperty(string name, string? value)
        {
            if (value is not null)
            {
                properties.Add(name, value);
            }
        }
    }

    private static async Task<(ExperimentSession Session, Dictionary<InstanceKey, ProjectId> Ids)> CreateBaseSessionAsync(
        SemanticSnapshot snapshot,
        AdmissionEvidence admission,
        CancellationToken cancellationToken)
    {
        var texts = await ReadTextsAsync(snapshot, admission, cancellationToken);
        var workspace = new AdhocWorkspace(MefHostServices.DefaultHost, "E0HydrateExperiment");
        var resources = new List<IDisposable>();
        ExperimentSession? session = null;
        try
        {
            var references = await ReadReferencesAsync(snapshot, admission, resources, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var ids = snapshot.Projects.ToDictionary(project => project.Key, _ => ProjectId.CreateNewId());
            foreach (var project in snapshot.Projects)
            {
                var info = ProjectInfo.Create(ids[project.Key], VersionStamp.Create(), project.Name,
                    project.AssemblyName, project.Language, filePath: project.Key.ProjectPath,
                    outputFilePath: project.OutputPath,
                    compilationOptions: RestoreCompilationOptions(project.CompilationOptions),
                    parseOptions: RestoreParseOptions(project.ParseOptions),
                    metadataReferences: references[project.Key]);
                workspace.AddProject(info);
            }

            var solution = workspace.CurrentSolution;
            foreach (var project in snapshot.Projects)
            {
                solution = solution.WithProjectOutputRefFilePath(ids[project.Key], project.OutputReferencePath);
                foreach (var document in project.Documents)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var id = DocumentId.CreateNewId(ids[project.Key]);
                    var loader = TextLoader.From(TextAndVersion.Create(texts[document.Path], VersionStamp.Create(), document.Path));
                    var info = DocumentInfo.Create(id, document.Name, document.Folders,
                        document.SourceKind, loader, document.Path, document.GeneratedOnDisk);
                    solution = document.Role switch
                    {
                        "compile" => solution.AddDocument(info),
                        "additional" => solution.AddAdditionalDocument(info),
                        "analyzer-config" => solution.AddAnalyzerConfigDocument(id, document.Name, texts[document.Path],
                            document.Folders, document.Path),
                        _ => throw new InvalidDataException("Unsupported document role."),
                    };
                }
            }

            foreach (var edge in snapshot.ProjectReferences)
            {
                solution = solution.AddProjectReference(ids[edge.From],
                    new ProjectReference(ids[edge.To], edge.Aliases, edge.EmbedInteropTypes));
            }

            if (!workspace.TryApplyChanges(solution))
            {
                throw new InvalidDataException("Adhoc workspace refused the hydrated base graph.");
            }

            var counter = new DtbEventCounter();
            counter.MarkHydrateClosedPath();
            session = new ExperimentSession(workspace, snapshot.Request, counter, []) { Admission = admission };
            foreach (var resource in resources)
            {
                session.Own(resource);
            }

            await ValidateConsumedBytesAsync(snapshot, admission, texts, cancellationToken);
            return (session, ids);
        }
        catch
        {
            if (session is null)
            {
                workspace.Dispose();
                foreach (var resource in resources)
                {
                    resource.Dispose();
                }
            }
            else
            {
                session.Dispose();
            }

            throw;
        }
    }

    private async Task AdmitAnalyzersAsync(
        ExperimentSession session,
        SemanticSnapshot snapshot,
        AdmissionEvidence evidence,
        Dictionary<InstanceKey, ProjectId> ids,
        CancellationToken cancellationToken)
    {
        var analyzers = snapshot.Projects.SelectMany(project => project.Analyzers).ToArray();
        if (analyzers.Length == 0)
        {
            session.AnalyzerAdmissionReady = true;
            return;
        }

        if (!evidence.OverlayReady || _analyzerAdmission is null)
        {
            return;
        }

        session.MarkDtbCoverageUnknown();
        var provenance = await _analyzerAdmission(snapshot, evidence, session.SessionId,
            session.Solution, cancellationToken);
        if (AnalyzerProvenanceCaptureGate.TryGetUnsuitableReason(provenance, session.SessionId) is not null)
        {
            return;
        }

        // A Complete flag alone cannot admit unrelated DTO entries. Each new project binding
        // must also be confirmed by the independently revalidated compiler/TaskOutput facts.
        foreach (var analyzer in analyzers)
        {
            if (!provenance!.Bindings.Any(binding => binding.Status == AnalyzerProvenanceBindingStatus.Confirmed
                && binding.ConsumerProjectId == ids[analyzer.Consumer]
                && Paths.Equals(binding.Identity, analyzer.OriginalPath))
                || !provenance.AnalyzerItems.Any(item => Paths.Equals(item.Identity, analyzer.OriginalPath)))
            {
                return;
            }
        }

        await ValidateEvidenceAsync(evidence, cancellationToken);
        var allowed = evidence.Entries.Where(entry => entry.Category is "analyzer" or "analyzer-dependency")
            .Select(entry => entry.PathOrRegion).Distinct(Paths).ToArray();
        var loader = new OwnedAnalyzerLoader(allowed);
        session.Own(loader);
        session.AnalyzerLoader = loader;
        var solution = session.Solution;
        foreach (var project in snapshot.Projects)
        {
            foreach (var analyzer in project.Analyzers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                solution = solution.AddAnalyzerReference(ids[project.Key], new AnalyzerFileReference(analyzer.OriginalPath, loader));
            }
        }

        if (!session.Workspace.TryApplyChanges(solution))
        {
            throw new InvalidDataException("Adhoc workspace refused admitted analyzer references.");
        }

        session.Publish(session.Workspace.CurrentSolution);
        session.AnalyzerAdmissionReady = true;
    }

    private static Dictionary<ProjectId, InstanceKey> BindInstances(Solution solution, AdmissionEvidence evidence)
    {
        var result = new Dictionary<ProjectId, InstanceKey>();
        foreach (var project in solution.Projects)
        {
            var matches = evidence.ExpectedInstances.Where(key => Paths.Equals(key.ProjectPath, project.FilePath)).ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidDataException("Project instance binding is missing or ambiguous.");
            }

            result.Add(project.Id, matches[0]);
        }

        if (result.Values.Distinct().Count() != evidence.ExpectedInstances.Length)
        {
            throw new InvalidDataException("Project instance inventory is incomplete.");
        }

        return result;
    }

    private static void ValidateInventories(SemanticSnapshot snapshot, AdmissionEvidence evidence)
    {
        ValidateAdmissionSchema(evidence);
        if (evidence.Disposition != "supported" || !evidence.AnalyzerInventoryComplete)
        {
            throw new InvalidDataException("Admission evidence is unsupported or analyzer inventory is incomplete.");
        }

        if (evidence.ExpectedInstances.IsDefault || evidence.ExpectedEdges.IsDefault
            || evidence.ExpectedCompilerInputPaths.IsDefault || evidence.Entries.IsDefault || evidence.Regions.IsDefault)
        {
            throw new InvalidDataException("Admission inventory arrays must be initialized.");
        }

        RequireSameSet(snapshot.Projects.Select(project => project.Key), evidence.ExpectedInstances, "project instances");
        if (snapshot.Projects.IsEmpty || evidence.ExpectedInstances.IsEmpty
            || !evidence.Entries.Any(entry => entry.Category == "expected-graph")
            || Path.GetExtension(snapshot.Request.WorkspacePath).Equals(".csproj", StringComparison.OrdinalIgnoreCase)
                && !evidence.ExpectedInstances.Any(key => Paths.Equals(key.ProjectPath, snapshot.Request.WorkspacePath))
            || snapshot.Request.SelectedRoots.Any(root => !evidence.ExpectedInstances.Any(key => Paths.Equals(key.ProjectPath, root))))
        {
            throw new InvalidDataException("Expected roots and independent graph evidence are incomplete.");
        }
        RequireSameSet(snapshot.ProjectReferences.Select(EdgeIdentity), evidence.ExpectedEdges.Select(EdgeIdentity), "project edges");
        var inputs = snapshot.Projects.SelectMany(project => project.Documents).Where(document => document.Role == "compile")
            .Select(document => document.Path).Distinct(Paths);
        RequireSameSet(inputs, evidence.ExpectedCompilerInputPaths, "compiler inputs", Paths);
        foreach (var project in snapshot.Projects)
        {
            var analyzerEvidence = evidence.Entries.Where(entry => entry.Category == "analyzer" && Equals(entry.Owner, project.Key)).ToArray();
            RequireSameSet(project.Analyzers.Select(analyzer => analyzer.OriginalPath),
                analyzerEvidence.Select(entry => entry.PathOrRegion), "analyzer inventory", Paths);
            foreach (var analyzer in project.Analyzers)
            {
                var entry = analyzerEvidence.Single(item => Paths.Equals(item.PathOrRegion, analyzer.OriginalPath));
                if (entry.IsAbsent || entry.Sha256 != analyzer.Sha256)
                {
                    throw new InvalidDataException("Analyzer hash inventory mismatch.");
                }
            }

            var dependencies = evidence.Entries.Where(entry => entry.Category == "analyzer-dependency"
                && Equals(entry.Owner, project.Key)).ToArray();
            RequireSameSet(project.Analyzers.SelectMany(analyzer => analyzer.DependencyPaths).Distinct(Paths),
                dependencies.Select(entry => entry.PathOrRegion), "analyzer dependencies", Paths);
            if (dependencies.Any(entry => entry.IsAbsent || entry.Sha256 is null))
            {
                throw new InvalidDataException("Analyzer dependency hash is missing.");
            }
        }

        if (evidence.Entries.Any(entry => entry.Category is "analyzer" or "analyzer-dependency"
            && (entry.Owner is null || !evidence.ExpectedInstances.Contains(entry.Owner))))
        {
            throw new InvalidDataException("Analyzer inventory has an unbound consumer.");
        }

        foreach (var project in snapshot.Projects)
        {
            foreach (var document in project.Documents)
            {
                if (!evidence.Entries.Any(entry => !entry.IsAbsent && entry.Sha256 is not null
                    && entry.Category == document.Role && Equals(entry.Owner, project.Key)
                    && Paths.Equals(entry.PathOrRegion, document.Path)))
                {
                    throw new InvalidDataException("Compiler dependency has no positive hash evidence.");
                }
            }

            foreach (var path in project.MetadataReferences.Select(reference => reference.Path)
                .Concat(project.MetadataReferences.Where(reference => reference.DocumentationPath is not null)
                    .Select(reference => reference.DocumentationPath!)))
            {
                if (!evidence.Entries.Any(entry => entry.Category == "metadata" && Equals(entry.Owner, project.Key)
                    && !entry.IsAbsent && entry.Sha256 is not null && Paths.Equals(entry.PathOrRegion, path)))
                {
                    throw new InvalidDataException("Metadata dependency has no bound positive hash evidence.");
                }
            }
        }
    }

    private static void ValidateAdmissionSchema(AdmissionEvidence evidence)
    {
        if (evidence is null || evidence.ProfileVersion != "sdk-project-v1/e0-1" || evidence.Disposition != "supported"
            || evidence.Reasons.IsDefault || evidence.ExpectedInstances.IsDefault || evidence.ExpectedEdges.IsDefault
            || evidence.ExpectedCompilerInputPaths.IsDefault || evidence.Entries.IsDefault || evidence.Regions.IsDefault)
        {
            throw new InvalidDataException("Unsupported or malformed admission profile.");
        }

        if (evidence.Reasons.Any(reason => reason is null) || evidence.ExpectedInstances.Any(key => key is null)
            || evidence.ExpectedEdges.Any(edge => edge is null || edge.From is null || edge.To is null || edge.Aliases.IsDefault)
            || evidence.ExpectedCompilerInputPaths.Any(path => path is null))
        {
            throw new InvalidDataException("Admission inventory contains missing values.");
        }

        foreach (var key in evidence.ExpectedInstances)
        {
            ValidateEvidenceInstance(key);
        }

        foreach (var edge in evidence.ExpectedEdges)
        {
            ValidateEvidenceInstance(edge.From);
            ValidateEvidenceInstance(edge.To);
            if (!evidence.ExpectedInstances.Contains(edge.From) || !evidence.ExpectedInstances.Contains(edge.To)
                || edge.Aliases.Any(alias => alias is null))
            {
                throw new InvalidDataException("Admission edge is malformed or has an unbound instance.");
            }
        }

        var categories = new HashSet<string>(StringComparer.Ordinal)
        {
            "project", "import", "compile", "additional", "analyzer-config", "metadata", "analyzer",
            "analyzer-dependency", "restore", "toolset", "sdk", "known-absent", "target-input", "expected-graph",
        };
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in evidence.Entries)
        {
            if (entry?.Owner is not null)
            {
                ValidateEvidenceInstance(entry.Owner);
            }

            if (entry is null || !categories.Contains(entry.Category) || string.IsNullOrWhiteSpace(entry.Source)
                || string.IsNullOrWhiteSpace(entry.SourceVersion) || string.IsNullOrWhiteSpace(entry.RawEvidenceLocator)
                || entry.Owner is not null && !evidence.ExpectedInstances.Contains(entry.Owner)
                || entry.IsAbsent != (entry.Category == "known-absent")
                || entry.IsAbsent && entry.Sha256 is not null || !entry.IsAbsent && !ValidHash(entry.Sha256))
            {
                throw new InvalidDataException("Malformed, unbound or unsupported dependency evidence.");
            }

            var path = RequireCanonicalPath(entry.PathOrRegion);
            var identity = JsonSerializer.Serialize(new
            {
                entry.Category, Owner = entry.Owner is null ? null : InstanceIdentity(entry.Owner),
                Path = OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path,
            });
            if (!identities.Add(identity))
            {
                throw new InvalidDataException("Duplicate dependency evidence entry.");
            }
        }

        var regionIdentities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var region in evidence.Regions)
        {
            if (region?.Owner is not null)
            {
                ValidateEvidenceInstance(region.Owner);
            }

            if (region is null || region.Owner is null || !evidence.ExpectedInstances.Contains(region.Owner)
                || !Paths.Equals(RequireCanonicalPath(region.Root), Path.GetDirectoryName(region.Owner.ProjectPath))
                || region.Role is not ("compile" or "additional" or "razor" or "web-asset" or "negative")
                || region.Includes.IsDefaultOrEmpty || region.Excludes.IsDefault || region.Members.IsDefault
                || !ValidHash(region.ManifestSha256) || string.IsNullOrWhiteSpace(region.RawEvidenceLocator))
            {
                throw new InvalidDataException("Malformed or unbound admission region.");
            }

            if (!regionIdentities.Add(InstanceIdentity(region.Owner) + "\n" + region.Role))
            {
                throw new InvalidDataException("Duplicate admission region.");
            }

            foreach (var pattern in region.Includes.Concat(region.Excludes))
            {
                ValidateGlob(pattern);
            }

            var paths = new HashSet<string>(Paths);
            string? previous = null;
            foreach (var member in region.Members)
            {
                if (member is null)
                {
                    throw new InvalidDataException("Admission region has a missing member.");
                }

                ValidateRelativePath(member.RelativePath, allowGlob: false);
                if (!ValidHash(member.Sha256) || !paths.Add(member.RelativePath)
                    || previous is not null && ComparePaths(previous, member.RelativePath) >= 0)
                {
                    throw new InvalidDataException("Malformed, duplicated or unsorted region member.");
                }

                previous = member.RelativePath;
            }

            if (ManifestHash(region.Members) != region.ManifestSha256)
            {
                throw new InvalidDataException("Admission manifest hash does not match its members.");
            }
        }
    }

    private static void ValidateEvidenceInstance(InstanceKey key)
    {
        if (key is null || key.EffectiveProperties is null || string.IsNullOrWhiteSpace(key.InnerTargetFramework))
        {
            throw new InvalidDataException("Admission instance key is malformed.");
        }

        RequireCanonicalPath(key.ProjectPath);
        foreach (var property in key.EffectiveProperties)
        {
            if (property.Key is not ("Configuration" or "Platform" or "TargetFramework" or "DesignTimeBuild"
                or "SkipCompilerExecution" or "ProvideCommandLineArgs" or "BuildProjectReferences" or "BuildingInsideVisualStudio")
                || property.Value is null)
            {
                throw new InvalidDataException("Admission instance contains unsupported effective properties.");
            }
        }

        if (key.EffectiveProperties.TryGetValue("TargetFramework", out var framework) && framework != key.InnerTargetFramework)
        {
            throw new InvalidDataException("Admission inner framework disagrees with its effective properties.");
        }
    }

    private static bool ValidHash(string? hash) => hash is { Length: 64 }
        && hash.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static string RequireCanonicalPath(string path)
    {
        var canonical = RequirePath(path);
        if (!string.Equals(path, canonical, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Admission path must be canonical.");
        }

        return canonical;
    }

    private static int ComparePaths(string left, string right)
    {
        var result = Paths.Compare(left, right);
        return result != 0 ? result : StringComparer.Ordinal.Compare(left, right);
    }

    private static void ValidateGlob(string pattern) => ValidateRelativePath(pattern, allowGlob: true);

    private static void ValidateRelativePath(string path, bool allowGlob)
    {
        if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path) || path.Contains('\\')
            || path.IndexOfAny(['$', '@', '(', ')', '[', ']', ';', ':', '"', '\'']) >= 0)
        {
            throw new InvalidDataException("Region path contains unsupported or unresolved syntax.");
        }

        foreach (var segment in path.Split('/'))
        {
            if (segment is "" or "." or ".." || segment.Contains("**", StringComparison.Ordinal) && segment != "**"
                || !allowGlob && segment.IndexOfAny(['*', '?']) >= 0)
            {
                throw new InvalidDataException("Region path contains a dot, escaping or malformed glob segment.");
            }
        }
    }

    private static void RequireSameSet<T>(IEnumerable<T> left, IEnumerable<T> right, string name, IEqualityComparer<T>? comparer = null)
    {
        var leftValues = left.ToArray();
        var rightValues = right.ToArray();
        var leftSet = new HashSet<T>(leftValues, comparer);
        var rightSet = new HashSet<T>(rightValues, comparer);
        if (leftSet.Count != leftValues.Length || rightSet.Count != rightValues.Length || !leftSet.SetEquals(rightSet))
        {
            throw new InvalidDataException($"Exact {name} inventory mismatch or duplicate.");
        }
    }

    private static string EdgeIdentity(ProjectEdge edge) => JsonSerializer.Serialize(new
    {
        From = InstanceIdentity(edge.From), To = InstanceIdentity(edge.To), edge.Aliases, edge.EmbedInteropTypes,
    });

    private static string InstanceIdentity(InstanceKey key) => JsonSerializer.Serialize(new
    {
        Path = OperatingSystem.IsWindows() ? key.ProjectPath.ToUpperInvariant() : key.ProjectPath,
        key.InnerTargetFramework, key.EffectiveProperties,
    });

    private static async Task ValidateEvidenceAsync(AdmissionEvidence evidence, CancellationToken cancellationToken)
    {
        foreach (var entry in evidence.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = RequirePath(entry.PathOrRegion);
            if (entry.IsAbsent)
            {
                if (File.Exists(path) || Directory.Exists(path))
                {
                    throw new InvalidDataException("Known-absent dependency appeared.");
                }

                continue;
            }

            if (entry.Sha256 is null || !File.Exists(path)
                || Hash(await ReadStableBytesAsync(path, cancellationToken)) != entry.Sha256)
            {
                throw new InvalidDataException("Dependency content changed or is unreadable.");
            }
        }

        foreach (var group in evidence.Regions.GroupBy(region => region.Root, Paths))
        {
            var allRegions = group.ToImmutableArray();
            var rootMembers = await EnumerateRegionAsync(allRegions, cancellationToken);
            foreach (var region in allRegions)
            {
                var members = rootMembers.Where(member => RegionMatches(region, member.RelativePath)).ToImmutableArray();
                if (members.Length != region.Members.Length
                    || !members.Zip(region.Members).All(pair => pair.First == pair.Second)
                    || ManifestHash(members) != region.ManifestSha256)
                {
                    throw new InvalidDataException("Region membership or manifest changed.");
                }
            }
        }
    }

    private static bool RegionMatches(RegionEntry region, string relative) =>
        region.Includes.Any(pattern => GlobMatches(relative, pattern))
            && !region.Excludes.Any(pattern => GlobMatches(relative, pattern));

    private static async Task<ImmutableArray<RegionMember>> EnumerateRegionAsync(
        ImmutableArray<RegionEntry> regions,
        CancellationToken cancellationToken)
    {
        var root = RequirePath(regions[0].Root);
        var members = new Dictionary<string, RegionMember>(Paths);
        var directories = new Dictionary<string, DateTime>(Paths);
        var observedPaths = new HashSet<string>(Paths);
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        var entries = 0;
        long totalBytes = 0;
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (directory.Depth > 64)
            {
                throw new InvalidDataException("Region enumeration depth limit exceeded.");
            }

            if (!Directory.Exists(directory.Path))
            {
                throw new InvalidDataException("Region directory disappeared during enumeration.");
            }

            RejectReparse(directory.Path);
            directories.Add(directory.Path, Directory.GetLastWriteTimeUtc(directory.Path));
            foreach (var path in Directory.EnumerateFileSystemEntries(directory.Path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++entries > 100000)
                {
                    throw new InvalidDataException("Region enumeration entry limit exceeded.");
                }

                RejectReparse(path);
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (!observedPaths.Add(relative))
                {
                    throw new InvalidDataException("Region entry path case collision.");
                }

                if (Directory.Exists(path))
                {
                    pending.Push((path, directory.Depth + 1));
                    continue;
                }

                if (!regions.Any(region => RegionMatches(region, relative)))
                {
                    continue;
                }

                var fileLength = new FileInfo(path).Length;
                if (fileLength > 256L * 1024 * 1024 - totalBytes)
                {
                    throw new InvalidDataException("Region enumeration byte limit exceeded.");
                }

                var bytes = await ReadStableBytesAsync(path, cancellationToken);
                totalBytes += bytes.Length;
                if (!members.TryAdd(relative, new RegionMember(relative, Hash(bytes))))
                {
                    throw new InvalidDataException("Region member path case collision.");
                }
            }
        }

        foreach (var pair in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(pair.Key) || Directory.GetLastWriteTimeUtc(pair.Key) != pair.Value)
            {
                throw new InvalidDataException("Region directory changed during enumeration.");
            }
        }

        return members.Values.OrderBy(member => member.RelativePath, Paths)
            .ThenBy(member => member.RelativePath, StringComparer.Ordinal).ToImmutableArray();
    }

    private static string ManifestHash(ImmutableArray<RegionMember> members)
    {
        var text = new StringBuilder("e0-region-1\n");
        foreach (var member in members)
        {
            text.Append(JsonSerializer.Serialize(new[] { member.RelativePath, member.Sha256 })).Append('\n');
        }

        return Hash(Encoding.UTF8.GetBytes(text.ToString()));
    }

    private static bool GlobMatches(string path, string pattern)
    {
        ValidateGlob(pattern);
        var normalized = pattern;
        var expression = new StringBuilder("\\A");
        for (var index = 0; index < normalized.Length; index++)
        {
            var character = normalized[index];
            if (character == '*' && index + 1 < normalized.Length && normalized[index + 1] == '*')
            {
                index++;
                if (index + 1 < normalized.Length && normalized[index + 1] == '/')
                {
                    index++;
                    expression.Append("(?:.*/)?");
                }
                else
                {
                    expression.Append(".*");
                }
            }
            else
            {
                expression.Append(character switch { '*' => "[^/]*", '?' => "[^/]", _ => Regex.Escape(character.ToString()) });
            }
        }

        expression.Append("\\z");
        return Regex.IsMatch(path, expression.ToString(),
            OperatingSystem.IsWindows() ? RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline
                : RegexOptions.CultureInvariant | RegexOptions.Singleline,
            TimeSpan.FromSeconds(1));
    }

    private static async Task<byte[]> ReadStableBytesAsync(string path, CancellationToken cancellationToken)
    {
        RejectReparse(path);
        var before = new FileInfo(path);
        var length = before.Length;
        var modified = before.LastWriteTimeUtc;
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var after = new FileInfo(path);
        if (after.Length != length || after.LastWriteTimeUtc != modified || bytes.LongLength != length)
        {
            throw new InvalidDataException("Input changed during its read.");
        }

        return bytes;
    }

    private static void RejectReparse(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Reparse points are unsupported experiment inputs.");
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string RequirePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new InvalidDataException("An absolute file path is required.");
        }

        return Path.GetFullPath(path);
    }

    private static bool IsUnder(string path, string directory) => path.StartsWith(
        Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static IEnumerable<(TextDocument Document, string Role)> AllDocuments(Project project) =>
        project.Documents.Select(document => ((TextDocument)document, "compile"))
            .Concat(project.AdditionalDocuments.Select(document => (document, "additional")))
            .Concat(project.AnalyzerConfigDocuments.Select(document => ((TextDocument)document, "analyzer-config")));

    private static async Task<Dictionary<string, SourceText>> ReadTextsAsync(
        SemanticSnapshot snapshot,
        AdmissionEvidence evidence,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, SourceText>(Paths);
        foreach (var group in snapshot.Projects.SelectMany(project => project.Documents).GroupBy(document => document.Path, Paths))
        {
            var policies = group.Select(document => JsonSerializer.Serialize(document.Encoding)).Distinct().ToArray();
            if (policies.Length != 1)
            {
                throw new InvalidDataException("Linked memberships have inconsistent decoding policies.");
            }

            var bytes = await ReadStableBytesAsync(group.Key, cancellationToken);
            foreach (var document in group)
            {
                VerifyConsumedHash(evidence, document.Project, document.Role, document.Path, bytes);
            }

            result.Add(group.Key, DecodeText(bytes, group.First().Encoding));
        }

        return result;
    }

    private static async Task<Dictionary<InstanceKey, ImmutableArray<MetadataReference>>> ReadReferencesAsync(
        SemanticSnapshot snapshot,
        AdmissionEvidence evidence,
        List<IDisposable> resources,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<InstanceKey, ImmutableArray<MetadataReference>>();
        foreach (var project in snapshot.Projects)
        {
            var references = ImmutableArray.CreateBuilder<MetadataReference>();
            foreach (var reference in project.MetadataReferences)
            {
                var bytes = await ReadStableBytesAsync(reference.Path, cancellationToken);
                VerifyConsumedHash(evidence, project.Key, "metadata", reference.Path, bytes);
                DocumentationProvider? documentation = null;
                if (reference.DocumentationPath is not null)
                {
                    var documentationBytes = await ReadStableBytesAsync(reference.DocumentationPath, cancellationToken);
                    VerifyConsumedHash(evidence, project.Key, "metadata", reference.DocumentationPath, documentationBytes);
                    documentation = XmlDocumentationProvider.CreateFromBytes(documentationBytes);
                }

                Metadata metadata = reference.Kind == MetadataImageKind.Assembly
                    ? AssemblyMetadata.CreateFromImage(bytes.ToImmutableArray())
                    : ModuleMetadata.CreateFromImage(bytes.ToImmutableArray());
                resources.Add(metadata);
                references.Add(new OwnedMetadataReference(metadata,
                    new MetadataReferenceProperties(reference.Kind, reference.Aliases, reference.EmbedInteropTypes),
                    documentation ?? DocumentationProvider.Default, reference.Path, reference.DocumentationPath));
            }

            result.Add(project.Key, references.ToImmutable());
        }

        return result;
    }

    private static void VerifyConsumedHash(AdmissionEvidence evidence, InstanceKey owner, string role, string path, byte[] bytes)
    {
        var entry = evidence.Entries.Single(item => item.Category == role && Equals(item.Owner, owner)
            && Paths.Equals(item.PathOrRegion, path));
        if (Hash(bytes) != entry.Sha256)
        {
            throw new InvalidDataException("Consumed input bytes differ from their admission evidence.");
        }
    }

    private static async Task ValidateConsumedBytesAsync(
        SemanticSnapshot snapshot,
        AdmissionEvidence evidence,
        Dictionary<string, SourceText> texts,
        CancellationToken cancellationToken)
    {
        // The fixture grants immutable ownership, but a hostile or accidental mutation must
        // still fail the attempt instead of publishing texts from a different byte generation.
        await ValidateEvidenceAsync(evidence, cancellationToken);
        foreach (var document in snapshot.Projects.SelectMany(project => project.Documents))
        {
            var bytes = await ReadStableBytesAsync(document.Path, cancellationToken);
            var expected = evidence.Entries.Single(entry => entry.Category == document.Role
                && Equals(entry.Owner, document.Project) && Paths.Equals(entry.PathOrRegion, document.Path));
            if (Hash(bytes) != expected.Sha256 || !DecodeText(bytes, document.Encoding).ContentEquals(texts[document.Path]))
            {
                throw new InvalidDataException("Consumed compiler bytes differ from their admitted generation.");
            }
        }
    }

    private static EncodingPolicy CaptureEncoding(Encoding? encoding, byte[] bytes)
    {
        if (encoding is null)
        {
            return new EncodingPolicy(null, [], "unspecified", null, "unspecified", null);
        }

        var preamble = encoding.GetPreamble();
        if (preamble.Length != 0 && !bytes.AsSpan().StartsWith(preamble))
        {
            // GetPreamble on Roslyn's UTF-8 default need not describe the actual disk BOM.
            // Retain disk/write policy rather than silently inserting a BOM on the first edit.
            preamble = [];
        }

        var (decoderKind, decoderReplacement) = encoding.DecoderFallback switch
        {
            DecoderExceptionFallback => ("exception", (string?)null),
            DecoderReplacementFallback replacement => ("replacement", replacement.DefaultString),
            _ => throw new InvalidDataException("unsupported-encoding: custom decoder fallback"),
        };
        var (encoderKind, encoderReplacement) = encoding.EncoderFallback switch
        {
            EncoderExceptionFallback => ("exception", (string?)null),
            EncoderReplacementFallback replacement => ("replacement", replacement.DefaultString),
            _ => throw new InvalidDataException("unsupported-encoding: custom encoder fallback"),
        };
        var policy = new EncodingPolicy(encoding.CodePage, preamble.ToImmutableArray(), decoderKind,
            decoderReplacement, encoderKind, encoderReplacement);
        var restored = RestoreEncoding(policy)!;
        var payload = bytes.AsSpan(preamble.Length).ToArray();
        if (!restored.GetBytes(restored.GetString(payload)).AsSpan().SequenceEqual(payload))
        {
            throw new InvalidDataException("unsupported-encoding: byte round trip failed");
        }

        return policy;
    }

    private static Encoding? RestoreEncoding(EncodingPolicy policy)
    {
        if (policy.CodePage is null)
        {
            return null;
        }

        Encoding encoding = policy.CodePage switch
        {
            65001 => new UTF8Encoding(policy.Preamble.Length != 0),
            1200 => new UnicodeEncoding(false, policy.Preamble.Length != 0),
            1201 => new UnicodeEncoding(true, policy.Preamble.Length != 0),
            _ => Encoding.GetEncoding(policy.CodePage.Value),
        };
        encoding = (Encoding)encoding.Clone();
        encoding.DecoderFallback = policy.DecoderFallback switch
        {
            "exception" => DecoderFallback.ExceptionFallback,
            "replacement" => new DecoderReplacementFallback(policy.DecoderReplacement!),
            _ => throw new InvalidDataException("unsupported-encoding: decoder fallback"),
        };
        encoding.EncoderFallback = policy.EncoderFallback switch
        {
            "exception" => EncoderFallback.ExceptionFallback,
            "replacement" => new EncoderReplacementFallback(policy.EncoderReplacement!),
            _ => throw new InvalidDataException("unsupported-encoding: encoder fallback"),
        };
        if (!encoding.GetPreamble().AsSpan().SequenceEqual(policy.Preamble.AsSpan()))
        {
            throw new InvalidDataException("unsupported-encoding: preamble reconstruction failed");
        }

        return encoding;
    }

    private static SourceText DecodeText(byte[] bytes, EncodingPolicy policy)
    {
        var encoding = RestoreEncoding(policy);
        if (encoding is null)
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var detected = SourceText.From(stream, encoding: null);
            return SourceText.From(detected.ToString(), encoding: null);
        }

        var preamble = policy.Preamble.AsSpan();
        if (!bytes.AsSpan().StartsWith(preamble))
        {
            throw new InvalidDataException("Input preamble changed.");
        }

        return SourceText.From(encoding.GetString(bytes.AsSpan(preamble.Length)), encoding);
    }

    private static async Task<List<WriteItem>> BuildWritePlanAsync(
        ExperimentSession session,
        Solution current,
        Solution candidate,
        CancellationToken cancellationToken)
    {
        if (!current.ProjectIds.SequenceEqual(candidate.ProjectIds))
        {
            throw new InvalidDataException("unsupported-project-change");
        }

        var changedPaths = new HashSet<string>(Paths);
        foreach (var project in candidate.Projects)
        {
            var previous = current.GetProject(project.Id)!;
            if (!Equals(project.ParseOptions, previous.ParseOptions) || !Equals(project.CompilationOptions, previous.CompilationOptions)
                || !project.ProjectReferences.SequenceEqual(previous.ProjectReferences)
                || !project.MetadataReferences.SequenceEqual(previous.MetadataReferences)
                || project.Name != previous.Name || project.AssemblyName != previous.AssemblyName
                || project.FilePath != previous.FilePath || project.OutputFilePath != previous.OutputFilePath
                || project.OutputRefFilePath != previous.OutputRefFilePath)
            {
                throw new InvalidDataException("unsupported-project-change");
            }

            var changes = project.GetChanges(previous);
            if (changes.GetAddedAdditionalDocuments().Any() || changes.GetRemovedAdditionalDocuments().Any()
                || changes.GetChangedAdditionalDocuments().Any() || changes.GetAddedAnalyzerConfigDocuments().Any()
                || changes.GetRemovedAnalyzerConfigDocuments().Any() || changes.GetChangedAnalyzerConfigDocuments().Any())
            {
                throw new InvalidDataException("unsupported-non-csharp-write");
            }

            foreach (var id in changes.GetChangedDocuments().Concat(changes.GetAddedDocuments()))
            {
                var document = candidate.GetDocument(id)!;
                changedPaths.Add(RequirePath(document.FilePath));
                if (current.GetDocument(id) is { } old && !Paths.Equals(old.FilePath, document.FilePath))
                {
                    changedPaths.Add(RequirePath(old.FilePath));
                }
            }

            foreach (var id in changes.GetRemovedDocuments())
            {
                changedPaths.Add(RequirePath(current.GetDocument(id)!.FilePath));
            }
        }

        var plan = new List<WriteItem>();
        foreach (var path in changedPaths.OrderBy(path => path, Paths))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("unsupported-non-csharp-write");
            }

            var oldDocuments = current.Projects.SelectMany(project => project.Documents)
                .Where(document => Paths.Equals(document.FilePath, path)).ToArray();
            var newDocuments = candidate.Projects.SelectMany(project => project.Documents)
                .Where(document => Paths.Equals(document.FilePath, path)).ToArray();
            var membershipChanged = oldDocuments.Length != newDocuments.Length
                || oldDocuments.Any(document => !newDocuments.Any(updated => updated.Id == document.Id));
            foreach (var document in oldDocuments.Concat(newDocuments))
            {
                RequireSdkGlobWrite(session, document.Project.FilePath!, path, membershipChanged);
            }

            if (oldDocuments.Length == 0 && (File.Exists(path) || Directory.Exists(path)))
            {
                throw new InvalidDataException("new-source-path-collision");
            }

            if (oldDocuments.Length != 0 && !File.Exists(path))
            {
                throw new InvalidDataException("missing-on-disk");
            }

            if (newDocuments.Length == 0)
            {
                plan.Add(new WriteItem(path, null, null));
                continue;
            }

            var texts = new List<SourceText>();
            foreach (var document in newDocuments)
            {
                texts.Add(await document.GetTextAsync(cancellationToken));
            }

            if (texts.Select(text => text.ToString()).Distinct(StringComparer.Ordinal).Count() != 1)
            {
                throw new InvalidDataException("linked-source-text-conflict");
            }

            if (oldDocuments.Length != 0 && oldDocuments.Any(document =>
                !newDocuments.Any(updated => updated.Id == document.Id)))
            {
                throw new InvalidDataException("linked-source-membership-change");
            }

            var originalText = oldDocuments.Length == 0 ? texts[0] : await oldDocuments[0].GetTextAsync(cancellationToken);
            if (originalText.Encoding is null)
            {
                throw new InvalidDataException("unsupported-encoding");
            }

            var bytes = oldDocuments.Length == 0 ? originalText.Encoding.GetPreamble()
                : await ReadStableBytesAsync(path, cancellationToken);
            var encoding = RestoreEncoding(CaptureEncoding(originalText.Encoding, bytes))!;
            try
            {
                var encoded = encoding.GetBytes(texts[0].ToString());
                if (encoding.GetString(encoded) != texts[0].ToString())
                {
                    throw new InvalidDataException("unsupported-encoding: output character round trip failed");
                }
            }
            catch (EncoderFallbackException exception)
            {
                throw new InvalidDataException("unsupported-encoding: output cannot be encoded", exception);
            }

            plan.Add(new WriteItem(path, texts[0].ToString(), encoding));
        }

        return plan;
    }

    private static async Task<Solution> NormalizeLinkedEditsAsync(
        Solution current,
        Solution candidate,
        CancellationToken cancellationToken)
    {
        var changed = new Dictionary<string, SourceText>(Paths);
        foreach (var project in candidate.Projects)
        {
            var previous = current.GetProject(project.Id);
            if (previous is null)
            {
                continue;
            }

            foreach (var id in project.GetChanges(previous).GetChangedDocuments())
            {
                var document = candidate.GetDocument(id)!;
                var old = current.GetDocument(id)!;
                if (!Paths.Equals(document.FilePath, old.FilePath))
                {
                    continue;
                }

                var text = await document.GetTextAsync(cancellationToken);
                if (text.ContentEquals(await old.GetTextAsync(cancellationToken)))
                {
                    continue;
                }

                var path = RequirePath(document.FilePath);
                if (changed.TryGetValue(path, out var alreadyChanged) && !alreadyChanged.ContentEquals(text))
                {
                    throw new InvalidDataException("linked-source-text-conflict");
                }

                changed[path] = text;
            }
        }

        foreach (var pair in changed)
        {
            foreach (var id in candidate.GetDocumentIdsWithFilePath(pair.Key))
            {
                candidate = candidate.WithDocumentText(id, pair.Value);
            }
        }

        return candidate;
    }

    private static void RequireSdkGlobWrite(ExperimentSession session, string projectPath, string path, bool membershipChanged)
    {
        var directory = Path.GetDirectoryName(projectPath)!;
        if (!IsUnder(path, directory))
        {
            // Linked sources may be edited only when every existing membership has explicit
            // dependency evidence. Adding/removing an external membership requires evaluation.
            if (!membershipChanged && session.Admission?.Entries.Any(entry => !entry.IsAbsent && entry.Sha256 is not null
                && Paths.Equals(entry.PathOrRegion, path)) == true)
            {
                return;
            }

            throw new InvalidDataException("unsupported-external-source-write");
        }

        using var stream = File.OpenRead(projectPath);
        using var reader = System.Xml.XmlReader.Create(stream, new System.Xml.XmlReaderSettings
        {
            DtdProcessing = System.Xml.DtdProcessing.Prohibit,
            XmlResolver = null,
        });
        var xml = XDocument.Load(reader);
        if (xml.Root?.Attribute("Sdk")?.Value != "Microsoft.NET.Sdk"
            || xml.Descendants().Any(element => element.Name.LocalName is "EnableDefaultCompileItems" or "EnableDefaultItems"
                && element.Value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase))
            || xml.Descendants().Where(element => element.Name.LocalName == "Compile" && element.Attribute("Include") is not null)
                .Any(element => !IsSupportedExternalInclude(element.Attribute("Include")!.Value, directory, session.Admission)))
        {
            throw new InvalidDataException("unsupported-compile-membership");
        }

        var relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
        var regions = session.Admission?.Regions.Where(region => Paths.Equals(region.Owner.ProjectPath, projectPath)
            && region.Role == "compile").ToArray() ?? [];
        if (regions.Length != 1 || !Paths.Equals(regions[0].Root, directory)
            || !regions[0].Includes.Any(pattern => GlobMatches(relative, pattern))
            || regions[0].Excludes.Any(pattern => GlobMatches(relative, pattern)))
        {
            throw new InvalidDataException("unsupported-compile-region");
        }
    }

    private static bool IsSupportedExternalInclude(string include, string directory, AdmissionEvidence? admission)
    {
        if (include.IndexOfAny(['*', '?', '$', '@', ';']) >= 0)
        {
            return false;
        }

        var path = Path.GetFullPath(include, directory);
        return !IsUnder(path, directory) && admission?.Entries.Any(entry => entry.Category == "compile"
            && !entry.IsAbsent && entry.Sha256 is not null && Paths.Equals(entry.PathOrRegion, path)) == true;
    }

    private static async Task ReconcileCommittedAsync(ExperimentSession session, List<string> saved, Solution? candidate)
    {
        if (saved.Count == 0)
        {
            return;
        }

        try
        {
            await ReconcileUnderLockAsync(session, saved.ToImmutableArray(), CancellationToken.None, candidate);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Keep the previous borrowed snapshot when any committed input cannot be read.
            // The result still reports persistence and the unapplied graph for the caller.
        }
    }

    private static async Task ReconcileUnderLockAsync(
        ExperimentSession session,
        ImmutableArray<string> changedPaths,
        CancellationToken cancellationToken,
        Solution? persistedCandidate = null)
    {
        var solution = session.Solution;
        if (persistedCandidate is not null)
        {
            var persistedPaths = changedPaths.ToHashSet(Paths);
            foreach (var document in persistedCandidate.Projects.SelectMany(project => project.Documents)
                .Where(document => document.FilePath is not null && persistedPaths.Contains(document.FilePath)))
            {
                if (solution.GetDocument(document.Id) is null)
                {
                    var text = await document.GetTextAsync(cancellationToken);
                    solution = solution.AddDocument(DocumentInfo.Create(document.Id, document.Name,
                        document.Folders, document.SourceCodeKind,
                        TextLoader.From(TextAndVersion.Create(text, VersionStamp.Create(), document.FilePath)),
                        document.FilePath));
                }
            }
        }

        foreach (var path in changedPaths.Distinct(Paths))
        {
            var documents = solution.Projects.SelectMany(AllDocuments)
                .Where(item => Paths.Equals(item.Document.FilePath, path)).ToArray();
            if (documents.Length == 0)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path))
            {
                if (persistedCandidate is null || persistedCandidate.Projects.SelectMany(project => project.Documents)
                    .Any(document => Paths.Equals(document.FilePath, path)))
                {
                    throw new FileNotFoundException("A reconciliation input is missing; the published snapshot remains pending.", path);
                }

                foreach (var (document, role) in documents)
                {
                    solution = role switch
                    {
                        "compile" => solution.RemoveDocument(document.Id),
                        "additional" => solution.RemoveAdditionalDocument(document.Id),
                        _ => solution.RemoveAnalyzerConfigDocument(document.Id),
                    };
                }

                continue;
            }

            var bytes = await ReadStableBytesAsync(path, cancellationToken);
            var old = await documents[0].Document.GetTextAsync(cancellationToken);
            var policy = CaptureEncoding(old.Encoding, bytes);
            var text = DecodeText(bytes, policy);
            foreach (var (document, role) in documents)
            {
                solution = role switch
                {
                    "compile" => solution.WithDocumentText(document.Id, text),
                    "additional" => solution.WithAdditionalDocumentText(document.Id, text),
                    _ => solution.WithAnalyzerConfigDocumentText(document.Id, text),
                };
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Published immutable texts include additional/config updates even on a host whose
        // TryApplyChanges cannot apply these roles. Reconciliation never invokes a writer.
        session.Publish(solution);
    }

    private static ImmutableSortedDictionary<string, string?> CaptureParseOptions(ParseOptions? options)
    {
        if (options is not CSharpParseOptions csharp || csharp.Errors.Any())
        {
            throw new InvalidDataException("unsupported-options: C# parse options required");
        }

        var values = ImmutableSortedDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        values.Add("SpecifiedLanguageVersion", Json(csharp.SpecifiedLanguageVersion.ToString()));
        values.Add("LanguageVersion", Json(csharp.LanguageVersion.ToString()));
        values.Add("SpecifiedKind", Json(csharp.SpecifiedKind.ToString()));
        values.Add("Kind", Json(csharp.Kind.ToString()));
        values.Add("DocumentationMode", Json(csharp.DocumentationMode.ToString()));
        values.Add("PreprocessorSymbolNames", Json(csharp.PreprocessorSymbolNames.ToArray()));
        values.Add("Features", Json(csharp.Features.ToImmutableSortedDictionary(StringComparer.Ordinal)));
        return values.ToImmutable();
    }

    private static CSharpParseOptions RestoreParseOptions(ImmutableSortedDictionary<string, string?> values)
    {
        var options = new CSharpParseOptions(EnumValue<LanguageVersion>(values, "SpecifiedLanguageVersion"),
                EnumValue<DocumentationMode>(values, "DocumentationMode"), EnumValue<SourceCodeKind>(values, "SpecifiedKind"),
                Value<string[]>(values, "PreprocessorSymbolNames"))
            .WithFeatures(Value<Dictionary<string, string>>(values, "Features"));
        if (!CaptureParseOptions(options).SequenceEqual(values))
        {
            throw new InvalidDataException("unsupported-options: parse option reconstruction mismatch");
        }

        return options;
    }

    private static ImmutableSortedDictionary<string, string?> CaptureCompilationOptions(CompilationOptions? options)
    {
        if (options is not CSharpCompilationOptions csharp || csharp.Errors.Any())
        {
            throw new InvalidDataException("unsupported-options: C# compilation options required");
        }

        var values = ImmutableSortedDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        values.Add("OutputKind", Json(csharp.OutputKind.ToString()));
        values.Add("ModuleName", Json(csharp.ModuleName));
        values.Add("MainTypeName", Json(csharp.MainTypeName));
        values.Add("ScriptClassName", Json(csharp.ScriptClassName));
        values.Add("Usings", Json(csharp.Usings));
        values.Add("OptimizationLevel", Json(csharp.OptimizationLevel.ToString()));
        values.Add("CheckOverflow", Json(csharp.CheckOverflow));
        values.Add("AllowUnsafe", Json(csharp.AllowUnsafe));
        values.Add("CryptoKeyContainer", Json(csharp.CryptoKeyContainer));
        values.Add("CryptoKeyFile", Json(csharp.CryptoKeyFile));
        values.Add("CryptoPublicKey", Json(Convert.ToBase64String(csharp.CryptoPublicKey.AsSpan())));
        values.Add("DelaySign", Json(csharp.DelaySign));
        values.Add("PublicSign", Json(csharp.PublicSign));
        values.Add("Platform", Json(csharp.Platform.ToString()));
        values.Add("GeneralDiagnosticOption", Json(csharp.GeneralDiagnosticOption.ToString()));
        values.Add("WarningLevel", Json(csharp.WarningLevel));
        values.Add("SpecificDiagnosticOptions", Json(csharp.SpecificDiagnosticOptions
            .ToImmutableSortedDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.Ordinal)));
        values.Add("ConcurrentBuild", Json(csharp.ConcurrentBuild));
        values.Add("Deterministic", Json(csharp.Deterministic));
        values.Add("MetadataImportOptions", Json(csharp.MetadataImportOptions.ToString()));
        values.Add("NullableContextOptions", Json(csharp.NullableContextOptions.ToString()));
        values.Add("ReportSuppressedDiagnostics", Json(csharp.ReportSuppressedDiagnostics));
        values.Add("MetadataReferenceResolver", csharp.MetadataReferenceResolver is null ? null
            : throw new InvalidDataException("unsupported-options: metadata reference resolver"));
        values.Add("XmlReferenceResolver", csharp.XmlReferenceResolver switch
        {
            null => null,
            XmlFileResolver resolver => Json(new { Kind = "XmlFileResolver", resolver.BaseDirectory }),
            _ => throw new InvalidDataException("unsupported-options: XML reference resolver"),
        });
        values.Add("SourceReferenceResolver", csharp.SourceReferenceResolver switch
        {
            null => null,
            SourceFileResolver resolver => Json(new
            {
                Kind = "SourceFileResolver", resolver.SearchPaths, resolver.BaseDirectory, resolver.PathMap,
            }),
            _ => throw new InvalidDataException("unsupported-options: source reference resolver"),
        });
        values.Add("StrongNameProvider", csharp.StrongNameProvider switch
        {
            null => null,
            DesktopStrongNameProvider => throw new InvalidDataException("unsupported-options: DesktopStrongNameProvider search paths have no public getter"),
            _ => throw new InvalidDataException("unsupported-options: strong name provider"),
        });
        values.Add("AssemblyIdentityComparer", csharp.AssemblyIdentityComparer switch
        {
            null => null,
            var comparer when ReferenceEquals(comparer, AssemblyIdentityComparer.Default) => Json(new { Kind = "default" }),
            var comparer when ReferenceEquals(comparer, DesktopAssemblyIdentityComparer.Default) => Json(new { Kind = "DesktopAssemblyIdentityComparer" }),
            _ => throw new InvalidDataException("unsupported-options: assembly identity comparer"),
        });
        return values.ToImmutable();
    }

    private static CSharpCompilationOptions RestoreCompilationOptions(ImmutableSortedDictionary<string, string?> values)
    {
        var options = new CSharpCompilationOptions(EnumValue<OutputKind>(values, "OutputKind"))
            .WithModuleName(Value<string?>(values, "ModuleName"))
            .WithMainTypeName(Value<string?>(values, "MainTypeName"))
            .WithScriptClassName(Value<string?>(values, "ScriptClassName"))
            .WithUsings(Value<string[]>(values, "Usings"))
            .WithOptimizationLevel(EnumValue<OptimizationLevel>(values, "OptimizationLevel"))
            .WithOverflowChecks(Value<bool>(values, "CheckOverflow"))
            .WithAllowUnsafe(Value<bool>(values, "AllowUnsafe"))
            .WithCryptoKeyContainer(Value<string?>(values, "CryptoKeyContainer"))
            .WithCryptoKeyFile(Value<string?>(values, "CryptoKeyFile"))
            .WithCryptoPublicKey(Convert.FromBase64String(Value<string>(values, "CryptoPublicKey")).ToImmutableArray())
            .WithDelaySign(Value<bool?>(values, "DelaySign"))
            .WithPublicSign(Value<bool>(values, "PublicSign"))
            .WithPlatform(EnumValue<Platform>(values, "Platform"))
            .WithGeneralDiagnosticOption(EnumValue<ReportDiagnostic>(values, "GeneralDiagnosticOption"))
            .WithWarningLevel(Value<int>(values, "WarningLevel"))
            .WithSpecificDiagnosticOptions(Value<Dictionary<string, string>>(values, "SpecificDiagnosticOptions")
                .Select(pair => new KeyValuePair<string, ReportDiagnostic>(pair.Key, Enum.Parse<ReportDiagnostic>(pair.Value))))
            .WithConcurrentBuild(Value<bool>(values, "ConcurrentBuild"))
            .WithDeterministic(Value<bool>(values, "Deterministic"))
            .WithMetadataImportOptions(EnumValue<MetadataImportOptions>(values, "MetadataImportOptions"))
            .WithNullableContextOptions(EnumValue<NullableContextOptions>(values, "NullableContextOptions"))
            .WithReportSuppressedDiagnostics(Value<bool>(values, "ReportSuppressedDiagnostics"))
            .WithMetadataReferenceResolver(null)
            .WithXmlReferenceResolver(RestoreXmlResolver(values["XmlReferenceResolver"]))
            .WithSourceReferenceResolver(RestoreSourceResolver(values["SourceReferenceResolver"]))
            .WithStrongNameProvider(RestoreStrongNameProvider(values["StrongNameProvider"]))
            .WithAssemblyIdentityComparer(RestoreIdentityComparer(values["AssemblyIdentityComparer"]));
        if (!CaptureCompilationOptions(options).SequenceEqual(values))
        {
            throw new InvalidDataException("unsupported-options: compilation option reconstruction mismatch");
        }

        return options;
    }

    private static XmlReferenceResolver? RestoreXmlResolver(string? descriptor)
    {
        if (descriptor is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(descriptor);
        return new XmlFileResolver(document.RootElement.GetProperty("BaseDirectory").GetString());
    }

    private static SourceReferenceResolver? RestoreSourceResolver(string? descriptor)
    {
        if (descriptor is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(descriptor);
        var root = document.RootElement;
        return new SourceFileResolver(root.GetProperty("SearchPaths").Deserialize<ImmutableArray<string>>(),
            root.GetProperty("BaseDirectory").GetString(),
            root.GetProperty("PathMap").Deserialize<ImmutableArray<KeyValuePair<string, string>>>());
    }

    private static StrongNameProvider? RestoreStrongNameProvider(string? descriptor)
    {
        if (descriptor is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(descriptor);
        return new DesktopStrongNameProvider(document.RootElement.GetProperty("KeyFileSearchPaths")
            .Deserialize<ImmutableArray<string>>());
    }

    private static AssemblyIdentityComparer? RestoreIdentityComparer(string? descriptor)
    {
        if (descriptor is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(descriptor);
        return document.RootElement.GetProperty("Kind").GetString() == "default"
            ? AssemblyIdentityComparer.Default : DesktopAssemblyIdentityComparer.Default;
    }

    private static string? Json<T>(T value) => value is null ? null : JsonSerializer.Serialize(value);

    private static T Value<T>(ImmutableSortedDictionary<string, string?> values, string name) =>
        values[name] is { } json ? JsonSerializer.Deserialize<T>(json)! : default!;

    private static T EnumValue<T>(ImmutableSortedDictionary<string, string?> values, string name) where T : struct, Enum =>
        Enum.Parse<T>(Value<string>(values, name), ignoreCase: false);

    private sealed class OwnedMetadataReference : PortableExecutableReference
    {
        public string? DocumentationPath { get; }

        public OwnedMetadataReference(Metadata metadata, MetadataReferenceProperties properties,
            DocumentationProvider documentation, string path, string? documentationPath)
            : base(properties, path, documentation)
        {
            _metadata = metadata;
            _documentation = documentation;
            DocumentationPath = documentationPath;
        }

        protected override DocumentationProvider CreateDocumentationProvider() => _documentation;

        protected override Metadata GetMetadataImpl() => _metadata;

        protected override PortableExecutableReference WithPropertiesImpl(MetadataReferenceProperties properties) =>
            new OwnedMetadataReference(_metadata, properties, _documentation, FilePath!, DocumentationPath);

        private readonly Metadata _metadata;
        private readonly DocumentationProvider _documentation;
    }

    private sealed class OwnedAnalyzerLoader : AssemblyLoadContext, IAnalyzerAssemblyLoader, IDisposable
    {
        public OwnedAnalyzerLoader(IEnumerable<string> paths) : base(isCollectible: true)
        {
            _paths = paths.ToHashSet(Paths);
            _identities = _paths.Select(path => (Path: path, Identity: AssemblyName.GetAssemblyName(path))).ToArray();
            var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var frameworkPaths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Where(path => Paths.Equals(Path.GetDirectoryName(path), runtimeDirectory));
            var roslynPaths = new[]
            {
                typeof(Compilation).Assembly.Location,
                typeof(CSharpCompilation).Assembly.Location,
                typeof(Microsoft.CodeAnalysis.Workspace).Assembly.Location,
            };
            _trusted = frameworkPaths.Concat(roslynPaths).Distinct(Paths)
                .Select(path => (Path: path, Identity: AssemblyName.GetAssemblyName(path))).ToArray();
        }

        public void AddDependencyLocation(string fullPath)
        {
            if (!_paths.Contains(RequirePath(fullPath)))
            {
                throw new InvalidDataException("Analyzer dependency is outside the admitted closure.");
            }
        }

        public Assembly LoadFromPath(string fullPath)
        {
            AddDependencyLocation(fullPath);
            return LoadFromAssemblyPath(fullPath);
        }

        public void Dispose() => Unload();

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var shared = _trusted.Where(entry => AssemblyIdentityMatches(assemblyName, entry.Identity)).ToArray();
            if (shared.Length > 1)
            {
                throw new InvalidDataException("Trusted runtime assembly identity is ambiguous.");
            }

            if (shared.Length == 1)
            {
                var assembly = Default.Assemblies.SingleOrDefault(candidate => Paths.Equals(candidate.Location, shared[0].Path))
                    ?? Default.LoadFromAssemblyPath(shared[0].Path);
                if (!Paths.Equals(assembly.Location, shared[0].Path)
                    || !AssemblyIdentityMatches(assemblyName, assembly.GetName()))
                {
                    throw new InvalidDataException("Trusted runtime assembly identity changed.");
                }

                return assembly;
            }

            var matches = _identities.Where(entry => AssemblyIdentityMatches(assemblyName, entry.Identity)).ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidDataException("Analyzer dependency identity is unavailable or ambiguous.");
            }

            return LoadFromAssemblyPath(matches[0].Path);
        }

        private static bool AssemblyIdentityMatches(AssemblyName request, AssemblyName candidate) =>
            request.Name == candidate.Name && request.Version == candidate.Version
            && (request.CultureName ?? "") == (candidate.CultureName ?? "")
            && (request.GetPublicKeyToken() ?? []).AsSpan().SequenceEqual(candidate.GetPublicKeyToken() ?? []);

        private readonly HashSet<string> _paths;
        private readonly (string Path, AssemblyName Identity)[] _identities;
        private readonly (string Path, AssemblyName Identity)[] _trusted;
    }

    private sealed record WriteItem(string Path, string? Text, Encoding? Encoding);

    private readonly ExperimentAnalyzerAdmission? _analyzerAdmission;
    private static readonly StringComparer Paths = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
