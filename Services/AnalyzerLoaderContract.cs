namespace RoslynMcpServer.Services;

/// <summary>
/// Chosen epoch-3 loader contract (U-ARB-02 / U-ARB-03).
/// In-process refresh of a same-identity generator is not supported; V2 execution
/// requires a new MCP process. Private helper DLLs are not a supported dependency
/// set — main-only only. The loader is owned by <see cref="SolutionManager"/> for
/// the process lifetime. <c>reset_workspace</c> does not unload CLR assemblies or
/// detach the resolve handler.
/// </summary>
internal static class AnalyzerLoaderContract
{
    public const string SupportedRefreshMode = "restart-required";
    public const string SupportedDependencyPolicy = "main-only";
    public const string RestartAction = "restart the MCP server process";

    public const string RestartRequiredReason =
        "in-process generator refresh is not supported for the same assembly identity; restart the MCP server process";

    public const string IdentityCollisionReason =
        "assembly identity already loaded from a different path; restart the MCP server process";

    public const string DependencyUnsupportedReason =
        "private analyzer dependencies are not supported; main-only generators only";

    public const string DependencyUnsupportedAction =
        "use a main-only generator without private helper DLLs";
}

internal enum AnalyzerPreparationStage
{
    None = 0,
    Prepared = 1,
    ReferenceRewritten = 2,
    LoadFailed = 3,
    ExecutionObserved = 4,
}

internal enum AnalyzerExecutionStatus
{
    None = 0,
    Prepared = 1,
    ReferenceRewritten = 2,
    RestartRequired = 3,
    DependencyUnsupported = 4,
    IdentityCollision = 5,
    LoadFailed = 6,
    ExecutionObserved = 7,
}

internal sealed record AnalyzerExecutionObservation
{
    public AnalyzerExecutionStatus Status { get; init; }

    public AnalyzerPreparationStage HighestStage { get; init; }

    public string? Reason { get; init; }

    public string? Action { get; init; }

    public string? ProjectName { get; init; }

    public string? GeneratorName { get; init; }

    public string? GenerationId { get; init; }

    public string? DependencyName { get; init; }

    public string? ExpectedPath { get; init; }

    public string? LoadedPath { get; init; }

    public string? AssemblyIdentity { get; init; }

    public bool PermitsExecution =>
        Status is AnalyzerExecutionStatus.None
            or AnalyzerExecutionStatus.Prepared
            or AnalyzerExecutionStatus.ReferenceRewritten
            or AnalyzerExecutionStatus.ExecutionObserved;

    public bool RequiresRestart =>
        Status is AnalyzerExecutionStatus.RestartRequired
            or AnalyzerExecutionStatus.IdentityCollision;

    public static AnalyzerExecutionObservation None { get; } = new()
    {
        Status = AnalyzerExecutionStatus.None,
        HighestStage = AnalyzerPreparationStage.None,
    };

    public AnalyzerExecutionObservation WithStage(AnalyzerPreparationStage stage, AnalyzerExecutionStatus? status = null)
    {
        return new AnalyzerExecutionObservation
        {
            Status = status ?? Status,
            HighestStage = stage > HighestStage ? stage : HighestStage,
            Reason = Reason,
            Action = Action,
            ProjectName = ProjectName,
            GeneratorName = GeneratorName,
            GenerationId = GenerationId,
            DependencyName = DependencyName,
            ExpectedPath = ExpectedPath,
            LoadedPath = LoadedPath,
            AssemblyIdentity = AssemblyIdentity,
        };
    }
}
