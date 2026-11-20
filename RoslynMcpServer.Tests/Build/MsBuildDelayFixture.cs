using System.Security;

namespace RoslynMcpServer.Tests.Build;

internal static class MsBuildDelayFixture
{
    internal static string Exec(int seconds)
    {
        // CI and restricted hosts may omit system tools from PATH or disable ICMP.
        var windowsPowerShell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var command = OperatingSystem.IsWindows()
            ? $"\"{windowsPowerShell}\" -NoLogo -NoProfile -NonInteractive -Command Start-Sleep -Seconds {seconds}"
            : $"sleep {seconds}";
        return $"""<Exec Command="{SecurityElement.Escape(command)}" />""";
    }

    internal static string WriteHangProject(string root)
    {
        var csproj = Path.Combine(root, "Hang.csproj");
        File.WriteAllText(csproj, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <Target Name="Hang">
                {Exec(60)}
              </Target>
            </Project>
            """);
        return csproj;
    }
}
