using System.Collections;
using System.Diagnostics;

namespace RoslynMcpServer.Services;

/// <summary>
/// Applies <c>global.json</c> SDK pin to child <c>dotnet</c> processes, or clears inherited MSBuild SDK
/// overrides so Locator/IDE pollution does not force an older SDK.
/// </summary>
public static class DotNetSdkEnvironment
{
    public const string SdkResolverSdksDirVariable = "DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR";
    public const string SdkResolverSdksVerVariable = "DOTNET_MSBUILD_SDK_RESOLVER_SDKS_VER";
    public const string SdkResolverCliDirVariable = "DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR";
    public const string DotNetRootVariable = "DOTNET_ROOT";
    public const string MultilevelLookupVariable = "DOTNET_MULTILEVEL_LOOKUP";
    public const string MsBuildExePathVariable = "MSBUILD_EXE_PATH";
    public const string MsBuildExtensionsPathVariable = "MSBuildExtensionsPath";
    public const string MsBuildSdksPathVariable = "MSBuildSDKsPath";

    public static SdkPinInfo? TryGetPin(string workingDirectory)
    {
        var pinnedVersion = GlobalJsonSdkReader.TryGetPinnedSdkVersion(workingDirectory);
        var sdkDir = GlobalJsonSdkReader.TryResolveSdkDirectory(workingDirectory, Environment.Is64BitProcess);
        if (sdkDir is null)
        {
            return pinnedVersion is null
                ? null
                : new SdkPinInfo(pinnedVersion, null, null, null, null);
        }

        var msbuildDll = Path.Combine(sdkDir, "MSBuild.dll");
        var sdksDir = Path.Combine(sdkDir, "Sdks");
        var dotnetRoot = Directory.GetParent(Path.GetDirectoryName(sdkDir)!)?.FullName;
        return new SdkPinInfo(pinnedVersion, sdkDir, msbuildDll, sdksDir, dotnetRoot);
    }

    /// <summary>
    /// Env vars that force MSBuild onto a specific SDK folder. <see cref="Microsoft.Build.Locator.MSBuildLocator"/>
    /// and IDE hosts often set these on the MCP process (e.g. to SDK 9.x); inheriting them into child
    /// <c>dotnet</c> causes <c>NETSDK1045</c> on net10 projects even when <c>dotnet --version</c> is 10.x.
    /// </summary>
    private static readonly string[] MsBuildSdkOverrideVariables =
    [
        MsBuildExePathVariable,
        MsBuildExtensionsPathVariable,
        MsBuildSdksPathVariable,
        SdkResolverSdksDirVariable,
        SdkResolverSdksVerVariable,
        SdkResolverCliDirVariable
    ];

    public static void ApplyPinnedSdk(ProcessStartInfo psi, string workingDirectory)
    {
        CopyInheritedEnvironment(psi);

        var pin = TryGetPin(workingDirectory);
        if (pin?.SdkDirectory is null)
        {
            // No global.json pin: let the host resolve SDK normally (do not keep Locator/IDE overrides).
            ClearMsBuildSdkOverrideVariables(psi);
            return;
        }

        psi.Environment[MsBuildExePathVariable] = pin.MsBuildDllPath!;
        psi.Environment[MsBuildExtensionsPathVariable] = pin.SdkDirectory;
        if (!string.IsNullOrEmpty(pin.SdksDirectory) && Directory.Exists(pin.SdksDirectory))
        {
            psi.Environment[MsBuildSdksPathVariable] = pin.SdksDirectory;
            psi.Environment[SdkResolverSdksDirVariable] = pin.SdksDirectory;
        }

        if (!string.IsNullOrEmpty(pin.PinnedVersion))
        {
            psi.Environment[SdkResolverSdksVerVariable] = pin.PinnedVersion;
        }

        if (!string.IsNullOrEmpty(pin.DotNetRoot) && Directory.Exists(pin.DotNetRoot))
        {
            psi.Environment[DotNetRootVariable] = pin.DotNetRoot;
            psi.Environment[SdkResolverCliDirVariable] = pin.DotNetRoot;
            psi.Environment[MultilevelLookupVariable] = "0";
        }
    }

    /// <summary>
    /// When <see cref="ProcessStartInfo.Environment"/> is modified, the child does not inherit the parent environment unless copied explicitly.
    /// </summary>
    private static void CopyInheritedEnvironment(ProcessStartInfo psi)
    {
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = entry.Key as string;
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            psi.Environment.TryAdd(key, entry.Value?.ToString());
        }
    }

    internal static void ClearMsBuildSdkOverrideVariables(ProcessStartInfo psi)
    {
        foreach (var key in MsBuildSdkOverrideVariables)
        {
            psi.Environment.Remove(key);
        }
    }

    public static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a.Trim()), Path.GetFullPath(b.Trim()), StringComparison.OrdinalIgnoreCase);

    public sealed record SdkPinInfo(
        string? PinnedVersion,
        string? SdkDirectory,
        string? MsBuildDllPath,
        string? SdksDirectory,
        string? DotNetRoot);
}
