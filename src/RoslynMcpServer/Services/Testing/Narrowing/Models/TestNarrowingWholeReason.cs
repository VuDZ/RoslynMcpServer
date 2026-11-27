namespace RoslynMcpServer.Services.Testing.Narrowing.Models;

/// <summary>Why a project, or one stage of it, runs whole.</summary>
/// <param name="Subject">The path, project or class the reason is about; null when it is about the whole project.</param>
/// <param name="Message">One line for the answer.</param>
internal sealed record TestNarrowingWholeReason(TestNarrowingWholeKind Kind, string? Subject, string Message);
