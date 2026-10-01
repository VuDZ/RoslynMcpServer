namespace RoslynMcpServer.Services.Models;

/// <summary>
/// What a watcher callback should do with one physical path. Irrelevant means the map proves the
/// path cannot affect this graph. Unknown provenance is a notice, not this irrelevant result.
/// </summary>
internal sealed class DiskEventDecision
{
    public string? CanonicalPath { get; }

    public bool ProvenIrrelevant { get; }

    public bool DirtyUserSource { get; }

    public bool CompositionStale { get; }

    public bool GraphFile { get; }

    public IReadOnlyList<InputPathNotice> Notices { get; }

    public DiskEventDecision(
        string? canonicalPath,
        bool provenIrrelevant,
        bool dirtyUserSource,
        bool compositionStale,
        bool graphFile,
        IReadOnlyList<InputPathNotice> notices)
    {
        ArgumentNullException.ThrowIfNull(notices);
        CanonicalPath = canonicalPath;
        ProvenIrrelevant = provenIrrelevant;
        DirtyUserSource = dirtyUserSource;
        CompositionStale = compositionStale;
        GraphFile = graphFile;
        Notices = notices;
    }

    public static DiskEventDecision Irrelevant(string? canonicalPath)
    {
        return new DiskEventDecision(
            canonicalPath,
            provenIrrelevant: true,
            dirtyUserSource: false,
            compositionStale: false,
            graphFile: false,
            Array.Empty<InputPathNotice>());
    }
}
