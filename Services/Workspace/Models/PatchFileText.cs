using System.Text;

namespace RoslynMcpServer.Services.Workspace.Models;

internal sealed record PatchFileText(string Path, string Text, Encoding Encoding);
