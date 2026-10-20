using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Runtime.Versioning;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.MSBuild;

namespace ParallelLoad;

[SupportedOSPlatform("windows")]
internal static class LoadExperiment
{
    public static async Task<int> RunAsync(RunOptions options, string registration, TimeProvider? timeProvider = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)!);
        var temporary = Path.Combine(Path.GetTempPath(), "ParallelLoad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var diagnostics = new ConcurrentQueue<string>();
        var nativeProgress = new ConcurrentQueue<string>();
        var nativeFailures = new ConcurrentQueue<string>();
        var groups = options.Mode == "parallel"
            ? ProjectPartition.Create(options.SolutionPath, options.Workers, options.Strategy)
            : new[] { Array.Empty<string>() };
        timeProvider ??= TimeProvider.System;
        var workspaces = groups.Select(_ => CreateWorkspace(diagnostics, nativeFailures, timeProvider)).ToArray();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(options.TimeoutMinutes));
        object? report = null;
        try
        {
            var paths = groups.Select((group, index) => options.Mode == "parallel"
                ? ProjectPartition.WriteFilter(options.SolutionPath, group, temporary, index)
                : options.SolutionPath).ToArray();
            using var sampler = await ExternalSampler.StartAsync(temporary);
            using var rootProcess = Process.GetCurrentProcess();
            var initialRootCpuMs = rootProcess.TotalProcessorTime.TotalMilliseconds;
            var wall = Stopwatch.StartNew();
            var groupWatches = groups.Select(_ => new Stopwatch()).ToArray();
            var progress = groups.Select(_ => new PhaseLoadProgress(timeProvider)).ToArray();
            SolutionInfo[] solutions = [];
            Solution? solution = null;
            double mergeMs = 0;
            string? failure = null;
            try
            {
                // Separate loaders and ProjectMaps; Task.WhenAll waits for every group before disposal, including on failure.
                var tasks = paths.Select((path, index) => LoadGroupAsync(
                    workspaces[index], path, options.Mode, options.BinlogDirectory ?? temporary,
                    groupWatches[index], nativeProgress, progress[index], options, timeout.Token)).ToArray();
                solutions = await Task.WhenAll(tasks);
                var merge = Stopwatch.StartNew();
                var info = options.Mode == "parallel"
                    ? ProjectMerger.Merge(solutions, groups, options.SolutionPath)
                    : solutions[0];
                using var mergedWorkspace = new AdhocWorkspace(MSBuildMefHostServices.DefaultServices);
                solution = mergedWorkspace.AddSolution(info);
                merge.Stop();
                mergeMs = merge.Elapsed.TotalMilliseconds;
                wall.Stop();
                var rootCpuMs = rootProcess.TotalProcessorTime.TotalMilliseconds - initialRootCpuMs;
                var processMetrics = await sampler.StopAsync(wall.Elapsed.TotalMilliseconds, rootCpuMs);
                var observation = SolutionObservation.Capture(solution);
                var total = solutions.Sum(item => item.Projects.Count);
                report = new
                {
                    Status = "ok", options.Mode, options.Workers, options.Strategy,
                    options.GlobalProperties, Properties = ExperimentLoadSettings.Properties(options),
                    LocalSdk = options.LocalSdkPath,
                    DotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"),
                    DiagnosticRun = options.Mode == "baseline-binlog" || options.BinlogDirectory is not null,
                    ProjectLoadProgress = progress.Select(value => value.Events).ToArray(),
                    Solution = options.SolutionPath, Registration = registration,
                    FilterLocation = options.Mode == "parallel" ? "temporary directory; absolute solution path" : "none",
                    WallMs = wall.Elapsed.TotalMilliseconds,
                    GroupMs = groupWatches.Select(watch => watch.Elapsed.TotalMilliseconds).ToArray(),
                    MergeMs = mergeMs, RequestedCounts = groups.Select(group => group.Length).ToArray(),
                    GroupProjectCounts = solutions.Select(item => item.Projects.Count).ToArray(),
                    TotalProjects = total, UniqueProjects = observation.Count, RepeatedProjects = total - observation.Count,
                    UnresolvedReferenceCount = info.Projects.Sum(project => project.ProjectReferences.Count(reference =>
                        !solution.ContainsProject(reference.ProjectId))),
                    DiagnosticCount = diagnostics.Count,
                    BinlogCount = Directory.GetFiles(options.BinlogDirectory ?? temporary, "*.binlog").Length,
                    BinlogBytes = Directory.GetFiles(options.BinlogDirectory ?? temporary, "*.binlog").Sum(path => new FileInfo(path).Length),
                    Diagnostics = diagnostics.Distinct().Order(StringComparer.Ordinal).ToArray(),
                    NativeProgress = nativeProgress.ToArray(), NativeFailures = nativeFailures.ToArray(),
                    Process = processMetrics,
                    Projects = observation,
                };
            }
            catch (Exception ex)
            {
                failure = ex.ToString();
                wall.Stop();
                var rootCpuMs = rootProcess.TotalProcessorTime.TotalMilliseconds - initialRootCpuMs;
                var processMetrics = await sampler.StopAsync(wall.Elapsed.TotalMilliseconds, rootCpuMs);
                report = new
                {
                    Status = timeout.IsCancellationRequested ? "timeout" : "failed",
                    options.Mode, options.Workers, options.Strategy, Solution = options.SolutionPath,
                    options.GlobalProperties, ProjectLoadProgress = progress.Select(value => value.Events).ToArray(),
                    Registration = registration, WallMs = wall.Elapsed.TotalMilliseconds,
                    GroupMs = groupWatches.Select(watch => watch.Elapsed.TotalMilliseconds).ToArray(),
                    Error = failure, DiagnosticCount = diagnostics.Count,
                    Diagnostics = diagnostics.Distinct().Order(StringComparer.Ordinal).ToArray(),
                    Process = processMetrics,
                };
            }

            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(options.OutputPath, Redact(json, options.SolutionPath, temporary));
            Console.WriteLine($"{options.Mode} workers={options.Workers} status={(failure is null ? "ok" : "failed")} wallMs={wall.Elapsed.TotalMilliseconds:F0} report={options.OutputPath}");
            return failure is null ? 0 : 2;
        }
        finally
        {
            foreach (var workspace in workspaces)
            {
                workspace.Dispose();
            }

            Directory.Delete(temporary, recursive: true);
        }
    }

    internal static string Redact(string json, string solutionPath, string temporary)
    {
        var replacements = new[]
        {
            (Path.GetDirectoryName(solutionPath)!, "<solution>"),
            (temporary, "<temporary>"),
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "<user>"),
            (Environment.MachineName, "<machine>"),
        };
        foreach (var (value, replacement) in replacements)
        {
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            // Snapshot fields contain JSON strings within JSON: redact both escaping levels, longest first.
            foreach (var variant in new[] { value.Replace("\\", "\\\\\\\\"), value.Replace("\\", "\\\\"), value.Replace("\\", "/"), value })
            {
                json = json.Replace(variant, replacement, StringComparison.OrdinalIgnoreCase);
            }
        }

        return json;
    }

    private static AdhocWorkspace CreateWorkspace(
        ConcurrentQueue<string> diagnostics,
        ConcurrentQueue<string> nativeFailures,
        TimeProvider timeProvider)
    {
        var workspace = new AdhocWorkspace(MSBuildMefHostServices.DefaultServices);
        workspace.RegisterWorkspaceFailedHandler(args =>
        {
            diagnostics.Enqueue(JsonSerializer.Serialize(new
            {
                Kind = args.Diagnostic.Kind.ToString(), args.Diagnostic.Message,
            }));
            if (args.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure &&
                args.Diagnostic.Message.Contains(".vcxproj", StringComparison.OrdinalIgnoreCase))
            {
                nativeFailures.Enqueue(JsonSerializer.Serialize(new
                {
                    Timestamp = timeProvider.GetUtcNow(), args.Diagnostic.Message,
                }));
            }
        });
        return workspace;
    }

    private static async Task<SolutionInfo> LoadGroupAsync(
        AdhocWorkspace workspace,
        string path,
        string mode,
        string temporary,
        Stopwatch watch,
        ConcurrentQueue<string> nativeProgress,
        PhaseLoadProgress progress,
        RunOptions options,
        CancellationToken cancellationToken)
    {
        var properties = ExperimentLoadSettings.Properties(options);
        var loader = new MSBuildProjectLoader(workspace, properties);
        ILogger? logger = mode == "baseline-binlog" ? new BinaryLogger
        {
            Parameters = Path.Combine(temporary, "capture.binlog") + ";ProjectImports=None",
            Verbosity = LoggerVerbosity.Diagnostic,
        } : null;
        logger ??= ExperimentLoadSettings.Logger(options, "ordinary");
        watch.Start();
        progress.Start();
        try
        {
            return await loader.LoadSolutionInfoAsync(path, progress: progress,
                msbuildLogger: logger, cancellationToken: cancellationToken);
        }
        finally
        {
            watch.Stop();
            progress.Stop();
            foreach (var value in progress.Events.Where(value => value.Contains(".vcxproj", StringComparison.OrdinalIgnoreCase)))
            {
                nativeProgress.Enqueue(value);
            }
        }
    }

}
