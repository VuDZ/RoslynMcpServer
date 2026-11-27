using System.Text;

namespace RoslynMcpServer.Services.Workspace.Models;

internal sealed record ExplicitFileWrite(string Path, Encoding Encoding, byte[] Bytes, bool AddedUtf8Bom);
