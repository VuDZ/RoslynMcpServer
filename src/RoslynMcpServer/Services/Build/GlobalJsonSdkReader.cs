using System.Text.Json;

namespace RoslynMcpServer.Services.Build;

/// <summary>Reads the requested SDK version from <c>global.json</c>.</summary>
public static class GlobalJsonSdkReader
{
    public static string? TryGetPinnedSdkVersion(string startDirectory) =>
        TryReadRequestedVersion(FindGlobalJsonPath(startDirectory));

    internal static string? TryReadRequestedVersion(string? globalJsonPath)
    {
        if (globalJsonPath is null)
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(globalJsonPath);
            using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("sdk", out var sdk)
                || sdk.ValueKind != JsonValueKind.Object)
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    public static string? FindGlobalJsonPath(string startDirectory)
    {
        var dir = WorkspaceRootResolver.FindDirectoryContainingGlobalJson(startDirectory);
        return dir is null ? null : Path.Combine(dir, "global.json");
    }

}
