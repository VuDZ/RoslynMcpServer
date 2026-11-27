namespace RoslynMcpServer.Services.Build;

/// <summary>Input revision of one canonical path captured with a build proof.</summary>
internal sealed class BuildPathRevision
{
    public string CanonicalPath { get; }

    public int InputRevision { get; }

    public BuildPathRevision(string canonicalPath, int inputRevision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        CanonicalPath = canonicalPath;
        InputRevision = inputRevision;
    }
}
