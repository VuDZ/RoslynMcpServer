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
    public void Build_escapes_quotes_in_filter()
    {
        var args = DotNetTestArguments.Build(Target, filter: "Name=\"X\"");

        Assert.Contains("--filter \"Name=\\\"X\\\"\"", args, StringComparison.Ordinal);
    }
}
