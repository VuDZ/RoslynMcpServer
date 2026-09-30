using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

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
              <Target Name="PauseTemporaryAssemblyCompilation" BeforeTargets="CoreCompile"
                      Condition="'$(WpfProbeGatePipe)' != '' and $([System.String]::Copy('$(MSBuildProjectName)').EndsWith('_wpftmp'))">
                <Exec Command="powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File &quot;$(WpfProbeGateScript)&quot; -PipeName &quot;$(WpfProbeGatePipe)&quot; -ProjectPath &quot;$(MSBuildProjectFullPath)&quot;" />
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

    public async Task<string> BuildWithPausedTemporaryCompilationAsync(Func<string, Task> duringCompilation)
    {
        var gateDirectory = Path.Combine(Root, "obj", "build-gate");
        Directory.CreateDirectory(gateDirectory);
        var scriptPath = Path.Combine(gateDirectory, "pause.ps1");
        await File.WriteAllTextAsync(scriptPath, """
            param([string] $PipeName, [string] $ProjectPath)
            $ErrorActionPreference = 'Stop'
            $pipe = [System.IO.Pipes.NamedPipeClientStream]::new('.', $PipeName, [System.IO.Pipes.PipeDirection]::InOut)
            try {
                $pipe.Connect(60000)
                $writer = [System.IO.StreamWriter]::new($pipe, [System.Text.UTF8Encoding]::new($false))
                $writer.AutoFlush = $true
                $reader = [System.IO.StreamReader]::new($pipe)
                $writer.WriteLine($ProjectPath)
                if ($reader.ReadLine() -ne 'continue') { throw 'The WPF compilation gate was closed without release.' }
            } finally {
                $pipe.Dispose()
            }
            """);
        var pipeName = "RoslynWpfCompilation-" + Guid.NewGuid().ToString("N");
        using var pipe = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var connected = pipe.WaitForConnectionAsync();
        var build = RunDotNetAsync(
            "build", SolutionPath, "--no-incremental", "--nologo",
            "-p:WpfProbeGatePipe=" + pipeName, "-p:WpfProbeGateScript=" + scriptPath);
        try
        {
            // WPF copies this target into its temporary project and invokes CoreCompile there.
            // The pipe pins that actual compilation phase; a delayed Created callback cannot move
            // the edit past build completion. Gate files stay under obj, outside watched inputs.
            // https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationBuildTasks/Microsoft/Build/Tasks/Windows/GenerateTemporaryTargetAssembly.cs
            if (await Task.WhenAny(connected, build).WaitAsync(TimeSpan.FromMinutes(1)) == build)
            {
                throw new InvalidOperationException("The WPF build did not enter temporary compilation: " + await build);
            }
            await connected;
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            var temporaryProject = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromMinutes(1));
            await duringCompilation(temporaryProject
                ?? throw new InvalidOperationException("The WPF build closed the compilation gate."));
        }
        finally
        {
            try
            {
                if (pipe.IsConnected)
                {
                    using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true);
                    await writer.WriteLineAsync("continue");
                    await writer.FlushAsync();
                }
            }
            finally
            {
                await build;
            }
        }
        return await build;
    }

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
