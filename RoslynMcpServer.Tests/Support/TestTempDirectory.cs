using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RoslynMcpServer.Tests.Support;

internal static class TestTempDirectory
{
    internal static void UsePhysicalMacOsPath()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        // /var aliases /private/var on macOS; cwd and MSBuild otherwise give fixtures two identities.
        // https://github.com/dotnet/runtime/issues/27771
        var resolved = RealPath(Path.GetTempPath(), IntPtr.Zero);
        if (resolved == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not resolve the physical test temp directory.");
        }

        try
        {
            Environment.SetEnvironmentVariable("TMPDIR", Marshal.PtrToStringUTF8(resolved));
        }
        finally
        {
            Free(resolved);
        }
    }

    [DllImport("libc", EntryPoint = "realpath", SetLastError = true)]
    private static extern IntPtr RealPath(string path, IntPtr resolved);

    [DllImport("libc", EntryPoint = "free")]
    private static extern void Free(IntPtr pointer);
}
