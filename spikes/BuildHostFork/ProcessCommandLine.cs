using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BuildHostFork;

/// <summary>
/// Reads the command line of another process: <c>/proc</c> on Linux, <c>NtQueryInformationProcess</c> on Windows.
/// The netcore BuildHost runs as <c>dotnet &lt;path&gt;/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll</c>,
/// so the command line is the only way to tell it from other dotnet processes and to see which folder it came from.
/// </summary>
internal static class ProcessCommandLine
{
    /// <returns>The command line, or null when it cannot be read (the process exited, access denied, other OS).</returns>
    public static string? TryRead(Process process)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var arguments = File.ReadAllText($"/proc/{process.Id}/cmdline").TrimEnd('\0').Split('\0');
                return string.Join(' ', arguments.Select(argument => argument.Contains(' ') ? $"\"{argument}\"" : argument));
            }

            return OperatingSystem.IsWindows() ? ReadWindows(process) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string? ReadWindows(Process process)
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

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }
}
