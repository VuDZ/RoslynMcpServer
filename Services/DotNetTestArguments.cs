namespace RoslynMcpServer.Services;

/// <summary>
/// Builds <c>dotnet test</c> argument strings for MCP test tools.
/// When <c>noBuild</c> is false, <see cref="BuildPlan"/> splits compile from VSTest
/// so the parser never sees MSBuild warning dumps.
/// </summary>
public static class DotNetTestArguments
{
    public sealed record CliPlan(string? PreTestBuildArguments, string TestArguments)
    {
        public bool IncludesPreTestBuild => PreTestBuildArguments is not null;
    }

    public static string Build(
        string targetPath,
        string? filter = null,
        bool noBuild = false,
        bool noRestore = false,
        string? configuration = null,
        string? platform = null,
        string? testAssemblyPath = null)
    {
        var testTarget = string.IsNullOrWhiteSpace(testAssemblyPath) ? targetPath : testAssemblyPath;
        ArgumentException.ThrowIfNullOrWhiteSpace(testTarget);

        var args = $"test \"{testTarget}\" --logger \"console;verbosity=normal\" --verbosity normal";
        args = DotNetConfigurationArguments.Append(args, configuration);
        args = DotNetConfigurationArguments.AppendPlatform(args, platform);

        if (noBuild)
        {
            args += " --no-build";
        }

        if (noRestore)
        {
            args += " --no-restore";
        }

        if (!string.IsNullOrWhiteSpace(filter))
        {
            args += $" --filter \"{filter.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
        }

        return args;
    }

    /// <summary>
    /// Incremental <c>dotnet build</c> (no <c>--no-incremental</c>) with the same configuration / platform as the test step
    /// (<c>-p:Configuration</c> on the build; <c>dotnet test</c> still uses <c>-c</c>).
    /// </summary>
    public static string BuildPreTestBuild(
        string targetPath,
        bool noRestore = false,
        string? configuration = null,
        string? platform = null,
        string? buildArgs = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var args = $"build \"{targetPath}\"";
        args = DotNetConfigurationArguments.AppendConfigurationProperty(args, configuration);
        args = DotNetConfigurationArguments.AppendPlatform(args, platform);
        args = DotNetBuildArguments.Append(args, buildArgs);
        if (noRestore)
        {
            args += " --no-restore";
        }

        return args;
    }

    /// <summary>
    /// When <paramref name="noBuild"/> is false: <c>dotnet build</c> then <c>dotnet test --no-build --no-restore</c>.
    /// When true: a single <c>dotnet test --no-build</c> (restore flag as requested).
    /// </summary>
    public static CliPlan BuildPlan(
        string targetPath,
        string? filter = null,
        bool noBuild = false,
        bool noRestore = false,
        string? configuration = null,
        string? platform = null,
        string? buildArgs = null,
        string? testAssemblyPath = null)
    {
        if (!string.IsNullOrWhiteSpace(testAssemblyPath))
        {
            return new CliPlan(
                PreTestBuildArguments: null,
                TestArguments: Build(
                    targetPath,
                    filter,
                    noBuild: true,
                    noRestore,
                    configuration,
                    platform,
                    testAssemblyPath));
        }

        if (noBuild)
        {
            return new CliPlan(
                PreTestBuildArguments: null,
                TestArguments: Build(
                    targetPath, filter, noBuild: true, noRestore, configuration, platform));
        }

        return new CliPlan(
            PreTestBuildArguments: BuildPreTestBuild(targetPath, noRestore, configuration, platform, buildArgs),
            TestArguments: Build(
                targetPath, filter, noBuild: true, noRestore: true, configuration, platform));
    }

    /// <summary>
    /// Remaining budget for a follow-up process. <see langword="null"/> overall stays unlimited.
    /// Returns <see cref="TimeSpan.Zero"/> when the budget is already exhausted (caller must not start the next process).
    /// </summary>
    public static TimeSpan? RemainingTimeout(TimeSpan? overall, TimeSpan elapsed)
    {
        if (overall is null)
        {
            return null;
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        var left = overall.Value - elapsed;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }
}
