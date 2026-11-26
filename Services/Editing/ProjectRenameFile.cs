using System.Text;
using System.Xml;
using RoslynMcpServer.Services.Workspace;

namespace RoslynMcpServer.Services.Editing;

internal sealed class ProjectRenameFile
{
    internal string Path { get; }

    internal string Text { get; }

    internal Encoding Encoding { get; }

    private ProjectRenameFile(string path, byte[] bytes)
    {
        Path = path;
        _bytes = bytes;
        Encoding = SourceTextEncoding.DetectBomEncoding(bytes) ?? SourceTextEncoding.BomFreeUtf8;
        Text = Encoding.GetString(bytes.AsSpan(Encoding.Preamble.Length));
    }

    internal static ProjectRenameFile Read(string path) => new(path, File.ReadAllBytes(path));

    internal Stream OpenBytes() => new MemoryStream(_bytes, writable: false);

    internal XmlReader CreateXmlReader() => XmlReader.Create(new StringReader(Text), new XmlReaderSettings
    {
        DtdProcessing = DtdProcessing.Parse,
        XmlResolver = null,
        CloseInput = true,
    }, Path);

    internal int GetOffset(int line, int column)
    {
        var starts = _lineStarts ??= ReadLineStarts();
        if (line > 0 && line <= starts.Length && column > 0)
        {
            var start = starts[line - 1];
            var limit = line < starts.Length ? starts[line] - 1 : Text.Length;
            if (column - 1 <= limit - start)
            {
                return start + column - 1;
            }
        }

        throw new InvalidOperationException($"The source location for an edit in `{Path}` is unavailable.");
    }

    internal void VerifyAttribute(int offset, string name, string value)
    {
        var attributes = _attributes ??= ReadAttributes();
        if (!attributes.TryGetValue(offset, out var attribute) || attribute != (name, value))
        {
            throw new InvalidOperationException($"Could not confirm the source location of `{name}` in `{Path}`.");
        }
    }

    internal void ValidateXmlEncoding()
    {
        using var reader = CreateXmlReader();
        if (!reader.Read() || reader.NodeType != XmlNodeType.XmlDeclaration || reader.GetAttribute("encoding") is not { Length: > 0 } name)
        {
            return;
        }

        var matches = name.ToLowerInvariant() switch
        {
            "utf-8" => Encoding.CodePage == 65001,
            "utf-16" => Encoding is UnicodeEncoding,
            "utf-16le" => Encoding.CodePage == 1200,
            "utf-16be" => Encoding.CodePage == 1201,
            // XmlReader interprets a generic UTF-32 declaration as little-endian.
            "utf-32" or "utf-32le" => Encoding.CodePage == 12000,
            "utf-32be" => Encoding.CodePage == 12001,
            _ => false,
        };
        if (!matches)
        {
            throw new InvalidOperationException(
                $"Unsupported XML encoding `{name}` in `{Path}`. A rewrite requires UTF-8 or a matching UTF-16 or UTF-32 BOM.");
        }
    }

    internal void VerifyRewrite(string text)
    {
        if (!PersistedPayload.FromText(Text, Encoding).AsSpan().SequenceEqual(_bytes))
        {
            throw new InvalidOperationException($"The original bytes in `{Path}` cannot be preserved in {Encoding.WebName}.");
        }

        PersistedPayload.FromText(text, Encoding);
    }

    internal void VerifyCurrent(string path)
    {
        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(_bytes))
        {
            throw new InvalidOperationException($"`{path}` changed after rename planning. Create a new rename plan.");
        }
    }

    private int[] ReadLineStarts()
    {
        var starts = new List<int> { 0 };
        for (var index = 0; index < Text.Length; index++)
        {
            if (Text[index] == '\r' && index + 1 < Text.Length && Text[index + 1] == '\n')
            {
                index++;
            }

            if (Text[index] is '\r' or '\n')
            {
                starts.Add(index + 1);
            }
        }

        return starts.ToArray();
    }

    private Dictionary<int, (string Name, string Value)> ReadAttributes()
    {
        var attributes = new Dictionary<int, (string, string)>();
        using var reader = CreateXmlReader();
        var location = (IXmlLineInfo)reader;
        int? rootStart = null;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            rootStart ??= GetOffset(location.LineNumber, location.LinePosition);
            while (reader.MoveToNextAttribute())
            {
                if (reader.IsDefault || location.LineNumber <= 0 || location.LinePosition <= 0)
                {
                    continue;
                }

                // Expanded markup can report coordinates in the DTD declaration instead of the entity use.
                var offset = GetOffset(location.LineNumber, location.LinePosition);
                if (offset > rootStart.Value && !attributes.TryAdd(offset, (reader.Name, reader.Value)))
                {
                    throw new InvalidOperationException($"XML attributes in `{Path}` have ambiguous source locations.");
                }
            }

            reader.MoveToElement();
        }

        return attributes;
    }

    private readonly byte[] _bytes;
    private int[]? _lineStarts;
    private Dictionary<int, (string Name, string Value)>? _attributes;
}
