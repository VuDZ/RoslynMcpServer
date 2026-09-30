namespace RoslynMcpServer.Services;

/// <summary>
/// Canonical physical paths. <see cref="Path.GetFullPath"/> collapses <c>.</c> and <c>..</c>
/// and does not resolve symlinks or other aliases; unproven correspondence stays a separate key.
/// Comparison follows the operating system.
/// </summary>
internal static class InputPathCanon
{
    public static StringComparer Comparer { get; } = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static StringComparison Comparison { get; } = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static string? TryCanonicalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
