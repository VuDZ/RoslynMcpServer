using System.Text;

namespace RoslynMcpServer.Services.Workspace.Models;

internal sealed record PatchFileText(string Text, Encoding Encoding, byte[] Bytes, bool HasDeclaredEncoding = false)
{
    public int?[]? EvaluatedCodePages { get; init; }
}
