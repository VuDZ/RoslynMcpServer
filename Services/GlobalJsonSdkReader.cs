using System.Text.Json;

namespace RoslynMcpServer.Services;

/// <summary>Reads <c>global.json</c> SDK pin and maps it to an on-disk SDK folder.</summary>
public static class GlobalJsonSdkReader
{
    public static string? TryGetPinnedSdkVersion(string startDirectory)
    {
        var globalJsonPath = FindGlobalJsonPath(startDirectory);
        if (globalJsonPath is null)
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(globalJsonPath);
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("sdk", out var sdk))
            {
                return null;
            }

            if (sdk.TryGetProperty("version", out var versionProp)
                && versionProp.ValueKind == JsonValueKind.String)
            {
                var v = versionProp.GetString();
                return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            }

            if (sdk.TryGetProperty("versions", out var versions)
                && versions.ValueKind == JsonValueKind.Array
                && versions.GetArrayLength() > 0)
            {
                var first = versions[0];
                if (first.ValueKind == JsonValueKind.String)
                {
                    var v = first.GetString();
                    return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }

        return null;
    }

    public static string? TryResolveSdkDirectory(string startDirectory, bool prefer64Bit)
    {
        var pinned = TryGetPinnedSdkVersion(startDirectory);
        if (pinned is null)
        {
            return null;
        }

        foreach (var sdkRoot in GetSdkRootCandidates(prefer64Bit))
        {
            if (!Directory.Exists(sdkRoot))
            {
                continue;
            }

            var exact = Path.Combine(sdkRoot, pinned);
            if (File.Exists(Path.Combine(exact, "Microsoft.Build.dll")))
            {
                return exact;
            }

            var rollForwardPrefix = GetRollForwardPrefix(pinned);
            var best = Directory.GetDirectories(sdkRoot)
                .Where(d =>
                {
                    var name = Path.GetFileName(d);
                    return string.Equals(name, pinned, StringComparison.OrdinalIgnoreCase)
                           || (rollForwardPrefix is not null
                               && name.StartsWith(rollForwardPrefix, StringComparison.OrdinalIgnoreCase));
                })
                .Where(d => File.Exists(Path.Combine(d, "Microsoft.Build.dll")))
                .OrderByDescending(d => TryParseSdkFolderVersion(Path.GetFileName(d)))
                .FirstOrDefault();

            if (best is not null)
            {
                return best;
            }
        }

        return null;
    }

    public static string? FindGlobalJsonPath(string startDirectory)
    {
        var dir = WorkspaceRootResolver.FindDirectoryContainingGlobalJson(startDirectory);
        return dir is null ? null : Path.Combine(dir, "global.json");
    }

    private static IEnumerable<string> GetSdkRootCandidates(bool prefer64Bit)
    {
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            yield return Path.Combine(dotnetRoot.Trim(), "sdk");
        }

        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            yield return Path.Combine(programFiles, "dotnet", "sdk");

            if (!prefer64Bit)
            {
                var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                yield return Path.Combine(programFilesX86, "dotnet", "sdk");
            }
        }
        else
        {
            yield return "/usr/local/share/dotnet/sdk";
            yield return "/usr/share/dotnet/sdk";
        }
    }

    private static Version TryParseSdkFolderVersion(string folderName)
    {
        return Version.TryParse(folderName, out var v) ? v : new Version(0, 0);
    }

    private static string? GetRollForwardPrefix(string pinned)
    {
        var parts = pinned.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3)
        {
            return $"{parts[0]}.{parts[1]}.{parts[2]}.";
        }

        if (parts.Length == 2)
        {
            return $"{parts[0]}.{parts[1]}.";
        }

        return null;
    }
}
