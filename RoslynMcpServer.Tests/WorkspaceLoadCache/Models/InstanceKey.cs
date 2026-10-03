using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

internal sealed record InstanceKey(
    string ProjectPath,
    string InnerTargetFramework,
    ImmutableSortedDictionary<string, string> EffectiveProperties)
{
    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public bool Equals(InstanceKey? other) => other is not null
        && PathComparer.Equals(ProjectPath, other.ProjectPath)
        && StringComparer.Ordinal.Equals(InnerTargetFramework, other.InnerTargetFramework)
        && EffectiveProperties.Count == other.EffectiveProperties.Count
        && EffectiveProperties.All(property => other.EffectiveProperties.TryGetValue(property.Key, out var value)
            && StringComparer.Ordinal.Equals(property.Value, value));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ProjectPath, PathComparer);
        hash.Add(InnerTargetFramework, StringComparer.Ordinal);
        foreach (var property in EffectiveProperties.OrderBy(property => property.Key, StringComparer.Ordinal))
        {
            hash.Add(property.Key, StringComparer.Ordinal);
            hash.Add(property.Value, StringComparer.Ordinal);
        }
        return hash.ToHashCode();
    }
}
