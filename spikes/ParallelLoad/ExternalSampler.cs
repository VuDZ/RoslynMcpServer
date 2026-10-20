using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using ParallelLoad.Models;

namespace ParallelLoad;

/// <summary>Keep sampling outside the loader process so its GC pauses cannot stop the sampling clock.</summary>
[SupportedOSPlatform("windows")]
internal sealed class ExternalSampler : IDisposable
{
    private ExternalSampler(Process process, string directory)
    {
        _process = process;
        _directory = directory;
    }

    public static async Task<ExternalSampler> StartAsync(string directory)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add(typeof(ExternalSampler).Assembly.Location);
        }

        start.ArgumentList.Add("sample-process");
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        start.ArgumentList.Add(directory);
        var sampler = new ExternalSampler(Process.Start(start)!, directory);
        var watch = Stopwatch.StartNew();
        while (!File.Exists(Path.Combine(directory, "sampler.ready")))
        {
            if (sampler._process.HasExited || watch.Elapsed > TimeSpan.FromSeconds(30))
            {
                sampler.Dispose();
                throw new InvalidOperationException("External sampler did not become ready.");
            }

            await Task.Delay(25);
        }

        return sampler;
    }

    public static async Task<int> RunAsync(int rootId, string directory)
    {
        var sampler = new FastProcessSampler(rootId);
        sampler.Prepare();
        sampler.Sample();
        sampler.ResetInterval();
        File.WriteAllText(Path.Combine(directory, "sampler.ready"), "ready");
        var stopPath = Path.Combine(directory, "sampler.stop");
        while (!File.Exists(stopPath))
        {
            sampler.Sample();
            await Task.Delay(100);
        }

        sampler.Sample();
        await sampler.CompleteAsync();
        var stop = JsonSerializer.Deserialize<double[]>(File.ReadAllText(stopPath))!;
        File.WriteAllText(Path.Combine(directory, "sampler.json"), JsonSerializer.Serialize(sampler.Capture(stop[0], stop[1])));
        return 0;
    }

    public async Task<ProcessMetrics> StopAsync(double wallMs, double rootCpuMs)
    {
        var stagingPath = Path.Combine(_directory, "sampler.stop.pending");
        File.WriteAllText(stagingPath, JsonSerializer.Serialize(new[] { wallMs, rootCpuMs }));
        File.Move(stagingPath, Path.Combine(_directory, "sampler.stop"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await _process.WaitForExitAsync(timeout.Token);
        if (_process.ExitCode != 0)
        {
            throw new InvalidOperationException($"External sampler failed with exit code {_process.ExitCode}.");
        }

        return JsonSerializer.Deserialize<ProcessMetrics>(File.ReadAllText(Path.Combine(_directory, "sampler.json")))!;
    }

    public void Dispose()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit();
        }

        _process.Dispose();
    }

    private readonly Process _process;
    private readonly string _directory;
}
