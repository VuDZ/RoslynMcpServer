using System.Collections;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using RoslynMcpServer.Diagnostics;

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

    private static readonly Regex SdkFolderInPath = new(
        @"[/\\]sdk[/\\](?<ver>\d+\.\d+\.\d+(?:[-\w.\+]*)?)[/\\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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

    /// <summary>Reads MSBuild SDK override vars from the current MCP process (before child env is built).</summary>
    public static InheritedMsBuildSdkOverrides CaptureInheritedOverrides() =>
        new(
            NullIfEmpty(Environment.GetEnvironmentVariable(MsBuildSdksPathVariable)),
            NullIfEmpty(Environment.GetEnvironmentVariable(MsBuildExePathVariable)),
            NullIfEmpty(Environment.GetEnvironmentVariable(MsBuildExtensionsPathVariable)),
            NullIfEmpty(Environment.GetEnvironmentVariable(SdkResolverSdksDirVariable)),
            NullIfEmpty(Environment.GetEnvironmentVariable(SdkResolverSdksVerVariable)),
            NullIfEmpty(Environment.GetEnvironmentVariable(SdkResolverCliDirVariable)));

    public static SdkApplyResult ApplyPinnedSdk(ProcessStartInfo psi, string workingDirectory)
    {
        var inherited = CaptureInheritedOverrides();
        CopyInheritedEnvironment(psi);

        var pin = TryGetPin(workingDirectory);
        if (pin?.SdkDirectory is null)
        {
            // No global.json pin: let the host resolve SDK normally (do not keep Locator/IDE overrides).
            ClearMsBuildSdkOverrideVariables(psi);
            var action = inherited.HasAny
                ? SdkEnvAction.StrippedInheritedOverrides
                : SdkEnvAction.HostDefault;
            return new SdkApplyResult(action, inherited, pin);
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

        return new SdkApplyResult(SdkEnvAction.PinnedFromGlobalJson, inherited, pin);
    }

    /// <summary>
    /// Appends agent-facing SDK env diagnostics (inherited overrides, strip/pin action, SDK inferred from log).
    /// Safe to call without a prior <see cref="ApplyPinnedSdk"/> — recomputes from process env + <c>global.json</c>.
    /// </summary>
    public static void AppendSdkEnvMetadata(StringBuilder metadata, string workingDirectory, string combinedOutput)
    {
        var inherited = CaptureInheritedOverrides();
        var pin = TryGetPin(workingDirectory);
        var action = pin?.SdkDirectory is not null
            ? SdkEnvAction.PinnedFromGlobalJson
            : inherited.HasAny
                ? SdkEnvAction.StrippedInheritedOverrides
                : SdkEnvAction.HostDefault;

        if (inherited.HasAny)
        {
            metadata.AppendLine(
                $"- **Inherited MSBuildSDKsPath:** `{inherited.MsBuildSdksPath ?? "(unset)"}`");
            if (!string.IsNullOrEmpty(inherited.MsBuildExePath))
            {
                metadata.AppendLine($"- **Inherited MSBUILD_EXE_PATH:** `{inherited.MsBuildExePath}`");
            }

            if (!string.IsNullOrEmpty(inherited.ResolverSdksVer))
            {
                metadata.AppendLine(
                    $"- **Inherited DOTNET_MSBUILD_SDK_RESOLVER_SDKS_VER:** `{inherited.ResolverSdksVer}`");
            }
        }
        else
        {
            metadata.AppendLine("- **Inherited MSBuild SDK overrides:** (none)");
        }

        metadata.AppendLine($"- **SDK env action:** `{DescribeAction(action, pin)}`");

        var effective = TryInferSdkVersionFromLog(combinedOutput);
        if (!string.IsNullOrEmpty(effective))
        {
            metadata.AppendLine($"- **SDK from MSBuild log paths:** `{effective}`");
        }
    }

    /// <summary>Best-effort SDK version from MSBuild executable path or any <c>\sdk\X.Y.Z\</c> in the log.</summary>
    public static string? TryInferSdkVersionFromLog(string combinedOutput)
    {
        if (string.IsNullOrWhiteSpace(combinedOutput))
        {
            return null;
        }

        var msbuildPath = MsBuildLogHighlighter.TryGetMsBuildExecutablePath(combinedOutput);
        if (!string.IsNullOrEmpty(msbuildPath))
        {
            var fromMsbuild = TryParseSdkVersionFromPath(msbuildPath);
            if (!string.IsNullOrEmpty(fromMsbuild))
            {
                return fromMsbuild;
            }
        }

        var match = SdkFolderInPath.Match(combinedOutput);
        return match.Success ? match.Groups["ver"].Value : null;
    }

    public static string? TryParseSdkVersionFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var match = SdkFolderInPath.Match(path.Replace('/', Path.DirectorySeparatorChar));
        return match.Success ? match.Groups["ver"].Value : null;
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

    private static string DescribeAction(SdkEnvAction action, SdkPinInfo? pin) =>
        action switch
        {
            SdkEnvAction.PinnedFromGlobalJson =>
                $"pinned from global.json → {pin?.SdkDirectory ?? pin?.PinnedVersion ?? "(unknown)"}",
            SdkEnvAction.StrippedInheritedOverrides =>
                "stripped inherited MSBuildSDKsPath / MSBUILD_EXE_PATH / DOTNET_MSBUILD_SDK_RESOLVER_* (host resolves SDK)",
            _ => "host default (no inherited overrides, no global.json pin)"
        };

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a.Trim()), Path.GetFullPath(b.Trim()), StringComparison.OrdinalIgnoreCase);

    public enum SdkEnvAction
    {
        HostDefault,
        StrippedInheritedOverrides,
        PinnedFromGlobalJson
    }

    public sealed record InheritedMsBuildSdkOverrides(
        string? MsBuildSdksPath,
        string? MsBuildExePath,
        string? MsBuildExtensionsPath,
        string? ResolverSdksDir,
        string? ResolverSdksVer,
        string? ResolverCliDir)
    {
        public bool HasAny =>
            MsBuildSdksPath is not null
            || MsBuildExePath is not null
            || MsBuildExtensionsPath is not null
            || ResolverSdksDir is not null
            || ResolverSdksVer is not null
            || ResolverCliDir is not null;
    }

    public sealed record SdkApplyResult(
        SdkEnvAction Action,
        InheritedMsBuildSdkOverrides Inherited,
        SdkPinInfo? Pin);

    public sealed record SdkPinInfo(
        string? PinnedVersion,
        string? SdkDirectory,
        string? MsBuildDllPath,
        string? SdksDirectory,
        string? DotNetRoot);
}
