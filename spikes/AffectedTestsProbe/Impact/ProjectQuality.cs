namespace AffectedTestsProbe;

/// <summary>Load quality of one project: missing framework references and declaration error counts by code.</summary>
internal sealed record ProjectQuality(string Name, bool NoFramework, IReadOnlyDictionary<string, int> ErrorCodes)
{
    public int Errors => ErrorCodes.Values.Sum();
}
