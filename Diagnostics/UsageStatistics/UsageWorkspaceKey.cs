using System.Globalization;
using System.Text;

namespace RoslynMcpServer.Diagnostics.UsageStatistics;

internal static class UsageWorkspaceKey
{
    internal static string? Create(string? path, string? configuration, string? platform, string? targetFramework)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var canonicalPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (OperatingSystem.IsWindows())
            {
                canonicalPath = canonicalPath.Replace('/', '\\').ToUpperInvariant();
            }

            var key = new StringBuilder();
            Append(key, canonicalPath);
            Append(key, Normalize(configuration));
            Append(key, Normalize(DotNetConfigurationArguments.NormalizePlatform(platform)));
            Append(key, Normalize(targetFramework));
            return key.ToString();
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static void Append(StringBuilder destination, string? value)
    {
        destination.Append(value?.Length.ToString(CultureInfo.InvariantCulture) ?? "-1");
        destination.Append(':');
        destination.Append(value);
    }
}
