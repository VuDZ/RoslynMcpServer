using System.Text;
using System.Text.Json;

namespace RoslynMcpServer.Config;

/// <summary>
/// Strips <c>//</c> and <c>/* */</c> comments from JSONC (respecting strings), then parses with
/// <see cref="JsonDocument"/>. Does not use <c>AddJsonFile</c> (comments are not guaranteed there).
/// </summary>
public static class RoslynMcpJsoncParser
{
    public static JsonDocument Parse(string jsonc)
    {
        ArgumentNullException.ThrowIfNull(jsonc);
        var json = StripComments(jsonc);
        return JsonDocument.Parse(json);
    }

    public static bool TryParse(string jsonc, out JsonDocument? document, out string? error)
    {
        try
        {
            document = Parse(jsonc);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            document = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Removes line and block comments outside of JSON string literals.</summary>
    public static string StripComments(string jsonc)
    {
        ArgumentNullException.ThrowIfNull(jsonc);
        var sb = new StringBuilder(jsonc.Length);
        var i = 0;
        var inString = false;
        var escaped = false;

        while (i < jsonc.Length)
        {
            var ch = jsonc[i];

            if (inString)
            {
                sb.Append(ch);
                if (escaped)
                {
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == '"')
                {
                    inString = false;
                }

                i++;
                continue;
            }

            if (ch == '"')
            {
                inString = true;
                sb.Append(ch);
                i++;
                continue;
            }

            if (ch == '/' && i + 1 < jsonc.Length)
            {
                var next = jsonc[i + 1];
                if (next == '/')
                {
                    i += 2;
                    while (i < jsonc.Length && jsonc[i] is not ('\r' or '\n'))
                    {
                        i++;
                    }

                    continue;
                }

                if (next == '*')
                {
                    i += 2;
                    while (i + 1 < jsonc.Length && !(jsonc[i] == '*' && jsonc[i + 1] == '/'))
                    {
                        i++;
                    }

                    if (i + 1 < jsonc.Length)
                    {
                        i += 2;
                    }

                    continue;
                }
            }

            sb.Append(ch);
            i++;
        }

        return sb.ToString();
    }
}
