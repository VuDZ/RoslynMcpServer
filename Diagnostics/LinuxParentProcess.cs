using System.Runtime.Versioning;
using System.Text;

namespace RoslynMcpServer.Diagnostics;

/// <summary>Parent image from <c>/proc/&lt;pid&gt;/exe</c>. Command line is read only when that image is node.</summary>
[SupportedOSPlatform("linux")]
internal static class LinuxParentProcess
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
        if (!File.Exists(SelfStatusPath))
        {
            return 0;
        }

        foreach (var line in File.ReadLines(SelfStatusPath))
        {
            if (!line.StartsWith("PPid:", StringComparison.Ordinal))
            {
                continue;
            }

            var value = line.AsSpan("PPid:".Length).Trim();
            return int.TryParse(value, out var pid) ? pid : 0;
        }

        return 0;
    }

    internal static string? TryGetImagePath(int processId)
    {
        var exe = new FileInfo($"/proc/{processId}/exe");
        if (!string.IsNullOrEmpty(exe.LinkTarget))
        {
            return exe.LinkTarget;
        }

        var commPath = $"/proc/{processId}/comm";
        if (!File.Exists(commPath))
        {
            return null;
        }

        var comm = File.ReadAllText(commPath).Trim();
        return comm.Length == 0 ? null : comm;
    }

    internal static string? TryReadCommandLine(int processId)
    {
        var path = $"/proc/{processId}/cmdline";
        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0)
        {
            return null;
        }

        return Encoding.UTF8.GetString(bytes).Replace('\0', ' ').Trim();
    }

    private const string SelfStatusPath = "/proc/self/status";
}
