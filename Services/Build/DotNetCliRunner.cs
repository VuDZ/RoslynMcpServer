using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using RoslynMcpServer.Diagnostics;

namespace RoslynMcpServer.Services.Build;

public static class DotNetCliRunner
{
    public const int DefaultTimeoutSeconds = 300;
    private const string PreparationTimeoutMetadata = "- **Command start:** skipped because the timeout elapsed during command preparation.";

    public sealed record RunResult(
        int ExitCode,
        string CombinedOutput,
        string RunMetadata,
        int StdOutLength,
        int StdErrLength,
        bool TimedOut = false,
        bool Cancelled = false,
        bool ProcessKilled = false)
    {
        internal TimeSpan? Elapsed { get; init; }
        internal bool OutputTruncated { get; init; }
        internal bool TestSummaryTruncated { get; init; }
    }

    public sealed record SeparatedRunResult(
        int ExitCode,
        string StdOut,
        string StdErr,
        string RunMetadata,
        bool TimedOut,
        string? ExceptionType)
    {
        internal bool OutputTruncated { get; init; }
    }

    public static async Task<(int ExitCode, string CombinedOutput)> RunAsync(
        string arguments,
        string? workingDirectory,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        var result = await RunWithMetadataAsync(arguments, workingDirectory, cancellationToken, timeout)
            .ConfigureAwait(false);
        return (result.ExitCode, result.CombinedOutput);
    }

    /// <summary>
    /// Runs the CLI process with stdout and stderr kept separate. When
    /// <paramref name="progress"/> is supplied, emits periodic heartbeats while the
    /// process is alive. Without a watch the behavior is byte-for-byte unchanged.
    /// </summary>
    public static async Task<SeparatedRunResult> RunSeparatedAsync(
        string arguments,
        string? workingDirectory,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default,
        CliProgressWatch? progress = null)
    {
        var deadline = CommandDeadline.Start(timeout);
        var workDir = string.IsNullOrWhiteSpace(workingDirectory)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(workingDirectory);

        if (progress is null)
        {
            return await RunSeparatedCoreAsync(arguments, workDir, deadline, cancellationToken).ConfigureAwait(false);
        }

        var heartbeat = StartHeartbeat(progress, cancellationToken);
        try
        {
            return await RunSeparatedCoreAsync(arguments, workDir, deadline, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await StopHeartbeatAsync(heartbeat).ConfigureAwait(false);
        }
    }

    private static async Task<SeparatedRunResult> RunSeparatedCoreAsync(
        string arguments,
        string workDir,
        CommandDeadline deadline,
        CancellationToken cancellationToken)
    {
        var dotnet = DotNetHostResolver.ResolveDotNetExecutable();
        var sdk = await ResolveSdkWithCommandBudgetAsync(dotnet, workDir, deadline, cancellationToken)
            .ConfigureAwait(false);
        if (deadline.Remaining == TimeSpan.Zero)
        {
            var metadata = BuildRunMetadata(dotnet, workDir, string.Empty, sdk);
            return new SeparatedRunResult(
                -1, string.Empty, string.Empty, metadata + Environment.NewLine + PreparationTimeoutMetadata,
                TimedOut: true, ExceptionType: null);
        }

        var run = await RunCommandAsync(dotnet, arguments, workDir, sdk, deadline, cancellationToken)
            .ConfigureAwait(false);
        var (stdout, stderr) = BoundedProcessOutput.ProtectCrossPipeSecrets(run.StdOut, run.StdErr);
        var runMetadata = BuildRunMetadata(dotnet, workDir, string.Join('\n', stdout.Text, stderr.Text), sdk);
        return new SeparatedRunResult(
            run.ExitCode, stdout.Text, stderr.Text, runMetadata, run.TimedOut,
            run.Failure?.SourceException.GetType().Name)
        {
            OutputTruncated = stdout.Truncated || stderr.Truncated,
        };
    }

    /// <summary>
    /// Runs the CLI process and, when <paramref name="progress"/> is supplied, emits periodic
    /// heartbeats while the process is alive. Without a watch the behavior is byte-for-byte
    /// unchanged (no timer, no reporting).
    /// </summary>
    public static async Task<RunResult> RunWithMetadataAsync(
        string arguments,
        string? workingDirectory,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null,
        CliProgressWatch? progress = null)
    {
        var deadline = CommandDeadline.Start(timeout);
        var workDir = string.IsNullOrWhiteSpace(workingDirectory)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(workingDirectory);
        var dotnet = DotNetHostResolver.ResolveDotNetExecutable();
        var sdk = await ResolveSdkWithCommandBudgetAsync(dotnet, workDir, deadline, cancellationToken)
            .ConfigureAwait(false);
        if (deadline.Remaining == TimeSpan.Zero)
        {
            var metadata = BuildRunMetadata(dotnet, workDir, string.Empty, sdk);
            return new RunResult(
                -1, string.Empty, metadata + Environment.NewLine + PreparationTimeoutMetadata, 0, 0, TimedOut: true);
        }

        return await RunWithSdkAsync(dotnet, arguments, workDir, sdk, deadline, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<DotNetSdkEnvironment.SdkPinInfo> ResolveSdkWithCommandBudgetAsync(
        string dotnet,
        string workDir,
        CommandDeadline deadline,
        CancellationToken cancellationToken)
    {
        var probeLimit = deadline.Remaining is { } finite && finite < DotNetSdkResolver.DefaultTimeout
            ? finite
            : DotNetSdkResolver.DefaultTimeout;
        var sdk = await DotNetSdkResolver.ResolveAsync(dotnet, workDir, probeLimit, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return sdk;
    }

    internal static Task<RunResult> RunWithSdkAsync(
        string dotnet,
        string arguments,
        string workingDirectory,
        DotNetSdkEnvironment.SdkPinInfo sdk,
        TimeSpan? timeout,
        CliProgressWatch? progress,
        CancellationToken cancellationToken)
        => RunWithSdkAsync(dotnet, arguments, workingDirectory, sdk, CommandDeadline.Start(timeout), progress, cancellationToken);

    private static async Task<RunResult> RunWithSdkAsync(
        string dotnet,
        string arguments,
        string workingDirectory,
        DotNetSdkEnvironment.SdkPinInfo sdk,
        CommandDeadline deadline,
        CliProgressWatch? progress,
        CancellationToken cancellationToken)
    {
        if (progress is null)
        {
            return await RunCoreAsync(dotnet, arguments, workingDirectory, sdk, deadline, cancellationToken)
                .ConfigureAwait(false);
        }

        var heartbeat = StartHeartbeat(progress, cancellationToken);
        try
        {
            return await RunCoreAsync(dotnet, arguments, workingDirectory, sdk, deadline, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            await StopHeartbeatAsync(heartbeat).ConfigureAwait(false);
        }
    }

    private static async Task<RunResult> RunCoreAsync(
        string dotnet,
        string arguments,
        string workDir,
        DotNetSdkEnvironment.SdkPinInfo sdk,
        CommandDeadline deadline,
        CancellationToken cancellationToken)
    {
        var run = await RunCommandAsync(dotnet, arguments, workDir, sdk, deadline, cancellationToken)
            .ConfigureAwait(false);
        run.Failure?.Throw();
        var (stdout, stderr) = BoundedProcessOutput.ProtectCrossPipeSecrets(run.StdOut, run.StdErr);
        var combined = CombineStreams(stdout.Text, stderr.Text);
        var metadata = BuildRunMetadata(dotnet, workDir, combined, sdk);
        return new RunResult(
            run.ExitCode,
            combined,
            metadata,
            run.StdOut.Length,
            run.StdErr.Length,
            TimedOut: run.TimedOut,
            ProcessKilled: run.ProcessKilled)
        {
            Elapsed = run.Elapsed,
            OutputTruncated = stdout.Truncated || stderr.Truncated,
            TestSummaryTruncated = stdout.SummaryTruncated || stderr.SummaryTruncated,
        };
    }

    private static async Task<CommandRunResult> RunCommandAsync(
        string dotnet,
        string arguments,
        string workDir,
        DotNetSdkEnvironment.SdkPinInfo sdk,
        CommandDeadline deadline,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var psi = CreateProcessStartInfo(dotnet, arguments, workDir, sdk);
        cancellationToken.ThrowIfCancellationRequested();
        if (deadline.Remaining == TimeSpan.Zero)
        {
            var empty = new BoundedProcessOutput.Capture(string.Empty, 0);
            return new CommandRunResult(-1, empty, empty, TimedOut: true);
        }

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
        {
            throw new InvalidOperationException(FailedToStartDotNetMessage(dotnet));
        }

        using var stdout = process.StandardOutput;
        using var stderr = process.StandardError;
        var stdoutTask = BoundedProcessOutput.ReadAsync(stdout);
        var stderrTask = BoundedProcessOutput.ReadAsync(stderr);
        // A descendant can keep a redirected pipe open after dotnet exits.
        // See docs/project-review-2026-11-25/task-04-p1-dotnet-pipe-deadline-hi.md.
        var completion = Task.WhenAll(process.WaitForExitAsync(CancellationToken.None), stdoutTask, stderrTask);

        bool completed;
        try
        {
            completed = await WaitForCompletionAsync(completion, deadline, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await CleanupAsync(process, completion).ConfigureAwait(false);
            if (ex is OperationCanceledException)
            {
                throw;
            }

            return new CommandRunResult(
                process.HasExited ? process.ExitCode : -1,
                ReadCompletedOutput(stdoutTask),
                ReadCompletedOutput(stderrTask),
                Failure: ExceptionDispatchInfo.Capture(ex));
        }

        if (completed)
        {
            return new CommandRunResult(
                process.ExitCode,
                ReadCompletedOutput(stdoutTask),
                ReadCompletedOutput(stderrTask),
                Stopwatch.GetElapsedTime(started));
        }

        var killed = await CleanupAsync(process, completion).ConfigureAwait(false);
        return new CommandRunResult(
            process.HasExited && process.ExitCode != 0 ? process.ExitCode : -1,
            ReadCompletedOutput(stdoutTask),
            ReadCompletedOutput(stderrTask),
            TimedOut: true,
            ProcessKilled: killed);
    }

    private static (CancellationTokenSource Cts, Task Task)? StartHeartbeat(
        CliProgressWatch progress,
        CancellationToken cancellationToken)
    {
        var interval = progress.HeartbeatInterval > TimeSpan.Zero
            ? progress.HeartbeatInterval
            : CliProgressWatch.DefaultHeartbeatInterval;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        return (cts, ReportHeartbeatAsync(progress, stopwatch, interval, cts.Token));
    }

    private static async Task StopHeartbeatAsync((CancellationTokenSource Cts, Task Task)? heartbeat)
    {
        if (heartbeat is not { } value)
        {
            return;
        }

        try
        {
            await value.Cts.CancelAsync().ConfigureAwait(false);
            await value.Task.ConfigureAwait(false);
        }
        catch
        {
            // Progress must never change the CLI outcome.
        }
        finally
        {
            value.Cts.Dispose();
        }
    }

    private static async Task ReportHeartbeatAsync(
        CliProgressWatch progress,
        Stopwatch stopwatch,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                progress.Reporter.Report(new CliProgressUpdate(progress.Stage, stopwatch.Elapsed, progress.LastExitCode));
            }
            catch
            {
                // A progress failure (client disconnect, host without progress support) is not a build failure.
            }
        }
    }

    public static async Task<string> CreateRunMetadataAsync(
        string workingDirectory,
        string combinedOutput,
        CancellationToken cancellationToken)
    {
        var dotnet = DotNetHostResolver.ResolveDotNetExecutable();
        var sdk = await DotNetSdkResolver.ResolveAsync(
                dotnet,
                workingDirectory,
                DotNetSdkResolver.DefaultTimeout,
                cancellationToken)
            .ConfigureAwait(false);
        return BuildRunMetadata(dotnet, workingDirectory, combinedOutput, sdk);
    }

    public static string FormatHangHints(bool timedOut, bool cancelled)
    {
        var sb = new StringBuilder();
        if (timedOut)
        {
            sb.AppendLine("**MCP_DOTNET_TIMEOUT:** operation reached the tool timeout.");
        }

        if (cancelled)
        {
            sb.AppendLine("**MCP_DOTNET_CANCELLED:** tool cancel requested.");
        }

        var listProcesses = OperatingSystem.IsWindows() ? "Get-Process dotnet" : "ps -ef";
        sb.AppendLine(
            $"If the next `dotnet` call is slow, check for lingering `dotnet` processes (`{listProcesses}`), delete locked `obj`/`bin` if needed, " +
            "avoid parallel MCP `run_dotnet_test`/`run_dotnet_build`, and compare with shell `dotnet` from the same WorkingDirectory.");
        return sb.ToString().TrimEnd();
    }

    internal static ProcessStartInfo CreateProcessStartInfo(
        string dotnet,
        string arguments,
        string workDir,
        DotNetSdkEnvironment.SdkPinInfo? sdk = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = dotnet,
            Arguments = arguments,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        DotNetSdkEnvironment.ApplySdkSelection(psi, sdk);
        EnsureWindowsProcessorArchitecture(psi, RuntimeInformation.ProcessArchitecture);
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        // Parsers are English-only; a ru-RU machine otherwise prints "Пройдено!" instead of "Passed!".
        psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
        return psi;
    }

    internal static void EnsureWindowsProcessorArchitecture(ProcessStartInfo psi, Architecture processArchitecture)
    {
        if (!OperatingSystem.IsWindows()
            || (psi.Environment.TryGetValue("PROCESSOR_ARCHITECTURE", out var inheritedArchitecture)
                && !string.IsNullOrEmpty(inheritedArchitecture)))
        {
            return;
        }

        // Restricted hosts can omit this normally inherited Windows variable. The SDK's InstallerBase
        // dereferences it during `dotnet --info`, so fill only the child environment before launching.
        // https://github.com/dotnet/sdk/blob/main/src/Cli/dotnet/Installer/Windows/InstallerBase.cs
        // DotNetHostResolver normally selects a host matching the MCP process architecture.
        psi.Environment["PROCESSOR_ARCHITECTURE"] = processArchitecture switch
        {
            Architecture.X64 => "AMD64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "ARM64",
            Architecture.Arm => "ARM",
            _ => processArchitecture.ToString().ToUpperInvariant()
        };
    }

    /// <summary>Returns whether the process exited and both pipes closed within the remaining budget.</summary>
    private static async Task<bool> WaitForCompletionAsync(
        Task completion,
        CommandDeadline deadline,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var remaining = deadline.Remaining;
        if (remaining is null)
        {
            await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (remaining == TimeSpan.Zero)
        {
            return false;
        }

        // Bound the combined task externally because neither process exit nor a pipe read is
        // guaranteed to observe a cancellation token promptly.
        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = Task.Delay(remaining.Value, delayCts.Token);
        if (await Task.WhenAny(completion, delayTask).ConfigureAwait(false) == completion)
        {
            await delayCts.CancelAsync().ConfigureAwait(false);
            await completion.ConfigureAwait(false);
            return true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }

    internal static bool TryKillProcessTree(Process process)
    {
        List<Process> descendants = [];
        var killed = false;
        try
        {
            if (!process.HasExited)
            {
                // VSTest hosts exit on parent shutdown and can orphan children before Kill(true) finds them.
                try
                {
                    descendants = CaptureDescendants(process);
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Still terminate the root when a descendant snapshot is unavailable.
                }
                for (var index = descendants.Count - 1; index >= 0; index--)
                {
                    try
                    {
                        descendants[index].Kill(entireProcessTree: true);
                        killed = true;
                    }
                    catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        // A descendant may already have exited with its parent.
                    }
                }
                process.Kill(entireProcessTree: true);
                killed = true;
            }
        }
        catch
        {
            // Best effort when the process exits concurrently or termination is denied.
        }
        finally
        {
            foreach (var descendant in descendants)
            {
                descendant.Dispose();
            }
        }

        return killed;
    }

    private static List<Process> CaptureDescendants(Process root)
    {
        var descendants = new List<Process>();
        if (!OperatingSystem.IsWindows())
        {
            return descendants;
        }

        var rootStarted = root.StartTime;
        var processes = Process.GetProcesses();
        try
        {
            var children = new Dictionary<int, List<Process>>();
            foreach (var process in processes)
            {
                var parentId = WindowsParentProcess.TryReadParentProcessId(process);
                if (!children.TryGetValue(parentId, out var siblings))
                {
                    children[parentId] = siblings = [];
                }
                siblings.Add(process);
            }
            var pending = new Queue<(int Id, DateTime Started)>();
            var visited = new HashSet<int> { root.Id };
            pending.Enqueue((root.Id, rootStarted));
            while (pending.TryDequeue(out var parent))
            {
                if (!children.TryGetValue(parent.Id, out var directChildren))
                {
                    continue;
                }
                foreach (var child in directChildren)
                {
                    try
                    {
                        var childStarted = child.StartTime;
                        if (visited.Add(child.Id) && childStarted >= parent.Started)
                        {
                            descendants.Add(child);
                            pending.Enqueue((child.Id, childStarted));
                        }
                    }
                    catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        // Process exit can race the descendant snapshot.
                    }
                }
            }
            return descendants;
        }
        finally
        {
            foreach (var process in processes)
            {
                if (!descendants.Contains(process))
                {
                    process.Dispose();
                }
            }
        }
    }

    private static async Task<bool> CleanupAsync(Process process, Task completion)
    {
        var killed = TryKillProcessTree(process);
        // Observe a late fault without extending the shared cleanup budget for exit and both pipes.
        var observed = completion.ContinueWith(
            static task => { _ = task.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        await Task.WhenAny(observed, Task.Delay(_outputDrainTimeout)).ConfigureAwait(false);
        return killed;
    }

    private static BoundedProcessOutput.Capture ReadCompletedOutput(Task<BoundedProcessOutput.Capture> output) =>
        output.IsCompletedSuccessfully ? output.Result : new BoundedProcessOutput.Capture(string.Empty, 0);

    private static string CombineStreams(string stdout, string stderr)
    {
        var combined = new StringBuilder();
        if (!string.IsNullOrEmpty(stdout))
        {
            combined.Append(stdout);
        }

        if (!string.IsNullOrEmpty(stderr))
        {
            if (combined.Length > 0)
            {
                combined.AppendLine();
            }

            combined.Append(stderr);
        }

        return combined.ToString();
    }

    internal static string BuildRunMetadata(
        string dotnetPath,
        string workDir,
        string combinedOutput,
        DotNetSdkEnvironment.SdkPinInfo sdk)
    {
        var metadata = new StringBuilder();
        metadata.AppendLine($"- **dotnet host:** `{dotnetPath}` ({(Environment.Is64BitProcess ? "64-bit" : "32-bit")} MCP process)");
        if (sdk.ResolvedVersion is not null)
        {
            metadata.AppendLine($"- **dotnet --version:** `{sdk.ResolvedVersion}` (from working directory)");
        }

        if (sdk.GlobalJsonPath is not null)
        {
            metadata.AppendLine($"- **global.json:** `{sdk.GlobalJsonPath}`");

            if (sdk.PinnedVersion is not null)
            {
                metadata.AppendLine($"- **Requested SDK:** `{sdk.PinnedVersion}`");
            }
        }

        if (sdk.SdkDirectory is not null)
        {
            metadata.AppendLine($"- **Resolved SDK directory:** `{sdk.SdkDirectory}`");
            metadata.AppendLine($"- **Expected MSBuild:** `{sdk.MsBuildDllPath}`");
        }
        else if (sdk.GlobalJsonPath is not null && sdk.PinnedVersion is not null && sdk.ResolvedVersion is null)
        {
            metadata.AppendLine(PinnedSdkNotFoundWarning(sdk.PinnedVersion));
        }

        if (DotNetSdkEnvironment.HasConfiguredSdk(sdk) && sdk.SdksDirectory is not null)
        {
            metadata.AppendLine($"- **MSBuildSDKsPath / SDKS_DIR:** `{sdk.SdksDirectory}`");
        }

        DotNetSdkEnvironment.AppendSdkEnvMetadata(metadata, sdk, combinedOutput);

        var logMsbuild = Diagnostics.MsBuildLogHighlighter.TryGetMsBuildExecutablePath(combinedOutput);
        if (!string.IsNullOrEmpty(logMsbuild))
        {
            metadata.AppendLine($"- **MSBuild from log:** `{logMsbuild}`");
        }

        metadata.AppendLine($"- **WorkingDirectory:** `{workDir}`");
        metadata.AppendLine("- **DOTNET_CLI_UI_LANGUAGE:** `en-US`");
        return metadata.ToString().TrimEnd();
    }

    internal static string FailedToStartDotNetMessage(string dotnet, bool windowsInstall)
    {
        if (windowsInstall)
        {
            return $"Failed to start `{dotnet}`. Ensure a 64-bit .NET SDK is installed under Program Files\\dotnet.";
        }

        return $"Failed to start `{dotnet}`. Ensure a 64-bit .NET SDK is on PATH or DOTNET_ROOT points at the install.";
    }

    internal static string FailedToStartDotNetMessage(string dotnet) =>
        FailedToStartDotNetMessage(dotnet, OperatingSystem.IsWindows());

    internal static string PinnedSdkNotFoundWarning(string pinnedVersion, bool windowsInstall)
    {
        if (windowsInstall)
        {
            return $"- **WARNING:** dotnet could not confirm an SDK for global.json request `{pinnedVersion}`. "
                + "Check rollForward and installed SDKs under Program Files\\dotnet\\sdk.";
        }

        return $"- **WARNING:** dotnet could not confirm an SDK for global.json request `{pinnedVersion}`. "
            + "Check rollForward and installed SDKs under DOTNET_ROOT or on PATH.";
    }

    internal static string PinnedSdkNotFoundWarning(string pinnedVersion) =>
        PinnedSdkNotFoundWarning(pinnedVersion, OperatingSystem.IsWindows());

    private readonly record struct CommandDeadline(long Started, TimeSpan? Limit)
    {
        public TimeSpan? Remaining => DotNetTestArguments.RemainingTimeout(Limit, Stopwatch.GetElapsedTime(Started));

        public static CommandDeadline Start(TimeSpan? timeout) =>
            new(Stopwatch.GetTimestamp(), timeout > TimeSpan.Zero ? timeout : null);
    }

    private sealed record CommandRunResult(
        int ExitCode,
        BoundedProcessOutput.Capture StdOut,
        BoundedProcessOutput.Capture StdErr,
        TimeSpan? Elapsed = null,
        bool TimedOut = false,
        bool ProcessKilled = false,
        ExceptionDispatchInfo? Failure = null);

    private static readonly TimeSpan _outputDrainTimeout = TimeSpan.FromSeconds(3);
}
