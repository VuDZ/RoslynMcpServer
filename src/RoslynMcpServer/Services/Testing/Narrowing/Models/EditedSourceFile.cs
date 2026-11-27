using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services.Testing.Narrowing.Models;

/// <summary>A C# document of a project that changed since load, with the time of its last write.</summary>
/// <param name="LastWriteTimeUtc">Null when the file no longer exists, so the time of the last edit is unknown.</param>
internal sealed record EditedSourceFile(ProjectId ProjectId, string Path, DateTime? LastWriteTimeUtc);
