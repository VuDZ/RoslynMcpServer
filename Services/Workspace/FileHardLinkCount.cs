using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RoslynMcpServer.Services.Workspace;

internal static class FileHardLinkCount
{
    internal static uint Read(SafeFileHandle handle, string path)
    {
        if (OperatingSystem.IsWindows())
        {
            const int FileStandardInfo = 1;
            if (!GetFileInformationByHandleEx(handle, FileStandardInfo, out var info, Marshal.SizeOf<WindowsFileInfo>()))
            {
                throw ReadError(path);
            }

            return info.LinkCount;
        }

        if (FStat(handle, out var status) != 0)
        {
            throw ReadError(path);
        }

        return status.LinkCount;
    }

    private static IOException ReadError(string path) =>
        new($"Could not read the hard link count for '{path}'.", new Win32Exception(Marshal.GetLastPInvokeError()));

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle handle, int infoClass, out WindowsFileInfo info, int size);

    [DllImport("System.Native", EntryPoint = "SystemNative_FStat", ExactSpelling = true, SetLastError = true)]
    private static extern int FStat(SafeFileHandle handle, out UnixFileStatus status);

    // FILE_STANDARD_INFO pads its final two byte flags to the alignment of LARGE_INTEGER.
    // https://learn.microsoft.com/windows/win32/api/winbase/ns-winbase-file_standard_info
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct WindowsFileInfo
    {
        [FieldOffset(16)] public uint LinkCount;
    }

    // The .NET 11 runtime normalizes libc stat layouts and resolves fstat for the target platform.
    // FileStatus is an internal runtime ABI. Recheck it when upgrading the target framework.
    // https://github.com/dotnet/runtime/blob/v11.0.0/src/native/libs/System.Native/pal_io.h
    [StructLayout(LayoutKind.Explicit, Size = 120)]
    private struct UnixFileStatus
    {
        [FieldOffset(116)] public uint LinkCount;
    }
}
