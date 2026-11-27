namespace RoslynMcpServer.Services.Build;

/// <summary>
/// Admission to run one operation for a build context in this process.
/// <see cref="Waited"/> means another operation in this process held that context.
/// It does not mean a build started outside this process was blocked or stopped.
/// </summary>
internal readonly struct ContextAdmission
{
    public bool Acquired { get; init; }

    public bool Waited { get; init; }

    public bool PriorFailed { get; init; }

    public bool PriorConfirmed { get; init; }

    public Guid PriorGeneration { get; init; }
}
