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

    [Fact]
    public void Format_package_will_not_be_pruned_as_warning()
    {
        const string msg = "PackageReference System.Formats.Asn1 will not be pruned. Consider removing this package from your dependencies, as it is unused.";
        var formatted = WorkspaceDiagnosticFormatter.Format("Failure", msg);
        Assert.StartsWith("Warning (NuGet prune):", formatted, StringComparison.Ordinal);
        Assert.False(WorkspaceDiagnosticFormatter.IsBlockingLoadFailure(formatted));
    }

    [Fact]
    public void Format_failed_to_load_prune_package_data_as_warning()
    {
        const string msg = "Failed to load prune package data from NuGet, please verify restore targets.";
        var formatted = WorkspaceDiagnosticFormatter.Format("Failure", msg);
        Assert.StartsWith("Warning (NuGet prune):", formatted, StringComparison.Ordinal);
        Assert.False(WorkspaceDiagnosticFormatter.IsBlockingLoadFailure(formatted));
    }

    [Fact]
    public void IsBlockingLoadFailure_true_for_generic_failure()
    {
        var formatted = WorkspaceDiagnosticFormatter.Format("Failure", "Msbuild failed when processing the file 'Foo.csproj' with message: The project file could not be loaded.");
        Assert.StartsWith("Failure:", formatted, StringComparison.Ordinal);
        Assert.True(WorkspaceDiagnosticFormatter.IsBlockingLoadFailure(formatted));
    }

    [Fact]
    public void IsBlockingLoadFailure_true_for_msbuild_error_line()
    {
        const string formatted = "C:\\src\\Foo.csproj(12,5): error MSB4019: The imported project was not found.";
        Assert.True(WorkspaceDiagnosticFormatter.IsBlockingLoadFailure(formatted));
    }
}
