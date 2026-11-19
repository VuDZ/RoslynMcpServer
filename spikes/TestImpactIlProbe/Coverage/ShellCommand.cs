using System.Diagnostics;

namespace TestImpactIlProbe;

/// <summary>Runs a command line through the shell of the platform and keeps its output in a file.</summary>
internal static class ShellCommand
{
    /// <returns>The exit code, or null when the command did not end in time and was stopped.</returns>
    public static async Task<int?> RunAsync(
        string commandLine,
        string workingDirectory,
        string logPath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (OperatingSystem.IsWindows())
        {
            // With /s cmd removes the first and the last quote and takes the rest as written.
            start.FileName = "cmd.exe";
            start.Arguments = "/d /s /c \"" + commandLine + "\"";
        }
        else
        {
            start.FileName = "/bin/sh";
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(commandLine);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start " + start.FileName);
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        int? exitCode;
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            exitCode = process.ExitCode;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A test host outlives the command that started it, so the whole tree goes.
            process.Kill(entireProcessTree: true);
            exitCode = null;
        }

        var output = commandLine + Environment.NewLine + await ReadAsync(stdout).ConfigureAwait(false) + await ReadAsync(stderr).ConfigureAwait(false);
        await File.WriteAllTextAsync(logPath, output, cancellationToken).ConfigureAwait(false);
        return exitCode;
    }

    private static async Task<string> ReadAsync(Task<string> stream)
    {
        try
        {
            // A process that escaped the stopped tree can keep the pipe open; its output is not worth waiting for.
            return await stream.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return string.Empty;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            return string.Empty;
        }
    }
}
