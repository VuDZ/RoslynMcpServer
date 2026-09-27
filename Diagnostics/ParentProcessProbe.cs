namespace RoslynMcpServer.Diagnostics;

/// <summary>Reads the immediate parent process. Platform readers stay behind <see cref="OperatingSystem"/> checks.</summary>
internal static class ParentProcessProbe
{
    internal static ParentProcessSnapshot Capture()
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsParentProcess.Capture();
        }

        if (OperatingSystem.IsLinux())
        {
            return LinuxParentProcess.Capture();
        }

        if (OperatingSystem.IsMacOS())
        {
            return MacOsParentProcess.Capture();
        }

        return ParentProcessSnapshot.Empty;
    }
}
