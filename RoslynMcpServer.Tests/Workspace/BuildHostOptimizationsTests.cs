using System.Diagnostics;
using RoslynMcpServer.Services.Workspace;
using RoslynMcpServer.Services.Workspace.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

public sealed class BuildHostOptimizationsTests
{
    [Fact]
    public void Resolve_with_nothing_set_turns_all_optimizations_on_with_the_default_workers()
    {
        var settings = BuildHostOptimizations.Resolve(fileEnabled: null, fileWorkers: null, enabledValue: null, workersValue: null);

        Assert.Equal(new BuildHostOptimizationSettings(ReferencesFirst: true, EvaluateAheadWorkers: 4, SharedSdkCache: true), settings);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData(" FALSE ")]
    public void Resolve_environment_off_wins_over_the_file_and_turns_all_optimizations_off(string value)
    {
        var settings = BuildHostOptimizations.Resolve(fileEnabled: true, fileWorkers: 8, enabledValue: value, workersValue: "8");

        Assert.Equal(new BuildHostOptimizationSettings(ReferencesFirst: false, EvaluateAheadWorkers: 0, SharedSdkCache: false), settings);
    }

    [Fact]
    public void Resolve_environment_on_wins_over_the_file_off()
    {
        var settings = BuildHostOptimizations.Resolve(fileEnabled: false, fileWorkers: null, enabledValue: "1", workersValue: null);

        Assert.Equal(new BuildHostOptimizationSettings(ReferencesFirst: true, EvaluateAheadWorkers: 4, SharedSdkCache: true), settings);
    }

    [Fact]
    public void Resolve_file_off_turns_all_optimizations_off()
    {
        var settings = BuildHostOptimizations.Resolve(fileEnabled: false, fileWorkers: 8, enabledValue: null, workersValue: null);

        Assert.Equal(new BuildHostOptimizationSettings(ReferencesFirst: false, EvaluateAheadWorkers: 0, SharedSdkCache: false), settings);
    }

    [Theory]
    [InlineData(null, 2, 2)]
    [InlineData("6", 2, 6)]
    [InlineData("0", 2, 0)]
    [InlineData("-1", 2, 2)]
    [InlineData("many", null, 4)]
    public void Resolve_workers_come_from_the_environment_then_the_file_then_the_default(string? workersValue, int? fileWorkers, int expected)
    {
        var settings = BuildHostOptimizations.Resolve(fileEnabled: null, fileWorkers, enabledValue: null, workersValue);

        Assert.True(settings.ReferencesFirst);
        Assert.True(settings.SharedSdkCache);
        Assert.Equal(expected, settings.EvaluateAheadWorkers);
    }

    [Theory]
    [InlineData("App.sln")]
    [InlineData("App.SLNX")]
    [InlineData("Part.slnf")]
    public void ForWorkspace_adds_the_solution_path_for_a_solution_or_a_filter(string fileName)
    {
        var path = Path.Combine(Path.GetTempPath(), "repo", fileName);
        var requested = new Dictionary<string, string> { ["Configuration"] = "Release" };

        var properties = BuildHostOptimizations.ForWorkspace(requested, path, new BuildHostOptimizationSettings(true, 4, true));

        Assert.Equal("Release", properties["Configuration"]);
        Assert.Equal(Path.GetFullPath(path), properties[BuildHostOptimizations.SolutionPathProperty]);
        Assert.False(requested.ContainsKey(BuildHostOptimizations.SolutionPathProperty));
    }

    [Fact]
    public void ForWorkspace_passes_no_solution_path_for_a_project()
    {
        var properties = BuildHostOptimizations.ForWorkspace(
            new Dictionary<string, string>(),
            Path.Combine(Path.GetTempPath(), "App.csproj"),
            new BuildHostOptimizationSettings(true, 4, true));

        Assert.False(properties.ContainsKey(BuildHostOptimizations.SolutionPathProperty));
    }

    [Theory]
    [InlineData(true, 4, true, "1", "4", "1")]
    [InlineData(true, 0, true, "1", "0", "1")]
    [InlineData(false, 0, false, "0", "0", "0")]
    public void ForWorkspace_always_passes_every_switch_so_the_process_environment_does_not_decide(
        bool referencesFirst,
        int workers,
        bool sharedSdkCache,
        string expectedReferencesFirst,
        string expectedWorkers,
        string expectedSharedSdkCache)
    {
        var properties = BuildHostOptimizations.ForWorkspace(
            new Dictionary<string, string>(),
            Path.Combine(Path.GetTempPath(), "App.sln"),
            new BuildHostOptimizationSettings(referencesFirst, workers, sharedSdkCache));

        Assert.Equal(expectedReferencesFirst, properties[BuildHostOptimizations.ReferencesFirstProperty]);
        Assert.Equal(expectedWorkers, properties[BuildHostOptimizations.EvaluateAheadProperty]);
        Assert.Equal(expectedSharedSdkCache, properties[BuildHostOptimizations.SharedSdkCacheProperty]);
    }

    // The server build replaces the package's BuildHost-netcore with forks/roslyn-buildhost, and the tests get the
    // server's copy-to-output items; a package update or a broken replacement would load with the package's host.
    [Fact]
    public void BuildHost_netcore_next_to_the_client_is_the_fork()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "BuildHost-netcore", "Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll");

        var version = FileVersionInfo.GetVersionInfo(path).ProductVersion;

        Assert.NotNull(version);
        Assert.StartsWith("5.9.0-fork+", version, StringComparison.Ordinal);
    }
}
