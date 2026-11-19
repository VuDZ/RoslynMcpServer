using RoslynMcpServer.Services.Testing.Narrowing.Models;

namespace RoslynMcpServer.Services.Testing.Models;

internal sealed record TestImpactProjectRun(DotNetTestResult Execution, TestNarrowingResult? Analysis = null);
