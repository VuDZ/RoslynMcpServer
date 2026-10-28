using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

internal static class WindowsProcess
{
    public static object CurrentJob()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new { platform = "unix", session = GetSid(0), processGroup = GetPgid(0) };
        }

        if (!IsProcessInJob(new IntPtr(-1), IntPtr.Zero, out var inJob))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var limits = new byte[144];
        var queried = inJob && QueryInformationJobObject(IntPtr.Zero, 9, limits, limits.Length, out _);
        // JOBOBJECT_EXTENDED_LIMIT_INFORMATION starts with two LARGE_INTEGERs, then LimitFlags.
        return new { inJob, limitFlags = queried ? BitConverter.ToUInt32(limits, 16) : (uint?)null };
    }

    public static object StartDetached(string executable, string directory, bool breakaway)
    {
        var kind = breakaway ? "breakaway" : "detached";
        var childDirectory = Path.Combine(Path.GetFullPath(directory), ".");
        var command = new StringBuilder($"\"{executable}\" child \"{childDirectory}\" {kind}");
        var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
        const uint DetachedProcess = 0x00000008;
        const uint NewProcessGroup = 0x00000200;
        const uint BreakawayFromJob = 0x01000000;
        var flags = DetachedProcess | NewProcessGroup | (breakaway ? BreakawayFromJob : 0);
        if (!CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, false, flags,
                IntPtr.Zero, null, ref startup, out var process))
        {
            return new { kind, pid = (int?)null, error = Marshal.GetLastWin32Error() };
        }

        CloseHandle(process.Process);
        CloseHandle(process.Thread);
        return new { kind, pid = (int?)process.ProcessId, error = 0 };
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(
        IntPtr job, int informationClass, byte[] information, int length, out int returnedLength);

    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(
        string application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        bool inheritHandles, uint flags, IntPtr environment, string? directory,
        ref StartupInfo startup, out ProcessInformation process);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("libc", EntryPoint = "getsid")]
    private static extern int GetSid(int processId);

    [DllImport("libc", EntryPoint = "getpgid")]
    private static extern int GetPgid(int processId);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public IntPtr Reserved;
        public IntPtr Desktop;
        public IntPtr Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow, ReservedSize;
        public IntPtr ReservedBytes, StandardInput, StandardOutput, StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process, Thread;
        public int ProcessId, ThreadId;
    }
}
