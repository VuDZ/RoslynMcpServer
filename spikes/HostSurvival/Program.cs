using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        if (args.Length < 2 || args[0] is not ("serve" or "child"))
        {
            throw new ArgumentException("Usage: HostSurvival serve|child <log-directory> [child-kind]");
        }

        Directory.CreateDirectory(args[1]);
        _logPath = Path.Combine(Path.GetFullPath(args[1]), $"{Environment.ProcessId}.jsonl");
        Write("start", new { mode = args[0], kind = args.ElementAtOrDefault(2), job = WindowsProcess.CurrentJob() });
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Write("process-exit");
        AppDomain.CurrentDomain.UnhandledException += (_, error) =>
            Write("unhandled-exception", new { type = error.ExceptionObject.GetType().Name });
        Console.CancelKeyPress += (_, _) => Write("console-cancel");
        using var termination = OperatingSystem.IsWindows() ? null
            : PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => Write("sigterm"));

        if (args[0] == "child")
        {
            for (var second = 0; second < 600; second++)
            {
                if (File.Exists(Path.Combine(args[1], "stop")))
                {
                    return;
                }
                Write("heartbeat");
                await Task.Delay(1000);
            }
            return;
        }

        StartChildren(args[1]);
        // Bound all probe processes even if an experiment is interrupted.
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        try
        {
            while (await Console.In.ReadLineAsync(lifetime.Token) is { } line)
            {
                using var document = JsonDocument.Parse(line);
                var request = document.RootElement;
                var method = request.GetProperty("method").GetString();
                Write("request", new { method });
                if (method == "resources/list" && args.ElementAtOrDefault(2) == "hold")
                {
                    Write("discovery-hold");
                    await Console.In.ReadToEndAsync(lifetime.Token);
                    Write("stdin-eof");
                    return;
                }
                if (!request.TryGetProperty("id", out var id))
                {
                    continue;
                }

                object? result = method switch
                {
                    "initialize" => new
                    {
                        protocolVersion = request.GetProperty("params").GetProperty("protocolVersion").GetString(),
                        capabilities = new { tools = new { }, resources = new { }, prompts = new { } },
                        serverInfo = new { name = "host-survival-probe", version = "1" }
                    },
                    "tools/list" => new { tools = Array.Empty<object>() },
                    "resources/list" => new { resources = Array.Empty<object>() },
                    "resources/templates/list" => new { resourceTemplates = Array.Empty<object>() },
                    "prompts/list" => new { prompts = Array.Empty<object>() },
                    "ping" => new { },
                    _ => null
                };
                object response = result is null
                    ? new { jsonrpc = "2.0", id, error = new { code = -32601, message = "Method not found" } }
                    : new { jsonrpc = "2.0", id, result };
                Console.WriteLine(JsonSerializer.Serialize(response));
            }
            Write("stdin-eof");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            Write("lifetime-expired");
        }
    }

    private static void StartChildren(string directory)
    {
        var executable = Environment.ProcessPath!;
        var ordinary = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        ordinary.ArgumentList.Add("child");
        ordinary.ArgumentList.Add(directory);
        ordinary.ArgumentList.Add("ordinary");
        using var child = Process.Start(ordinary)!;
        Write("child-start", new { kind = "ordinary", pid = child.Id });

        if (OperatingSystem.IsWindows())
        {
            Write("child-start", WindowsProcess.StartDetached(executable, directory, breakaway: false));
            Write("child-start", WindowsProcess.StartDetached(executable, directory, breakaway: true));
        }
        else
        {
            var detached = new ProcessStartInfo("setsid")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            detached.ArgumentList.Add(executable);
            detached.ArgumentList.Add("child");
            detached.ArgumentList.Add(directory);
            detached.ArgumentList.Add("setsid");
            using var session = Process.Start(detached)!;
            Write("child-start", new { kind = "setsid", pid = session.Id });
        }
    }

    private static void Write(string name, object? data = null)
    {
        lock (_writeLock)
        {
            File.AppendAllText(_logPath, JsonSerializer.Serialize(new
            {
                utc = DateTimeOffset.UtcNow,
                pid = Environment.ProcessId,
                name,
                data
            }) + Environment.NewLine);
        }
    }

    private static string _logPath = "";
    private static readonly object _writeLock = new();
}
