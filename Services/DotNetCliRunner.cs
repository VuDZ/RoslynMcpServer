using System.Diagnostics;
using System.Text;

namespace RoslynMcpServer.Services;

public static class DotNetCliRunner
{
    public const int DefaultTimeoutSeconds = 300;

    public sealed record RunResult(
        int ExitCode,
        string CombinedOutput,
        string RunMetadata,
        int StdOutLength,
        int StdErrLength,
        bool TimedOut = false,
        bool Cancelled = false,
        bool ProcessKilled = false);

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
        var psi = CreateProcessStartInfo(dotnet, arguments, workDir);

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
        {
            throw new InvalidOperationException(
                $"Failed to start `{dotnet}`. Ensure a 64-bit .NET SDK is installed under Program Files\\dotnet.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            if (!await WaitForExitOrTimeoutAsync(process, timeout, cancellationToken).ConfigureAwait(false))
            {
                var stdout = (await stdoutTask.ConfigureAwait(false)).TrimEnd();
                var stderr = (await stderrTask.ConfigureAwait(false)).TrimEnd();
                var metadata = await CreateRunMetadataAsync(workDir, string.Join('\n', stdout, stderr), CancellationToken.None)
                    .ConfigureAwait(false);

                return new SeparatedRunResult(process.ExitCode, stdout, stderr, metadata, false, null);
            }

            TryKillProcessTree(process);
            var (partialStdout, partialStderr) = await AwaitOutputAsync(stdoutTask, stderrTask, OutputDrainTimeout)
                .ConfigureAwait(false);
            var meta = await CreateRunMetadataAsync(workDir, string.Join('\n', partialStdout, partialStderr), CancellationToken.None)
                .ConfigureAwait(false);

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
            var meta = await CreateRunMetadataAsync(workDir, string.Join('\n', partialStdout, partialStderr), CancellationToken.None)
                .ConfigureAwait(false);
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
        if (progress is null)
        {
            return await RunCoreAsync(arguments, workingDirectory, cancellationToken, timeout).ConfigureAwait(false);
        }

        var heartbeat = StartHeartbeat(progress, cancellationToken);
        try
        {
            return await RunCoreAsync(arguments, workingDirectory, cancellationToken, timeout).ConfigureAwait(false);
        }
        finally
        {
            await StopHeartbeatAsync(heartbeat).ConfigureAwait(false);
        }
    }

    private static async Task<RunResult> RunCoreAsync(
        string arguments,
        string? workingDirectory,
        CancellationToken cancellationToken,
        TimeSpan? timeout)
    {
        var workDir = string.IsNullOrWhiteSpace(workingDirectory)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(workingDirectory);

        var dotnet = DotNetHostResolver.ResolveDotNetExecutable();
        var psi = CreateProcessStartInfo(dotnet, arguments, workDir);

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
        {
            throw new InvalidOperationException(
                $"Failed to start `{dotnet}`. Ensure a 64-bit .NET SDK is installed under Program Files\\dotnet.");
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
                var combinedText = CombineStreams(stdout, stderr);
                var metadata = await CreateRunMetadataAsync(workDir, combinedText, CancellationToken.None)
                    .ConfigureAwait(false);

                return new RunResult(
                    process.ExitCode,
                    combinedText,
                    metadata,
                    stdout.Length,
                    stderr.Length);
            }

            TryKillProcessTree(process);
            var (partialStdout, partialStderr) = await AwaitOutputAsync(stdoutTask, stderrTask, OutputDrainTimeout)
                .ConfigureAwait(false);
            var combinedTimedOut = CombineStreams(partialStdout, partialStderr);
            var timedOutMetadata = await CreateRunMetadataAsync(workDir, combinedTimedOut, CancellationToken.None)
                .ConfigureAwait(false);
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
        var sdkVersion = await TryGetDotNetSdkVersionAsync(dotnet, workingDirectory, cancellationToken)
            .ConfigureAwait(false);
        return BuildRunMetadata(dotnet, workingDirectory, combinedOutput, sdkVersion);
    }

    public static string FormatHangHints(bool timedOut, bool cancelled)
    {
        var sb = new StringBuilder();
        if (timedOut)
        {
            sb.AppendLine("**MCP_DOTNET_TIMEOUT:** process exceeded the tool timeout and was killed (`Kill(entireProcessTree)`).");
        }

        if (cancelled)
        {
            sb.AppendLine("**MCP_DOTNET_CANCELLED:** tool cancel requested; process tree was killed.");
        }

        sb.AppendLine(
            "If the next `dotnet` call is slow: check zombie processes (`Get-Process dotnet`), delete locked `obj`/`bin` if needed, " +
            "avoid parallel MCP `run_dotnet_test`/`run_dotnet_build`, and compare with shell `dotnet` from the same WorkingDirectory.");
        return sb.ToString().TrimEnd();
    }

    internal static ProcessStartInfo CreateProcessStartInfo(string dotnet, string arguments, string workDir)
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

        DotNetSdkEnvironment.ApplyPinnedSdk(psi, workDir);
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        // Parsers are English-only; a ru-RU machine otherwise prints "Пройдено!" instead of "Passed!".
        psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
        return psi;
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

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // best effort
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
        string? sdkVersion)
    {
        var globalJson = GlobalJsonSdkReader.FindGlobalJsonPath(workDir);
        var pin = DotNetSdkEnvironment.TryGetPin(workDir);
        var metadata = new StringBuilder();
        metadata.AppendLine($"- **dotnet host:** `{dotnetPath}` ({(Environment.Is64BitProcess ? "64-bit" : "32-bit")} MCP process)");
        if (!string.IsNullOrEmpty(sdkVersion))
        {
            metadata.AppendLine($"- **dotnet --version:** `{sdkVersion}` (from working directory)");
        }

        if (globalJson is not null)
        {
            metadata.AppendLine($"- **global.json:** `{globalJson}`");
        }

        if (!string.IsNullOrEmpty(pin?.PinnedVersion))
        {
            metadata.AppendLine($"- **Pinned SDK:** `{pin.PinnedVersion}`");
        }

        if (!string.IsNullOrEmpty(pin?.SdkDirectory))
        {
            metadata.AppendLine($"- **Resolved SDK directory:** `{pin.SdkDirectory}`");
            metadata.AppendLine($"- **Expected MSBuild:** `{pin.MsBuildDllPath}`");
        }
        else if (!string.IsNullOrEmpty(pin?.PinnedVersion))
        {
            metadata.AppendLine(
                $"- **WARNING:** Pinned SDK `{pin.PinnedVersion}` was not found under Program Files\\dotnet\\sdk. Install it or fix rollForward.");
        }

        if (!string.IsNullOrEmpty(pin?.SdksDirectory))
        {
            metadata.AppendLine($"- **MSBuildSDKsPath / SDKS_DIR:** `{pin.SdksDirectory}`");
        }

        DotNetSdkEnvironment.AppendSdkEnvMetadata(metadata, workDir, combinedOutput);

        var logMsbuild = Diagnostics.MsBuildLogHighlighter.TryGetMsBuildExecutablePath(combinedOutput);
        if (!string.IsNullOrEmpty(logMsbuild))
        {
            metadata.AppendLine($"- **MSBuild from log:** `{logMsbuild}`");
        }

        metadata.AppendLine($"- **WorkingDirectory:** `{workDir}`");
        metadata.AppendLine("- **DOTNET_CLI_UI_LANGUAGE:** `en-US`");
        return metadata.ToString().TrimEnd();
    }

    private static async Task<string?> TryGetDotNetSdkVersionAsync(
        string dotnetPath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = dotnetPath,
                Arguments = "--version",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            DotNetSdkEnvironment.ApplyPinnedSdk(psi, workingDirectory);

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                var output = (await process.StandardOutput.ReadToEndAsync(cts.Token).ConfigureAwait(false)).Trim();
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                return string.IsNullOrEmpty(output) ? null : output;
            }
            catch (OperationCanceledException)
            {
                TryKillProcessTree(process);
                return null;
            }
        }
        catch (Exception)
        {
            return null;
        }
    }
}
