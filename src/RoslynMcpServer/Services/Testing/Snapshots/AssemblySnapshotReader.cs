using RoslynMcpServer.Services.Testing.Il;
using RoslynMcpServer.Services.Testing.Il.Models;
using RoslynMcpServer.Services.Testing.Snapshots.Models;

namespace RoslynMcpServer.Services.Testing.Snapshots;

/// <summary>
/// Turns one output assembly file into a snapshot entry: the facts for the graph and the fingerprints come
/// from one in-memory image, and the image is dropped as soon as both are computed.
/// </summary>
/// <remarks>
/// The file content is read only by <see cref="IlAssemblyImageLoader"/>, which opens it with full sharing so
/// a build may overwrite it at any moment. That is why every read is bracketed by the size and time of the
/// file: an image taken while a build wrote the file may be torn, and a stamp that moved during the read
/// says so. The stamp is read from the directory entry; the file is not opened for it.
/// </remarks>
internal sealed class AssemblySnapshotReader
{
    /// <summary>Runs while the file is open for its content, before the content is read.</summary>
    internal Action<string>? WhileFileIsOpenForTests { get; set; }

    /// <summary>Receives every image right after it was read, before it is parsed.</summary>
    internal Action<IlAssemblyImage>? ImageLoadedForTests { get; set; }

    /// <summary>How many times the content of a file was read.</summary>
    internal int FileReads => Volatile.Read(ref _fileReads);

    /// <summary>
    /// The read for the snapshot before the edit. The entry is <see cref="AssemblySnapshotState.Unknown"/>
    /// when the file may already hold a build made after the publication: its time is later than
    /// <paramref name="publishedAtUtc"/>, or its size or time moved while it was read.
    /// </summary>
    public AssemblySnapshotEntry ReadFirst(string path, DateTime publishedAtUtc, IReadOnlySet<string> solutionAssemblies)
    {
        var (stamp, failure) = Stamp(path);
        if (failure is not null)
        {
            return AssemblySnapshotEntry.Unreadable(path, stamp: null, failure);
        }

        if (stamp is null)
        {
            return AssemblySnapshotEntry.Missing(path);
        }

        // The rule of the document baseline: a file written after publication cannot stand for the state
        // before an edit made after publication.
        if (stamp.Value.LastWriteTimeUtc > publishedAtUtc)
        {
            return AssemblySnapshotEntry.Unknown(path, "The file was written after the baseline was published.");
        }

        var attempt = ReadStable(path, stamp.Value, solutionAssemblies);
        return attempt ?? AssemblySnapshotEntry.Unknown(path, "The file changed while it was read for the first time.");
    }

    /// <summary>
    /// The read for the current snapshot. <paramref name="previous"/> is returned as it is while the file
    /// keeps its size and time; otherwise the file is read again. A file that keeps changing while it is
    /// read is <see cref="AssemblySnapshotState.Unreadable"/> for this request.
    /// </summary>
    public AssemblySnapshotEntry ReadCurrent(string path, AssemblySnapshotEntry? previous, IReadOnlySet<string> solutionAssemblies)
    {
        for (var attempt = 0; attempt < CurrentAttempts; attempt++)
        {
            var (stamp, failure) = Stamp(path);
            if (failure is not null)
            {
                return AssemblySnapshotEntry.Unreadable(path, stamp: null, failure);
            }

            if (stamp is null)
            {
                return previous is { State: AssemblySnapshotState.Missing } ? previous : AssemblySnapshotEntry.Missing(path);
            }

            if (previous is not null && previous.Stamp == stamp)
            {
                return previous;
            }

            var entry = ReadStable(path, stamp.Value, solutionAssemblies);
            if (entry is not null)
            {
                return entry;
            }
        }

        var unstable = new IlUnreadAssembly(
            path,
            IlAssemblyFailureKind.Inaccessible,
            $"The file changed while it was read, {CurrentAttempts} times in a row.");
        return AssemblySnapshotEntry.Unreadable(path, stamp: null, unstable);
    }

    // A build replaces a file within milliseconds; three reads in a row that all overlap a write mean the
    // file is being written continuously, and the request should not wait for it.
    private const int CurrentAttempts = 3;

    /// <summary>
    /// Reads the content and parses it when the file still has <paramref name="stamp"/> after the read;
    /// null when the stamp moved. The image is not kept by anything the method returns.
    /// </summary>
    private AssemblySnapshotEntry? ReadStable(string path, AssemblyFileStamp stamp, IReadOnlySet<string> solutionAssemblies)
    {
        var whileOpen = WhileFileIsOpenForTests;
        Interlocked.Increment(ref _fileReads);
        var load = IlAssemblyImageLoader.Load(path, whileOpen is null ? null : () => whileOpen(path));

        // The stamp is compared before the content is trusted in any way: a torn image may still parse.
        var (after, _) = Stamp(path);
        if (after != stamp)
        {
            return null;
        }

        if (load.Image is null)
        {
            // A sharing violation can disappear without changing the file stamp.
            return AssemblySnapshotEntry.Unreadable(path, stamp: null, load.Failure!);
        }

        ImageLoadedForTests?.Invoke(load.Image);
        return Parse(load.Image, stamp, solutionAssemblies);
    }

    private static AssemblySnapshotEntry Parse(IlAssemblyImage image, AssemblyFileStamp stamp, IReadOnlySet<string> solutionAssemblies)
    {
        var read = IlAssemblyReader.Read(image);
        if (read.Failure is not null)
        {
            return AssemblySnapshotEntry.Unreadable(image.Path, stamp, read.Failure);
        }

        try
        {
            return AssemblySnapshotEntry.Read(image.Path, stamp, read, IlAssemblyFingerprints.Compute(image, solutionAssemblies));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The fingerprint reader rejects images it cannot hash completely (for example native-sized
            // initial data in an AnyCPU image). Without fingerprints the assembly cannot be compared, so
            // it counts as unreadable rather than as read without changes.
            var failure = new IlUnreadAssembly(
                image.Path,
                IlAssemblyFailureKind.NotAnAssembly,
                $"{ex.GetType().Name}: {ex.Message}");
            return AssemblySnapshotEntry.Unreadable(image.Path, stamp, failure);
        }
    }

    /// <summary>The size and time of the file, null for no file; a failure when the file system refuses to tell.</summary>
    private static (AssemblyFileStamp? Stamp, IlUnreadAssembly? Failure) Stamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? (new AssemblyFileStamp(info.Length, info.LastWriteTimeUtc), null) : (null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (null, new IlUnreadAssembly(path, IlAssemblyFailureKind.Inaccessible, ex.Message));
        }
    }

    private int _fileReads;
}
