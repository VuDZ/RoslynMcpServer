using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace RoslynMcpServer.Diagnostics;

/// <summary>
/// Parent image via <c>QueryFullProcessImageName</c>. Command line is read from the parent PEB
/// only when the image is node; other parents (Cursor's utility host) are identified by the exe name.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsParentProcess
{
    internal static ParentProcessSnapshot Capture()
    {
        var parentId = TryGetParentProcessId();
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

    internal static int TryGetParentProcessId()
    {
        using var current = Process.GetCurrentProcess();
        var info = default(ProcessBasicInformation);
        var status = NtQueryInformationProcess(
            current.Handle,
            ProcessBasicInformationClass,
            ref info,
            Marshal.SizeOf<ProcessBasicInformation>(),
            out _);
        if (status != 0)
        {
            return 0;
        }

        var parentId = info.InheritedFromUniqueProcessId.ToInt64();
        if (parentId <= 0 || parentId > int.MaxValue)
        {
            return 0;
        }

        return (int)parentId;
    }

    internal static string? TryGetImagePath(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new char[ImagePathCapacity];
            var size = buffer.Length;
            if (!QueryFullProcessImageName(handle, 0, buffer, ref size) || size <= 0)
            {
                return null;
            }

            return new string(buffer, 0, size);
        }
        finally
        {
            _ = CloseHandle(handle);
        }
    }

    internal static string? TryReadCommandLine(int processId)
    {
        var handle = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return ReadCommandLine(handle);
        }
        finally
        {
            _ = CloseHandle(handle);
        }
    }

    private static string? ReadCommandLine(IntPtr processHandle)
    {
        // ProcessBasicInformation.PebBaseAddress on a WOW64 process is the 64-bit PEB.
        // The command line the process actually uses lives in the 32-bit PEB (class 26).
        if (IsWow64Process(processHandle, out var wow64) && wow64)
        {
            return ReadCommandLineAt(
                processHandle,
                ReadWow64Peb(processHandle),
                pointerSize: 4,
                parametersOffset: PebProcessParametersOffsetWow64,
                commandLineOffset: ParametersCommandLineOffsetWow64,
                unicodeBufferOffset: 4);
        }

        var info = default(ProcessBasicInformation);
        var status = NtQueryInformationProcess(
            processHandle,
            ProcessBasicInformationClass,
            ref info,
            Marshal.SizeOf<ProcessBasicInformation>(),
            out _);
        if (status != 0)
        {
            return null;
        }

        return ReadCommandLineAt(
            processHandle,
            info.PebBaseAddress,
            pointerSize: IntPtr.Size,
            parametersOffset: PebProcessParametersOffset,
            commandLineOffset: ParametersCommandLineOffset,
            unicodeBufferOffset: 8);
    }

    private static IntPtr ReadWow64Peb(IntPtr processHandle)
    {
        var peb32 = IntPtr.Zero;
        var status = NtQueryInformationProcessPointer(
            processHandle,
            ProcessWow64InformationClass,
            ref peb32,
            IntPtr.Size,
            out _);
        return status == 0 ? peb32 : IntPtr.Zero;
    }

    private static string? ReadCommandLineAt(
        IntPtr processHandle,
        IntPtr peb,
        int pointerSize,
        int parametersOffset,
        int commandLineOffset,
        int unicodeBufferOffset)
    {
        if (peb == IntPtr.Zero)
        {
            return null;
        }

        var parameters = ReadPointer(processHandle, peb + parametersOffset, pointerSize);
        if (parameters == IntPtr.Zero)
        {
            return null;
        }

        var commandLineAddress = parameters + commandLineOffset;
        var byteLength = ReadUInt16(processHandle, commandLineAddress);
        if (byteLength == 0 || byteLength > MaxCommandLineBytes)
        {
            return null;
        }

        var bufferAddress = ReadPointer(processHandle, commandLineAddress + unicodeBufferOffset, pointerSize);
        if (bufferAddress == IntPtr.Zero)
        {
            return null;
        }

        var bytes = new byte[byteLength];
        if (!ReadProcessMemory(processHandle, bufferAddress, bytes, bytes.Length, out var read) || read != bytes.Length)
        {
            return null;
        }

        return Encoding.Unicode.GetString(bytes);
    }

    private static IntPtr ReadPointer(IntPtr processHandle, IntPtr address, int pointerSize)
    {
        var bytes = new byte[pointerSize];
        if (!ReadProcessMemory(processHandle, address, bytes, bytes.Length, out var read) || read != pointerSize)
        {
            return IntPtr.Zero;
        }

        return pointerSize == 4
            ? new IntPtr(BitConverter.ToInt32(bytes, 0))
            : new IntPtr(BitConverter.ToInt64(bytes, 0));
    }

    private static int ReadUInt16(IntPtr processHandle, IntPtr address)
    {
        var bytes = new byte[2];
        if (!ReadProcessMemory(processHandle, address, bytes, bytes.Length, out var read) || read != 2)
        {
            return 0;
        }

        return BitConverter.ToUInt16(bytes, 0);
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref ProcessBasicInformation processInformation,
        int processInformationLength,
        out int returnLength);

    [DllImport("ntdll.dll", EntryPoint = "NtQueryInformationProcess")]
    private static extern int NtQueryInformationProcessPointer(
        IntPtr processHandle,
        int processInformationClass,
        ref IntPtr processInformation,
        int processInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        IntPtr processHandle,
        int flags,
        char[] exeName,
        ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(
        IntPtr processHandle,
        IntPtr baseAddress,
        byte[] buffer,
        int size,
        out int numberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process(IntPtr processHandle, out bool wow64Process);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    private const int ProcessBasicInformationClass = 0;

    private const int ProcessWow64InformationClass = 26;

    private const uint ProcessQueryInformation = 0x0400;

    private const uint ProcessVmRead = 0x0010;

    private const uint ProcessQueryLimitedInformation = 0x1000;

    private const int PebProcessParametersOffset = 0x20;

    private const int PebProcessParametersOffsetWow64 = 0x10;

    private const int ParametersCommandLineOffset = 0x70;

    private const int ParametersCommandLineOffsetWow64 = 0x40;

    private const int ImagePathCapacity = 1024;

    private const int MaxCommandLineBytes = 32 * 1024;
}
