using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services.Models;

/// <summary>
/// One physical path in a pull snapshot. <see cref="InputRevision"/> counts significant input edits.
/// <see cref="OutputRevision"/> is the separate signal that a producing build's generated output changed.
/// </summary>
internal sealed class InputPathSnapshot
{
    public string CanonicalPath { get; }

    public int InputRevision { get; }

    public int OutputRevision { get; }

    public bool Present { get; }

    public bool Pending { get; }

    public IReadOnlyList<ProjectId> Owners { get; }

    public IReadOnlyList<InputRole> Roles { get; }

    public IReadOnlyList<ProjectId> Producers { get; }

    public InputPathSnapshot(
        string canonicalPath,
        int inputRevision,
        int outputRevision,
        bool present,
        bool pending,
        IReadOnlyList<ProjectId> owners,
        IReadOnlyList<InputRole> roles,
        IReadOnlyList<ProjectId> producers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        ArgumentNullException.ThrowIfNull(owners);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(producers);
        CanonicalPath = canonicalPath;
        InputRevision = inputRevision;
        OutputRevision = outputRevision;
        Present = present;
        Pending = pending;
        Owners = owners;
        Roles = roles;
        Producers = producers;
    }
}
