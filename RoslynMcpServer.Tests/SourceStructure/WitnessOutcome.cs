namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>What happened to one witness operation, including the wall-clock time the parent bounded it by.</summary>
internal sealed record WitnessOutcome(WitnessStatus Status, string Output, string Error, TimeSpan Elapsed)
{
    /// <summary>True when the host reported the operation result before the bound expired.</summary>
    public bool IsCompleted => Status == WitnessStatus.Completed;

    /// <summary>Message used in assertions; it states what was bounded and what the host said.</summary>
    public string Describe() => Status switch
    {
        WitnessStatus.Completed => $"completed in {Elapsed.TotalMilliseconds:F0} ms with `{Output}`",
        WitnessStatus.TimedOut => $"did not finish within {Elapsed.TotalMilliseconds:F0} ms and its process tree was killed",
        _ => $"failed in {Elapsed.TotalMilliseconds:F0} ms with `{Error}`",
    };
}
