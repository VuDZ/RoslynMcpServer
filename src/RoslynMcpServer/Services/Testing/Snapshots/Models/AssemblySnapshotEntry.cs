using RoslynMcpServer.Services.Testing.Il.Models;

namespace RoslynMcpServer.Services.Testing.Snapshots.Models;

/// <summary>
/// One output assembly in a snapshot. The value is immutable and keeps neither the image of the file nor
/// any Roslyn object: an entry that did not change is shared by both snapshots of a session.
/// </summary>
internal sealed class AssemblySnapshotEntry
{
    /// <summary>The output path of the project exactly as the workspace gives it.</summary>
    public string Path { get; }

    public AssemblySnapshotState State { get; }

    /// <summary>The file the entry was made from; null when there was no file or the content was not trusted.</summary>
    public AssemblyFileStamp? Stamp { get; }

    /// <summary>
    /// For <see cref="AssemblySnapshotState.Read"/>, the facts for the graph; for
    /// <see cref="AssemblySnapshotState.Missing"/> and <see cref="AssemblySnapshotState.Unreadable"/>, the
    /// failure with its reason; null for <see cref="AssemblySnapshotState.Unknown"/>.
    /// </summary>
    public IlAssemblyReadResult? Result { get; }

    /// <summary>Set only for <see cref="AssemblySnapshotState.Read"/>.</summary>
    public IlFingerprintSnapshot? Fingerprints { get; }

    /// <summary>Why the entry is not <see cref="AssemblySnapshotState.Read"/>; null when it is.</summary>
    public string? Reason { get; }

    public IlAssemblyFacts? Facts => Result?.Facts;

    private AssemblySnapshotEntry(
        string path,
        AssemblySnapshotState state,
        AssemblyFileStamp? stamp,
        IlAssemblyReadResult? result,
        IlFingerprintSnapshot? fingerprints,
        string? reason)
    {
        Path = path;
        State = state;
        Stamp = stamp;
        Result = result;
        Fingerprints = fingerprints;
        Reason = reason;
    }

    public static AssemblySnapshotEntry Read(
        string path,
        AssemblyFileStamp stamp,
        IlAssemblyReadResult result,
        IlFingerprintSnapshot fingerprints) =>
        new(path, AssemblySnapshotState.Read, stamp, result, fingerprints, reason: null);

    public static AssemblySnapshotEntry Missing(string path) =>
        new(
            path,
            AssemblySnapshotState.Missing,
            stamp: null,
            IlAssemblyReadResult.Unread(new IlUnreadAssembly(path, IlAssemblyFailureKind.Missing, MissingMessage)),
            fingerprints: null,
            MissingMessage);

    /// <param name="stamp">The file the failure belongs to; null when even its size and time could not be read.</param>
    public static AssemblySnapshotEntry Unreadable(string path, AssemblyFileStamp? stamp, IlUnreadAssembly failure) =>
        new(
            path,
            AssemblySnapshotState.Unreadable,
            stamp,
            IlAssemblyReadResult.Unread(failure),
            fingerprints: null,
            $"{failure.Reason}: {failure.Message}");

    public static AssemblySnapshotEntry Unknown(string path, string reason) =>
        new(path, AssemblySnapshotState.Unknown, stamp: null, result: null, fingerprints: null, reason);

    private const string MissingMessage = "The output assembly does not exist.";
}
