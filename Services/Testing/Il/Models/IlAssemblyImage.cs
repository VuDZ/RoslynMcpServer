using System.Collections.Immutable;

namespace RoslynMcpServer.Services.Testing.Il.Models;

/// <summary>
/// The bytes of one assembly file, read in full while the file was open. Everything that needs the content
/// of the file works on this value, so the file is opened once: the caller parses the image for the graph,
/// computes whatever else it needs from <see cref="Content"/> and drops the value. Nothing produced from an
/// image keeps a reference to it.
/// </summary>
internal sealed class IlAssemblyImage
{
    /// <summary>The path the image was read from, exactly as the caller gave it.</summary>
    public string Path { get; }

    public ImmutableArray<byte> Content { get; }

    public IlAssemblyImage(string path, ImmutableArray<byte> content)
    {
        Path = path;
        Content = content;
    }
}
