namespace RoslynMcpServer.Services.Models;

/// <summary>One reason the input watch is not complete, with the scope that reason affects.</summary>
internal sealed class InputCoverageGap
{
    public string Reason { get; }

    public InputCoverageScope Scope { get; }

    public InputCoverageGap(string reason, InputCoverageScope scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(scope);
        Reason = reason;
        Scope = scope;
    }
}
