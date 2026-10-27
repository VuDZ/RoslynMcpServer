namespace RoslynMcpServer.Services.Workspace;

/// <summary>
/// One process observed while looking for a BuildHost the workspace client did not register.
/// <see cref="CommandLine"/> is null when it could not be read; that is not evidence the process is a BuildHost.
/// </summary>
internal readonly record struct BuildHostProcessObservation(
    int Id,
    int? ParentId,
    DateTime StartTime,
    string ProcessName,
    string? CommandLine,
    bool HasExited);
