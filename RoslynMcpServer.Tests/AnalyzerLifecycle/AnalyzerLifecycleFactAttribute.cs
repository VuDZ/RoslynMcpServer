using RoslynMcpServer.LifecycleTestHost;
using Xunit;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

/// <summary>
/// Dynamically skips Analyzer Lifecycle tests when the isolated MSBuild host cannot start.
/// Unavailable environment must not be reported as passed.
/// </summary>
internal sealed class AnalyzerLifecycleFactAttribute : FactAttribute
{
    public AnalyzerLifecycleFactAttribute()
    {
        var (available, reason) = LifecycleEnvironment.Probe.Value;
        if (!available)
        {
            Skip = "Environment unavailable: " + reason;
        }
    }
}

internal static class LifecycleEnvironment
{
    internal static readonly Lazy<(bool Available, string? Reason)> Probe = new(RunProbe);

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
