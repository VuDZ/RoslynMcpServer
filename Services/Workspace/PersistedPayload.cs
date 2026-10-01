using System.Text;

namespace RoslynMcpServer.Services.Workspace;

/// <summary>
/// Exact bytes a successful text write persists, including the encoding preamble.
/// A hash of those bytes does not identify who wrote them.
/// </summary>
internal static class PersistedPayload
{
    public static byte[] FromText(string text, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(encoding);
        var preamble = encoding.GetPreamble();
        var body = encoding.GetBytes(text);
        if (preamble.Length == 0)
        {
            return body;
        }

        var payload = new byte[preamble.Length + body.Length];
        preamble.CopyTo(payload, 0);
        body.CopyTo(payload, preamble.Length);
        return payload;
    }
}
