using System.Diagnostics;

namespace PipeRelay.Checks;

internal static class ProcessIdentity
{
    public static long Stamp(int pid)
    {
        if (OperatingSystem.IsLinux())
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            return long.Parse(stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[19]);
        }

        using var process = Process.GetProcessById(pid);
        return process.StartTime.ToUniversalTime().Ticks;
    }
}
