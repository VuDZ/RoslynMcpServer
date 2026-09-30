using System.Diagnostics;

namespace RoslynMcpServer.Tests.Support;

internal sealed class WpfTemporaryProjectFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "RoslynWpfWatching-" + Guid.NewGuid().ToString("N"));

    public string ProjectPath => Path.Combine(Root, "App", "WpfProbe.csproj");

    public string SolutionPath => Path.Combine(Root, "WpfProbe.slnx");

    public WpfTemporaryProjectFixture()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProjectPath)!);
        File.WriteAllText(SolutionPath, "<Solution><Project Path=\"App/WpfProbe.csproj\" /></Solution>");
        File.WriteAllText(ProjectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0-windows</TargetFramework>
                <UseWPF>true</UseWPF>
              </PropertyGroup>
              <Target Name="ObserveTemporaryAssemblyPass" BeforeTargets="GenerateTemporaryTargetAssembly">
                <Message Importance="High" Text="WPF_PROBE project=$(MSBuildProjectFullPath) startupDirectory=$(MSBuildStartupDirectory) projectDirectory=$(MSBuildProjectDirectory) temporary=$(AssemblyName)_wpftmp" />
              </Target>
            </Project>
            """);
        File.WriteAllText(Path.Combine(Root, "App", "LocalWidget.cs"), """
            namespace WpfProbe;
            public sealed class LocalWidget : System.Windows.Controls.UserControl { }
            """);
        File.WriteAllText(Path.Combine(Root, "App", "MainWindow.xaml"), """
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:local="clr-namespace:WpfProbe" x:Class="WpfProbe.MainWindow">
              <local:LocalWidget />
            </Window>
            """);
        File.WriteAllText(Path.Combine(Root, "App", "MainWindow.xaml.cs"), """
            namespace WpfProbe;
            public partial class MainWindow : System.Windows.Window
            {
                public MainWindow() { InitializeComponent(); }
            }
            """);
    }

    public Task<string> BuildAsync() => RunDotNetAsync("build", SolutionPath, "--no-incremental", "--nologo");

    public async Task<string> RunDotNetAsync(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3));
        }
        catch
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        var text = await output + await error;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet {string.Join(' ', arguments)} failed: {text}");
        }
        return text;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Build-host cleanup may briefly keep an intermediate file open.
        }
    }
}
