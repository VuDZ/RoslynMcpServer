namespace RoslynMcpServer.Services.Testing.Snapshots.Models;

/// <summary>What a snapshot knows about one output assembly.</summary>
internal enum AssemblySnapshotState
{
    /// <summary>The file was read: facts for the graph and fingerprints are there.</summary>
    Read,

    /// <summary>There was no file when the snapshot looked for it.</summary>
    Missing,

    /// <summary>The file exists and gave no facts or no fingerprints; the reason is named.</summary>
    Unreadable,

    /// <summary>
    /// Only in the snapshot before the edit: the file may already hold a build made after the edit, so its
    /// content cannot stand for the state before it. The state lasts until a new baseline.
    /// </summary>
    Unknown,
}
