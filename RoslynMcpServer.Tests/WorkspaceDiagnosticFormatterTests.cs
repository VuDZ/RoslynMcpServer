using RoslynMcpServer.Diagnostics;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class WorkspaceDiagnosticFormatterTests
{
    [Fact]
    public void Format_nuget_audit_as_warning_not_failure()
    {
        const string msg =
            "Package 'System.Drawing.Common' 5.0.0 has a known critical severity vulnerability, https://github.com/advisories/GHSA-rxg9-xrhp-64gj";
        var formatted = WorkspaceDiagnosticFormatter.Format("Failure", msg);
        Assert.StartsWith("Warning (NuGet audit):", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void IsBlockingLoadFailure_false_for_nuget_audit()
    {
        const string msg =
            "Package 'System.Drawing.Common' 5.0.0 has a known critical severity vulnerability, https://github.com/advisories/GHSA-rxg9-xrhp-64gj";
        var formatted = WorkspaceDiagnosticFormatter.Format("Failure", msg);
        Assert.False(WorkspaceDiagnosticFormatter.IsBlockingLoadFailure(formatted));
    }
}
