namespace RoslynMcpServer.Services.Workspace;

/// <summary>
/// Reads the bytes currently on disk twice. The watcher callback does not call this:
/// <see cref="FileSystemEventArgs"/> carries a path and a change type, not the file body.
/// </summary>
internal static class StableFileBytes
{
    public static StableFileRead Read(string canonicalPath)
    {
        var first = ReadOnce(canonicalPath);
        if (first.Failed || first.Missing || first.IsDirectory)
        {
            return first;
        }

        var second = ReadOnce(canonicalPath);
        if (second.Failed || second.Missing)
        {
            return StableFileRead.NotStable;
        }

        if (first.Bytes is null || second.Bytes is null || !first.Bytes.AsSpan().SequenceEqual(second.Bytes))
        {
            return StableFileRead.NotStable;
        }

        return first;
    }

    private static StableFileRead ReadOnce(string canonicalPath)
    {
        try
        {
            if (!File.Exists(canonicalPath))
            {
                // File.Exists is false for a directory. A Created directory that still exists
                // is not a missing file; reporting it absent stores input-missing, and membership
                // re-evaluation does not treat that gap as a directory rename.
                if (Directory.Exists(canonicalPath))
                {
                    return StableFileRead.ExistingDirectory;
                }

                return StableFileRead.Absent;
            }

            using var stream = new FileStream(
                canonicalPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return StableFileRead.OfBytes(buffer.ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return StableFileRead.Unreadable;
        }
    }
}
