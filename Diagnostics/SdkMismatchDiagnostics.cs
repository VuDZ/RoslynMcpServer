using RoslynMcpServer.Services;

namespace RoslynMcpServer.Diagnostics;

/// <summary>Synthetic errors when a build log shows a different MSBuild from the host's SDK selection.</summary>
public static class SdkMismatchDiagnostics
{
    public const string MismatchCode = "MCP_MSBUILD_SDK_MISMATCH";

    public static IReadOnlyList<DotNetBuildDiagnosticParser.DiagnosticEntry> CreateErrors(
        DotNetSdkEnvironment.SdkPinInfo? pin,
        string combinedOutput)
    {
        if (!DotNetSdkEnvironment.HasConfiguredSdk(pin) || pin?.MsBuildDllPath is not { } expectedMsBuild)
        {
            return [];
        }

        var logMsbuild = MsBuildLogHighlighter.TryGetMsBuildExecutablePath(combinedOutput);
        if (string.IsNullOrEmpty(logMsbuild) || DotNetSdkEnvironment.PathsEqual(logMsbuild, expectedMsBuild))
        {
            return [];
        }

        return
        [
            new DotNetBuildDiagnosticParser.DiagnosticEntry(
                "error",
                MismatchCode,
                "(msbuild-log)",
                $"MSBuild in log is `{logMsbuild}` but dotnet resolved `{expectedMsBuild}` using global.json.")
        ];
    }
}
