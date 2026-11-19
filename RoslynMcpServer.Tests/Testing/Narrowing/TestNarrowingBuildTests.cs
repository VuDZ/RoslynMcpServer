using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Testing.Narrowing;
using RoslynMcpServer.Services.Testing.Narrowing.Models;
using RoslynMcpServer.Services.Testing.Snapshots.Models;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.Testing.Narrowing;

/// <summary>
/// Narrowing through a real load, real builds and the snapshot of the session: edit, build, narrow; edit
/// again without a build; build again.
/// </summary>
[Collection("BaselineObservationGap")]
public sealed class TestNarrowingBuildTests : IAsyncDisposable
{
    private string SolutionPath => Path.Combine(_root, "Narrow.slnx");

    public TestNarrowingBuildTests()
    {
        WriteProject("Lib", "<TargetFramework>net10.0</TargetFramework>", "");
        File.WriteAllText(SourcePath("Lib", "A.cs"), "namespace Lib; public class A { public int Run() => 1; }");
        File.WriteAllText(SourcePath("Lib", "B.cs"), "namespace Lib; public class B { public int Other() => 2; }");
        WriteProject(
            "Tests",
            "<TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject>",
            """
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
            <PackageReference Include="xunit" Version="2.9.3" />
            <PackageReference Include="xunit.runner.visualstudio" Version="3.1.0" />
            <ProjectReference Include="../Lib/Lib.csproj" />
            """);
        File.WriteAllText(SourcePath("Tests", "Cases.cs"), """
            using System;
            using Xunit;
            namespace Tests;
            public class ReferencesA { [Fact] public void Runs() => GC.KeepAlive(new Lib.A().Run()); }
            public class ReferencesB { [Fact] public void Runs() => GC.KeepAlive(new Lib.B().Other()); }
            public class Unrelated { [Fact] public void Runs() => GC.KeepAlive(1); }
            """);
        File.WriteAllText(SolutionPath, "<Solution><Project Path=\"Lib/Lib.csproj\" /><Project Path=\"Tests/Tests.csproj\" /></Solution>");
    }

    public async ValueTask DisposeAsync()
    {
        await _manager.ClearWorkspaceAsync();
        await _manager.Baseline.CaptureTask.WaitAsync(Timeout);
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A build server may release the output directory just after the test.
        }
    }

    [Fact]
    public async Task Edit_and_build_narrow_a_second_edit_without_a_build_keeps_the_project_whole_and_a_build_narrows_both()
    {
        await BuildAsync();
        await _manager.LoadAsync(SolutionPath, CancellationToken.None, configuration: "Release", platform: "Any CPU");
        await _manager.Baseline.CaptureTask.WaitAsync(Timeout);
        await _manager.Baseline.Assemblies.FirstReadTask.WaitAsync(Timeout);
        Assert.All(_manager.Baseline.Assemblies.PeekBefore()!.Entries, entry => Assert.Equal(AssemblySnapshotState.Read, entry.State));

        await WriteAsync(SourcePath("Lib", "A.cs"), "namespace Lib; public class A { public int Run() => 10; }");
        await BuildAsync();
        var first = await NarrowAsync();
        Assert.Equal(TestNarrowingOutcome.Narrowed, first.Outcome);
        Assert.Equal([], NarrowingAssert.Names(first, TestClassStageLevel.Changed));
        Assert.Equal(["Tests.ReferencesA"], NarrowingAssert.Names(first, TestClassStageLevel.References));
        Assert.Equal(["Tests.ReferencesA"], NarrowingAssert.Names(first, TestClassStageLevel.DiscoveryWithLoads));
        Assert.Equal("FullyQualifiedName~Tests.ReferencesA.", first.Stage(TestClassStageLevel.References).Filter);

        await WriteAsync(SourcePath("Lib", "B.cs"), "namespace Lib; public class B { public int Other() => 20; }");
        var second = await NarrowAsync();
        Assert.Equal(TestNarrowingOutcome.WholeProject, second.Outcome);
        Assert.Equal(TestNarrowingWholeKind.NotRebuilt, second.Whole?.Kind);

        await BuildAsync();
        var third = await NarrowAsync();
        Assert.Equal(TestNarrowingOutcome.Narrowed, third.Outcome);
        Assert.Equal(["Tests.ReferencesA", "Tests.ReferencesB"], NarrowingAssert.Names(third, TestClassStageLevel.References));
        Assert.Equal(["Tests.ReferencesA", "Tests.ReferencesB"], NarrowingAssert.Names(third, TestClassStageLevel.DiscoveryWithLoads));
    }

    private string SourcePath(string project, string file) => Path.Combine(_root, project, file);

    private void WriteProject(string name, string properties, string items)
    {
        Directory.CreateDirectory(Path.Combine(_root, name));
        File.WriteAllText(Path.Combine(_root, name, name + ".csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>{properties}</PropertyGroup>
              <ItemGroup>{items}</ItemGroup>
            </Project>
            """);
    }

    private async Task BuildAsync()
    {
        MsBuildBootstrapper.Register();
        var build = await DotNetCliRunner.RunWithMetadataAsync(
            "build \"" + SolutionPath + "\" -c Release", _root, CancellationToken.None, TimeSpan.FromMinutes(3));
        Assert.True(build.ExitCode == 0, "The build failed: " + build.CombinedOutput);
    }

    private async Task WriteAsync(string path, string source)
    {
        var result = await new EditingTools(NullLogger<EditingTools>.Instance, _manager).WriteFile(path, source);
        Assert.Contains("Successfully wrote", result, StringComparison.Ordinal);
    }

    /// <summary>What <c>estimate_affected_tests</c> computes for epoch 1, then narrowing of the test project.</summary>
    private async Task<TestProjectNarrowing> NarrowAsync()
    {
        var solution = (await _manager.GetPublishedSolutionAfterDiskSyncAsync(CancellationToken.None))!;
        var estimate = await TestImpactEstimator.EstimateAsync(
            solution,
            _manager.DiskWatcherSession!.InputMap!,
            _manager.Baseline.GetSnapshot(),
            _manager.GetLoadedWorkspacePath(),
            _manager.ProjectGraphStale,
            _manager.ProjectGraphStaleFromGraphFile,
            CancellationToken.None);
        var result = await TestClassNarrower.NarrowAsync(solution, estimate, _manager.Baseline.Assemblies, CancellationToken.None);
        var tests = solution.Projects.Single(project => project.Name == "Tests").Id;
        var project = Assert.Single(result.Projects);
        Assert.Equal(tests, project.ProjectId);
        NarrowingAssert.StagesAreNested(project);
        return project;
    }

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly string _root = Directory.CreateTempSubdirectory("RoslynMcpNarrowingBuild-").FullName;
    private readonly SolutionManager _manager = SolutionManagerTestFactory.Create();
}
