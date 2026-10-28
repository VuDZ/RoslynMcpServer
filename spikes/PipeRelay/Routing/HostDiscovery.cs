using System.Text.Json;
using PipeRelay.Routing.Models;

namespace PipeRelay.Routing;

internal static class HostDiscovery
{
    public static async Task<RouteConnection> Open(RouteOptions options, CancellationToken cancellationToken)
    {
        var files = new RouteFiles(options);
        var admitted = Environment.TickCount64;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        FileStream? launcher = null;
        System.Diagnostics.Process? spawned = null;
        try
        {
            while (true)
            {
                var connection = await TryConnect(options, files, deadline.Token);
                if (connection is not null)
                {
                    return connection;
                }

                if (spawned?.HasExited == true)
                {
                    using var owner = files.TryLock("host.lock");
                    if (owner is not null)
                    {
                        File.WriteAllText(files.PathFor("failed-at"), Environment.TickCount64.ToString());
                        File.WriteAllText(files.PathFor("next-launch"),
                            (Environment.TickCount64 + 2000).ToString());
                        throw new IOException("The candidate host exited before readiness.");
                    }
                }

                if (launcher is null)
                {
                    launcher = files.TryLock("launch.lock");
                }

                // A closing host can release its lease after this request acquired launch.lock.
                if (launcher is not null && spawned is null)
                {
                    // The child acquires host.lock before initialization; overlap with launch.lock closes the handoff gap.
                    using var host = files.TryLock("host.lock");
                    if (host is not null)
                    {
                        var failed = ReadTimestamp(files.PathFor("failed-at"));
                        // Future timestamps belong to a stale clock epoch, including a previous OS boot.
                        if (failed >= admitted && failed <= Environment.TickCount64)
                        {
                            throw new IOException("This request joined a failed startup attempt; it cannot launch another one.");
                        }

                        var budget = files.PathFor("next-launch");
                        var nextLaunch = ReadTimestamp(budget);
                        var now = Environment.TickCount64;
                        if (nextLaunch > now && nextLaunch <= now + 2000)
                        {
                            throw new IOException("Shared startup cooldown; make a new explicit attempt after two seconds.");
                        }

                        File.WriteAllText(budget, (Environment.TickCount64 + 2000).ToString());
                        var image = ShadowImage.Prepare(files);
                        var config = files.PathFor("options.json");
                        File.WriteAllText(config, JsonSerializer.Serialize(options));
                        files.Event("launch", Guid.Empty);
                        // Release the probe lease before spawning; launch.lock still excludes other launchers.
                        host.Dispose();
                        spawned = ShadowImage.Start(image, config);
                        files.Event("spawned", Guid.Empty, new
                        {
                            pid = spawned.Id, ticks = Checks.ProcessIdentity.Stamp(spawned.Id),
                        });
                        await RouteFiles.Barrier(options.LaunchBarrier, deadline.Token);
                    }
                }

                await Task.Delay(30, deadline.Token);
            }
        }
        finally
        {
            launcher?.Dispose();
            spawned?.Dispose();
        }
    }

    private static long ReadTimestamp(string path) => File.Exists(path)
        && long.TryParse(File.ReadAllText(path), out var value) ? value : 0;

    private static async Task<RouteConnection?> TryConnect(RouteOptions options, RouteFiles files,
        CancellationToken cancellationToken)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(TimeSpan.FromMilliseconds(250));
        try
        {
            return await RouteConnection.Connect(options, files, attempt.Token);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException
            or ModelContextProtocol.McpException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }
}
