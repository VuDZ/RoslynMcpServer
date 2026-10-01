namespace RoslynMcpServer.Services.Build;

/// <summary>
/// Outcome of one DLL-route ensure. <see cref="TestRun"/> is the test process only;
/// the build log stays on <see cref="BuildRun"/> and is not a VSTest report.
/// </summary>
internal sealed class TestDllEnsureResult
{
    public bool TestsStarted { get; init; }

    public string? Error { get; init; }

    public required string Reason { get; init; }

    public string? SolutionTarget { get; init; }

    public string? AssemblyPath { get; init; }

    public string? BuildArguments { get; init; }

    public string? TestArguments { get; init; }

    public DotNetCliRunner.RunResult? BuildRun { get; init; }

    public DotNetCliRunner.RunResult? TestRun { get; init; }

    public bool Cancelled { get; init; }

    public bool TimedOut { get; init; }
}
