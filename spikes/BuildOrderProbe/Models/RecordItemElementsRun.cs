namespace BuildOrderProbe.Models;

internal sealed record RecordItemElementsRun(
    string? MsBuildVersion,
    string[] BuildDocuments,
    string[] OriginalGlobs,
    string[] RecordedGlobs,
    bool OriginalDirtyAfterBuild,
    bool RecordedDirtyAfterBuild);
