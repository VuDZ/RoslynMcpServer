using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services.Build;

/// <summary>
/// One DLL-route ensure call. <see cref="Context"/> is the build identity the command must use.
/// <see cref="PullSnapshot"/> does not take the workspace semaphore.
/// </summary>
internal sealed class TestDllEnsureRequest
{
    public required ProjectId ProjectId { get; init; }

    public required BuildContext Context { get; init; }

    public required TestBuildPolicy Policy { get; init; }

    public required Func<WorkspaceInputSnapshot> PullSnapshot { get; init; }

    public required string SolutionPath { get; init; }

    public required string ProjectNeedle { get; init; }

    public required string AssemblyPath { get; init; }

    public required Func<string, bool> AssemblyExists { get; init; }

    public bool NoRestore { get; init; }

    public string? Filter { get; init; }

    public string? TestWorkingDirectory { get; init; }

    public string? BuildWorkingDirectory { get; init; }

    public TimeSpan? Timeout { get; init; }

    public string? MappingError { get; init; }
}
