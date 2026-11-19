namespace RoslynMcpServer.Services.Testing.Baseline.Models;

internal enum BaselineChangeKind
{
    MemberModified,
    MemberAdded,
    MemberRemoved,
    TypeChanged,
    FileChanged,
    ProjectChanged,
}
