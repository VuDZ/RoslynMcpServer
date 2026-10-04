using System.Collections.Immutable;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

/// <summary>
/// A materialized oracle fixture root with its request and independently authored expectation.
/// The caller owns the fixture until <see cref="DisposeAsync"/>, performs at most one
/// mutation/build/oracle stage at a time, and resets between stages with
/// <see cref="RestoreAsync"/>. No manager or session lock is acquired.
/// </summary>
internal sealed class OracleFixtureCase : IAsyncDisposable
{
    public FixtureScenario Scenario { get; }
    public string Root { get; }
    public ExperimentRequest Request { get; }
    public FixtureExpectation Expected { get; }

    internal OracleFixtureCase(
        FixtureScenario scenario,
        string root,
        ExperimentRequest request,
        FixtureExpectation expected,
        ImmutableSortedDictionary<string, ImmutableArray<byte>> originalFiles)
    {
        Scenario = scenario;
        Root = Path.GetFullPath(root);
        Request = request;
        Expected = expected;
        _originalFiles = originalFiles;
    }

    /// <summary>
    /// Returns the absolute path for a root-relative fixture path. Rooted paths and normalized
    /// paths escaping the owned root are rejected before any file system access.
    /// </summary>
    /// <exception cref="ArgumentException">The path is empty, rooted, or escapes the owned root.</exception>
    public string FilePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("A root-relative fixture path is required.", nameof(relativePath));
        }
        var path = Path.GetFullPath(Path.Combine(Root, relativePath));
        if (!path.StartsWith(Root + Path.DirectorySeparatorChar, PathComparison))
        {
            throw new ArgumentException("The fixture path must stay inside its owned root.", nameof(relativePath));
        }
        return path;
    }

    /// <summary>
    /// Restores every original file byte, removes scenario-created files, and removes owned bin/obj
    /// directories so restored bytes are not confused with stale generated output. Deletion targets
    /// are checked to stay under the owned root; the parent directory is never removed. I/O and
    /// restore failures throw; repeatability is never claimed over a failed restore.
    /// </summary>
    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        DeleteGeneratedOutputDirectories();
        await DeleteFilesOutsideOriginalInventoryAsync(cancellationToken).ConfigureAwait(false);
        foreach (var (relativePath, bytes) in _originalFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = FilePath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes.ToArray(), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Compares the current tree with the original inventory: every original file must hold its
    /// exact bytes and no unexpected source/project/config/additional path may remain. Files under
    /// bin/ or obj/ directories are ignored as the explicitly documented build-stage generated
    /// output; require a fully clean tree by calling <see cref="RestoreAsync"/> first, which
    /// removes those directories.
    /// </summary>
    /// <exception cref="InvalidDataException">An original file differs or an unexpected path remains.</exception>
    public async Task VerifyRestoredAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        var unexpected = new List<string>();
        foreach (var path in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = ToRelativePath(path);
            if (!_originalFiles.ContainsKey(relativePath) && !IsGeneratedOutputPath(relativePath))
            {
                unexpected.Add(relativePath);
            }
        }
        if (unexpected.Count > 0)
        {
            unexpected.Sort(StringComparer.Ordinal);
            throw new InvalidDataException("Unexpected files remain under the fixture root: " + string.Join(", ", unexpected));
        }
        foreach (var (relativePath, expectedBytes) in _originalFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var actual = await File.ReadAllBytesAsync(FilePath(relativePath), cancellationToken).ConfigureAwait(false);
            if (!actual.AsSpan().SequenceEqual(expectedBytes.AsSpan()))
            {
                throw new InvalidDataException("Fixture file bytes differ from the original inventory: " + relativePath);
            }
        }
    }

    /// <summary>Deletes the owned unique root directory. Cleanup failure is thrown, not suppressed.</summary>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }
        _disposed = true;
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (Exception exception)
        {
            return ValueTask.FromException(exception);
        }
        return ValueTask.CompletedTask;
    }

    private void DeleteGeneratedOutputDirectories()
    {
        // Materialize first: deleting a directory during streaming enumeration can skip siblings.
        foreach (var directory in Directory.EnumerateDirectories(Root, "*", SearchOption.AllDirectories).ToList())
        {
            var name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (name is not ("bin" or "obj"))
            {
                continue;
            }
            var full = Path.GetFullPath(directory);
            if (!full.StartsWith(Root + Path.DirectorySeparatorChar, PathComparison))
            {
                throw new IOException("Refusing to delete a directory outside the owned fixture root: " + full);
            }
            // Deleting an outer bin/obj already removed every bin/obj nested inside it; skip
            // entries that vanished with their parent instead of failing on a second delete.
            if (!Directory.Exists(full))
            {
                continue;
            }
            Directory.Delete(full, recursive: true);
        }
    }

    private async Task DeleteFilesOutsideOriginalInventoryAsync(CancellationToken cancellationToken)
    {
        foreach (var path in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories).ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = ToRelativePath(path);
            if (_originalFiles.ContainsKey(relativePath))
            {
                continue;
            }
            var full = Path.GetFullPath(path);
            if (!full.StartsWith(Root + Path.DirectorySeparatorChar, PathComparison))
            {
                throw new IOException("Refusing to delete a file outside the owned fixture root: " + full);
            }
            File.Delete(full);
        }
    }

    private string ToRelativePath(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    private static bool IsGeneratedOutputPath(string relativePath)
        => relativePath.Split('/').Any(segment => segment is "bin" or "obj");

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(OracleFixtureCase));
        }
    }

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly ImmutableSortedDictionary<string, ImmutableArray<byte>> _originalFiles;
    private bool _disposed;
}
