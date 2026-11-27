using RoslynMcpServer.LifecycleTestHost;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

internal static class LifecycleEnvironment
{
    internal static readonly Lazy<(bool Available, string? Reason)> Probe = new(RunProbe);

    /// <summary>
    /// Returns <see langword="null"/> when the isolated MSBuild host answers the probe, or the
    /// skip text when it cannot start.
    /// </summary>
    internal static string? GetUnavailableReason()
    {
        try
        {
            var (available, reason) = Probe.Value;
            return available ? null : "Environment unavailable: " + reason;
        }
        catch (Exception ex) when (
            ex is FileNotFoundException
                or FileLoadException
                or BadImageFormatException
                or TypeLoadException
                or InvalidOperationException)
        {
            return "Environment unavailable: " + ex.GetType().Name + ": " + ex.Message;
        }
    }

    private static (bool Available, string? Reason) RunProbe()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var client = LifecycleHostClient.Start();
            try
            {
                var env = client.SendAsync(new HostCommand { Op = "env" }, timeout.Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
                if (env.Skip)
                {
                    return (false, env.Error ?? "MSBuild bootstrap failed");
                }

                if (!env.Ok)
                {
                    return (false, env.Error ?? "host env failed");
                }

                return (true, env.Environment?.Bootstrap);
            }
            finally
            {
                client.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            return (false, ex.GetType().Name + ": " + ex.Message);
        }
    }
}
