using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace RoslynMcpServer.Diagnostics;

/// <summary>
/// Parent image via <c>proc_pidpath</c>. When the image is node, <c>KERN_PROCARGS2</c> supplies the argument blob.
/// Package markers are specific enough that splitting argv from the environment is unnecessary.
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

    private static string? TryReadCommandLine(int processId)
    {
        var name = new[] { CtlKern, KernProcArgs2, processId };
        var buffer = new byte[CommandLineCapacity];
        var size = (nuint)buffer.Length;
        if (Sysctl(name, (uint)name.Length, buffer, ref size, IntPtr.Zero, 0) != 0 || size == 0)
        {
            return null;
        }

        var count = (int)Math.Min(size, (nuint)buffer.Length);
        return Encoding.UTF8.GetString(buffer, 0, count).Replace('\0', ' ').Trim();
    }

    [DllImport("libc", EntryPoint = "getppid")]
    private static extern int GetParentPid();

    [DllImport("libproc", EntryPoint = "proc_pidpath")]
    private static extern int ProcPidPath(int pid, byte[] buffer, uint bufferSize);

    [DllImport("libc", EntryPoint = "sysctl")]
    private static extern int Sysctl(
        int[] name,
        uint nameLength,
        byte[] oldValue,
        ref nuint oldLength,
        IntPtr newValue,
        nuint newLength);

    private const int CtlKern = 1;

    private const int KernProcArgs2 = 49;

    private const int ImagePathCapacity = 4096;

    private const int CommandLineCapacity = 64 * 1024;
}
