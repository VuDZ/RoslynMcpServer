namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>Result of a bounded witness operation.</summary>
internal enum WitnessStatus
{
    /// <summary>The host reported <c>result:</c> before the bound expired.</summary>
    Completed,

    /// <summary>The bound expired; the host process tree was killed by the parent.</summary>
    TimedOut,

    /// <summary>The host reported <c>error:</c>, exited, or broke the protocol.</summary>
    Failed,
}
