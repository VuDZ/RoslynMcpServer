namespace RoslynMcpServer.Services.Build;

/// <summary>Resolves a <c>dotnet</c> host executable matching the MCP process bitness.</summary>
public static class DotNetHostResolver
{
    private static string ExecutableName => OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

    public static string ResolveDotNetExecutable()
    {
        var configuredHost = ResolveConfiguredHost(
            Environment.GetEnvironmentVariable("DOTNET_ROOT"),
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"));
        if (configuredHost is not null)
        {
            return configuredHost;
        }

        if (Environment.Is64BitProcess && OperatingSystem.IsWindows())
        {
            var programFiles = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "dotnet",
                ExecutableName);
            if (File.Exists(programFiles))
            {
                return programFiles;
            }
        }

        return ExecutableName;
    }

    internal static string? ResolveConfiguredHost(string? dotnetRoot, string? hostPath)
    {
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            var fromRoot = Path.Combine(dotnetRoot.Trim(), ExecutableName);
            if (IsUsableHost(fromRoot))
            {
                return fromRoot;
            }
        }

        // Locator can publish a host for a private installation. A host with only a runtime
        // can run the server but may need a workspace-specific SDK path to run SDK commands.
        var host = hostPath?.Trim();
        if (host is null || !IsUsableHost(host))
        {
            return null;
        }

        try
        {
            var target = File.ResolveLinkTarget(host, returnFinalTarget: true)?.FullName ?? host;
            var sdkRoot = Path.Combine(Path.GetDirectoryName(target)!, "sdk");
            return Directory.Exists(sdkRoot)
                && Directory.EnumerateDirectories(sdkRoot).Any(sdk => File.Exists(Path.Combine(sdk, "dotnet.dll")))
                ? target
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsUsableHost(string path) =>
        Path.IsPathFullyQualified(path)
        && string.Equals(
            Path.GetFileName(path),
            ExecutableName,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
        && (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess || Is64BitHostPath(path))
        && File.Exists(path);

    public static bool Is64BitHostPath(string dotnetPath) =>
        !dotnetPath.Contains("(x86)", StringComparison.OrdinalIgnoreCase);
}
