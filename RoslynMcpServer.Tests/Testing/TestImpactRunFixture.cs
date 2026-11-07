using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Testing.Baseline.Models;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

internal sealed class TestImpactRunFixture : IAsyncDisposable
{
    internal string Root { get; } = Directory.CreateTempSubdirectory("RoslynMcpImpactRun-").FullName;
    internal SolutionManager Manager { get; }
    internal string SolutionPath => Path.Combine(Root, "Run.slnx");
    internal TestTools Tools => new(Manager, NullLogger<TestTools>.Instance);

    internal TestImpactRunFixture(SolutionManager? manager = null)
    {
        Manager = manager ?? SolutionManagerTestFactory.Create();
        foreach (var name in new[] { "First", "Second" })
        {
            Directory.CreateDirectory(Path.Combine(Root, name));
            File.WriteAllText(ProjectPath(name), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
                    <PackageReference Include="xunit" Version="2.9.3" />
                    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.0" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(SourcePath(name), Source(name));
            File.SetLastWriteTimeUtc(SourcePath(name), DateTime.UtcNow.AddDays(-1));
        }
        File.WriteAllText(SolutionPath,
            "<Solution><Project Path=\"First/First.csproj\" /><Project Path=\"Second/Second.csproj\" /></Solution>");
    }

    public async ValueTask DisposeAsync()
    {
        await Manager.ClearWorkspaceAsync();
        await Manager.Baseline.CaptureTask.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A killed test host may release its temporary output just after cancellation.
        }
    }

    internal string ProjectPath(string name) => Path.Combine(Root, name, name + ".csproj");
    internal string SourcePath(string name) => Path.Combine(Root, name, "Cases.cs");
    internal string MarkerPath(string name) => Path.Combine(Root, name + ".seen");
    internal string ProcessPath => Path.Combine(Root, "processes.txt");

    internal async Task LoadAsync()
    {
        MsBuildBootstrapper.Register();
        var build = await DotNetCliRunner.RunWithMetadataAsync(
            "build \"" + SolutionPath + "\" -c Release", Root, CancellationToken.None, TimeSpan.FromMinutes(2));
        Assert.Equal(0, build.ExitCode);
        await Manager.LoadAsync(SolutionPath, CancellationToken.None, configuration: "Release", platform: "Any CPU");
        await Manager.Baseline.CaptureTask.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.All(Manager.Baseline.GetSnapshot().Documents,
            document => Assert.Equal(BaselineDocumentStatus.Known, document.Status));
    }

    internal async Task ChangeAsync(string name, string body)
    {
        var source = Source(name).Replace("/* first */ Assert.True(true);", body, StringComparison.Ordinal);
        var result = await new EditingTools(NullLogger<EditingTools>.Instance, Manager).WriteFile(SourcePath(name), source);
        Assert.Contains("Successfully wrote", result, StringComparison.Ordinal);
    }

    internal async Task RestoreAsync(string name)
    {
        var result = await new EditingTools(NullLogger<EditingTools>.Instance, Manager).WriteFile(SourcePath(name), Source(name));
        Assert.Contains("Successfully wrote", result, StringComparison.Ordinal);
    }

    internal async Task ExpandProjectAsync(string name)
    {
        var source = Source(name).Replace("Value() => 1", "Value() => 2", StringComparison.Ordinal);
        var result = await new EditingTools(NullLogger<EditingTools>.Instance, Manager).WriteFile(SourcePath(name), source);
        Assert.Contains("Successfully wrote", result, StringComparison.Ordinal);
    }

    internal string HangingBody() => $$"""
        using var child = Process.Start(new ProcessStartInfo {
            FileName = OperatingSystem.IsWindows() ? "ping" : "sleep",
            Arguments = OperatingSystem.IsWindows() ? "-n 300 127.0.0.1" : "300",
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
        })!;
        File.WriteAllText({{JsonSerializer.Serialize(ProcessPath)}}, Environment.ProcessId + "," + child.Id);
        Thread.Sleep(300000);
        """;

    internal async Task<int[]> WaitForProcessesAsync(Task? operation = null)
    {
        var elapsed = Stopwatch.StartNew();
        while (!File.Exists(ProcessPath) && elapsed.Elapsed < TimeSpan.FromSeconds(45) && operation?.IsCompleted != true)
        {
            await Task.Delay(25);
        }
        Assert.True(File.Exists(ProcessPath), "The selected test did not start its process tree.");
        return (await File.ReadAllTextAsync(ProcessPath)).Split(',').Select(int.Parse).ToArray();
    }

    internal static async Task AssertStoppedAsync(IEnumerable<int> ids)
    {
        foreach (var id in ids)
        {
            var elapsed = Stopwatch.StartNew();
            while (IsAlive(id) && elapsed.Elapsed < TimeSpan.FromSeconds(10))
            {
                await Task.Delay(25);
            }
            Assert.False(IsAlive(id), "Cancelled process is still alive: " + id);
        }
    }

    internal static void Kill(IEnumerable<int> ids)
    {
        foreach (var id in ids)
        {
            try
            {
                using var process = Process.GetProcessById(id);
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (ArgumentException)
            {
            }
        }
    }

    private string Source(string name) => $$"""
        using System;
        using System.Diagnostics;
        using System.IO;
        using System.Threading;
        using Xunit;
        using Xunit.Abstractions;
        namespace Ns;
        public class Cases
        {
            public Cases(ITestOutputHelper output) => _output = output;

            [Theory]
            [InlineData(1)]
            [InlineData(2)]
            public void First(int value) { Seen("First" + value); /* first */ Assert.True(true); }
            [Fact] public void FirstExtra() { Seen("FirstExtra"); }
            [Fact] public void Other() { Seen("Other"); }
            private static void Seen(string name) => File.AppendAllText({{JsonSerializer.Serialize(MarkerPath(name))}}, name + "\n");

            private readonly ITestOutputHelper _output;
        }
        public static class Production { public static int Value() => 1; }
        """;

    private static bool IsAlive(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
