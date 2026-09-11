using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.LifecycleTestHost;

internal sealed class HostSession
{
    private readonly SolutionManager _manager = new(NullLogger<SolutionManager>.Instance);
    private readonly StringComparison _pathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private Solution? _heldOldSolution;
    private Solution? _heldNewSolution;

    public async Task<HostResponse> ExecuteAsync(HostCommand command, CancellationToken cancellationToken)
    {
        try
        {
            return command.Op switch
            {
                "env" => Env(),
                "load" => await LoadAsync(command, cancellationToken).ConfigureAwait(false),
                "oracle" => await OracleAsync(command, cancellationToken).ConfigureAwait(false),
                "reset" => await ResetAsync(cancellationToken).ConfigureAwait(false),
                "updateDocument" => await UpdateDocumentAsync(command, cancellationToken).ConfigureAwait(false),
                "applyOverlayEdit" => await ApplyOverlayEditAsync(command, cancellationToken).ConfigureAwait(false),
                "waitDirty" => await WaitDirtyAsync(command, cancellationToken).ConfigureAwait(false),
                "flushFind" => await FlushFindAsync(command, cancellationToken).ConfigureAwait(false),
                "flushGetter" => await FlushGetterAsync(cancellationToken).ConfigureAwait(false),
                "inspect" => Inspect("inspect"),
                "injectPrepareFailure" => InjectPrepareFailure(),
                "injectApplyFailure" => InjectApplyFailure(),
                "injectFileWriteFailure" => InjectFileWriteFailure(command),
                "injectReconciliationFailure" => InjectReconciliationFailure(),
                "injectCancelAfterWrites" => InjectCancelAfterWrites(command),
                "holdOverlayEdit" => HoldOverlayEdit(command),
                "applyHeld" => await ApplyHeldAsync(cancellationToken).ConfigureAwait(false),
                "applyUnknownAnalyzerDiff" => await ApplyUnknownAnalyzerDiffAsync(command, cancellationToken).ConfigureAwait(false),
                "forceCopyFailure" => ForceCopyFailure(),
                "publishGeneration" => PublishGeneration(command),
                "rename" => await RenameOverlayAsync(command, cancellationToken).ConfigureAwait(false),
                "snapshotCsproj" => SnapshotCsproj(command),
                "build" => await BuildAsync(command, cancellationToken).ConfigureAwait(false),
                "hashFile" => HashFile(command),
                "concurrentLoad" => await ConcurrentLoadAsync(command, cancellationToken).ConfigureAwait(false),
                _ => Fail(command.Op, "unknown-op:" + command.Op),
            };
        }
        catch (Exception ex)
        {
            return Fail(command.Op, ex.GetType().Name + ": " + ex.Message);
        }
    }

    public HostResponse Env()
    {
        var roslyn = typeof(Microsoft.CodeAnalysis.Workspace).Assembly.GetName().Version?.ToString();
        string? sdk = null;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = DotNetHostResolver.ResolveDotNetExecutable(),
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = System.Diagnostics.Process.Start(psi);
            sdk = process?.StandardOutput.ReadToEnd().Trim();
            process?.WaitForExit(10_000);
        }
        catch (Exception ex)
        {
            sdk = "error:" + ex.Message;
        }

        return new HostResponse
        {
            Ok = true,
            Op = "env",
            Environment = new EnvironmentDto
            {
                Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                Sdk = sdk,
                Roslyn = roslyn,
                Bootstrap = MsBuildEnvironmentInfo.RegistrationSummary,
                DotNetHost = DotNetHostResolver.ResolveDotNetExecutable(),
                WorkingSetBytes = Environment.WorkingSet,
                PrivateMemoryBytes = System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64,
            },
        };
    }

    private async Task<HostResponse> LoadAsync(HostCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Path))
        {
            return Fail("load", "path-required");
        }

        _ = await _manager.LoadAsync(
            command.Path,
            cancellationToken,
            command.Configuration,
            command.Platform,
            command.TargetFramework).ConfigureAwait(false);

        IReadOnlyList<AnalyzerReferenceShadowCopier.RewriteResult>? rewrite = null;
        if (command.ShadowCopy == true)
        {
            rewrite = await _manager.ShadowCopyInSolutionAnalyzerReferencesAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var response = Inspect("load");
        response.Rewrite = MapRewrite(rewrite ?? _manager.LastShadowCopyResults);
        return response;
    }

    private async Task<HostResponse> OracleAsync(HostCommand command, CancellationToken cancellationToken)
    {
        var projectName = string.IsNullOrWhiteSpace(command.Project) ? "Consumer" : command.Project;
        var fromWorkspace = string.Equals(command.OracleSource, "workspace", StringComparison.OrdinalIgnoreCase);
        var solution = fromWorkspace
            ? _manager.GetWorkspaceCurrentSolution()
            : _manager.GetCurrentSolution();
        if (solution is null)
        {
            return Fail("oracle", "no-solution");
        }

        var project = solution.Projects.FirstOrDefault(p =>
            string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase));
        if (project is null)
        {
            return Fail("oracle", "no-project:" + projectName);
        }

        if (_manager.LastExecutionObservation.RequiresRestart
            || _manager.LastExecutionObservation.Status == AnalyzerExecutionStatus.DependencyUnsupported)
        {
            var blocked = Inspect("oracle");
            blocked.OracleSuccess = false;
            blocked.Marker = null;
            blocked.OracleFailure = "execution-not-permitted:" + _manager.LastExecutionObservation.Status;
            FillAnalyzerPaths(blocked, project);
            blocked.Ok = true;
            return blocked;
        }

        var observation = await SourceGeneratorOracle.ReadAsync(project, cancellationToken).ConfigureAwait(false);
        _manager.ObserveAnalyzerExecution(project);
        var response = Inspect("oracle");
        response.OracleSuccess = observation.Success;
        response.Marker = observation.Marker;
        response.OracleFailure = observation.Failure;
        response.GeneratedText = observation.GeneratedText;
        FillAnalyzerPaths(response, project);
        response.LoadedAssemblies = _manager.AnalyzerAssemblyLoader
            .SnapshotLoadedAssemblies()
            .Select(a => new LoadedAssemblyDto
            {
                RequestedPath = a.RequestedPath,
                Identity = a.Identity,
                Location = a.Location,
            })
            .ToList();

        var loaded = response.LoadedAssemblies.FirstOrDefault(a =>
            a.RequestedPath.Contains("Generator", StringComparison.OrdinalIgnoreCase)
            || a.Identity.StartsWith("Generator,", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileNameWithoutExtension(a.RequestedPath), "Generator", StringComparison.OrdinalIgnoreCase));
        if (loaded is not null)
        {
            response.AssemblyIdentity = loaded.Identity;
            response.LoadedAnalyzerPath = loaded.Location;
        }

        response.Ok = true;
        return response;
    }

    private async Task<HostResponse> ResetAsync(CancellationToken cancellationToken)
    {
        await _manager.ClearWorkspaceAsync(cancellationToken).ConfigureAwait(false);
        return Inspect("reset");
    }

    private async Task<HostResponse> UpdateDocumentAsync(HostCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Path) || command.Text is null)
        {
            return Fail("updateDocument", "path-and-text-required");
        }

        var fullPath = Path.GetFullPath(command.Path);
        var write = await _manager.UpdateDocumentInMemoryAsync(fullPath, command.Text, cancellationToken)
            .ConfigureAwait(false);
        var response = Inspect("updateDocument");
        AttachWrite(response, write);
        response.DocumentText = command.Text;
        response.Ok = write.IsFullSuccess;
        if (!write.IsFullSuccess)
        {
            response.Error = write.Reason ?? write.Status.ToString();
        }

        return response;
    }

    private async Task<HostResponse> ApplyOverlayEditAsync(HostCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Path) || command.Text is null)
        {
            return Fail("applyOverlayEdit", "path-and-text-required");
        }

        var solution = _manager.GetCurrentSolution();
        if (solution is null)
        {
            return Fail("applyOverlayEdit", "no-solution");
        }

        var fullPath = Path.GetFullPath(command.Path);
        var document = FindDocument(solution, fullPath);
        if (document is null)
        {
            return Fail("applyOverlayEdit", "document-not-found");
        }

        var newSolution = solution.WithDocumentText(
            document.Id,
            SourceText.From(command.Text, Encoding.UTF8));
        var write = await _manager.ApplySolutionChangesToDiskAsync(solution, newSolution, cancellationToken)
            .ConfigureAwait(false);
        var response = Inspect("applyOverlayEdit");
        AttachWrite(response, write);
        response.DocumentText = command.Text;
        response.Ok = write.IsFullSuccess;
        if (!write.IsFullSuccess)
        {
            response.Error = write.Reason ?? write.Status.ToString();
        }

        return response;
    }

    private async Task<HostResponse> WaitDirtyAsync(HostCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Path))
        {
            return Fail("waitDirty", "path-required");
        }

        var timeout = TimeSpan.FromMilliseconds(command.TimeoutMs <= 0 ? 15_000 : command.TimeoutMs);
        var wait = await _manager.WaitForDirtySourceAsync(command.Path, timeout, cancellationToken)
            .ConfigureAwait(false);
        var response = Inspect("waitDirty");
        response.DirtyDelivered = wait.Delivered;
        response.DirtyTimedOut = wait.TimedOut;
        response.PendingDirtyCount = wait.PendingCount;
        response.DirtyWaitMs = (long)wait.Elapsed.TotalMilliseconds;
        response.Ok = wait.Delivered;
        if (!wait.Delivered)
        {
            response.Error = "dirty-event-not-delivered";
        }

        return response;
    }

    private async Task<HostResponse> FlushFindAsync(HostCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Path))
        {
            return Fail("flushFind", "path-required");
        }

        var document = await _manager.FindDocumentAsync(command.Path, cancellationToken).ConfigureAwait(false);
        var response = Inspect("flushFind");
        if (document is null)
        {
            response.Ok = false;
            response.Error = "flush-find-missed-document";
            return response;
        }

        response.DocumentText = (await document.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
        return response;
    }

    private async Task<HostResponse> FlushGetterAsync(CancellationToken cancellationToken)
    {
        var solution = await _manager.GetCurrentSolutionAfterDiskSyncAsync(cancellationToken).ConfigureAwait(false);
        var response = Inspect("flushGetter");
        if (solution is null)
        {
            response.Ok = false;
            response.Error = "flush-getter-no-solution";
        }

        return response;
    }

    private HostResponse PublishGeneration(HostCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Path) || !File.Exists(command.Path))
        {
            return Fail("publishGeneration", "source-not-found");
        }

        var root = command.ShadowRoot;
        if (string.IsNullOrWhiteSpace(root))
        {
            return Fail("publishGeneration", "shadow-root-required");
        }

        if (!string.IsNullOrWhiteSpace(command.GatePath))
        {
            AnalyzerShadowGenerationPublisher.BeforeMoveGatePath = command.GatePath;
            AnalyzerShadowGenerationPublisher.BeforeMoveGateTimeout = TimeSpan.FromMilliseconds(
                command.TimeoutMs <= 0 ? 30_000 : command.TimeoutMs);
        }

        try
        {
            var published = AnalyzerShadowGenerationPublisher.PublishMainOnly(command.Path, root, "Generator");
            var response = Inspect("publishGeneration");
            response.Ok = published.Success;
            response.Error = published.FailureReason;
            response.ReusedExisting = published.ReusedExisting;
            response.GenerationDirectory = published.GenerationDirectory;
            response.GenerationBytes = published.GenerationBytes;
            response.ShadowRootBytes = AnalyzerShadowGenerationPublisher.MeasureDirectoryBytes(root);
            if (published.Success)
            {
                response.Rewrite =
                [
                    new RewriteDto
                    {
                        ProjectName = "Consumer",
                        MatchedProjectName = "Generator",
                        OriginalFullPath = command.Path,
                        ShadowCopyPath = published.MainShadowPath,
                        Applied = true,
                        Generation = published.GenerationId,
                    },
                ];
            }

            return response;
        }
        finally
        {
            AnalyzerShadowGenerationPublisher.BeforeMoveGatePath = null;
        }
    }

    private HostResponse InjectPrepareFailure()
    {
        _manager.FailNextOverlayPrepare = true;
        var response = Inspect("injectPrepareFailure");
        return response;
    }

    private HostResponse InjectApplyFailure()
    {
        _manager.FailNextTryApplyChanges = true;
        return Inspect("injectApplyFailure");
    }

    private HostResponse InjectFileWriteFailure(HostCommand command)
    {
        _manager.FailNextDocumentWritePath = string.IsNullOrWhiteSpace(command.Path) ? "*" : Path.GetFullPath(command.Path);
        return Inspect("injectFileWriteFailure");
    }

    private HostResponse InjectReconciliationFailure()
    {
        _manager.FailNextReconciliation = true;
        return Inspect("injectReconciliationFailure");
    }

    private HostResponse InjectCancelAfterWrites(HostCommand command)
    {
        _manager.CancelAfterDocumentWrites = command.CancelAfterWrites <= 0 ? 1 : command.CancelAfterWrites;
        return Inspect("injectCancelAfterWrites");
    }

    private HostResponse HoldOverlayEdit(HostCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Path) || command.Text is null)
        {
            return Fail("holdOverlayEdit", "path-and-text-required");
        }

        var solution = _manager.GetCurrentSolution();
        if (solution is null)
        {
            return Fail("holdOverlayEdit", "no-solution");
        }

        var fullPath = Path.GetFullPath(command.Path);
        var document = FindDocument(solution, fullPath);
        if (document is null)
        {
            return Fail("holdOverlayEdit", "document-not-found");
        }

        _heldOldSolution = solution;
        _heldNewSolution = solution.WithDocumentText(
            document.Id,
            SourceText.From(command.Text, Encoding.UTF8));
        return Inspect("holdOverlayEdit");
    }

    private async Task<HostResponse> ApplyHeldAsync(CancellationToken cancellationToken)
    {
        if (_heldOldSolution is null || _heldNewSolution is null)
        {
            return Fail("applyHeld", "no-held-candidate");
        }

        var write = await _manager.ApplySolutionChangesToDiskAsync(
                _heldOldSolution,
                _heldNewSolution,
                cancellationToken)
            .ConfigureAwait(false);
        var response = Inspect("applyHeld");
        AttachWrite(response, write);
        response.Ok = write.IsFullSuccess;
        if (!write.IsFullSuccess)
        {
            response.Error = write.Reason ?? write.Status.ToString();
        }

        return response;
    }

    private async Task<HostResponse> ApplyUnknownAnalyzerDiffAsync(HostCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Path) || command.Text is null)
        {
            return Fail("applyUnknownAnalyzerDiff", "path-and-text-required");
        }

        var solution = _manager.GetCurrentSolution();
        if (solution is null)
        {
            return Fail("applyUnknownAnalyzerDiff", "no-solution");
        }

        var fullPath = Path.GetFullPath(command.Path);
        var document = FindDocument(solution, fullPath);
        if (document is null)
        {
            return Fail("applyUnknownAnalyzerDiff", "document-not-found");
        }

        var extraPath = Path.Combine(Path.GetTempPath(), "RoslynMcpServer.UnknownAnalyzer", "Unknown.dll");
        var extra = new Microsoft.CodeAnalysis.Diagnostics.AnalyzerFileReference(
            extraPath,
            _manager.AnalyzerAssemblyLoader);
        var refs = document.Project.AnalyzerReferences.ToList();
        refs.Add(extra);
        var candidate = solution
            .WithProjectAnalyzerReferences(document.Project.Id, refs)
            .WithDocumentText(document.Id, SourceText.From(command.Text, Encoding.UTF8));
        var write = await _manager.ApplySolutionChangesToDiskAsync(solution, candidate, cancellationToken)
            .ConfigureAwait(false);
        var response = Inspect("applyUnknownAnalyzerDiff");
        AttachWrite(response, write);
        response.Ok = write.IsFullSuccess;
        if (!write.IsFullSuccess)
        {
            response.Error = write.Reason ?? write.Status.ToString();
        }

        return response;
    }

    private HostResponse ForceCopyFailure()
    {
        AnalyzerReferenceShadowCopier.RemainingForcedCopyFailures = 1;
        return Inspect("forceCopyFailure");
    }

    private async Task<HostResponse> RenameOverlayAsync(HostCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Path)
            || string.IsNullOrWhiteSpace(command.Symbol)
            || string.IsNullOrWhiteSpace(command.NewName))
        {
            return Fail("rename", "path-symbol-newName-required");
        }

        var document = await _manager.FindDocumentAsync(command.Path, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return Fail("rename", "document-not-found");
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return Fail("rename", "no-semantic-model");
        }

        ISymbol? symbol = null;
        foreach (var node in root.DescendantNodes())
        {
            if (node is TypeDeclarationSyntax type && type.Identifier.ValueText == command.Symbol)
            {
                symbol = model.GetDeclaredSymbol(type, cancellationToken);
                break;
            }

            if (node is MethodDeclarationSyntax method && method.Identifier.ValueText == command.Symbol)
            {
                symbol = model.GetDeclaredSymbol(method, cancellationToken);
                break;
            }
        }

        if (symbol is null)
        {
            return Fail("rename", "symbol-not-found:" + command.Symbol);
        }

        var afterSymbol = _manager.GetCurrentSolution();
        var sameSnapshot = afterSymbol is not null && ReferenceEquals(document.Project.Solution, afterSymbol);
        var baseSolution = document.Project.Solution;
        var renamed = await Renamer.RenameSymbolAsync(
            baseSolution,
            symbol,
            new SymbolRenameOptions(),
            command.NewName,
            cancellationToken).ConfigureAwait(false);

        var write = await _manager.ApplySolutionChangesToDiskAsync(baseSolution, renamed, cancellationToken)
            .ConfigureAwait(false);

        var response = Inspect("rename");
        AttachWrite(response, write);
        response.Ok = write.IsFullSuccess;
        if (!write.IsFullSuccess)
        {
            response.Error = write.Reason ?? write.Status.ToString();
        }

        response.SameSnapshotAfterSymbol = sameSnapshot;
        response.RenamedTo = command.NewName;
        var updated = await _manager.FindDocumentAsync(command.Path, cancellationToken).ConfigureAwait(false);
        if (updated is not null)
        {
            response.DocumentText = (await updated.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
        }

        return response;
    }

    private HostResponse SnapshotCsproj(HostCommand command)
    {
        var root = command.Path;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = _manager.GetLoadedWorkspaceDirectory();
        }

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return Fail("snapshotCsproj", "root-not-found");
        }

        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var analyzerIncludes = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
        {
            hashes[file] = Sha256Hex(File.ReadAllBytes(file));
            var text = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(
                         text,
                         @"<Analyzer\s+Include\s*=\s*""([^""]+)""\s*/?>",
                         RegexOptions.IgnoreCase))
            {
                analyzerIncludes.Add(file + "::" + match.Groups[1].Value);
            }
        }

        var response = Inspect("snapshotCsproj");
        response.CsprojSha256 = hashes;
        response.TemporaryAnalyzerIncludes = analyzerIncludes.ToArray();
        return response;
    }

    private async Task<HostResponse> BuildAsync(HostCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Path))
        {
            return Fail("build", "path-required");
        }

        var fullPath = Path.GetFullPath(command.Path);
        var workDir = File.Exists(fullPath)
            ? Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory
            : fullPath;
        var target = File.Exists(fullPath) ? $"\"{fullPath}\"" : string.Empty;
        var args = new StringBuilder("build");
        if (!string.IsNullOrWhiteSpace(target))
        {
            args.Append(' ').Append(target);
        }

        if (command.NoIncremental)
        {
            args.Append(" --no-incremental");
        }

        if (!string.IsNullOrWhiteSpace(command.Configuration))
        {
            args.Append(" -c ").Append(command.Configuration);
        }

        if (!string.IsNullOrWhiteSpace(command.Arguments))
        {
            args.Append(' ').Append(command.Arguments);
        }

        var run = await DotNetCliRunner.RunWithMetadataAsync(
            args.ToString(),
            workDir,
            cancellationToken,
            TimeSpan.FromSeconds(45)).ConfigureAwait(false);

        var response = Inspect("build");
        response.BuildExitCode = run.ExitCode;
        response.BuildOutput = TrimOutput(run.CombinedOutput);
        response.Ok = run.ExitCode == 0;
        if (!response.Ok)
        {
            response.Error = "build-exit-" + run.ExitCode;
        }

        return response;
    }

    private HostResponse HashFile(HostCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Path) || !File.Exists(command.Path))
        {
            return Fail("hashFile", "file-not-found");
        }

        var response = Inspect("hashFile");
        response.FileSha256 = Sha256Hex(File.ReadAllBytes(command.Path));
        return response;
    }

    private async Task<HostResponse> ConcurrentLoadAsync(HostCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Path))
        {
            return Fail("concurrentLoad", "path-required");
        }

        async Task<string?> OneAsync()
        {
            try
            {
                _ = await _manager.LoadAsync(command.Path, cancellationToken).ConfigureAwait(false);
                if (command.ShadowCopy == true)
                {
                    _ = await _manager.ShadowCopyInSolutionAnalyzerReferencesAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                return null;
            }
            catch (Exception ex)
            {
                return ex.GetType().Name + ": " + ex.Message;
            }
        }

        var first = OneAsync();
        var second = OneAsync();
        await Task.WhenAll(first, second).ConfigureAwait(false);

        var oracle = await OracleAsync(new HostCommand { Op = "oracle", Project = command.Project ?? "Consumer" }, cancellationToken)
            .ConfigureAwait(false);
        var response = Inspect("concurrentLoad");
        response.Concurrency = new ConcurrencyDto
        {
            BothCompleted = first.Result is null && second.Result is null,
            FirstError = first.Result,
            SecondError = second.Result,
            ShadowEnabledAfter = _manager.ShadowCopyAnalyzersEnabled,
            MarkerAfter = oracle.Marker,
        };
        response.OracleSuccess = oracle.OracleSuccess;
        response.Marker = oracle.Marker;
        response.OracleFailure = oracle.OracleFailure;
        return response;
    }

    private HostResponse Inspect(string op)
    {
        var response = new HostResponse
        {
            Ok = true,
            Op = op,
            CacheHit = _manager.LastLoadWasCacheHit,
            ReopenedGraph = _manager.LastLoadReopenedGraph,
            PrepareAttempted = _manager.LastPrepareAttempted,
            PrepareInjectedFailure = _manager.LastPrepareInjectedFailure,
            LastRefreshStale = _manager.LastRefreshStale,
            MappingPresent = _manager.AnalyzerShadowMapping is { HasAnyApplied: true },
            OverlayPrepareCount = _manager.OverlayPrepareCount,
            AnalyzerFileIoCount = AnalyzerShadowGenerationPublisher.AnalyzerFileIoCount,
            ShadowEnabled = _manager.ShadowCopyAnalyzersEnabled,
            ShadowRoot = _manager.ShadowCopyRootDirectory,
            LoadedWorkspacePath = _manager.GetLoadedWorkspacePath(),
            PendingDirtyCount = _manager.GetPendingDirtySourcePaths().Count,
            Rewrite = MapRewrite(_manager.LastShadowCopyResults),
            Execution = MapExecution(_manager.LastExecutionObservation),
        };
        if (_manager.LastWriteResult is { } lastWrite)
        {
            AttachWrite(response, lastWrite);
        }

        var overlay = _manager.GetCurrentSolution();
        var workspace = _manager.GetWorkspaceCurrentSolution();
        var consumer = overlay?.Projects.FirstOrDefault(p =>
            string.Equals(p.Name, "Consumer", StringComparison.OrdinalIgnoreCase)
            || p.Name.StartsWith("Consumer", StringComparison.OrdinalIgnoreCase));
        if (consumer is not null && overlay is not null)
        {
            FillAnalyzerPaths(response, consumer);
        }

        if (workspace is not null)
        {
            var raw = workspace.Projects.FirstOrDefault(p =>
                consumer is not null && p.Id == consumer.Id);
            if (raw is not null)
            {
                response.WorkspaceAnalyzerPath = raw.AnalyzerReferences
                    .Select(r => r.FullPath)
                    .FirstOrDefault(p =>
                        p is not null
                        && string.Equals(Path.GetFileNameWithoutExtension(p), "Generator", StringComparison.OrdinalIgnoreCase));
            }
        }

        return response;
    }

    private void FillAnalyzerPaths(HostResponse response, Project project)
    {
        var overlayRef = project.AnalyzerReferences.FirstOrDefault(r =>
            r.FullPath is not null
            && string.Equals(Path.GetFileNameWithoutExtension(r.FullPath), "Generator", StringComparison.OrdinalIgnoreCase));
        response.OverlayAnalyzerPath = overlayRef?.FullPath;

        var workspace = _manager.GetWorkspaceCurrentSolution();
        var raw = workspace?.GetProject(project.Id);
        response.WorkspaceAnalyzerPath ??= raw?.AnalyzerReferences
            .Select(r => r.FullPath)
            .FirstOrDefault(p =>
                p is not null
                && string.Equals(Path.GetFileNameWithoutExtension(p), "Generator", StringComparison.OrdinalIgnoreCase));
    }

    private static void AttachWrite(HostResponse response, WorkspaceWriteResult write)
    {
        response.WriteStatus = write.Status.ToString();
        response.WriteReason = write.Reason;
        response.SavedPaths = write.SavedPaths.ToArray();
        response.OverlayPublished = write.OverlayPublished;
        response.UnappliedProjectState = write.UnappliedProjectState;
        response.WorkspaceApplied = write.WorkspaceApplied;
    }

    private static ExecutionDto MapExecution(AnalyzerExecutionObservation observation)
    {
        return new ExecutionDto
        {
            Status = observation.Status.ToString(),
            Stage = observation.HighestStage.ToString(),
            Reason = observation.Reason,
            Action = observation.Action,
            Project = observation.ProjectName,
            Generator = observation.GeneratorName,
            Generation = observation.GenerationId,
            Dependency = observation.DependencyName,
            ExpectedPath = observation.ExpectedPath,
            LoadedPath = observation.LoadedPath,
            Identity = observation.AssemblyIdentity,
        };
    }

    private static List<RewriteDto> MapRewrite(IReadOnlyList<AnalyzerReferenceShadowCopier.RewriteResult> results)
    {
        return results.Select(r => new RewriteDto
        {
            ProjectName = r.ProjectName,
            OriginalFullPath = r.OriginalFullPath,
            MatchedProjectName = r.MatchedProjectName,
            ShadowCopyPath = r.ShadowCopyPath,
            Applied = r.Applied,
            SkipReason = r.SkipReason,
            Generation = r.GenerationId ?? TryGeneration(r.ShadowCopyPath),
        }).ToList();
    }

    private static string? TryGeneration(string? shadowPath)
    {
        if (string.IsNullOrWhiteSpace(shadowPath))
        {
            return null;
        }

        try
        {
            return Path.GetFileName(Path.GetDirectoryName(shadowPath));
        }
        catch
        {
            return null;
        }
    }

    private Document? FindDocument(Solution solution, string fullPath)
    {
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                if (document.FilePath is not null
                    && string.Equals(Path.GetFullPath(document.FilePath), fullPath, _pathComparison))
                {
                    return document;
                }
            }
        }

        return null;
    }

    private static HostResponse Fail(string? op, string error) =>
        new()
        {
            Ok = false,
            Op = op,
            Error = error,
        };

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string TrimOutput(string output)
    {
        if (string.IsNullOrEmpty(output) || output.Length <= 4000)
        {
            return output;
        }

        return output[^4000..];
    }
}
