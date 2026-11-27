using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace RoslynMcpServer.Diagnostics;

/// <summary>
/// Process image via <c>proc_pidpath</c>, parent id via <c>proc_pidinfo</c>, and argv via <c>KERN_PROCARGS2</c>.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacOsParentProcess
{
    internal static ParentProcessSnapshot Capture()
    {
        var parentId = GetParentPid();
        if (parentId <= 0)
        {
            return ParentProcessSnapshot.Empty;
        }

        var imagePath = TryGetImagePath(parentId);
        string? commandLine = null;
        if (HarnessNameResolver.IsNodeImage(imagePath))
        {
            commandLine = TryReadCommandLine(parentId);
        }

        return new ParentProcessSnapshot(imagePath, commandLine);
    }

    internal static int TryGetParentProcessId(int processId)
    {
        if (processId <= 0)
        {
            return 0;
        }

        var size = Marshal.SizeOf<BsdShortInfo>();
        return ProcPidInfo(processId, ProcPidShortBsdInfo, 0, out var info, size) == size
            && info.ProcessId == processId
            ? info.ParentId
            : 0;
    }

    internal static string? TryReadCommandLine(int processId)
    {
        if (processId <= 0)
        {
            return null;
        }

        var name = new[] { CtlKern, KernProcArgs2, processId };
        nuint size = 0;
        if (Sysctl(name, (uint)name.Length, null, ref size, IntPtr.Zero, 0) != 0 || size == 0)
        {
            return null;
        }

        var buffer = new byte[checked((int)size)];
        if (Sysctl(name, (uint)name.Length, buffer, ref size, IntPtr.Zero, 0) != 0 || size > (nuint)buffer.Length)
        {
            return null;
        }

        return MacOsProcessArguments.Parse(buffer.AsSpan(0, (int)size));
    }

    private static string? TryGetImagePath(int processId)
    {
        var buffer = new byte[ImagePathCapacity];
        var length = ProcPidPath(processId, buffer, (uint)buffer.Length);
        if (length <= 0)
        {
            return null;
        }

        var count = Math.Min(length, buffer.Length);
        return Encoding.UTF8.GetString(buffer, 0, count).TrimEnd('\0');
    }

    [DllImport("libc", EntryPoint = "getppid")]
    private static extern int GetParentPid();

    [DllImport("libproc", EntryPoint = "proc_pidpath")]
    private static extern int ProcPidPath(int pid, byte[] buffer, uint bufferSize);

    [DllImport("libproc", EntryPoint = "proc_pidinfo")]
    private static extern int ProcPidInfo(int pid, int flavor, ulong argument, out BsdShortInfo info, int bufferSize);

    [DllImport("libc", EntryPoint = "sysctl")]
    private static extern int Sysctl(
        int[] name,
        uint nameLength,
        byte[]? oldValue,
        ref nuint oldLength,
        IntPtr newValue,
        nuint newLength);

    private const int CtlKern = 1;

    private const int KernProcArgs2 = 49;

    private const int ImagePathCapacity = 4096;

    private const int ProcPidShortBsdInfo = 13;

    // Only pid and ppid are needed from Apple's fixed 64-byte proc_bsdshortinfo ABI.
    // https://github.com/apple-oss-distributions/xnu/blob/main/bsd/sys/proc_info.h
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct BsdShortInfo
    {
        [FieldOffset(0)]
        public int ProcessId;

        [FieldOffset(4)]
        public int ParentId;
    }
}
