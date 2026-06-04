using RoslynMcpServer.Services;

namespace RoslynMcpServer.Diagnostics;

/// <summary>Synthetic errors when MSBuild in the log does not match <c>global.json</c> pinned SDK.</summary>
public static class SdkMismatchDiagnostics
{
    public const string MismatchCode = "MCP_MSBUILD_SDK_MISMATCH";

    public static IReadOnlyList<DotNetBuildDiagnosticParser.DiagnosticEntry> CreateErrors(
        string workingDirectory,
        string combinedOutput,
        string? dotnetVersionFromMetadata)
    {
        var pin = DotNetSdkEnvironment.TryGetPin(workingDirectory);
        if (pin?.MsBuildDllPath is null)
        {
            return [];
        }

        var list = new List<DotNetBuildDiagnosticParser.DiagnosticEntry>();
        var logMsbuild = MsBuildLogHighlighter.TryGetMsBuildExecutablePath(combinedOutput);
        if (!string.IsNullOrEmpty(logMsbuild)
            && !DotNetSdkEnvironment.PathsEqual(logMsbuild, pin.MsBuildDllPath))
        {
            list.Add(new DotNetBuildDiagnosticParser.DiagnosticEntry(
                "error",
                MismatchCode,
                "(msbuild-log)",
                $"MSBuild in log is `{logMsbuild}` but global.json expects `{pin.MsBuildDllPath}`. "
                + "Child restore/build nodes are using a different SDK than pinned."));
        }

        if (!string.IsNullOrEmpty(pin.PinnedVersion)
            && !string.IsNullOrEmpty(dotnetVersionFromMetadata)
            && !string.Equals(pin.PinnedVersion, dotnetVersionFromMetadata, StringComparison.OrdinalIgnoreCase)
            && !dotnetVersionFromMetadata.StartsWith(pin.PinnedVersion, StringComparison.OrdinalIgnoreCase))
        {
            list.Add(new DotNetBuildDiagnosticParser.DiagnosticEntry(
                "error",
                MismatchCode,
                "(dotnet-cli)",
                $"`dotnet --version` reported `{dotnetVersionFromMetadata}` but global.json pins `{pin.PinnedVersion}`."));
        }

        return list;
    }

    public static string? TryParseDotNetVersionFromMetadata(string runMetadata)
    {
        const string prefix = "- **dotnet --version:** `";
        foreach (var line in runMetadata.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var start = prefix.Length;
            var end = trimmed.IndexOf('`', start);
            if (end > start)
            {
                return trimmed[start..end];
            }
        }

        return null;
    }
}
