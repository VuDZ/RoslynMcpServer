namespace RoslynMcpServer.Services.Workspace.StateCache.Models;

public sealed record SnapshotProject(
    SnapshotProjectKey Key,
    SnapshotEvaluatedProperties EvaluatedProperties,
    string Name,
    string AssemblyName,
    string Language,
    string? OutputFilePath,
    string? OutputRefFilePath,
    string? DefaultNamespace,
    string? CompilationAssemblyPath,
    string? GeneratedFilesOutputDirectory,
    SnapshotParseOptions ParseOptions,
    SnapshotCompilationOptions CompilationOptions,
    SnapshotDocument[] Documents,
    SnapshotProjectReference[] ProjectReferences,
    SnapshotMetadataReference[] MetadataReferences,
    SnapshotAnalyzerReference[] AnalyzerReferences);
