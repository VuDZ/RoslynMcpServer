namespace RoslynMcpServer.Diagnostics;

/// <summary>
/// Picks an MSBuild installation compatible with the current process bitness.
/// Avoids 32-bit SDK paths under <c>Program Files (x86)</c> when the MCP host is 64-bit.
/// </summary>
public static class MsBuildInstanceSelector
{
    public static MsBuildInstanceCandidate? SelectBest(
        IReadOnlyList<MsBuildInstanceCandidate> instances,
        bool is64BitProcess)
    {
        if (instances.Count == 0)
        {
            return null;
        }

        var compatible = instances
            .Where(i => IsCompatibleWithProcess(i.MSBuildPath, i.VisualStudioRootPath, is64BitProcess))
            .ToList();

        if (compatible.Count == 0)
        {
            return null;
        }

        var visualStudio = compatible
            .Where(IsVisualStudioInstallation)
            .OrderByDescending(i => i.Version)
            .ToList();

        var vs2022OrNewer = visualStudio.FirstOrDefault(i => i.Version.Major >= 17);
        if (vs2022OrNewer != default)
        {
            return vs2022OrNewer;
        }

        if (visualStudio.Count > 0)
        {
            return visualStudio[0];
        }

        return compatible
            .OrderByDescending(i => i.Version)
            .First();
    }

    /// <summary>Latest .NET SDK folder containing <c>Microsoft.Build.dll</c> (64-bit Program Files on Windows).</summary>
    public static string? FindLatestCompatibleDotNetSdkDirectory(bool is64BitProcess)
    {
        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var sdkRoot = Path.Combine(programFiles, "dotnet", "sdk");
            var from64 = FindLatestSdkWithMsBuild(sdkRoot);
            if (from64 is not null || is64BitProcess)
            {
                return from64;
            }

            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            return FindLatestSdkWithMsBuild(Path.Combine(programFilesX86, "dotnet", "sdk"));
        }

        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            var fromEnv = FindLatestSdkWithMsBuild(Path.Combine(dotnetRoot, "sdk"));
            if (fromEnv is not null)
            {
                return fromEnv;
            }
        }

        return FindLatestSdkWithMsBuild("/usr/local/share/dotnet/sdk")
               ?? FindLatestSdkWithMsBuild("/usr/share/dotnet/sdk");
    }

    /// <summary>Visual Studio <c>MSBuild\\Current\\Bin\\amd64</c> when discovery only returned x86 SDKs.</summary>
    public static string? FindVisualStudioMsBuildDirectory()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var vsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Microsoft Visual Studio");

        if (!Directory.Exists(vsRoot))
        {
            return null;
        }

        string? best = null;
        Version? bestVersion = null;

        foreach (var yearDir in Directory.EnumerateDirectories(vsRoot))
        {
            if (!int.TryParse(Path.GetFileName(yearDir), out var year) || year < 2017)
            {
                continue;
            }

            foreach (var editionDir in Directory.EnumerateDirectories(yearDir))
            {
                var amd64 = Path.Combine(editionDir, "MSBuild", "Current", "Bin", "amd64");
                if (!File.Exists(Path.Combine(amd64, "MSBuild.exe")))
                {
                    continue;
                }

                var version = new Version(year, 0);
                if (bestVersion is null || version > bestVersion)
                {
                    bestVersion = version;
                    best = amd64;
                }
            }
        }

        return best;
    }

    public static bool IsCompatibleWithProcess(
        string? msBuildPath,
        string? visualStudioRootPath,
        bool is64BitProcess)
    {
        if (!is64BitProcess)
        {
            return true;
        }

        return !Is32BitWindowsPath(msBuildPath) && !Is32BitWindowsPath(visualStudioRootPath);
    }

    public static bool Is32BitWindowsPath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && path.Contains("(x86)", StringComparison.OrdinalIgnoreCase);

    public static bool IsVisualStudioInstallation(MsBuildInstanceCandidate candidate) =>
        (candidate.VisualStudioRootPath?.Contains("Microsoft Visual Studio", StringComparison.OrdinalIgnoreCase) ?? false)
        || candidate.MSBuildPath.Contains(@"MSBuild\Current\Bin", StringComparison.OrdinalIgnoreCase);

    private static string? FindLatestSdkWithMsBuild(string sdkRoot)
    {
        if (!Directory.Exists(sdkRoot))
        {
            return null;
        }

        foreach (var dir in Directory.GetDirectories(sdkRoot).OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(Path.Combine(dir, "Microsoft.Build.dll")))
            {
                return dir;
            }
        }

        return null;
    }
}
