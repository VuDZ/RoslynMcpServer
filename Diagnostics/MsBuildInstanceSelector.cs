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

        return FindLatestSdkDirectory(NonWindowsSdkRoots(
            Environment.GetEnvironmentVariable("DOTNET_ROOT"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
    }

    /// <summary>
    /// SDK folders searched when <c>MSBuildLocator</c> did not register an instance.
    /// <paramref name="dotnetRoot"/> is first. The fixed paths are the Microsoft install,
    /// distro packages, and Ubuntu's <c>/usr/lib/dotnet</c>. <paramref name="userProfile"/>
    /// adds the <c>dotnet-install.sh</c> default <c>~/.dotnet</c>.
    /// </summary>
    internal static IReadOnlyList<string> NonWindowsSdkRoots(string? dotnetRoot, string? userProfile)
    {
        var roots = new List<string>(5);
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            roots.Add(Path.Combine(dotnetRoot.Trim(), "sdk"));
        }

        roots.Add("/usr/local/share/dotnet/sdk");
        roots.Add("/usr/share/dotnet/sdk");
        roots.Add("/usr/lib/dotnet/sdk");
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            roots.Add(Path.Combine(userProfile, ".dotnet", "sdk"));
        }

        return roots;
    }

    internal static string? FindLatestSdkDirectory(IEnumerable<string> sdkRoots)
    {
        foreach (var root in sdkRoots)
        {
            var found = FindLatestSdkWithMsBuild(root);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
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
