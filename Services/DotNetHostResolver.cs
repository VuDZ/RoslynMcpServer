namespace RoslynMcpServer.Services;

/// <summary>Resolves a <c>dotnet</c> host executable matching the MCP process bitness.</summary>
public static class DotNetHostResolver
{
    public static string ResolveDotNetExecutable()
    {
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            var fromEnv = Path.Combine(dotnetRoot.Trim(), OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(fromEnv))
            {
                return fromEnv;
            }
        }

        if (Environment.Is64BitProcess && OperatingSystem.IsWindows())
        {
            var programFiles = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "dotnet",
                "dotnet.exe");
            if (File.Exists(programFiles))
            {
                return programFiles;
            }
        }

        return OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
    }

    public static bool Is64BitHostPath(string dotnetPath) =>
        !dotnetPath.Contains("(x86)", StringComparison.OrdinalIgnoreCase);
}
