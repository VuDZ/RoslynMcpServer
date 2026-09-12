using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RoslynMcpServer.Diagnostics;

namespace RoslynMcpServer.Services;

public sealed class AnalyzerProvenanceCaptureOptions
{
    public const string SectionName = "AnalyzerProvenanceCapture";

    public int CleanupAttempts { get; set; } = 5;

    public TimeSpan CleanupRetryDelay { get; set; } = TimeSpan.FromMilliseconds(50);
}

internal enum AnalyzerProvenanceCaptureStatus
{
    Complete,
    Incomplete,
    Failed,
}

internal enum AnalyzerProvenanceCaptureFailureMode
{
    None,
    Missing,
    Corrupt,
    MixedValidAndCorrupt,
}

internal enum AnalyzerProvenanceBindingStatus
{
    Confirmed,
    CaptureIncomplete,
    MissingConsumer,
    AmbiguousConsumer,
    MissingSourceMetadata,
    MissingSourceProject,
    AmbiguousSourceProject,
}

internal sealed record AnalyzerProvenanceToolset(
    string? RegisteredMsBuildPath,
    string? RegisteredMsBuildVersion,
    string RuntimeMsBuildAssemblyPath,
    string? RuntimeMsBuildAssemblyVersion,
    string? DotNetSdkPath,
    string? DotNetSdkVersion);

internal readonly record struct AnalyzerProvenanceProjectContextKey(
    int SubmissionId,
    int NodeId,
    int ProjectInstanceId,
    int ProjectContextId)
{
    public static AnalyzerProvenanceProjectContextKey From(BuildEventContext context) =>
        new(context.SubmissionId, context.NodeId, context.ProjectInstanceId, context.ProjectContextId);
}

internal readonly record struct AnalyzerProvenanceEventContextKey(
    int SubmissionId,
    int NodeId,
    int ProjectInstanceId,
    int ProjectContextId,
    int TargetId,
    int TaskId)
{
    public static AnalyzerProvenanceEventContextKey From(BuildEventContext context) =>
        new(
            context.SubmissionId,
            context.NodeId,
            context.ProjectInstanceId,
            context.ProjectContextId,
            context.TargetId,
            context.TaskId);
}

internal sealed record AnalyzerProvenanceProjectContext(
    int BinlogOrdinal,
    AnalyzerProvenanceProjectContextKey Context,
    AnalyzerProvenanceEventContextKey? ParentContext,
    string ProjectFile,
    ImmutableDictionary<string, string> GlobalProperties,
    ImmutableDictionary<string, string> Properties);

internal sealed record CapturedAnalyzerProvenanceItem(
    int BinlogOrdinal,
    AnalyzerProvenanceProjectContextKey ConsumerContext,
    AnalyzerProvenanceEventContextKey TaskContext,
    string Identity,
    string? SourceProjectFile,
    string? ReferenceSourceTarget,
    string? OutputItemType,
    string? ReferenceOutputAssembly,
    string? NearestTargetFramework,
    string? SetTargetFramework,
    string? SetConfiguration,
    string? SetPlatform,
    string? GlobalPropertiesToRemove,
    string? UndefineProperties,
    ImmutableDictionary<string, string> Metadata);

internal sealed record AnalyzerProvenanceBinding(
    AnalyzerProvenanceBindingStatus Status,
    ProjectId? ConsumerProjectId,
    ProjectId? SourceProjectId,
    string Identity,
    string? SourceProjectFile,
    string? SelectedInnerTargetFramework,
    ImmutableDictionary<string, string> EffectiveGlobalProperties);

internal sealed record AnalyzerProvenanceCaptureMetrics(
    int FileCount,
    long TotalBytes,
    TimeSpan ReplayDuration,
    int ProjectContextCount,
    int AnalyzerItemCount,
    int ConfirmedBindingCount);

internal sealed record AnalyzerProvenanceSnapshot(
    Guid LoadSessionId,
    string LoadedPath,
    ImmutableDictionary<string, string> RequestedGlobalProperties,
    AnalyzerProvenanceToolset Toolset,
    AnalyzerProvenanceCaptureStatus Status,
    ImmutableArray<AnalyzerProvenanceProjectContext> ProjectContexts,
    ImmutableArray<CapturedAnalyzerProvenanceItem> AnalyzerItems,
    ImmutableArray<AnalyzerProvenanceBinding> Bindings,
    ImmutableArray<string> FailureCodes,
    AnalyzerProvenanceCaptureMetrics Metrics);

public sealed class AnalyzerProvenanceCaptureService
{
    private const string BinlogSearchPattern = "*.binlog";
    private const string BinaryLoggerParameters = "ProjectImports=None";

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly ILogger<AnalyzerProvenanceCaptureService> _logger;
    private readonly AnalyzerProvenanceCaptureOptions _options;
    private readonly TimeProvider _timeProvider;
    private long _captureAttemptCount;
    private long _completeCaptureCount;
    private long _failedCaptureCount;
    private int _nextFailureMode;

    public AnalyzerProvenanceCaptureService(
        ILogger<AnalyzerProvenanceCaptureService> logger,
        IOptions<AnalyzerProvenanceCaptureOptions> options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _logger = logger;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    internal long CaptureAttemptCount => Interlocked.Read(ref _captureAttemptCount);

    internal long CompleteCaptureCount => Interlocked.Read(ref _completeCaptureCount);

    internal long FailedCaptureCount => Interlocked.Read(ref _failedCaptureCount);

    internal AnalyzerProvenanceCaptureFailureMode FailureModeForNextCapture
    {
        set => Interlocked.Exchange(ref _nextFailureMode, (int)value);
    }

    internal async Task<AnalyzerProvenanceSnapshot> OpenAndCaptureAsync(
        MSBuildWorkspace workspace,
        string fullPath,
        Guid loadSessionId,
        IReadOnlyDictionary<string, string> requestedGlobalProperties,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentNullException.ThrowIfNull(requestedGlobalProperties);

        Interlocked.Increment(ref _captureAttemptCount);
        var tempDirectory = CreatePrivateTempDirectory();
        var requestedBinlogPath = Path.Combine(tempDirectory, "capture.binlog");
        var binaryLogger = new BinaryLogger
        {
            Parameters = $"{requestedBinlogPath};{BinaryLoggerParameters}",
            Verbosity = LoggerVerbosity.Normal,
        };

        try
        {
            await OpenWorkspaceAsync(workspace, fullPath, binaryLogger, cancellationToken).ConfigureAwait(false);

            var binlogPaths = Directory
                .EnumerateFiles(tempDirectory, BinlogSearchPattern, SearchOption.TopDirectoryOnly)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            ApplyFailureSeam(binlogPaths);
            binlogPaths = Directory
                .EnumerateFiles(tempDirectory, BinlogSearchPattern, SearchOption.TopDirectoryOnly)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var totalBytes = binlogPaths.Sum(path => new FileInfo(path).Length);
            var replayStarted = _timeProvider.GetTimestamp();
            var replay = await Task.Run(
                    () => ReplayAll(binlogPaths, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
            var replayDuration = _timeProvider.GetElapsedTime(replayStarted);
            var snapshot = BuildSnapshot(
                loadSessionId,
                fullPath,
                requestedGlobalProperties,
                workspace.CurrentSolution,
                replay,
                binlogPaths.Length,
                totalBytes,
                replayDuration);

            if (snapshot.Status == AnalyzerProvenanceCaptureStatus.Complete)
            {
                Interlocked.Increment(ref _completeCaptureCount);
            }
            else
            {
                Interlocked.Increment(ref _failedCaptureCount);
            }

            _logger.LogInformation(
                "Analyzer provenance capture finished: Status={Status}, Files={FileCount}, Bytes={Bytes}, ReplayMs={ReplayMs:F1}, ProjectContexts={ProjectContexts}, AnalyzerItems={AnalyzerItems}, ConfirmedBindings={ConfirmedBindings}",
                snapshot.Status,
                snapshot.Metrics.FileCount,
                snapshot.Metrics.TotalBytes,
                snapshot.Metrics.ReplayDuration.TotalMilliseconds,
                snapshot.Metrics.ProjectContextCount,
                snapshot.Metrics.AnalyzerItemCount,
                snapshot.Metrics.ConfirmedBindingCount);
            return snapshot;
        }
        finally
        {
            await DeleteDirectoryWithRetryAsync(tempDirectory).ConfigureAwait(false);
        }
    }

    private static async Task OpenWorkspaceAsync(
        MSBuildWorkspace workspace,
        string fullPath,
        BinaryLogger binaryLogger,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fullPath);
        if (string.Equals(extension, ".sln", PathComparison)
            || string.Equals(extension, ".slnx", PathComparison))
        {
            _ = await workspace
                .OpenSolutionAsync(fullPath, msbuildLogger: binaryLogger, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (string.Equals(extension, ".csproj", PathComparison))
        {
            var project = await workspace
                .OpenProjectAsync(fullPath, msbuildLogger: binaryLogger, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            _ = workspace.CurrentSolution.GetProject(project.Id)
                ?? throw new InvalidOperationException($"Unable to load project '{fullPath}'.");
            return;
        }

        throw new NotSupportedException("Only .sln, .slnx, and .csproj files are supported.");
    }

    private AnalyzerProvenanceSnapshot BuildSnapshot(
        Guid loadSessionId,
        string fullPath,
        IReadOnlyDictionary<string, string> requestedGlobalProperties,
        Solution solution,
        ReplayOutcome replay,
        int fileCount,
        long totalBytes,
        TimeSpan replayDuration)
    {
        var bindings = replay.Items
            .Select(item => replay.Status == AnalyzerProvenanceCaptureStatus.Complete
                ? Bind(solution, replay.Contexts, item)
                : new AnalyzerProvenanceBinding(
                    AnalyzerProvenanceBindingStatus.CaptureIncomplete,
                    null,
                    null,
                    item.Identity,
                    item.SourceProjectFile,
                    item.NearestTargetFramework,
                    ImmutableDictionary<string, string>.Empty))
            .ToImmutableArray();
        var msbuildAssembly = Assembly.Load(new AssemblyName("Microsoft.Build"));
        var workspaceDirectory = Path.GetDirectoryName(fullPath)!;
        var dotNetSdkPath = GlobalJsonSdkReader.TryResolveSdkDirectory(
            workspaceDirectory,
            Environment.Is64BitProcess);
        var runtimeMsBuildDirectory = Path.GetDirectoryName(msbuildAssembly.Location);
        if (dotNetSdkPath is null
            && runtimeMsBuildDirectory is not null
            && File.Exists(Path.Combine(runtimeMsBuildDirectory, "dotnet.dll")))
        {
            dotNetSdkPath = runtimeMsBuildDirectory;
        }

        var metrics = new AnalyzerProvenanceCaptureMetrics(
            fileCount,
            totalBytes,
            replayDuration,
            replay.Contexts.Length,
            replay.Items.Length,
            bindings.Count(binding => binding.Status == AnalyzerProvenanceBindingStatus.Confirmed));

        return new AnalyzerProvenanceSnapshot(
            loadSessionId,
            Path.GetFullPath(fullPath),
            requestedGlobalProperties.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase),
            new AnalyzerProvenanceToolset(
                MsBuildEnvironmentInfo.RegisteredMsBuildPath,
                MsBuildEnvironmentInfo.RegisteredInstanceVersion,
                msbuildAssembly.Location,
                msbuildAssembly.GetName().Version?.ToString(),
                dotNetSdkPath,
                GlobalJsonSdkReader.TryGetPinnedSdkVersion(workspaceDirectory)
                    ?? (dotNetSdkPath is null ? null : Path.GetFileName(dotNetSdkPath))),
            replay.Status,
            replay.Contexts,
            replay.Items,
            bindings,
            replay.FailureCodes,
            metrics);
    }

    private static ReplayOutcome ReplayAll(
        IReadOnlyList<string> binlogPaths,
        CancellationToken cancellationToken)
    {
        if (binlogPaths.Count == 0)
        {
            return new ReplayOutcome(
                AnalyzerProvenanceCaptureStatus.Failed,
                [],
                [],
                ["missing_binlog"]);
        }

        var contexts = ImmutableArray.CreateBuilder<AnalyzerProvenanceProjectContext>();
        var items = ImmutableArray.CreateBuilder<CapturedAnalyzerProvenanceItem>();
        var failures = ImmutableArray.CreateBuilder<string>();
        for (var index = 0; index < binlogPaths.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var replay = ReplayOne(binlogPaths[index], index, cancellationToken);
                contexts.AddRange(replay.Contexts);
                items.AddRange(replay.Items);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                failures.Add($"replay_failed:{index}:{ex.GetType().Name}");
            }
        }

        var status = failures.Count switch
        {
            0 => AnalyzerProvenanceCaptureStatus.Complete,
            _ when failures.Count == binlogPaths.Count => AnalyzerProvenanceCaptureStatus.Failed,
            _ => AnalyzerProvenanceCaptureStatus.Incomplete,
        };
        return new ReplayOutcome(status, contexts.ToImmutable(), items.ToImmutable(), failures.ToImmutable());
    }

    private void ApplyFailureSeam(IReadOnlyList<string> binlogPaths)
    {
        var failureMode = (AnalyzerProvenanceCaptureFailureMode)Interlocked.Exchange(
            ref _nextFailureMode,
            (int)AnalyzerProvenanceCaptureFailureMode.None);
        if (failureMode == AnalyzerProvenanceCaptureFailureMode.None || binlogPaths.Count == 0)
        {
            return;
        }

        switch (failureMode)
        {
            case AnalyzerProvenanceCaptureFailureMode.Missing:
                foreach (var path in binlogPaths)
                {
                    File.Delete(path);
                }

                break;
            case AnalyzerProvenanceCaptureFailureMode.Corrupt:
                foreach (var path in binlogPaths)
                {
                    File.WriteAllBytes(path, [0x01, 0x02, 0x03, 0x04]);
                }

                break;
            case AnalyzerProvenanceCaptureFailureMode.MixedValidAndCorrupt:
                var validCopy = Path.Combine(
                    Path.GetDirectoryName(binlogPaths[0])!,
                    "capture-valid-copy.binlog");
                File.Copy(binlogPaths[0], validCopy);
                File.WriteAllBytes(binlogPaths[0], [0x01, 0x02, 0x03, 0x04]);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(failureMode), failureMode, "Unsupported capture failure mode.");
        }
    }

    private static ReplayFileOutcome ReplayOne(
        string binlogPath,
        int binlogOrdinal,
        CancellationToken cancellationToken)
    {
        var contexts = new List<RawProjectContext>();
        var items = new List<RawAnalyzerItem>();
        var replay = new BinaryLogReplayEventSource();
        replay.ProjectStarted += (_, args) =>
        {
            if (args.BuildEventContext is null || string.IsNullOrWhiteSpace(args.ProjectFile))
            {
                return;
            }

            contexts.Add(new RawProjectContext(
                AnalyzerProvenanceProjectContextKey.From(args.BuildEventContext),
                args.ParentProjectBuildEventContext is null
                    ? null
                    : AnalyzerProvenanceEventContextKey.From(args.ParentProjectBuildEventContext),
                NormalizePath(args.ProjectFile),
                Copy(args.GlobalProperties),
                ReadProperties(args.Properties)));
        };
        replay.AnyEventRaised += (_, args) =>
        {
            if (args is not TaskParameterEventArgs
                {
                    Kind: TaskParameterMessageKind.TaskOutput,
                    ItemType: "Analyzer",
                    BuildEventContext: not null,
                } taskOutput)
            {
                return;
            }

            foreach (var item in taskOutput.Items.OfType<ITaskItem>())
            {
                var eventContext = taskOutput.BuildEventContext!;
                var metadata = item.MetadataNames
                    .Cast<string>()
                    .ToImmutableDictionary(
                        name => name,
                        item.GetMetadata,
                        StringComparer.OrdinalIgnoreCase);
                items.Add(new RawAnalyzerItem(
                    AnalyzerProvenanceProjectContextKey.From(eventContext),
                    AnalyzerProvenanceEventContextKey.From(eventContext),
                    item.ItemSpec,
                    metadata));
            }
        };

        using var stream = new FileStream(
            binlogPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        replay.Replay(stream, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var immutableContexts = contexts
            .Select(context => new AnalyzerProvenanceProjectContext(
                binlogOrdinal,
                context.Context,
                context.ParentContext,
                context.ProjectFile,
                context.GlobalProperties,
                context.Properties))
            .ToImmutableArray();
        var contextFiles = contexts
            .GroupBy(context => context.Context)
            .ToDictionary(group => group.Key, group => group.Select(value => value.ProjectFile).Distinct().ToArray());
        var immutableItems = items
            .Select(item =>
            {
                contextFiles.TryGetValue(item.ConsumerContext, out var consumerFiles);
                var consumerProjectFile = consumerFiles is { Length: 1 } ? consumerFiles[0] : null;
                var identity = ResolveEventPath(item.ItemSpec, consumerProjectFile);
                var sourceProjectFile = ResolveEventPath(Get(item.Metadata, "MSBuildSourceProjectFile"), consumerProjectFile);
                return new CapturedAnalyzerProvenanceItem(
                    binlogOrdinal,
                    item.ConsumerContext,
                    item.TaskContext,
                    identity,
                    sourceProjectFile,
                    Get(item.Metadata, "ReferenceSourceTarget"),
                    Get(item.Metadata, "OutputItemType"),
                    Get(item.Metadata, "ReferenceOutputAssembly"),
                    Get(item.Metadata, "NearestTargetFramework"),
                    Get(item.Metadata, "SetTargetFramework"),
                    Get(item.Metadata, "SetConfiguration"),
                    Get(item.Metadata, "SetPlatform"),
                    Get(item.Metadata, "GlobalPropertiesToRemove"),
                    Get(item.Metadata, "UndefineProperties"),
                    item.Metadata);
            })
            .ToImmutableArray();
        return new ReplayFileOutcome(immutableContexts, immutableItems);
    }

    private static AnalyzerProvenanceBinding Bind(
        Solution solution,
        ImmutableArray<AnalyzerProvenanceProjectContext> contexts,
        CapturedAnalyzerProvenanceItem item)
    {
        var consumerContext = contexts
            .Where(context => context.BinlogOrdinal == item.BinlogOrdinal
                && context.Context == item.ConsumerContext)
            .ToArray();
        var consumerFiles = consumerContext
            .Select(context => context.ProjectFile)
            .Distinct(PathComparer.Instance)
            .ToArray();
        var consumerCandidates = consumerFiles.Length == 1
            ? solution.Projects
                .Where(project => PathEquals(project.FilePath, consumerFiles[0])
                    && project.AnalyzerReferences.Any(reference => PathEquals(reference.FullPath, item.Identity)))
                .ToArray()
            : [];
        var selectedConsumer = SelectProjectByExactOutputs(consumerCandidates, consumerContext, item.Identity);
        if (selectedConsumer.Status != ProjectSelectionStatus.Selected)
        {
            return CreateUnconfirmedBinding(
                selectedConsumer.Status == ProjectSelectionStatus.Ambiguous
                    ? AnalyzerProvenanceBindingStatus.AmbiguousConsumer
                    : AnalyzerProvenanceBindingStatus.MissingConsumer,
                selectedConsumer.Project?.Id,
                item);
        }

        if (string.IsNullOrWhiteSpace(item.SourceProjectFile))
        {
            return CreateUnconfirmedBinding(
                AnalyzerProvenanceBindingStatus.MissingSourceMetadata,
                selectedConsumer.Project!.Id,
                item);
        }

        var sourceCandidates = solution.Projects
            .Where(project => PathEquals(project.FilePath, item.SourceProjectFile))
            .ToArray();
        var nestedContexts = contexts
            .Where(context => context.BinlogOrdinal == item.BinlogOrdinal
                && context.ParentContext == item.TaskContext
                && PathEquals(context.ProjectFile, item.SourceProjectFile))
            .ToArray();
        var selectedSource = SelectProjectByExactOutputs(sourceCandidates, nestedContexts, item.Identity);
        if (selectedSource.Status != ProjectSelectionStatus.Selected)
        {
            return new AnalyzerProvenanceBinding(
                selectedSource.Status == ProjectSelectionStatus.Ambiguous
                    ? AnalyzerProvenanceBindingStatus.AmbiguousSourceProject
                    : AnalyzerProvenanceBindingStatus.MissingSourceProject,
                selectedConsumer.Project!.Id,
                selectedSource.Project?.Id,
                item.Identity,
                item.SourceProjectFile,
                item.NearestTargetFramework,
                ImmutableDictionary<string, string>.Empty);
        }

        var effectiveGlobals = SelectEffectiveGlobals(nestedContexts, selectedSource.Project!);
        return new AnalyzerProvenanceBinding(
            AnalyzerProvenanceBindingStatus.Confirmed,
            selectedConsumer.Project!.Id,
            selectedSource.Project!.Id,
            item.Identity,
            item.SourceProjectFile,
            Get(effectiveGlobals, "TargetFramework") ?? item.NearestTargetFramework,
            effectiveGlobals);
    }

    private static AnalyzerProvenanceBinding CreateUnconfirmedBinding(
        AnalyzerProvenanceBindingStatus status,
        ProjectId? consumerProjectId,
        CapturedAnalyzerProvenanceItem item) =>
        new(
            status,
            consumerProjectId,
            null,
            item.Identity,
            item.SourceProjectFile,
            item.NearestTargetFramework,
            ImmutableDictionary<string, string>.Empty);

    private static ProjectSelection SelectProjectByExactOutputs(
        IReadOnlyList<Project> candidates,
        IReadOnlyList<AnalyzerProvenanceProjectContext> contexts,
        string analyzerIdentity)
    {
        if (candidates.Count == 0)
        {
            return new ProjectSelection(ProjectSelectionStatus.Missing, null);
        }

        if (candidates.Count == 1)
        {
            return new ProjectSelection(ProjectSelectionStatus.Selected, candidates[0]);
        }

        var exactIds = new HashSet<ProjectId>();
        foreach (var project in candidates)
        {
            if (PathEquals(analyzerIdentity, project.OutputFilePath))
            {
                exactIds.Add(project.Id);
            }

            foreach (var context in contexts)
            {
                var targetPath = ResolveProjectValue(context.ProjectFile, Get(context.Properties, "TargetPath"));
                var intermediateAssembly = ResolveProjectValue(
                    context.ProjectFile,
                    Get(context.Properties, "IntermediateAssembly"));
                if (PathEquals(targetPath, project.OutputFilePath)
                    || PathEquals(intermediateAssembly, project.CompilationOutputInfo.AssemblyPath))
                {
                    exactIds.Add(project.Id);
                }
            }
        }

        return exactIds.Count == 1
            ? new ProjectSelection(
                ProjectSelectionStatus.Selected,
                candidates.Single(project => exactIds.Contains(project.Id)))
            : new ProjectSelection(ProjectSelectionStatus.Ambiguous, null);
    }

    private static ImmutableDictionary<string, string> SelectEffectiveGlobals(
        IReadOnlyList<AnalyzerProvenanceProjectContext> contexts,
        Project selectedProject)
    {
        var exact = contexts
            .Where(context =>
            {
                var targetPath = ResolveProjectValue(context.ProjectFile, Get(context.Properties, "TargetPath"));
                var intermediateAssembly = ResolveProjectValue(
                    context.ProjectFile,
                    Get(context.Properties, "IntermediateAssembly"));
                return PathEquals(targetPath, selectedProject.OutputFilePath)
                    || PathEquals(intermediateAssembly, selectedProject.CompilationOutputInfo.AssemblyPath);
            })
            .Select(context => context.GlobalProperties)
            .Distinct(ImmutableDictionaryComparer.Instance)
            .ToArray();
        if (exact.Length == 1)
        {
            return exact[0];
        }

        return contexts.Count == 1
            ? contexts[0].GlobalProperties
            : ImmutableDictionary<string, string>.Empty;
    }

    private string CreatePrivateTempDirectory()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"RoslynMcpServer-{Environment.ProcessId}",
            Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(root);
        }
        else
        {
            Directory.CreateDirectory(
                root,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return root;
    }

    private async Task DeleteDirectoryWithRetryAsync(string path)
    {
        var attempts = Math.Max(1, _options.CleanupAttempts);
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    return;
                }

                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == attempts - 1)
                {
                    _logger.LogWarning(
                        "Analyzer provenance temporary directory cleanup failed after {AttemptCount} attempts ({ExceptionType})",
                        attempts,
                        ex.GetType().Name);
                    return;
                }

                await Task.Delay(_options.CleanupRetryDelay, _timeProvider).ConfigureAwait(false);
            }
        }
    }

    private static ImmutableDictionary<string, string> Copy(IDictionary<string, string>? source) =>
        source is null
            ? ImmutableDictionary.Create<string, string>(StringComparer.OrdinalIgnoreCase)
            : source.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    private static ImmutableDictionary<string, string> ReadProperties(IEnumerable? properties)
    {
        var result = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        if (properties is null)
        {
            return result.ToImmutable();
        }

        foreach (var property in properties)
        {
            if (property is DictionaryEntry entry && entry.Key is string name)
            {
                result[name] = entry.Value?.ToString() ?? string.Empty;
                continue;
            }

            var type = property?.GetType();
            var reflectedName = type?.GetProperty("Name")?.GetValue(property)?.ToString()
                ?? type?.GetProperty("Key")?.GetValue(property)?.ToString();
            var reflectedValue = type?.GetProperty("EvaluatedValue")?.GetValue(property)?.ToString()
                ?? type?.GetProperty("Value")?.GetValue(property)?.ToString();
            if (!string.IsNullOrWhiteSpace(reflectedName))
            {
                result[reflectedName] = reflectedValue ?? string.Empty;
            }
        }

        return result.ToImmutable();
    }

    private static string? Get(IReadOnlyDictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static string NormalizePath(string path) => Path.GetFullPath(path);

    private static string ResolveEventPath(string? value, string? consumerProjectFile)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(value))
        {
            return Path.GetFullPath(value);
        }

        var baseDirectory = string.IsNullOrWhiteSpace(consumerProjectFile)
            ? Environment.CurrentDirectory
            : Path.GetDirectoryName(consumerProjectFile)!;
        return Path.GetFullPath(Path.Combine(baseDirectory, value));
    }

    private static string? ResolveProjectValue(string projectFile, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Path.IsPathRooted(value)
            ? Path.GetFullPath(value)
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectFile)!, value));
    }

    private static bool PathEquals(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), PathComparison);

    private sealed record RawProjectContext(
        AnalyzerProvenanceProjectContextKey Context,
        AnalyzerProvenanceEventContextKey? ParentContext,
        string ProjectFile,
        ImmutableDictionary<string, string> GlobalProperties,
        ImmutableDictionary<string, string> Properties);

    private sealed record RawAnalyzerItem(
        AnalyzerProvenanceProjectContextKey ConsumerContext,
        AnalyzerProvenanceEventContextKey TaskContext,
        string ItemSpec,
        ImmutableDictionary<string, string> Metadata);

    private sealed record ReplayFileOutcome(
        ImmutableArray<AnalyzerProvenanceProjectContext> Contexts,
        ImmutableArray<CapturedAnalyzerProvenanceItem> Items);

    private sealed record ReplayOutcome(
        AnalyzerProvenanceCaptureStatus Status,
        ImmutableArray<AnalyzerProvenanceProjectContext> Contexts,
        ImmutableArray<CapturedAnalyzerProvenanceItem> Items,
        ImmutableArray<string> FailureCodes);

    private enum ProjectSelectionStatus
    {
        Selected,
        Missing,
        Ambiguous,
    }

    private sealed record ProjectSelection(ProjectSelectionStatus Status, Project? Project);

    private sealed class PathComparer : IEqualityComparer<string>
    {
        public static PathComparer Instance { get; } = new();

        public bool Equals(string? x, string? y) => PathEquals(x, y);

        public int GetHashCode(string obj) =>
            Path.GetFullPath(obj).GetHashCode(
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private sealed class ImmutableDictionaryComparer :
        IEqualityComparer<ImmutableDictionary<string, string>>
    {
        public static ImmutableDictionaryComparer Instance { get; } = new();

        public bool Equals(
            ImmutableDictionary<string, string>? x,
            ImmutableDictionary<string, string>? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null || x.Count != y.Count)
            {
                return false;
            }

            return x.All(pair => y.TryGetValue(pair.Key, out var value)
                && string.Equals(pair.Value, value, StringComparison.Ordinal));
        }

        public int GetHashCode(ImmutableDictionary<string, string> obj)
        {
            var hash = new HashCode();
            foreach (var pair in obj.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                hash.Add(pair.Key, StringComparer.OrdinalIgnoreCase);
                hash.Add(pair.Value, StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }
    }
}
