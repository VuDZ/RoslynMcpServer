using System.Globalization;
using System.Xml;

namespace TestImpactIlProbe;

/// <summary>Reads which source files a Cobertura report names and which of them have an executed line.</summary>
internal static class CoberturaReader
{
    public static CoberturaReport Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static CoberturaReport Read(Stream stream)
    {
        var sources = new List<string>();
        var files = new Dictionary<string, bool>(StringComparer.Ordinal);
        string? file = null;
        var inSource = false;

        // A report of a large solution takes hundreds of megabytes, so it is read as a stream. Some writers
        // add a DOCTYPE that points to a DTD on the network; it is not needed and must not be fetched.
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using var reader = XmlReader.Create(stream, settings);
        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element when reader.LocalName == "source":
                    inSource = !reader.IsEmptyElement;
                    break;
                case XmlNodeType.Text or XmlNodeType.CDATA when inSource:
                    sources.Add(reader.Value.Trim());
                    break;
                case XmlNodeType.EndElement when reader.LocalName == "source":
                    inSource = false;
                    break;
                case XmlNodeType.Element when reader.LocalName == "class":
                    file = reader.GetAttribute("filename");
                    if (string.IsNullOrEmpty(file))
                    {
                        file = null;
                    }
                    else
                    {
                        files.TryAdd(file, false);
                    }

                    break;
                case XmlNodeType.Element when reader.LocalName == "line" && file is not null:
                    if (IsHit(reader.GetAttribute("hits")))
                    {
                        files[file] = true;
                    }

                    break;
            }
        }

        return new CoberturaReport(sources, files);
    }

    private static bool IsHit(string? hits) =>
        long.TryParse(hits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0;
}
