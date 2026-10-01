namespace RoslynMcpServer.Services.Workspace;

/// <summary>
/// Two reads of one file. Unequal reads are unstable: the bytes are not evidence of an echo.
/// </summary>
internal readonly struct StableFileRead
{
    public bool Missing { get; }

    public bool Failed { get; }

    public bool Unstable { get; }

    /// <summary>The path exists as a directory. It is not file content and not a missing file.</summary>
    public bool IsDirectory { get; }

    public byte[]? Bytes { get; }

    public StableFileRead(bool missing, bool failed, bool unstable, byte[]? bytes, bool isDirectory = false)
    {
        Missing = missing;
        Failed = failed;
        Unstable = unstable;
        Bytes = bytes;
        IsDirectory = isDirectory;
    }

    public static StableFileRead OfBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return new StableFileRead(missing: false, failed: false, unstable: false, bytes);
    }

    public static StableFileRead Unreadable { get; } = new(missing: false, failed: true, unstable: false, bytes: null);

    public static StableFileRead NotStable { get; } = new(missing: false, failed: false, unstable: true, bytes: null);

    public static StableFileRead Absent { get; } = new(missing: true, failed: false, unstable: false, bytes: null);

    public static StableFileRead ExistingDirectory { get; } = new(
        missing: false,
        failed: false,
        unstable: false,
        bytes: null,
        isDirectory: true);
}
