using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace RoslynMcpServer.Services.Build;

internal static class DotNetSdkResolver
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    public static async Task<DotNetSdkEnvironment.SdkPinInfo> ResolveAsync(
        string dotnet,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var globalJson = GlobalJsonSdkReader.FindGlobalJsonPath(workingDirectory);
        var requestedVersion = GlobalJsonSdkReader.TryReadRequestedVersion(globalJson);
        var startInfo = new ProcessStartInfo
        {
            FileName = dotnet,
            Arguments = "--version",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        DotNetSdkEnvironment.ClearMsBuildSdkOverrideVariables(startInfo);
        DotNetCliRunner.EnsureWindowsProcessorArchitecture(startInfo, System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        // The version alone cannot identify an SDK under a custom global.json paths root.
        // --info can fail in restricted Windows hosts while querying workload installers.
        // The native SDK-selection trace stays in this child and is never included in tool output.
        startInfo.Environment["DOTNET_HOST_TRACE"] = "1";
        startInfo.Environment["DOTNET_HOST_TRACE_VERBOSITY"] = "4";
        startInfo.Environment.Remove("DOTNET_HOST_TRACEFILE");
        startInfo.Environment["COREHOST_TRACE"] = "1";
        startInfo.Environment["COREHOST_TRACE_VERBOSITY"] = "4";
        startInfo.Environment.Remove("COREHOST_TRACEFILE");

        var remaining = timeout > TimeSpan.Zero ? timeout - Stopwatch.GetElapsedTime(started) : TimeSpan.Zero;
        var (version, trace) = await ProbeAsync(startInfo, remaining, cancellationToken).ConfigureAwait(false);
        return FromHostResult(globalJson, requestedVersion, version, trace);
    }

    internal static DotNetSdkEnvironment.SdkPinInfo FromHostResult(
        string? globalJson,
        string? requestedVersion,
        string? version,
        string? trace)
    {
        var sdkDirectory = FindResolvedDirectory(version, trace);
        var sdkRoot = sdkDirectory is null ? null : Directory.GetParent(sdkDirectory)?.Parent?.FullName;
        return new DotNetSdkEnvironment.SdkPinInfo(
            requestedVersion,
            sdkDirectory,
            sdkDirectory is null ? null : Path.Combine(sdkDirectory, "MSBuild.dll"),
            sdkDirectory is null ? null : Path.Combine(sdkDirectory, "Sdks"),
            sdkRoot)
        {
            GlobalJsonPath = globalJson,
            ResolvedVersion = version
        };
    }

    private static string? FindResolvedDirectory(string? version, string? trace)
    {
        if (version is null || trace is null)
        {
            return null;
        }

        const string prefix = "SDK path resolved to [";
        foreach (var rawLine in trace.AsSpan().EnumerateLines())
        {
            var line = rawLine.Trim();
            if (!line.StartsWith(prefix, StringComparison.Ordinal) || !line.EndsWith("]", StringComparison.Ordinal))
            {
                continue;
            }

            var directory = Path.TrimEndingDirectorySeparator(line[prefix.Length..^1].ToString());
            if (Path.IsPathFullyQualified(directory)
                && string.Equals(Path.GetFileName(directory), version, StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(directory, "MSBuild.dll"))
                && File.Exists(Path.Combine(directory, "Microsoft.Build.dll")))
            {
                return directory;
            }
        }

        return null;
    }

    private static async Task<(string? Version, string? Trace)> ProbeAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (timeout <= TimeSpan.Zero)
        {
            return default;
        }

        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(timeout);
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return default;
            }

            try
            {
                var stdout = process.StandardOutput.ReadToEndAsync(budget.Token);
                var stderr = process.StandardError.ReadToEndAsync(budget.Token);
                await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(budget.Token))
                    .WaitAsync(budget.Token)
                    .ConfigureAwait(false);

                var version = (await stdout.ConfigureAwait(false)).Trim();
                return process.ExitCode == 0 && version.Length > 0 && !version.Contains('\n', StringComparison.Ordinal)
                    ? (version, await stderr.ConfigureAwait(false))
                    : default;
            }
            catch (OperationCanceledException)
            {
                DotNetCliRunner.TryKillProcessTree(process);
                cancellationToken.ThrowIfCancellationRequested();
                return default;
            }
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            return default;
        }
    }
}
