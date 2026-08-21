using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class DotNetTestArgumentsTests
{
    private const string Target = @"C:\src\App.Tests.csproj";

    [Fact]
    public void Build_without_flags_omits_no_build_and_no_restore()
    {
        var args = DotNetTestArguments.Build(Target);

        Assert.Equal(
            $"test \"{Target}\" --logger \"console;verbosity=normal\" --verbosity normal",
            args);
        Assert.DoesNotContain("--no-build", args, StringComparison.Ordinal);
        Assert.DoesNotContain("--no-restore", args, StringComparison.Ordinal);
        Assert.DoesNotContain("--filter", args, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_noBuild_appends_flag()
    {
        var args = DotNetTestArguments.Build(Target, noBuild: true);

        Assert.EndsWith(" --no-build", args, StringComparison.Ordinal);
        Assert.DoesNotContain("--no-restore", args, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_noRestore_appends_flag()
    {
        var args = DotNetTestArguments.Build(Target, noRestore: true);

        Assert.EndsWith(" --no-restore", args, StringComparison.Ordinal);
        Assert.DoesNotContain("--no-build", args, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_both_flags_and_filter_in_stable_order()
    {
        var args = DotNetTestArguments.Build(
            Target,
            filter: "FullyQualifiedName~Foo",
            noBuild: true,
            noRestore: true);

        Assert.Equal(
            $"test \"{Target}\" --logger \"console;verbosity=normal\" --verbosity normal --no-build --no-restore --filter \"FullyQualifiedName~Foo\"",
            args);
    }

    [Fact]
    public void Build_configuration_before_no_build_flags()
    {
        var args = DotNetTestArguments.Build(
            Target,
            configuration: "Sit-Debug",
            noBuild: true,
            noRestore: true);

        Assert.Equal(
            $"test \"{Target}\" --logger \"console;verbosity=normal\" --verbosity normal -c \"Sit-Debug\" --no-build --no-restore",
            args);
    }

    [Fact]
    public void Build_platform_after_configuration()
    {
        var args = DotNetTestArguments.Build(
            Target,
            configuration: "kart",
            platform: "x64");

        Assert.Equal(
            $"test \"{Target}\" --logger \"console;verbosity=normal\" --verbosity normal -c \"kart\" -p:Platform=\"x64\"",
            args);
    }

    [Fact]
    public void Build_escapes_quotes_in_filter()
    {
        var args = DotNetTestArguments.Build(Target, filter: "Name=\"X\"");

        Assert.Contains("--filter \"Name=\\\"X\\\"\"", args, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPlan_noBuild_false_splits_build_then_test_without_restore()
    {
        var plan = DotNetTestArguments.BuildPlan(
            Target,
            filter: "FullyQualifiedName~Foo",
            noBuild: false,
            noRestore: false,
            configuration: "Sit-Debug",
            platform: "x64");

        Assert.True(plan.IncludesPreTestBuild);
        Assert.Equal(
            $"build \"{Target}\" -c \"Sit-Debug\" -p:Platform=\"x64\"",
            plan.PreTestBuildArguments);
        Assert.Equal(
            $"test \"{Target}\" --logger \"console;verbosity=normal\" --verbosity normal -c \"Sit-Debug\" -p:Platform=\"x64\" --no-build --no-restore --filter \"FullyQualifiedName~Foo\"",
            plan.TestArguments);
        Assert.DoesNotContain("--no-incremental", plan.PreTestBuildArguments, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPlan_noBuild_false_forwards_noRestore_to_pre_test_build()
    {
        var plan = DotNetTestArguments.BuildPlan(Target, noBuild: false, noRestore: true);

        Assert.Equal($"build \"{Target}\" --no-restore", plan.PreTestBuildArguments);
        Assert.Contains("--no-build", plan.TestArguments, StringComparison.Ordinal);
        Assert.Contains("--no-restore", plan.TestArguments, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPlan_noBuild_true_skips_pre_test_build()
    {
        var plan = DotNetTestArguments.BuildPlan(Target, noBuild: true, noRestore: false);

        Assert.False(plan.IncludesPreTestBuild);
        Assert.Null(plan.PreTestBuildArguments);
        Assert.Equal(
            $"test \"{Target}\" --logger \"console;verbosity=normal\" --verbosity normal --no-build",
            plan.TestArguments);
        Assert.DoesNotContain("--no-restore", plan.TestArguments, StringComparison.Ordinal);
    }

    [Fact]
    public void RemainingTimeout_unlimited_stays_null()
    {
        Assert.Null(DotNetTestArguments.RemainingTimeout(null, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void RemainingTimeout_subtracts_elapsed_and_clamps_to_zero()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(40),
            DotNetTestArguments.RemainingTimeout(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(60)));
        Assert.Equal(
            TimeSpan.Zero,
            DotNetTestArguments.RemainingTimeout(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)));
        Assert.Equal(
            TimeSpan.Zero,
            DotNetTestArguments.RemainingTimeout(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)));
    }
}
