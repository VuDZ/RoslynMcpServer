namespace RoslynMcpServer.Services.Testing.Snapshots.Models;

/// <summary>
/// Size and last write time of an assembly file. A build that rewrites the file changes at least one of
/// them, so an equal stamp lets a snapshot reuse what it read before instead of opening the file again.
/// </summary>
internal readonly record struct AssemblyFileStamp(long Length, DateTime LastWriteTimeUtc);
