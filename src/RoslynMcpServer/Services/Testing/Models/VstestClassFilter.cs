using RoslynMcpServer.Services.Testing.Narrowing.Models;

namespace RoslynMcpServer.Services.Testing.Models;

/// <summary>A VSTest filter that runs whole test classes, or why the project has to run whole instead.</summary>
/// <param name="Filter">Null when there are no classes or when <paramref name="Whole"/> is set.</param>
internal sealed record VstestClassFilter(string? Filter, TestNarrowingWholeReason? Whole);
