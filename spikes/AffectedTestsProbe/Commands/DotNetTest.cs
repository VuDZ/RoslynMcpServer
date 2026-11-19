using System.Diagnostics;

namespace AffectedTestsProbe;

internal static class DotNetTest
{
    public static async Task<(int ExitCode, bool TimedOut, string Output)> RunAsync(
        string project,
        string configuration,
        string? framework,
        string diag,
        string trx,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("test");
        start.ArgumentList.Add(project);
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(configuration);
        if (!string.IsNullOrEmpty(framework) && !string.Equals(framework, "unspecified", StringComparison.OrdinalIgnoreCase))
        {
            // One log name for every target framework overwrites the previous framework's results.
            start.ArgumentList.Add("-f");
            start.ArgumentList.Add(framework);
        }

        start.ArgumentList.Add("--diag");
        start.ArgumentList.Add(diag);
        start.ArgumentList.Add("--logger");
        start.ArgumentList.Add("trx;LogFileName=" + trx);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            var drained = await DrainAsync(stdout, stderr).ConfigureAwait(false);
            return (-1, true, drained);
        }

        var output = await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false);
        return (process.ExitCode, false, output);
    }

    private static async Task<string> DrainAsync(Task<string> stdout, Task<string> stderr)
    {
        try
        {
            return await stdout.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false)
                + await stderr.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The tree was killed because the run exceeded its budget. Restore must not wait on it.
            return "";
        }
    }
}
