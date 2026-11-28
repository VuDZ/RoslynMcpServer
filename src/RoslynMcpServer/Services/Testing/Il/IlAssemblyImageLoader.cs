using System.Runtime.InteropServices;
using RoslynMcpServer.Services.Testing.Il.Models;

namespace RoslynMcpServer.Services.Testing.Il;

/// <summary>
/// Reads an assembly file into memory in full and closes it. This is the only place of the IL graph code that
/// opens a file.
/// </summary>
/// <remarks>
/// A limit on Linux that the sharing mode cannot lift: .NET holds an advisory lock on every file it has open,
/// so for the milliseconds of a read a writer in another .NET process that asks for exclusive access
/// (<see cref="FileShare.None"/>, or <c>File.Copy</c> with overwrite) gets an <see cref="IOException"/>.
/// A <c>dotnet build</c> copies its output over a file held this way without an error.
/// </remarks>
internal static class IlAssemblyImageLoader
{
    /// <summary>Never throws for a missing or unreadable file: the failure is part of the result.</summary>
    public static IlAssemblyImageLoad Load(string path) => Load(path, whileFileIsOpenForTests: null);

    /// <param name="whileFileIsOpenForTests">Runs after the file is opened and before its content is read.</param>
    internal static IlAssemblyImageLoad Load(string path, Action? whileFileIsOpenForTests)
    {
        try
        {
            using var stream = new FileStream(path, CreateOpenOptions());
            whileFileIsOpenForTests?.Invoke();
            return Loaded(path, ReadToEnd(stream));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Failed(path, IlAssemblyFailureKind.Missing, ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            // ArgumentException and NotSupportedException are a path the file system cannot take, such as
            // the empty output path of a project that builds nothing.
            return Failed(path, IlAssemblyFailureKind.Inaccessible, ex);
        }
    }

    /// <summary>
    /// How an assembly file is opened. A build of the agent may write the file at any moment. Without write
    /// and delete sharing the reader makes that build fail on Windows with an access error (File.OpenRead
    /// shares reading only), so the file is opened with full sharing and held just for one sequential read.
    /// The price is that the content may be torn by a concurrent write: the caller detects that by comparing
    /// the file before and after, and a torn image fails to parse.
    /// </summary>
    internal static FileStreamOptions CreateOpenOptions() => new()
    {
        Mode = FileMode.Open,
        Access = FileAccess.Read,
        Share = FileShare.ReadWrite | FileShare.Delete,
        Options = FileOptions.SequentialScan,

        // The whole file goes into one buffer of its own; a second buffer inside the stream would only copy.
        BufferSize = 0,
    };

    /// <summary>
    /// Reads to the real end of the file, not to the length seen at the start: a concurrent writer can make
    /// the file longer or shorter while it is read.
    /// </summary>
    private static byte[] ReadToEnd(FileStream stream)
    {
        var expected = (int)Math.Min(stream.Length, Array.MaxLength);
        using var buffer = new MemoryStream(expected);
        stream.CopyTo(buffer);

        // The usual case: the stream filled the buffer exactly, so the buffer is the content without a copy.
        return buffer.Length == buffer.Capacity ? buffer.GetBuffer() : buffer.ToArray();
    }

    private static IlAssemblyImageLoad Loaded(string path, byte[] content) =>
        new IlAssemblyImage(path, ImmutableCollectionsMarshal.AsImmutableArray(content));

    private static IlAssemblyImageLoad Failed(string path, IlAssemblyFailureKind reason, Exception ex) =>
        new IlUnreadAssembly(path, reason, ex.Message);
}
