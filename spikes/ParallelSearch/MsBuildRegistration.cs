using System.Text.Json;
using Microsoft.Build.Locator;

namespace ParallelSearch;

internal static class MsBuildRegistration
{
    /// <summary>
    /// Registers the SDK pinned by the solution's <c>global.json</c> from the dotnet root of this process,
    /// so evaluation uses the same SDK as the solution's own build; otherwise MSBuildLocator's default.
    /// </summary>
    public static string Register(string solutionDirectory)
    {
        var pinned = TryReadPinnedSdk(solutionDirectory);
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT")
            ?? Path.GetDirectoryName(Environment.ProcessPath);
        if (pinned is not null && dotnetRoot is not null)
        {
            var sdk = Path.Combine(dotnetRoot, "sdk", pinned);
            if (File.Exists(Path.Combine(sdk, "Microsoft.Build.dll")))
            {
                MSBuildLocator.RegisterMSBuildPath(sdk);
                return sdk;
            }
        }

        return MSBuildLocator.RegisterDefaults().MSBuildPath;
    }

    private static string? TryReadPinnedSdk(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            var file = Path.Combine(current.FullName, "global.json");
            if (!File.Exists(file))
            {
                continue;
            }

            using var json = JsonDocument.Parse(File.ReadAllText(file));
            return json.RootElement.TryGetProperty("sdk", out var sdk) && sdk.TryGetProperty("version", out var version)
                ? version.GetString()
                : null;
        }

        return null;
    }
}
