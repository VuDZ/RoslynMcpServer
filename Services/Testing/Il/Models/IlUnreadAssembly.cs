namespace RoslynMcpServer.Services.Testing.Il.Models;

/// <summary>An assembly file that is missing from the graph, with the reason.</summary>
/// <param name="Path">The path exactly as the caller gave it.</param>
/// <param name="Message">The text of the underlying failure, for a log or a diagnostic line.</param>
internal sealed record IlUnreadAssembly(string Path, IlAssemblyFailureKind Reason, string Message);
