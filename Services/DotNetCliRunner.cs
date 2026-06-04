using System.Diagnostics;
using System.Text;

namespace RoslynMcpServer.Services;

public static class DotNetCliRunner
{
    public sealed record RunResult(
        int ExitCode,
        string CombinedOutput,
        string RunMetadata,
        int StdOutLength,
        int StdErrLength);

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
        CancellationToken cancellationToken)
    {
        var result = await RunWithMetadataAsync(arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
        return (result.ExitCode, result.CombinedOutput);
    }

    public static async Task<SeparatedRunResult> RunSeparatedAsync(
        string arguments,
        string? workingDirectory,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var workDir = string.IsNullOrWhiteSpace(workingDirectory)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(workingDirectory);

        var dotnet = DotNetHostResolver.ResolveDotNetExecutable();
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

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
        {
            throw new InvalidOperationException(
                $"Failed to start `{dotnet}`. Ensure a 64-bit .NET SDK is installed under Program Files\\dotnet.");
        }

        CancellationTokenSource? timeoutCts = null;
        if (timeout.HasValue && timeout.Value > TimeSpan.Zero)
        {
            timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout.Value);
        }

        using (timeoutCts)
        {
            var token = timeoutCts?.Token ?? cancellationToken;
            var timedOut = false;
            string? exceptionType = null;

            try
            {
                var stdoutTask = process.StandardOutput.ReadToEndAsync(token);
                var stderrTask = process.StandardError.ReadToEndAsync(token);
                await process.WaitForExitAsync(token).ConfigureAwait(false);
                var stdout = (await stdoutTask.ConfigureAwait(false)).TrimEnd();
                var stderr = (await stderrTask.ConfigureAwait(false)).TrimEnd();
                var metadata = await CreateRunMetadataAsync(workDir, string.Join('\n', stdout, stderr), token)
                    .ConfigureAwait(false);

                return new SeparatedRunResult(process.ExitCode, stdout, stderr, metadata, false, null);
            }
            catch (OperationCanceledException) when (timeoutCts is not null && timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                timedOut = true;
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
            catch (Exception ex)
            {
                exceptionType = ex.GetType().Name;
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

            string partialStdout = string.Empty;
            string partialStderr = string.Empty;
            try
            {
                partialStdout = await process.StandardOutput.ReadToEndAsync(CancellationToken.None).ConfigureAwait(false);
                partialStderr = await process.StandardError.ReadToEndAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }

            partialStdout = partialStdout.TrimEnd();
            partialStderr = partialStderr.TrimEnd();
            var meta = await CreateRunMetadataAsync(workDir, string.Join('\n', partialStdout, partialStderr), CancellationToken.None)
                .ConfigureAwait(false);

            return new SeparatedRunResult(
                process.HasExited ? process.ExitCode : -1,
                partialStdout,
                partialStderr,
                meta,
                timedOut,
                exceptionType);
        }
    }

    public static async Task<RunResult> RunWithMetadataAsync(
        string arguments,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        var workDir = string.IsNullOrWhiteSpace(workingDirectory)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(workingDirectory);

        var dotnet = DotNetHostResolver.ResolveDotNetExecutable();
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

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
        {
            throw new InvalidOperationException(
                $"Failed to start `{dotnet}`. Ensure a 64-bit .NET SDK is installed under Program Files\\dotnet.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(cancellationToken)).ConfigureAwait(false);

        var stdout = (await stdoutTask.ConfigureAwait(false)).TrimEnd();
        var stderr = (await stderrTask.ConfigureAwait(false)).TrimEnd();

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

        var combinedText = combined.ToString();
        var metadata = await CreateRunMetadataAsync(workDir, combinedText, cancellationToken).ConfigureAwait(false);

        return new RunResult(
            process.ExitCode,
            combinedText,
            metadata,
            stdout.Length,
            stderr.Length);
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

    private static string BuildRunMetadata(
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

        var logMsbuild = Diagnostics.MsBuildLogHighlighter.TryGetMsBuildExecutablePath(combinedOutput);
        if (!string.IsNullOrEmpty(logMsbuild))
        {
            metadata.AppendLine($"- **MSBuild from log:** `{logMsbuild}`");
        }

        metadata.AppendLine($"- **WorkingDirectory:** `{workDir}`");
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

            var output = (await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false)).Trim();
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return string.IsNullOrEmpty(output) ? null : output;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
