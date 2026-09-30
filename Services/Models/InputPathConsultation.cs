namespace RoslynMcpServer.Services.Models;

/// <summary>
/// Answer for a physical path. Absence from <see cref="Known"/> does not prove the path is outside membership.
/// <see cref="Ignored"/> stays false: an unknown intermediate path is not dropped as generated.
/// </summary>
internal sealed class InputPathConsultation
{
    public string? CanonicalPath { get; }

    public InputPathEntry? Known { get; }

    public IReadOnlyList<MembershipRegion> PotentialRegions { get; }

    public bool ProvenExcluded { get; }

    public bool Ignored { get; }

    public InputRole UnindexedRole { get; }

    /// <summary>
    /// True when the path sits beside a known input that lives outside every project directory.
    /// A new sibling of an external glob is not a proven member and not a proven non-member.
    /// </summary>
    public bool ExternalAppearanceUnknown { get; }

    public InputPathConsultation(
        string? canonicalPath,
        InputPathEntry? known,
        IReadOnlyList<MembershipRegion> potentialRegions,
        bool provenExcluded,
        bool ignored,
        InputRole unindexedRole,
        bool externalAppearanceUnknown)
    {
        CanonicalPath = canonicalPath;
        Known = known;
        PotentialRegions = potentialRegions;
        ProvenExcluded = provenExcluded;
        Ignored = ignored;
        UnindexedRole = unindexedRole;
        ExternalAppearanceUnknown = externalAppearanceUnknown;
    }
}
