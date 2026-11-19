namespace RoslynMcpServer.Services.Testing.Il.Models;

/// <summary>The outcome of loading one assembly file into memory: exactly one of the two values is set.</summary>
internal sealed record IlAssemblyImageLoad(IlAssemblyImage? Image, IlUnreadAssembly? Failure);
