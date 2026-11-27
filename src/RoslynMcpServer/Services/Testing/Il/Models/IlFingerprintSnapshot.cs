using System.Collections.Immutable;

namespace RoslynMcpServer.Services.Testing.Il.Models;

/// <summary>Canonical hashes of one assembly and its top-level types; no image bytes are retained.</summary>
internal sealed record IlFingerprintSnapshot(string Assembly, string AssemblyHash, ImmutableDictionary<IlTypeName, string> Types)
{
    public IlFingerprintChanges Compare(IlFingerprintSnapshot current)
    {
        if (!string.Equals(Assembly, current.Assembly, StringComparison.Ordinal))
        {
            throw new ArgumentException("Snapshots must belong to the same assembly.", nameof(current));
        }

        return new IlFingerprintChanges(
            [.. Types.Keys.Intersect(current.Types.Keys).Where(type => Types[type] != current.Types[type]).OrderBy(type => type.Name, StringComparer.Ordinal)],
            [.. current.Types.Keys.Except(Types.Keys).OrderBy(type => type.Name, StringComparer.Ordinal)],
            [.. Types.Keys.Except(current.Types.Keys).OrderBy(type => type.Name, StringComparer.Ordinal)],
            AssemblyHash != current.AssemblyHash);
    }
}
