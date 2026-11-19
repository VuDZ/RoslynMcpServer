namespace AffectedTestsProbe;

/// <summary>
/// One query: the members an edit would change. A file target holds every member with a body in that file,
/// a member or sample target holds one member. <see cref="Locations"/> covers every project key of each member.
/// </summary>
internal sealed record ImpactTarget(
    string Source,
    string Label,
    IReadOnlyList<string> Members,
    IReadOnlyList<string> Kinds,
    IReadOnlyList<MemberLocation> Locations);
