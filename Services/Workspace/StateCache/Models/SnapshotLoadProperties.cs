namespace RoslynMcpServer.Services.Workspace.StateCache.Models;

/// <summary>Null means absent in request/globals; evaluated values have their own source and unknown marker.</summary>
public sealed record SnapshotLoadProperties(SnapshotPropertySet RawRequested, SnapshotPropertySet LoaderGlobals);
