using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services.Models;

/// <summary>A workspace document whose file did not exist when the workspace was opened.</summary>
internal sealed record MissingSourceDocument(DocumentId Id, TextDocumentKind Kind, string Path);
