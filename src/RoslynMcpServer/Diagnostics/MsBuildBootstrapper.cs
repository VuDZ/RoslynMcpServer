using Microsoft.Build.Locator;

namespace RoslynMcpServer.Diagnostics;

/// <summary>Registers MSBuild for Roslyn <see cref="Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace"/> at process startup.</summary>
public static class MsBuildBootstrapper
{
    public static void Register()
    {
        var is64Bit = Environment.Is64BitProcess;
        MsBuildEnvironmentInfo.ProcessDescription = is64Bit ? "64-bit" : "32-bit";

        if (MSBuildLocator.IsRegistered)
        {
            MsBuildEnvironmentInfo.RegistrationSummary ??= "already registered";
            MsBuildEnvironmentInfo.RefreshRegisteredInstance();
            return;
        }

        try
        {
            RegisterCore(is64Bit);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("already loaded", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("RegisterInstance", StringComparison.OrdinalIgnoreCase))
        {
            MsBuildEnvironmentInfo.RegistrationSummary =
                "MSBuild assemblies already loaded; RegisterInstance skipped: " + ex.Message;
            Console.Error.WriteLine($"[WARN] {MsBuildEnvironmentInfo.RegistrationSummary}");
            MsBuildEnvironmentInfo.RefreshRegisteredInstance();
        }
    }

    private static void RegisterCore(bool is64Bit)
    {
        var startupSearchDir = ResolveStartupSearchDirectory();
        var requestedSdk = GlobalJsonSdkReader.TryGetPinnedSdkVersion(startupSearchDir);
        var instances = QueryStartupInstances(startupSearchDir);
        var queriedLines = new List<string>();
        foreach (var i in instances)
        {
            var detail =
                $"{i.Name} ({i.Version}) at {i.MSBuildPath}, VisualStudioRootPath={i.VisualStudioRootPath}";
            Console.Error.WriteLine($"[DEBUG] Found MSBuild: {detail}");
            queriedLines.Add(detail);
        }

        MsBuildEnvironmentInfo.QueriedInstanceLines = queriedLines;

        // Locator ranks the SDK resolved by hostfxr first when it is a compatible installed SDK.
        var preferredSdk = requestedSdk is null
            ? null
            : instances.FirstOrDefault(i =>
                i.DiscoveryType == DiscoveryType.DotNetSdk
                && MsBuildInstanceSelector.IsCompatibleWithProcess(i.MSBuildPath, i.VisualStudioRootPath, is64Bit));
        if (preferredSdk is not null)
        {
            MSBuildLocator.RegisterInstance(preferredSdk);
            MsBuildEnvironmentInfo.SelectedMsBuildPath = preferredSdk.MSBuildPath;
            MsBuildEnvironmentInfo.RegistrationSummary =
                $"RegisterInstance (startup SDK discovery): {preferredSdk.Version} at {preferredSdk.MSBuildPath}";
            Console.Error.WriteLine($"[DEBUG] Using MSBuild from: {preferredSdk.MSBuildPath}");
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
            MsBuildEnvironmentInfo.SelectedMsBuildPath = instance.MSBuildPath;
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
            MsBuildEnvironmentInfo.SelectedMsBuildPath = sdkDir;
            MsBuildEnvironmentInfo.RegistrationSummary = $"RegisterMSBuildPath: {sdkDir}";
            Console.Error.WriteLine($"[DEBUG] Using MSBuild from .NET SDK: {sdkDir}");
            MsBuildEnvironmentInfo.RefreshRegisteredInstance();
            return;
        }

        var vsMsBuild = MsBuildInstanceSelector.FindVisualStudioMsBuildDirectory();
        if (vsMsBuild is not null)
        {
            MSBuildLocator.RegisterMSBuildPath(vsMsBuild);
            MsBuildEnvironmentInfo.SelectedMsBuildPath = vsMsBuild;
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

        Console.Error.WriteLine("[ERR] No compatible MSBuild installation found.");
        MsBuildEnvironmentInfo.RegistrationSummary = "Failed: no compatible MSBuild installation found.";
    }

    internal static IReadOnlyList<VisualStudioInstance> QueryStartupInstances(string workingDirectory)
    {
        // hostfxr reads global.json without running the SDK. Locator returns only SDKs
        // enumerated under installed dotnet hosts, even when global.json has custom SDK paths.
        try
        {
            return QueryInstances(workingDirectory);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(
                "[WARN] MSBuild discovery could not resolve an SDK in the startup directory. Trying installed toolsets.");
            Console.Error.WriteLine($"[DEBUG] MSBuild discovery details: {ex.Message}");
        }

        var volumeRoot = Path.GetPathRoot(AppContext.BaseDirectory)!;
        try
        {
            return QueryInstances(volumeRoot);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"[DEBUG] MSBuild discovery at the filesystem root failed: {ex.Message}");
            return [];
        }
    }

    private static List<VisualStudioInstance> QueryInstances(string workingDirectory)
    {
        var options = VisualStudioInstanceQueryOptions.Default;
        options.WorkingDirectory = workingDirectory;
        return MSBuildLocator.QueryVisualStudioInstances(options).ToList();
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
