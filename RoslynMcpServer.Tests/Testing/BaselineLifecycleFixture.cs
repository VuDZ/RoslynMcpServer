using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Tools;

namespace RoslynMcpServer.Tests.Testing;

internal sealed class BaselineLifecycleFixture : IAsyncDisposable
{
    internal string Root { get; }
    internal string ProjectPath { get; }
    internal string FirstPath { get; }
    internal string SecondPath { get; }
    internal SolutionManager Manager { get; }

    internal BaselineLifecycleFixture()
    {
        MsBuildBootstrapper.Register();
        Root = Path.Combine(Path.GetTempPath(), "RoslynMcpBaseline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        ProjectPath = CreateProject(Root, "App");
        FirstPath = Path.Combine(Root, "A.cs");
        SecondPath = Path.Combine(Root, "B.cs");
        Manager = SolutionManagerTestFactory.Create();
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
            // A BuildHost may release a temporary file just after workspace disposal.
        }
    }

    internal Task<Solution> LoadAsync() => Manager.LoadAsync(ProjectPath);

    internal async Task WriteThroughMcpAsync(string path, string source)
    {
        var tools = new EditingTools(NullLogger<EditingTools>.Instance, Manager);
        var result = await tools.WriteFile(path, source);
        Xunit.Assert.Contains("Successfully wrote", result, StringComparison.Ordinal);
    }

    internal void Deliver(string path)
    {
        var session = Manager.DiskWatcherSession;
        Xunit.Assert.NotNull(session);
        Manager.NotifyDiskWatcherChange(session,
            new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.GetDirectoryName(path)!, Path.GetFileName(path)));
    }

    internal string OtherProject() => CreateProject(Path.Combine(Root, "Other"), "Other");

    internal string LinkedSolution()
    {
        var shared = Path.Combine(Root, "Shared.cs");
        File.WriteAllText(shared, """
            class Shared
            {
            #if FIRST
                void First() {}
            #else
                void Second() {}
            #endif
            }
            """);
        CreateProject(Path.Combine(Root, "First"), "First", shared, "FIRST");
        CreateProject(Path.Combine(Root, "Second"), "Second", shared, "SECOND");
        var solution = Path.Combine(Root, "Linked.slnx");
        File.WriteAllText(solution, """
            <Solution>
              <Project Path="First/First.csproj" />
              <Project Path="Second/Second.csproj" />
            </Solution>
            """);
        return solution;
    }

    private static string CreateProject(string directory, string name, string? shared = null, string defines = "")
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "A.cs"), "partial class C { partial void Run(); }");
        File.WriteAllText(Path.Combine(directory, "B.cs"), "partial class C { partial void Run() {} }");
        var path = Path.Combine(directory, name + ".csproj");
        var linked = shared == null ? "" : "<Compile Include=\"" + System.Security.SecurityElement.Escape(shared) + "\" Link=\"Shared.cs\" />";
        File.WriteAllText(path, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <DefineConstants>{{defines}}</DefineConstants>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
                <GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>
                <ImplicitUsings>disable</ImplicitUsings>
              </PropertyGroup>
              <ItemGroup><Compile Include="A.cs" /><Compile Include="B.cs" />{{linked}}</ItemGroup>
            </Project>
            """);
        return path;
    }
}
