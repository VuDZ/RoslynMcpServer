using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using RoslynMcpServer.Diagnostics;

namespace RoslynMcpServer.Services.Build;

public static class DotNetCliRunner
{
    public const int DefaultTimeoutSeconds = 300;
    private const string SdkCheckTimeoutMetadata = "- **Command start:** skipped because the timeout elapsed during SDK resolution.";

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
    }

    public sealed record SeparatedRunResult(
        int ExitCode,
        string StdOut,
        string StdErr,
        string RunMetadata,
        bool TimedOut,
        string? ExceptionType);

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
        var workDir = string.IsNullOrWhiteSpace(workingDirectory)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(workingDirectory);

        if (progress is null)
        {
            return await RunSeparatedCoreAsync(arguments, workDir, timeout, cancellationToken).ConfigureAwait(false);
        }

        var heartbeat = StartHeartbeat(progress, cancellationToken);
        try
        {
            return await RunSeparatedCoreAsync(arguments, workDir, timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await StopHeartbeatAsync(heartbeat).ConfigureAwait(false);
        }
    }

    private static async Task<SeparatedRunResult> RunSeparatedCoreAsync(
        string arguments,
        string workDir,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var dotnet = DotNetHostResolver.ResolveDotNetExecutable();
        var (sdk, remaining) = await ResolveSdkWithCommandBudgetAsync(dotnet, workDir, timeout, cancellationToken)
            .ConfigureAwait(false);
        if (remaining == TimeSpan.Zero)
        {
            var metadata = BuildRunMetadata(dotnet, workDir, string.Empty, sdk);
            return new SeparatedRunResult(
                -1, string.Empty, string.Empty, metadata + Environment.NewLine + SdkCheckTimeoutMetadata,
                TimedOut: true, ExceptionType: null);
        }

        var psi = CreateProcessStartInfo(dotnet, arguments, workDir, sdk);

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
        {
            throw new InvalidOperationException(FailedToStartDotNetMessage(dotnet));
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            if (!await WaitForExitOrTimeoutAsync(process, remaining, cancellationToken).ConfigureAwait(false))
            {
                var stdout = (await stdoutTask.ConfigureAwait(false)).TrimEnd();
                var stderr = (await stderrTask.ConfigureAwait(false)).TrimEnd();
                var metadata = BuildRunMetadata(dotnet, workDir, string.Join('\n', stdout, stderr), sdk);

                return new SeparatedRunResult(process.ExitCode, stdout, stderr, metadata, false, null);
            }

            TryKillProcessTree(process);
            var (partialStdout, partialStderr) = await AwaitOutputAsync(stdoutTask, stderrTask, OutputDrainTimeout)
                .ConfigureAwait(false);
            var meta = BuildRunMetadata(dotnet, workDir, string.Join('\n', partialStdout, partialStderr), sdk);

            return new SeparatedRunResult(
                process.HasExited ? process.ExitCode : -1,
                partialStdout,
                partialStderr,
                meta,
                TimedOut: true,
                ExceptionType: null);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            throw;
        }
        catch (Exception ex)
        {
            TryKillProcessTree(process);
            var (partialStdout, partialStderr) = await AwaitOutputAsync(stdoutTask, stderrTask, OutputDrainTimeout)
                .ConfigureAwait(false);
            var meta = BuildRunMetadata(dotnet, workDir, string.Join('\n', partialStdout, partialStderr), sdk);
            return new SeparatedRunResult(
                process.HasExited ? process.ExitCode : -1,
                partialStdout,
                partialStderr,
                meta,
                TimedOut: false,
                ExceptionType: ex.GetType().Name);
        }
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
        var workDir = string.IsNullOrWhiteSpace(workingDirectory)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(workingDirectory);
        var dotnet = DotNetHostResolver.ResolveDotNetExecutable();
        var (sdk, remaining) = await ResolveSdkWithCommandBudgetAsync(dotnet, workDir, timeout, cancellationToken)
            .ConfigureAwait(false);
        if (remaining == TimeSpan.Zero)
        {
            var metadata = BuildRunMetadata(dotnet, workDir, string.Empty, sdk);
            return new RunResult(
                -1, string.Empty, metadata + Environment.NewLine + SdkCheckTimeoutMetadata, 0, 0, TimedOut: true);
        }

        return await RunWithSdkAsync(dotnet, arguments, workDir, sdk, remaining, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<(DotNetSdkEnvironment.SdkPinInfo Sdk, TimeSpan? Remaining)> ResolveSdkWithCommandBudgetAsync(
        string dotnet,
        string workDir,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var limit = timeout > TimeSpan.Zero ? timeout : null;
        var probeLimit = limit is { } finite && finite < DotNetSdkResolver.DefaultTimeout
            ? finite
            : DotNetSdkResolver.DefaultTimeout;
        var sdk = await DotNetSdkResolver.ResolveAsync(dotnet, workDir, probeLimit, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return (sdk, DotNetTestArguments.RemainingTimeout(limit, Stopwatch.GetElapsedTime(started)));
    }

    internal static async Task<RunResult> RunWithSdkAsync(
        string dotnet,
        string arguments,
        string workingDirectory,
        DotNetSdkEnvironment.SdkPinInfo sdk,
        TimeSpan? timeout,
        CliProgressWatch? progress,
        CancellationToken cancellationToken)
    {
        if (progress is null)
        {
            return await RunCoreAsync(dotnet, arguments, workingDirectory, sdk, timeout, cancellationToken)
                .ConfigureAwait(false);
        }

        var heartbeat = StartHeartbeat(progress, cancellationToken);
        try
        {
            return await RunCoreAsync(dotnet, arguments, workingDirectory, sdk, timeout, cancellationToken)
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
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var psi = CreateProcessStartInfo(dotnet, arguments, workDir, sdk);

        using var process = new Process { StartInfo = psi };
        var started = Stopwatch.GetTimestamp();
        if (!process.Start())
        {
            throw new InvalidOperationException(FailedToStartDotNetMessage(dotnet));
        }

        // Do not cancel pipe reads: redirected stdout does not observe a cancellation token.
        // Wait with WhenAny+Delay (WaitForExitAsync(token) is not reliable for nested `dotnet build`),
        // then Kill and drain with a bounded wait so the caller always gets a result.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            if (!await WaitForExitOrTimeoutAsync(process, timeout, cancellationToken).ConfigureAwait(false))
            {
                var stdout = (await stdoutTask.ConfigureAwait(false)).TrimEnd();
                var stderr = (await stderrTask.ConfigureAwait(false)).TrimEnd();
                var elapsed = Stopwatch.GetElapsedTime(started);
                var combinedText = CombineStreams(stdout, stderr);
                var metadata = BuildRunMetadata(dotnet, workDir, combinedText, sdk);

                return new RunResult(
                    process.ExitCode,
                    combinedText,
                    metadata,
                    stdout.Length,
                    stderr.Length) { Elapsed = elapsed };
            }

            TryKillProcessTree(process);
            var (partialStdout, partialStderr) = await AwaitOutputAsync(stdoutTask, stderrTask, OutputDrainTimeout)
                .ConfigureAwait(false);
            var combinedTimedOut = CombineStreams(partialStdout, partialStderr);
            var timedOutMetadata = BuildRunMetadata(dotnet, workDir, combinedTimedOut, sdk);
            return new RunResult(
                process.HasExited ? process.ExitCode : -1,
                combinedTimedOut,
                timedOutMetadata,
                partialStdout.Length,
                partialStderr.Length,
                TimedOut: true,
                ProcessKilled: true);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            throw;
        }
        catch
        {
            TryKillProcessTree(process);
            throw;
        }
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
            sb.AppendLine("**MCP_DOTNET_CANCELLED:** tool cancel requested; process tree was killed.");
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

    /// <summary>
    /// Returns <c>true</c> when the timeout elapsed before the process exited.
    /// Uses <see cref="Task.WhenAny"/> + <see cref="Task.Delay"/> because
    /// <see cref="Process.WaitForExitAsync(CancellationToken)"/> often ignores cancellation
    /// for nested <c>dotnet build</c> / MSBuild.
    /// </summary>
    private static async Task<bool> WaitForExitOrTimeoutAsync(
        Process process,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var exitTask = process.WaitForExitAsync(CancellationToken.None);
        if (timeout is not { } limit || limit <= TimeSpan.Zero)
        {
            await exitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = Task.Delay(limit, delayCts.Token);
        var completed = await Task.WhenAny(exitTask, delayTask).ConfigureAwait(false);
        if (completed == exitTask)
        {
            await delayCts.CancelAsync().ConfigureAwait(false);
            await exitTask.ConfigureAwait(false);
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return true;
    }

    internal static void TryKillProcessTree(Process process)
    {
        List<Process> descendants = [];
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
                    }
                    catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        // A descendant may already have exited with its parent.
                    }
                }
                process.Kill(entireProcessTree: true);
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

    private static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(3);

    private static async Task<(string StdOut, string StdErr)> AwaitOutputAsync(
        Task<string> stdoutTask,
        Task<string> stderrTask,
        TimeSpan? drainTimeout = null)
    {
        return (
            await AwaitOneAsync(stdoutTask, drainTimeout).ConfigureAwait(false),
            await AwaitOneAsync(stderrTask, drainTimeout).ConfigureAwait(false));
    }

    private static async Task<string> AwaitOneAsync(Task<string> readTask, TimeSpan? drainTimeout)
    {
        try
        {
            if (drainTimeout is { } limit && limit > TimeSpan.Zero)
            {
                var finished = await Task.WhenAny(readTask, Task.Delay(limit)).ConfigureAwait(false);
                if (finished != readTask)
                {
                    return string.Empty;
                }
            }

            return (await readTask.ConfigureAwait(false)).TrimEnd();
        }
        catch
        {
            return string.Empty;
        }
    }

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

}
