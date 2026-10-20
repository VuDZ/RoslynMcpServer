using Microsoft.Build.Locator;
using ParallelLoad.Bootstrap;

namespace ParallelLoad;

internal static class MsBuildRegistration
{
    public static string Register(string solutionDirectory, string? localSdkPath = null)
    {
        if (localSdkPath is not null)
        {
            var path = Path.GetFullPath(localSdkPath);
            var localPinnedVersion = GlobalJsonSdkReader.TryGetPinnedSdkVersion(solutionDirectory);
            var root = Path.GetDirectoryName(Path.GetDirectoryName(path))!;
            if (Path.GetFileName(path) != "10.0.301" || localPinnedVersion != "10.0.301" ||
                !File.Exists(Path.Combine(path, "Microsoft.Build.dll")) || !File.Exists(Path.Combine(root, "dotnet.exe")))
            {
                throw new InvalidOperationException("Local registration requires the exact pinned SDK 10.0.301 and its dotnet host.");
            }
            // BuildHost inherits these variables. Both registration and SDK resolution must remain on the local installation.
            Environment.SetEnvironmentVariable("DOTNET_ROOT", root);
            Environment.SetEnvironmentVariable("DOTNET_ROOT_X64", root);
            Environment.SetEnvironmentVariable("DOTNET_HOST_PATH", Path.Combine(root, "dotnet.exe"));
            Environment.SetEnvironmentVariable("MSBUILD_EXE_PATH", Path.Combine(path, "MSBuild.dll"));
            Environment.SetEnvironmentVariable("MSBuildSDKsPath", Path.Combine(path, "Sdks"));
            Environment.SetEnvironmentVariable("DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR", root);
            Environment.SetEnvironmentVariable("DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR", path);
            Environment.SetEnvironmentVariable("DOTNET_MSBUILD_SDK_RESOLVER_SDKS_VER", "10.0.301");
            // Roslyn's public loader starts dotnet.exe by name; DOTNET_ROOT alone does not choose that executable.
            Environment.SetEnvironmentVariable("PATH", root + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
            MSBuildLocator.RegisterMSBuildPath(path);
            return path;
        }
        var is64Bit = Environment.Is64BitProcess;
        var instances = MSBuildLocator.QueryVisualStudioInstances().ToList();
        var pinned = GlobalJsonSdkReader.TryResolveSdkDirectory(solutionDirectory, is64Bit);
        if (pinned is not null)
        {
            MSBuildLocator.RegisterMSBuildPath(pinned);
            return pinned;
        }

        var candidates = instances.Select(instance => new MsBuildInstanceCandidate(
            instance.Version, instance.Name, instance.MSBuildPath, instance.VisualStudioRootPath)).ToList();
        var selected = MsBuildInstanceSelector.SelectBest(candidates, is64Bit);
        if (selected is not null)
        {
            MSBuildLocator.RegisterInstance(instances.First(instance =>
                string.Equals(instance.MSBuildPath, selected.Value.MSBuildPath, StringComparison.OrdinalIgnoreCase)));
            return selected.Value.MSBuildPath;
        }

        var fallback = MsBuildInstanceSelector.FindLatestCompatibleDotNetSdkDirectory(is64Bit)
            ?? MsBuildInstanceSelector.FindVisualStudioMsBuildDirectory();
        if (fallback is not null)
        {
            MSBuildLocator.RegisterMSBuildPath(fallback);
            return fallback;
        }

        if (is64Bit && OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException("No compatible 64-bit MSBuild found.");
        }

        return MSBuildLocator.RegisterDefaults().MSBuildPath;
    }
}
