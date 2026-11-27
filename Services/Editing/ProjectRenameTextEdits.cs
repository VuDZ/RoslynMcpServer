using System.Text;
using System.Xml;

namespace RoslynMcpServer.Services.Editing;

internal sealed class ProjectRenameTextEdits
{
    internal ProjectRenameTextEdits(ProjectRenameFile file)
    {
        _file = file;
    }

    internal void Replace(int start, int length, string replacement)
    {
        if (start < 0 || length < 0 || start > _file.Text.Length - length)
        {
            throw new InvalidOperationException($"An edit for `{_file.Path}` is outside the original text.");
        }

        _edits.Add((start, length, replacement));
    }

    internal void ReplaceAttribute(int line, int column, string name, string oldValue, string newValue)
    {
        var offset = _file.GetOffset(line, column);
        // MSBuild supplies a start position only. Check it against the same XML snapshot before locating the quotes.
        _file.VerifyAttribute(offset, name, oldValue);
        var text = _file.Text;
        if (text.AsSpan(offset).StartsWith(name, StringComparison.Ordinal))
        {
            var equals = SkipXmlWhitespace(text, offset + name.Length);
            if (equals < text.Length && text[equals] == '=')
            {
                var quote = SkipXmlWhitespace(text, equals + 1);
                if (quote < text.Length && text[quote] is '\'' or '"')
                {
                    var start = quote + 1;
                    var end = text.IndexOf(text[quote], start);
                    if (end >= 0)
                    {
                        Replace(start, end - start, EscapeAttribute(newValue, text[quote]));
                        return;
                    }
                }
            }
        }

        throw new InvalidOperationException($"Could not locate the original value of `{name}` in `{_file.Path}`.");
    }

    internal string? GetUpdatedText()
    {
        if (_edits.Count == 0)
        {
            return null;
        }

        // Classic solutions and JSON solution filters have no XML declaration to reconcile.
        if (Path.GetExtension(_file.Path).ToLowerInvariant() is not (".sln" or ".slnf"))
        {
            _file.ValidateXmlEncoding();
        }

        var text = new StringBuilder(_file.Text);
        var previousStart = text.Length;
        foreach (var (start, length, replacement) in _edits.OrderByDescending(edit => edit.Start))
        {
            if (start + length > previousStart)
            {
                throw new InvalidOperationException($"Rename edits overlap in `{_file.Path}`.");
            }

            text.Remove(start, length).Insert(start, replacement);
            previousStart = start;
        }

        var updated = text.ToString();
        _file.VerifyRewrite(updated);
        return updated;
    }

    private static int SkipXmlWhitespace(string text, int start)
    {
        while (start < text.Length && text[start] is ' ' or '\t' or '\r' or '\n')
        {
            start++;
        }

        return start;
    }

    private static string EscapeAttribute(string value, char quote) => XmlConvert.VerifyXmlChars(value)
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(quote.ToString(), quote == '\'' ? "&apos;" : "&quot;", StringComparison.Ordinal)
        .Replace("\t", "&#x9;", StringComparison.Ordinal)
        .Replace("\n", "&#xA;", StringComparison.Ordinal)
        .Replace("\r", "&#xD;", StringComparison.Ordinal);

    private readonly ProjectRenameFile _file;
    private readonly List<(int Start, int Length, string Replacement)> _edits = [];
}
