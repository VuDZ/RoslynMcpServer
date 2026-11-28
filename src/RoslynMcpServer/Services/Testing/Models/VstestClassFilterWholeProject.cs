using RoslynMcpServer.Services.Testing.Narrowing.Models;

namespace RoslynMcpServer.Services.Testing.Models;

internal sealed record VstestClassFilterWholeProject(TestNarrowingWholeReason Reason) : VstestClassFilter;
