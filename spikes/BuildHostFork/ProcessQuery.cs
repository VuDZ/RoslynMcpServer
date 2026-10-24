using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BuildHostFork;

/// <summary>
/// Reads the command line and the parent of another process: <c>/proc</c> on Linux, <c>NtQueryInformationProcess</c>
/// on Windows. The netcore BuildHost runs as <c>dotnet &lt;path&gt;/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll</c>,
/// so the command line is the only way to tell it from other dotnet processes and to see which folder it came from;
/// the parent tells this run's BuildHost from one another program started at the same time.
/// </summary>
internal static class ProcessQuery
{
    /// <returns>The command line, or null when it cannot be read (the process exited, access denied, other OS).</returns>
    public static string? TryReadCommandLine(Process process)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var arguments = File.ReadAllText($"/proc/{process.Id}/cmdline").TrimEnd('\0').Split('\0');
                return string.Join(' ', arguments.Select(argument => argument.Contains(' ') ? $"\"{argument}\"" : argument));
            }

            return OperatingSystem.IsWindows() ? ReadWindowsCommandLine(process) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <returns>
    /// The id of the process that started this one, or null when it cannot be read. The parent may have exited and
    /// its id may belong to a newer process since; the caller compares start times.
    /// </returns>
    public static int? TryReadParentId(Process process)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                // /proc/<pid>/stat: "pid (comm) state ppid ...", where comm may itself contain spaces and parentheses.
                var stat = File.ReadAllText($"/proc/{process.Id}/stat");
                var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
                return int.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture);
            }

            if (OperatingSystem.IsWindows())
            {
                const int ProcessBasicInformationClass = 0;
                var size = Marshal.SizeOf<ProcessBasicInformation>();
                return NtQueryInformationProcess(process.SafeHandle, ProcessBasicInformationClass, out var information, size, out _) == 0
                    ? (int)information.InheritedFromUniqueProcessId
                    : null;
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string? ReadWindowsCommandLine(Process process)
    {
        // ProcessCommandLineInformation returns a UNICODE_STRING whose buffer points into the same output block.
        const int ProcessCommandLineInformation = 60;
        NtQueryInformationProcess(process.SafeHandle, ProcessCommandLineInformation, IntPtr.Zero, 0, out var length);
        if (length <= 0)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (NtQueryInformationProcess(process.SafeHandle, ProcessCommandLineInformation, buffer, length, out _) != 0)
            {
                return null;
            }

            var text = Marshal.PtrToStructure<UnicodeString>(buffer);
            return Marshal.PtrToStringUni(text.Buffer, text.Length / 2);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        Microsoft.Win32.SafeHandles.SafeProcessHandle process,
        int processInformationClass,
        IntPtr processInformation,
        int processInformationLength,
        out int returnLength);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        Microsoft.Win32.SafeHandles.SafeProcessHandle process,
        int processInformationClass,
        out ProcessBasicInformation processInformation,
        int processInformationLength,
        out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    // PROCESS_BASIC_INFORMATION; every member is pointer-sized, ExitStatus included, on x86, x64 and ARM64.
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }
}
