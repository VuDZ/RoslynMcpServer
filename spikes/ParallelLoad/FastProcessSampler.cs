using System.ComponentModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ParallelLoad.Models;

namespace ParallelLoad;

/// <summary>Native process inventory and CPU counters; WMI is used once per new dotnet child to identify BuildHost.</summary>
[SupportedOSPlatform("windows")]
internal sealed class FastProcessSampler
{
    public FastProcessSampler(int rootId)
    {
        _rootId = rootId;
        _known.Add(rootId);
    }

    public void Sample()
    {
        var timestamp = Stopwatch.GetTimestamp();
        if (_previousSample != 0)
        {
            _maxSampleGapMs = Math.Max(_maxSampleGapMs, Stopwatch.GetElapsedTime(_previousSample, timestamp).TotalMilliseconds);
        }

        _previousSample = timestamp;
        var entries = ReadProcessTree();
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var (id, parent, _) in entries)
            {
                if (id != Environment.ProcessId && _known.Contains(parent))
                {
                    changed |= _known.Add(id);
                }
            }
        }

        var hosts = 0;
        long workingSet = 0;
        foreach (var (id, _, name) in entries)
        {
            if (id == _rootId || !_known.Contains(id))
            {
                continue;
            }

            var handle = OpenProcess(0x410, false, (uint)id);
            if (handle == IntPtr.Zero)
            {
                continue;
            }

            try
            {
                if (!GetProcessTimes(handle, out var creation, out _, out var kernel, out var user))
                {
                    continue;
                }

                var identity = id + ":" + creation;
                var cpuMs = (kernel + user) / 10000d;
                _cpu[identity] = Math.Max(_cpu.GetValueOrDefault(identity), cpuMs);
                var observedMs = Stopwatch.GetElapsedTime(_intervalStart, timestamp).TotalMilliseconds;
                if (!_observations.TryGetValue(identity, out var observation))
                {
                    observation = new BuildHostObservation(observedMs, cpuMs);
                    _observations[identity] = observation;
                }
                else
                {
                    observation.Observe(observedMs, cpuMs);
                }
                if (!_hostFlags.ContainsKey(identity))
                {
                    StartClassification(id, name, identity);
                }

                if (_hostFlags.GetValueOrDefault(identity))
                {
                    _buildHosts.Add(identity);
                    hosts++;
                    var counters = new MemoryCounters { Size = (uint)Marshal.SizeOf<MemoryCounters>() };
                    if (GetProcessMemoryInfo(handle, ref counters, counters.Size))
                    {
                        workingSet += (long)counters.WorkingSetSize.ToUInt64();
                    }
                    else
                    {
                        _errors.Enqueue($"Cannot read working set of BuildHost {id}: {Marshal.GetLastWin32Error()}");
                    }
                }
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        _peakConcurrentHosts = Math.Max(_peakConcurrentHosts, hosts);
        _peakWorkingSet = Math.Max(_peakWorkingSet, workingSet);
        _samples++;
    }

    public void Prepare()
    {
        // Warm WMI before the measured interval; subsequent calls only classify newly observed dotnet hosts.
        using var searcher = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId={_rootId}");
        using var rows = searcher.Get();
        _ = rows.Count;
    }

    public ProcessMetrics Capture(double wallMs, double rootCpuMs) => new(
        _hostFlags.Values.Count(value => value),
        _peakConcurrentHosts,
        _peakWorkingSet,
        rootCpuMs,
        _cpu.Values.Sum(),
        (rootCpuMs + _cpu.Values.Sum()) / wallMs,
        _maxSampleGapMs,
        _samples,
        _errors.Distinct().ToArray())
    {
        BuildHosts = _observations.Where(pair => _hostFlags.GetValueOrDefault(pair.Key))
            .Select(pair => pair.Value.Capture(pair.Key, _cpu[pair.Key])).OrderBy(host => host.FirstObservedMs).ToArray(),
    };

    public Task CompleteAsync() => Task.WhenAll(_classificationTasks);

    public void ResetInterval()
    {
        _previousSample = Stopwatch.GetTimestamp();
        _intervalStart = _previousSample;
        _observations.Clear();
        _maxSampleGapMs = 0;
    }

    private void StartClassification(int id, string name, string identity)
    {
        if (name.Contains("BuildHost", StringComparison.OrdinalIgnoreCase))
        {
            _hostFlags[identity] = true;
            return;
        }

        _hostFlags[identity] = false;
        if (!name.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // WMI can block for more than one second while hosts are starting. Classification must not stop polling.
        _classificationTasks.Add(Task.Factory.StartNew(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId={id}");
                using var rows = searcher.Get();
                _hostFlags[identity] = rows.Cast<ManagementObject>().Any(row =>
                    (Convert.ToString(row["CommandLine"]) ?? "").Contains("BuildHost", StringComparison.OrdinalIgnoreCase));
            }
            catch (ManagementException ex)
            {
                _errors.Enqueue($"Cannot classify dotnet process {id}: {ex.Message}");
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));
    }

    private static List<(int Id, int Parent, string Name)> ReadProcessTree()
    {
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new IntPtr(-1))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            var result = new List<(int, int, string)>();
            if (Process32First(snapshot, ref entry))
            {
                do
                {
                    result.Add(((int)entry.ProcessId, (int)entry.ParentProcessId, entry.FileName));
                }
                while (Process32Next(snapshot, ref entry));
            }

            return result;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint id);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(IntPtr process, ref MemoryCounters counters, uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    // Field order and widths are the Windows PROCESSENTRY32W ABI.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public UIntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string FileName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryCounters
    {
        public uint Size;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize;
        public UIntPtr WorkingSetSize;
        public UIntPtr QuotaPeakPagedPoolUsage;
        public UIntPtr QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage;
        public UIntPtr QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage;
        public UIntPtr PeakPagefileUsage;
    }

    private readonly int _rootId;
    private readonly HashSet<int> _known = new();
    private readonly HashSet<string> _buildHosts = new();
    private readonly ConcurrentDictionary<string, bool> _hostFlags = new();
    private readonly Dictionary<string, double> _cpu = new();
    private readonly Dictionary<string, BuildHostObservation> _observations = new();
    private readonly ConcurrentQueue<string> _errors = new();
    private readonly List<Task> _classificationTasks = new();
    private long _previousSample;
    private long _intervalStart = Stopwatch.GetTimestamp();
    private double _maxSampleGapMs;
    private int _peakConcurrentHosts;
    private long _peakWorkingSet;
    private int _samples;
}
