using System.Xml;

namespace TestImpactIlProbe;

/// <summary>
/// Writes the executed files of a report as a Cobertura file with one line per source file. A full report of
/// one test class repeats every instrumented line of the solution and takes tens of megabytes; hundreds of
/// classes need only the names of the files each of them executed.
/// </summary>
internal static class CoberturaWriter
{
    public static void WriteExecutedFiles(string path, IEnumerable<CoberturaReport> reports)
    {
        var sources = new SortedSet<string>(StringComparer.Ordinal);
        var files = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var report in reports)
        {
            sources.UnionWith(report.Sources);
            files.UnionWith(report.Files.Where(file => file.Value).Select(file => file.Key));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = XmlWriter.Create(path, new XmlWriterSettings { Indent = true });
        writer.WriteStartElement("coverage");
        writer.WriteStartElement("sources");
        foreach (var source in sources)
        {
            writer.WriteElementString("source", source);
        }

        writer.WriteEndElement();
        writer.WriteStartElement("packages");
        writer.WriteStartElement("package");
        writer.WriteStartElement("classes");
        foreach (var file in files)
        {
            writer.WriteStartElement("class");
            writer.WriteAttributeString("filename", file);
            writer.WriteStartElement("lines");
            writer.WriteStartElement("line");
            writer.WriteAttributeString("number", "1");
            writer.WriteAttributeString("hits", "1");
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }
}
