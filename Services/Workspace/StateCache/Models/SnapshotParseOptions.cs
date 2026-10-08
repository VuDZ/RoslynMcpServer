namespace RoslynMcpServer.Services.Workspace.StateCache.Models;

public sealed record SnapshotParseOptions(
    string SpecifiedLanguageVersion,
    string LanguageVersion,
    string DocumentationMode,
    string Kind,
    string[] PreprocessorSymbols,
    SnapshotNameValue[] Features);
