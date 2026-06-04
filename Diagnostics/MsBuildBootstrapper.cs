using Microsoft.Build.Locator;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Diagnostics;

/// <summary>Registers MSBuild for Roslyn <see cref="Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace"/> at process startup.</summary>
public static class MsBuildBootstrapper
{
    public static void Register()
    {
        var is64Bit = Environment.Is64BitProcess;
        MsBuildEnvironmentInfo.ProcessDescription = is64Bit ? "64-bit" : "32-bit";

        var instances = MSBuildLocator.QueryVisualStudioInstances().ToList();
        var queriedLines = new List<string>();
        foreach (var i in instances)
        {
            var detail =
                $"{i.Name} ({i.Version}) at {i.MSBuildPath}, VisualStudioRootPath={i.VisualStudioRootPath}";
            Console.Error.WriteLine($"[DEBUG] Found MSBuild: {detail}");
            queriedLines.Add(detail);
        }

        MsBuildEnvironmentInfo.QueriedInstanceLines = queriedLines;

        var startupSearchDir = ResolveStartupSearchDirectory();
        var fromGlobalJson = GlobalJsonSdkReader.TryResolveSdkDirectory(startupSearchDir, is64Bit);
        if (fromGlobalJson is not null)
        {
            MSBuildLocator.RegisterMSBuildPath(fromGlobalJson);
            var pinned = GlobalJsonSdkReader.TryGetPinnedSdkVersion(startupSearchDir) ?? "(unknown)";
            MsBuildEnvironmentInfo.RegistrationSummary =
                $"RegisterMSBuildPath (global.json SDK {pinned}): {fromGlobalJson}";
            Console.Error.WriteLine($"[DEBUG] Using MSBuild from global.json SDK: {fromGlobalJson}");
            MsBuildEnvironmentInfo.RefreshRegisteredInstance();
            return;
        }

        var candidates = instances
            .Select(i => new MsBuildInstanceCandidate(i.Version, i.Name, i.MSBuildPath, i.VisualStudioRootPath))
            .ToList();

        var selected = MsBuildInstanceSelector.SelectBest(candidates, is64Bit);
        if (selected is not null)
        {
            var instance = instances.First(i =>
                string.Equals(i.MSBuildPath, selected.Value.MSBuildPath, StringComparison.OrdinalIgnoreCase));
            MSBuildLocator.RegisterInstance(instance);
            MsBuildEnvironmentInfo.RegistrationSummary =
                $"RegisterInstance: {instance.Name} ({instance.Version}) at {instance.MSBuildPath}";
            Console.Error.WriteLine($"[DEBUG] Using MSBuild from: {instance.MSBuildPath}");
            MsBuildEnvironmentInfo.RefreshRegisteredInstance();
            return;
        }

        var sdkDir = MsBuildInstanceSelector.FindLatestCompatibleDotNetSdkDirectory(is64Bit);
        if (sdkDir is not null)
        {
            MSBuildLocator.RegisterMSBuildPath(sdkDir);
            MsBuildEnvironmentInfo.RegistrationSummary = $"RegisterMSBuildPath: {sdkDir}";
            Console.Error.WriteLine($"[DEBUG] Using MSBuild from .NET SDK: {sdkDir}");
            MsBuildEnvironmentInfo.RefreshRegisteredInstance();
            return;
        }

        var vsMsBuild = MsBuildInstanceSelector.FindVisualStudioMsBuildDirectory();
        if (vsMsBuild is not null)
        {
            MSBuildLocator.RegisterMSBuildPath(vsMsBuild);
            MsBuildEnvironmentInfo.RegistrationSummary = $"RegisterMSBuildPath (VS): {vsMsBuild}";
            Console.Error.WriteLine($"[DEBUG] Using MSBuild from Visual Studio: {vsMsBuild}");
            MsBuildEnvironmentInfo.RefreshRegisteredInstance();
            return;
        }

        if (is64Bit && OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine(
                "[ERR] No 64-bit MSBuild found. Install Visual Studio 2022 Build Tools or 64-bit .NET SDK "
                + @"(C:\Program Files\dotnet). Avoid RegisterDefaults — it may bind 32-bit SDK under Program Files (x86).");
            MsBuildEnvironmentInfo.RegistrationSummary =
                "Failed: no compatible 64-bit MSBuild (see stderr). Uninstall x86-only SDK or install VS 2022 / 64-bit .NET SDK.";
            return;
        }

        Console.Error.WriteLine("[WARN] No compatible MSBuild from QueryVisualStudioInstances; falling back to RegisterDefaults.");
        MSBuildLocator.RegisterDefaults();
        MsBuildEnvironmentInfo.RegistrationSummary = "RegisterDefaults (fallback)";
        MsBuildEnvironmentInfo.RefreshRegisteredInstance();
    }

    private static string ResolveStartupSearchDirectory()
    {
        var fromEnv = Environment.GetEnvironmentVariable("ROSLYN_MCP_WORKSPACE");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            try
            {
                return Path.GetFullPath(fromEnv.Trim());
            }
            catch
            {
                // fall through
            }
        }

        return Environment.CurrentDirectory;
    }
}
