namespace RoslynMcpServer.LifecycleTestHost;

public sealed class HostCommand
{
    public string Op { get; set; } = "";
    public string? Path { get; set; }
    public bool? ShadowCopy { get; set; }
    public string? Project { get; set; }
    public string? Text { get; set; }
    public string? Symbol { get; set; }
    public string? NewName { get; set; }
    public string? Configuration { get; set; }
    public string? Platform { get; set; }
    public string? TargetFramework { get; set; }
    public string? OracleSource { get; set; }
    public string? Arguments { get; set; }
    public string? ShadowRoot { get; set; }
    public string? GatePath { get; set; }
    public int TimeoutMs { get; set; } = 15_000;
    public bool NoIncremental { get; set; } = true;
    public int CancelAfterWrites { get; set; }
    public string? CaptureFailureMode { get; set; }
}

public sealed class HostResponse
{
    public bool Ok { get; set; }
    public bool Skip { get; set; }
    public string? Error { get; set; }
    public string? Op { get; set; }

    public bool CacheHit { get; set; }
    public bool ReopenedGraph { get; set; }
    public bool PrepareAttempted { get; set; }
    public bool PrepareInjectedFailure { get; set; }
    public bool LastRefreshStale { get; set; }
    public bool MappingPresent { get; set; }
    public int OverlayPrepareCount { get; set; }
    public int AnalyzerFileIoCount { get; set; }
    public long ProvenanceCaptureCount { get; set; }
    public bool ProvenanceSnapshotPresent { get; set; }
    public string? ProvenanceCaptureStatus { get; set; }
    public int ProvenanceAnalyzerItemCount { get; set; }
    public int ProvenanceConfirmedBindingCount { get; set; }
    public int ProvenanceTempDirectoryCount { get; set; }
    public bool ReusedExisting { get; set; }
    public string? GenerationDirectory { get; set; }
    public long GenerationBytes { get; set; }
    public long ShadowRootBytes { get; set; }
    public bool ShadowEnabled { get; set; }
    public string? ShadowRoot { get; set; }
    public string? LoadedWorkspacePath { get; set; }

    public bool OracleSuccess { get; set; }
    public string? Marker { get; set; }
    public string? OracleFailure { get; set; }
    public string? GeneratedText { get; set; }
    public string? AssemblyIdentity { get; set; }
    public string? LoadedAnalyzerPath { get; set; }
    public string? OverlayAnalyzerPath { get; set; }
    public string? WorkspaceAnalyzerPath { get; set; }
    public string[]? OverlayAnalyzerPaths { get; set; }
    public string[]? WorkspaceAnalyzerPaths { get; set; }
    public string? DocumentText { get; set; }

    public bool DirtyDelivered { get; set; }
    public bool DirtyTimedOut { get; set; }
    public int PendingDirtyCount { get; set; }
    public long DirtyWaitMs { get; set; }

    public int BuildExitCode { get; set; }
    public string? BuildOutput { get; set; }
    public string? FileSha256 { get; set; }
    public Dictionary<string, string>? CsprojSha256 { get; set; }
    public string[]? TemporaryAnalyzerIncludes { get; set; }

    public bool SameSnapshotAfterSymbol { get; set; }
    public string? RenamedTo { get; set; }

    public string? WriteStatus { get; set; }
    public string? WriteReason { get; set; }
    public string[]? SavedPaths { get; set; }
    public bool OverlayPublished { get; set; }
    public bool UnappliedProjectState { get; set; }
    public bool WorkspaceApplied { get; set; }

    public List<RewriteDto>? Rewrite { get; set; }
    public List<LoadedAssemblyDto>? LoadedAssemblies { get; set; }
    public List<LoadedAssemblyDto>? ProcessAnalyzerAssemblies { get; set; }
    public EnvironmentDto? Environment { get; set; }
    public ConcurrencyDto? Concurrency { get; set; }
    public ExecutionDto? Execution { get; set; }
}

public sealed class RewriteDto
{
    public string ProjectName { get; set; } = "";
    public string? OriginalFullPath { get; set; }
    public string MatchedProjectName { get; set; } = "";
    public string? ShadowCopyPath { get; set; }
    public bool Applied { get; set; }
    public string? SkipReason { get; set; }
    public string? Generation { get; set; }
    public string? ReasonCode { get; set; }
    public string? OriginalPathState { get; set; }
    public string? SelectedSourcePath { get; set; }
    public string? SelectedSourcePathState { get; set; }
    public string? SelectionBasis { get; set; }
}

public sealed class LoadedAssemblyDto
{
    public string RequestedPath { get; set; } = "";
    public string Identity { get; set; } = "";
    public string Location { get; set; } = "";
}

public sealed class EnvironmentDto
{
    public string Os { get; set; } = "";
    public string Runtime { get; set; } = "";
    public string? Sdk { get; set; }
    public string? Roslyn { get; set; }
    public string? Bootstrap { get; set; }
    public string? DotNetHost { get; set; }
    public long WorkingSetBytes { get; set; }
    public long PrivateMemoryBytes { get; set; }
}

public sealed class ExecutionDto
{
    public string Status { get; set; } = "";
    public string? Stage { get; set; }
    public string? Reason { get; set; }
    public string? Action { get; set; }
    public string? Project { get; set; }
    public string? Generator { get; set; }
    public string? Generation { get; set; }
    public string? Dependency { get; set; }
    public string? ExpectedPath { get; set; }
    public string? LoadedPath { get; set; }
    public string? Identity { get; set; }
}

public sealed class ConcurrencyDto
{
    public bool BothCompleted { get; set; }
    public string? FirstError { get; set; }
    public string? SecondError { get; set; }
    public bool ShadowEnabledAfter { get; set; }
    public string? MarkerAfter { get; set; }
}
