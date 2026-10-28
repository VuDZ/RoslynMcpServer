using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using PipeRelay.Checks;

namespace PipeRelay;

internal static class SpikeChecks
{
    public static async Task<int> Run(string selected)
    {
        Require(selected is "all" or "sessions", "Expected all or sessions.");
        var results = new List<object>();
        var failures = 0;
        foreach (var protocol in new[] { "2025-11-25", "2026-07-28" })
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var direct = await McpClient.CreateAsync(Transport("direct"),
                new() { ProtocolVersion = protocol }, cancellationToken: deadline.Token);
            results.Add(new { protocol, mode = "direct", benchmark = await CountBenchmark.Run(direct, deadline.Token) });
            foreach (var roles in new[] { true, false })
            {
                var model = roles ? "roles" : "separate-host";
                try
                {
                    await using var pair = await ClientPair.Open(protocol, roles);
                    var sessions = await SessionChecks.Run(pair, protocol);
                    var benchmark = await CountBenchmark.Run(pair.Second, pair.Token);
                    object? transport = null;
                    object? access = null;
                    if (selected == "all")
                    {
                        transport = await TransportChecks.Run(pair);
                        access = WindowsAccessCheck.Run(pair.PipeName);
                    }

                    var proxy = Read(await pair.Second.CallToolAsync("toggle", cancellationToken: pair.Token));
                    using var proxyProcess = Process.GetProcessById(proxy.GetProperty("processId").GetInt32());
                    proxyProcess.Refresh();
                    pair.Host.Refresh();
                    var memory = new
                    {
                        method = "System.Diagnostics.Process.PrivateMemorySize64/WorkingSet64; no forced GC",
                        proxyPid = proxyProcess.Id,
                        proxyPrivateBytes = proxyProcess.PrivateMemorySize64,
                        proxyWorkingSet = proxyProcess.WorkingSet64,
                        proxyServerGc = proxy.GetProperty("serverGc").GetBoolean(),
                        hostPid = pair.Host.Id,
                        hostPrivateBytes = pair.Host.PrivateMemorySize64,
                        hostWorkingSet = pair.Host.WorkingSet64,
                    };
                    var disconnect = await SessionChecks.Disconnect(pair);
                    results.Add(new { protocol, model, status = "pass", sessions, benchmark, memory, transport, access, disconnect });
                    Console.WriteLine($"PASS {protocol}/{model}");
                }
                catch (Exception exception)
                {
                    failures++;
                    results.Add(new { protocol, model, status = "fail", error = exception.ToString() });
                    Console.WriteLine($"FAIL {protocol}/{model}: {exception.Message}");
                }
            }
        }

        var output = Path.Combine(AppContext.BaseDirectory, "checks", "result.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
        {
            utc = DateTimeOffset.UtcNow,
            runtime = Environment.Version.ToString(),
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            sdk = typeof(ModelContextProtocol.Server.McpServer).Assembly.GetName().Version?.ToString(),
            buildIdentity = BuildIdentity.Value,
            selected,
            results,
            failures,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output);
        return failures == 0 ? 0 : 1;
    }

    public static StdioClientTransport Transport(params string[] args) =>
        TransportForAssembly(Assembly.GetExecutingAssembly().Location, args);

    public static StdioClientTransport TransportForAssembly(string assembly, params string[] args)
    {
        var start = StartInfoForAssembly(assembly, args);
        return new(new StdioClientTransportOptions
        {
            Command = start.FileName,
            Arguments = start.ArgumentList.ToArray(),
            Name = "PipeRelay check",
            ShutdownTimeout = TimeSpan.FromSeconds(2),
        });
    }

    public static ProcessStartInfo StartInfo(params string[] args) =>
        StartInfoForAssembly(Assembly.GetExecutingAssembly().Location, args);

    public static JsonElement Read(CallToolResult result)
    {
        var text = string.Join("", result.Content.OfType<TextContentBlock>().Select(c => c.Text));
        Require(result.IsError != true, "Unexpected tool error: " + text);
        return JsonSerializer.Deserialize<JsonElement>(text);
    }

    public static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static ProcessStartInfo StartInfoForAssembly(string assembly, string[] args)
    {
        var dotnet = Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet";
        var start = new ProcessStartInfo(dotnet ? Environment.ProcessPath! :
            Path.Combine(Path.GetDirectoryName(assembly)!, OperatingSystem.IsWindows() ? "PipeRelay.exe" : "PipeRelay"))
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (dotnet)
        {
            start.ArgumentList.Add(assembly);
        }

        foreach (var argument in args)
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }
}
