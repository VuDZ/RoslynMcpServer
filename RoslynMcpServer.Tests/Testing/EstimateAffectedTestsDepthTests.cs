using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Testing.Baseline.Models;
using RoslynMcpServer.Tests.Testing.Snapshots;
using RoslynMcpServer.Tools;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcpServer.Tests.Testing;

[Trait("Category", TestCategories.TiaIntegration)]
public sealed class EstimateAffectedTestsDepthTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Estimate_displays_all_stage_sizes_and_only_the_requested_filter()
    {
        await using var fixture = new Fixture();
        await fixture.LoadAsync();
        await fixture.ChangeValueAsync("A");
        await fixture.EditTestsAsync();
        await fixture.BuildAsync();

        foreach (var depth in new[] { "changed", "direct", "di", "all" })
        {
            var response = await fixture.Tools.EstimateAffectedTests(depth: depth, includeDetails: true);

            output.WriteLine(response);
            Assert.Contains($"State: selected depth {depth}.", response, StringComparison.Ordinal);
            Assert.Contains("2 changed types", response, StringComparison.Ordinal);
            Assert.Contains("Stages (classes): changed=1, direct=2, di=3, all=4.", response, StringComparison.Ordinal);
            var filter = Assert.Single(response.Split(Environment.NewLine), line => line.StartsWith("Run: ", StringComparison.Ordinal));
            Assert.Contains("FullyQualifiedName~Cases.Edited.", filter, StringComparison.Ordinal);
            Assert.Equal(depth != "changed", filter.Contains("FullyQualifiedName~Cases.ReferencesA.", StringComparison.Ordinal));
            Assert.Equal(depth is "di" or "all", filter.Contains("FullyQualifiedName~Cases.ScansWorkers.", StringComparison.Ordinal));
            Assert.Equal(depth == "all", filter.Contains("FullyQualifiedName~Cases.LoadsByName.", StringComparison.Ordinal));
            Assert.DoesNotContain("Cases.Unrelated", response, StringComparison.Ordinal);
            Assert.Contains("Changed types:", response, StringComparison.Ordinal);
            Assert.Contains("Lib.A, Lib", response, StringComparison.Ordinal);
            Assert.Contains("Cases.Edited, Tests", response, StringComparison.Ordinal);
            Assert.Contains("changed (1): Cases.Edited", response, StringComparison.Ordinal);
            Assert.Contains("Cases.ReferencesA", response, StringComparison.Ordinal);
            Assert.Contains("Cases.ScansWorkers", response, StringComparison.Ordinal);
            Assert.Contains("Cases.LoadsByName", response, StringComparison.Ordinal);
            Assert.Empty(fixture.Commands);
        }
    }

    [Fact]
    public async Task External_save_then_build_narrows_on_the_first_estimate_without_rewriting_the_source()
    {
        await using var fixture = new Fixture();
        await fixture.LoadAsync();
        var path = Path.Combine(fixture.Root, "Lib", "A.cs");
        var original = await File.ReadAllTextAsync(path);
        var first = original.Replace("Run() => 1", "Run() => 10", StringComparison.Ordinal);
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        await File.WriteAllTextAsync(path, first, encoding);
        var timestamp = File.GetLastWriteTimeUtc(path);
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.True((await fixture.Manager.WaitForDirtySourceAsync(path, TimeSpan.FromSeconds(15))).Delivered);
        await fixture.BuildAsync();

        var response = await fixture.Tools.EstimateAffectedTests(depth: "direct");

        output.WriteLine(response);
        Assert.Contains("Stages (classes): changed=0, direct=1, di=2, all=3.", response, StringComparison.Ordinal);
        Assert.DoesNotContain("whole project", response, StringComparison.Ordinal);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));

        var second = original.Replace("Run() => 1", "Run() => 20", StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, second, encoding);
        Assert.True((await fixture.Manager.WaitForDirtySourceAsync(path, TimeSpan.FromSeconds(15))).Delivered);

        AssertNotRebuilt(await fixture.Tools.EstimateAffectedTests(depth: "direct"));
        Assert.Equal(second, await File.ReadAllTextAsync(path));
        Assert.Empty(fixture.Commands);
    }

    [Fact]
    public async Task Unknown_depth_returns_the_legal_values_even_without_a_loaded_workspace()
    {
        var tools = EstimateAffectedTestsTests.Tools(SolutionManagerTestFactory.Create());

        var response = await tools.EstimateAffectedTests(depth: "wide", run: true);

        Assert.Contains("Invalid depth", response, StringComparison.Ordinal);
        Assert.Contains("changed, direct, di, all", response, StringComparison.Ordinal);
        Assert.DoesNotContain("No workspace loaded", response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Depth_names_ignore_case()
    {
        var tools = EstimateAffectedTestsTests.Tools(SolutionManagerTestFactory.Create());

        var response = await tools.EstimateAffectedTests(depth: "Direct");

        Assert.DoesNotContain("Invalid depth", response, StringComparison.Ordinal);
        Assert.Contains("State: selected depth direct.", response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Edit_during_the_run_build_keeps_the_project_whole_without_asking_for_another_build()
    {
        await using var fixture = new Fixture();
        await fixture.LoadAsync();
        await fixture.ChangeValueAsync("A");
        fixture.AfterBuild = () => fixture.TouchLibrary("A");

        var response = await fixture.Tools.EstimateAffectedTests(run: true);

        output.WriteLine(response);
        Assert.Contains("Outcome: passed", response, StringComparison.Ordinal);
        Assert.Contains("not newer than the last edit", response, StringComparison.Ordinal);
        Assert.Contains("changed during the build", response, StringComparison.Ordinal);
        Assert.DoesNotContain("build and call again", response, StringComparison.Ordinal);
        fixture.AssertCommands(string.Empty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Narrowing_failure_after_the_build_runs_the_whole_project_and_names_the_error(bool directTestEdit)
    {
        await using var fixture = new Fixture();
        await fixture.LoadAsync();
        if (directTestEdit)
        {
            await fixture.EditTestsAsync();
            var estimate = await fixture.Tools.EstimateAffectedTests();
            Assert.Contains("with filter `FullyQualifiedName~Cases.Edited", estimate, StringComparison.Ordinal);
        }
        else
        {
            await fixture.ChangeValueAsync("A");
        }

        fixture.AfterBuild = () => fixture.Manager.Baseline.Assemblies.BeforePairForTests =
            _ => throw new InvalidOperationException("Injected narrowing failure.");

        var response = await fixture.Tools.EstimateAffectedTests(includeDetails: true, run: true);

        output.WriteLine(response);
        Assert.Contains("Outcome: passed", response, StringComparison.Ordinal);
        Assert.Contains("whole project", response, StringComparison.Ordinal);
        Assert.Contains("Injected narrowing failure.", response, StringComparison.Ordinal);
        Assert.Equal(["Edited", "LoadsByName", "ReferencesA", "ScansWorkers", "Unrelated", "UsesLimit"], fixture.Markers());
        fixture.AssertCommands(string.Empty);
        Assert.Contains("Runtime: Tests: Cases.Unrelated.Runs", response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_precedes_narrowing_and_direct_executes_fewer_classes_than_all_without_rebuilding_twice()
    {
        await using var fixture = new Fixture();
        await fixture.LoadAsync();
        await fixture.ChangeValueAsync("A");

        var unbuilt = await fixture.Tools.EstimateAffectedTests();
        AssertNotRebuilt(unbuilt);
        Assert.Empty(fixture.Commands);

        var direct = await fixture.Tools.EstimateAffectedTests(depth: "direct", run: true);
        output.WriteLine(direct);
        Assert.Contains("Test execution: sequential (limit 1 project).", direct, StringComparison.Ordinal);
        Assert.Contains("Outcome: passed", direct, StringComparison.Ordinal);
        Assert.Contains("Stages (classes): changed=0, direct=1, di=2, all=3.", direct, StringComparison.Ordinal);
        Assert.Equal(["ReferencesA"], fixture.Markers());
        Assert.Equal("10", File.ReadAllText(fixture.MarkerPath("ReferencesA")));
        fixture.AssertCommands("FullyQualifiedName~Cases.ReferencesA.");

        fixture.ClearRun();
        var all = await fixture.Tools.EstimateAffectedTests(run: true);
        output.WriteLine(all);
        Assert.Contains("State: selected depth all.", all, StringComparison.Ordinal);
        Assert.Contains("Outcome: passed", all, StringComparison.Ordinal);
        Assert.Equal(["LoadsByName", "ReferencesA", "ScansWorkers"], fixture.Markers());
        fixture.AssertCommands("FullyQualifiedName~Cases.LoadsByName.");

        fixture.ClearRun();
        await fixture.ChangeValueAsync("B");
        AssertNotRebuilt(await fixture.Tools.EstimateAffectedTests());
        Assert.Empty(fixture.Commands);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task All_stages_empty_builds_without_starting_a_test_and_explains_why(bool commentOnly)
    {
        await using var fixture = new Fixture(broadTests: false);
        await fixture.LoadAsync();
        if (commentOnly)
        {
            await fixture.EditLibraryAsync("A", source => source + "\n// New comment.");
        }
        else
        {
            await fixture.ChangeValueAsync("B");
        }

        var response = await fixture.Tools.EstimateAffectedTests(run: true);

        output.WriteLine(response);
        Assert.Contains("Outcome: not run", response, StringComparison.Ordinal);
        Assert.DoesNotContain("Outcome: passed", response, StringComparison.Ordinal);
        Assert.Contains("Stages (classes): changed=0, direct=0, di=0, all=0.", response, StringComparison.Ordinal);
        if (commentOnly)
        {
            Assert.Contains("0 changed types", response, StringComparison.Ordinal);
            Assert.Contains("Compiled code did not change; no tests selected.", response, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("1 changed type identified", response, StringComparison.Ordinal);
            Assert.Contains("No test class reaches", response, StringComparison.Ordinal);
            Assert.Contains("Lib.B, Lib", response, StringComparison.Ordinal);
            Assert.Contains("names in strings or configuration", response, StringComparison.Ordinal);
            Assert.DoesNotContain("Compiled code did not change", response, StringComparison.Ordinal);
        }
        fixture.AssertCommands();
        Assert.Empty(fixture.Markers());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Empty_selected_stage_leaves_the_wider_stages_available_without_a_test_process(int maximum)
    {
        await using var fixture = new Fixture();
        await fixture.LoadAsync();
        await fixture.ChangeValueAsync("A");

        var response = await fixture.Tools.EstimateAffectedTests(depth: "changed", run: true, maxParallelProjects: maximum);

        output.WriteLine(response);
        Assert.Contains("Test execution: no test projects to run.", response, StringComparison.Ordinal);
        Assert.Contains("Outcome: not run", response, StringComparison.Ordinal);
        Assert.Contains("No test classes at depth changed", response, StringComparison.Ordinal);
        Assert.Contains("Stages (classes): changed=0, direct=1, di=2, all=3.", response, StringComparison.Ordinal);
        fixture.AssertCommands();
        Assert.Empty(fixture.Markers());
    }

    [Fact]
    public async Task Const_edit_runs_the_test_whose_compiled_code_contains_the_new_constant()
    {
        await using var fixture = new Fixture();
        await fixture.LoadAsync();
        await fixture.EditLibraryAsync("A", source => source.Replace("Max = 1", "Max = 10", StringComparison.Ordinal));

        var response = await fixture.Tools.EstimateAffectedTests(depth: "changed", run: true);

        output.WriteLine(response);
        Assert.Contains("Outcome: passed", response, StringComparison.Ordinal);
        Assert.Equal(["UsesLimit"], fixture.Markers());
        Assert.Equal("10", File.ReadAllText(fixture.MarkerPath("UsesLimit")));
        fixture.AssertCommands("FullyQualifiedName~Cases.UsesLimit.");
    }

    [Fact]
    public async Task Build_failure_has_no_stage_report_or_test_process()
    {
        await using var fixture = new Fixture();
        await fixture.LoadAsync();
        await fixture.EditLibraryAsync("A", source => source.Replace("Run() => 1", "Run() => Missing", StringComparison.Ordinal));

        var response = await fixture.Tools.EstimateAffectedTests(run: true);

        output.WriteLine(response);
        Assert.Contains("Outcome: build failed", response, StringComparison.Ordinal);
        Assert.Contains("CS0103", response, StringComparison.Ordinal);
        Assert.DoesNotContain("Stages (classes):", response, StringComparison.Ordinal);
        Assert.DoesNotContain("FullyQualifiedName~", response, StringComparison.Ordinal);
        fixture.AssertCommands();
        Assert.Empty(fixture.Markers());
    }

    [Fact]
    public async Task Rebuilding_before_the_first_snapshot_read_runs_the_whole_project_with_the_unknown_baseline_reason()
    {
        await using var fixture = new Fixture();
        using var gate = new AssemblyReadGate(Path.Combine(fixture.Root, "Lib", "bin", "Release", "net10.0", "Lib.dll"));
        fixture.Manager.Baseline.Assemblies.BeforeFirstReadForTests = gate.WaitAsync;
        try
        {
            await fixture.LoadAsync(waitForAssemblies: false);
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(30));
            await fixture.ChangeValueAsync("A");
            fixture.AfterBuild = gate.Release;

            var response = await fixture.Tools.EstimateAffectedTests(run: true);

            output.WriteLine(response);
            Assert.Contains("whole project", response, StringComparison.Ordinal);
            Assert.Contains("before the edit is unknown", response, StringComparison.Ordinal);
            Assert.Contains("Outcome: passed", response, StringComparison.Ordinal);
            Assert.Equal(["Edited", "LoadsByName", "ReferencesA", "ScansWorkers", "Unrelated", "UsesLimit"], fixture.Markers());
            fixture.AssertCommands(string.Empty);
        }
        finally
        {
            gate.Release();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Comment_edit_preserves_a_changed_test_and_keeps_the_project_whole_for_a_dual_role_input(bool additionalFile)
    {
        await using var fixture = new Fixture(broadTests: false, additionalFile);
        await fixture.LoadAsync();
        await fixture.EditLibraryAsync("A", source => source + "\n// New comment.");
        await fixture.EditTestsAsync();

        var response = await fixture.Tools.EstimateAffectedTests(run: true, depth: "changed");

        output.WriteLine(response);
        Assert.Contains("Outcome: passed", response, StringComparison.Ordinal);
        if (additionalFile)
        {
            Assert.Contains("whole project", response, StringComparison.Ordinal);
            Assert.Contains("Non-C# input changed:", response, StringComparison.Ordinal);
            Assert.Equal(["Edited", "ReferencesA", "Unrelated", "UsesLimit"], fixture.Markers());
            fixture.AssertCommands(string.Empty);
        }
        else
        {
            Assert.Equal(["Edited"], fixture.Markers());
            fixture.AssertCommands("FullyQualifiedName~Cases.Edited.");
        }
    }

    private static void AssertNotRebuilt(string response)
    {
        Assert.Contains("whole project", response, StringComparison.Ordinal);
        Assert.Contains("not newer than the last edit", response, StringComparison.Ordinal);
        Assert.Contains("build and call again", response, StringComparison.Ordinal);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("RoslynMcpImpactDepth-").FullName;
        internal SolutionManager Manager { get; } = SolutionManagerTestFactory.Create();
        internal List<string> Commands { get; } = [];
        internal Action? AfterBuild { get; set; }
        internal TestTools Tools => new(Manager, NullLogger<TestTools>.Instance, RunCommandAsync);
        private string SolutionPath => Path.Combine(Root, "Depth.slnx");

        internal Fixture(bool broadTests = true, bool additionalFile = false)
        {
            Directory.CreateDirectory(Path.Combine(Root, "Lib"));
            Directory.CreateDirectory(Path.Combine(Root, "Tests"));
            WriteProject("Lib", additionalFile ? "<AdditionalFiles Include=\"A.cs\" />" : "");
            WriteProject("Tests", """
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
                <PackageReference Include="xunit" Version="2.9.3" />
                <PackageReference Include="xunit.runner.visualstudio" Version="3.1.0" />
                <ProjectReference Include="../Lib/Lib.csproj" />
                """);
            WriteBaseline(LibrarySource("A"), """
                namespace Lib;
                public interface IWorker { }
                public class A : IWorker { public int Run() => 1; }
                public static class Limits { public const int Max = 1; }
                public static class Registry { public static void Scan(System.Reflection.Assembly assembly) { } }
                public static class Loader { public static System.Reflection.Assembly Load(string name) => System.Reflection.Assembly.Load(name); }
                """);
            WriteBaseline(LibrarySource("B"), "namespace Lib; public class B { public int Run() => 1; }");
            WriteBaseline(Path.Combine(Root, "Tests", "Cases.cs"), $$"""
                using System;
                using System.IO;
                using Xunit;
                namespace Cases;
                public class ReferencesA { [Fact] public void Runs() => Mark.Write(nameof(ReferencesA), new Lib.A().Run()); }
                public class UsesLimit { [Fact] public void Runs() => Mark.Write(nameof(UsesLimit), Lib.Limits.Max); }
                public class Unrelated { [Fact] public void Runs() => Mark.Write(nameof(Unrelated)); }
                public class Edited { [Fact] public void Runs() => Mark.Write(nameof(Edited), 1); }
                internal static class Mark
                {
                    internal static void Write(string name, int value = 1) =>
                        File.WriteAllText(Path.Combine({{JsonSerializer.Serialize(Root)}}, name + ".seen"), value.ToString());
                }
                """ + (broadTests ? """

                public class ScansWorkers
                {
                    [Fact] public void Runs()
                    {
                        Lib.Registry.Scan(typeof(Lib.IWorker).Assembly);
                        Mark.Write(nameof(ScansWorkers));
                    }
                }
                public class LoadsByName
                {
                    [Fact] public void Runs()
                    {
                        GC.KeepAlive(typeof(Lib.Loader));
                        Mark.Write(nameof(LoadsByName));
                    }
                }
                """ : string.Empty));
            File.WriteAllText(SolutionPath,
                "<Solution><Project Path=\"Lib/Lib.csproj\" /><Project Path=\"Tests/Tests.csproj\" /></Solution>");
        }

        public async ValueTask DisposeAsync()
        {
            await Manager.ClearWorkspaceAsync();
            await Manager.Baseline.CaptureTask.WaitAsync(TimeSpan.FromSeconds(30));
            await Manager.Baseline.Assemblies.FirstReadTask.WaitAsync(TimeSpan.FromSeconds(30));
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // MSBuild can still be releasing the temporary fixture directory.
            }
        }

        internal async Task LoadAsync(bool waitForAssemblies = true)
        {
            MsBuildBootstrapper.Register();
            await BuildAsync();
            await Manager.LoadAsync(SolutionPath, CancellationToken.None, configuration: "Release", platform: "Any CPU");
            await Manager.Baseline.CaptureTask.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.All(Manager.Baseline.GetSnapshot().Documents,
                document => Assert.Equal(BaselineDocumentStatus.Known, document.Status));
            if (waitForAssemblies)
            {
                await Manager.Baseline.Assemblies.FirstReadTask.WaitAsync(TimeSpan.FromSeconds(30));
            }
        }

        internal async Task BuildAsync()
        {
            var result = await DotNetCliRunner.RunWithMetadataAsync(
                $"build \"{SolutionPath}\" -c Release", Root, CancellationToken.None, TimeSpan.FromMinutes(2));
            Assert.True(result.ExitCode == 0, result.CombinedOutput);
        }

        internal Task ChangeValueAsync(string name) =>
            EditLibraryAsync(name, source => source.Replace("Run() => 1", "Run() => 10", StringComparison.Ordinal));

        internal Task EditLibraryAsync(string name, Func<string, string> edit) => EditAsync(LibrarySource(name), edit);

        internal Task EditTestsAsync() => EditAsync(Path.Combine(Root, "Tests", "Cases.cs"),
            source => source.Replace("Write(nameof(Edited), 1)", "Write(nameof(Edited), 2)", StringComparison.Ordinal));

        /// <summary>Dates the source after the build that has just compiled it, as an edit during that build would.</summary>
        internal void TouchLibrary(string name) => File.SetLastWriteTimeUtc(LibrarySource(name), DateTime.UtcNow.AddMinutes(1));

        internal string MarkerPath(string name) => Path.Combine(Root, name + ".seen");

        internal string[] Markers() => Directory.GetFiles(Root, "*.seen")
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Order(StringComparer.Ordinal).ToArray();

        internal void ClearRun()
        {
            foreach (var marker in Directory.GetFiles(Root, "*.seen"))
            {
                File.Delete(marker);
            }
            Commands.Clear();
        }

        internal void AssertCommands(string? filter = null)
        {
            Assert.Equal(filter is null ? 1 : 2, Commands.Count);
            Assert.StartsWith("build ", Commands[0], StringComparison.Ordinal);
            if (filter is not null)
            {
                var test = Commands[1];
                Assert.StartsWith("test ", test, StringComparison.Ordinal);
                Assert.Contains(" --no-build --no-restore", test, StringComparison.Ordinal);
                if (filter.Length > 0)
                {
                    Assert.Contains(filter, test, StringComparison.Ordinal);
                }
                else
                {
                    Assert.DoesNotContain(" --filter ", test, StringComparison.Ordinal);
                }
            }
        }

        private string LibrarySource(string name) => Path.Combine(Root, "Lib", name + ".cs");

        private void WriteProject(string name, string items)
        {
            var isTestProject = name == "Tests" ? "<IsTestProject>true</IsTestProject>" : string.Empty;
            File.WriteAllText(Path.Combine(Root, name, name + ".csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
                    <GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>
                    {isTestProject}
                  </PropertyGroup>
                  <ItemGroup>{items}</ItemGroup>
                </Project>
                """);
        }

        private static void WriteBaseline(string path, string source)
        {
            File.WriteAllText(path, source);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-1));
        }

        private async Task EditAsync(string path, Func<string, string> edit)
        {
            var result = await new EditingTools(NullLogger<EditingTools>.Instance, Manager)
                .WriteFile(path, edit(File.ReadAllText(path)));
            Assert.Contains("Successfully wrote", result, StringComparison.Ordinal);
        }

        private async Task<DotNetCliRunner.RunResult> RunCommandAsync(
            string arguments,
            string? workingDirectory,
            TimeSpan? timeout,
            int? previousExit,
            CancellationToken cancellationToken)
        {
            Commands.Add(arguments);
            var result = await DotNetCliRunner.RunWithMetadataAsync(arguments, workingDirectory, cancellationToken, timeout);
            if (arguments.StartsWith("build ", StringComparison.Ordinal))
            {
                AfterBuild?.Invoke();
            }
            return result;
        }
    }
}
