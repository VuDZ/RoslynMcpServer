using System.Text;

namespace RoslynMcpServer.Services.Workspace.Models;

internal sealed record DeclaredSourceEncoding(Encoding Encoding, string Path, string Key);
