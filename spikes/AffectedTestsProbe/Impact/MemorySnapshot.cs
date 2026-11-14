using System.Diagnostics;

namespace AffectedTestsProbe;

/// <summary>
/// Process memory in megabytes. <see cref="ManagedMb"/> is measured after a full compacting collection,
/// so it is what the workspace still holds, not garbage that a collection has not reached yet.
/// </summary>
internal readonly record struct MemorySnapshot(long ManagedMb, long WorkingSetMb, long PeakWorkingSetMb)
{
    public static MemorySnapshot Capture()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        using var process = Process.GetCurrentProcess();
        return new MemorySnapshot(
            GC.GetTotalMemory(forceFullCollection: false) / Megabyte,
            process.WorkingSet64 / Megabyte,
            process.PeakWorkingSet64 / Megabyte);
    }

    private const long Megabyte = 1024 * 1024;
}
