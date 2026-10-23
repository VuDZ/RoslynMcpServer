using System.Diagnostics;

namespace BuildHostFork;

/// <summary>
/// Polls the dotnet processes that start during a load every 100 ms. MSBuildWorkspace stops its BuildHost
/// processes when OpenSolutionAsync returns, so they must be observed while it runs. CPU is the last observed
/// value, a lower bound; other dotnet processes started meanwhile on the machine would be counted too.
/// </summary>
internal sealed class BuildHostSampler : IDisposable
{
    public BuildHostSampler()
    {
        _since = DateTime.Now;
        _timer = new Timer(_ => Sample(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
    }

    public object Stop()
    {
        _timer.Dispose();
        Sample();
        lock (_gate)
        {
            return new
            {
                count = _processes.Count,
                cpuMs = _processes.Values.Sum(process => process.CpuMs),
                peakWorkingSetMb = _processes.Count == 0 ? 0 : _processes.Values.Max(process => process.PeakWorkingSetBytes) / 1048576.0,
            };
        }
    }

    public void Dispose() => _timer.Dispose();

    private void Sample()
    {
        foreach (var candidate in Process.GetProcessesByName("dotnet"))
        {
            using (candidate)
            {
                try
                {
                    if (candidate.Id == Environment.ProcessId || candidate.StartTime < _since)
                    {
                        continue;
                    }

                    var cpuMs = candidate.TotalProcessorTime.TotalMilliseconds;
                    var peak = candidate.PeakWorkingSet64;
                    lock (_gate)
                    {
                        var previous = _processes.GetValueOrDefault(candidate.Id);
                        _processes[candidate.Id] = (Math.Max(previous.CpuMs, cpuMs), Math.Max(previous.PeakWorkingSetBytes, peak));
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // The process exited between the listing and the read.
                }
            }
        }
    }

    private readonly DateTime _since;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private readonly Dictionary<int, (double CpuMs, long PeakWorkingSetBytes)> _processes = [];
}
