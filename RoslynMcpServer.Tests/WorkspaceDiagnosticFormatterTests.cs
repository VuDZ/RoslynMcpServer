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

    [Fact]
    public void Format_nu1701_as_compat_warning_not_failure()
    {
        const string msg =
            "Msbuild failed when processing the file 'Ucp.Tests.Snippets.csproj' with message: "
            + "warning NU1701: Package 'Microsoft.VisualStudio.SDK' 17.0.32112.339 was restored using "
            + "'.NETFramework,Version=v4.7.2' instead of the project target framework '.NETCoreApp,Version=v10.0'. "
            + "This package may not be fully compatible with your project.";
        var formatted = WorkspaceDiagnosticFormatter.Format("Failure", msg);
        Assert.StartsWith("Warning (NuGet compat):", formatted, StringComparison.Ordinal);
        Assert.False(WorkspaceDiagnosticFormatter.IsBlockingLoadFailure(formatted));
    }

    [Fact]
    public void Format_tfm_compat_text_without_code_as_warning()
    {
        const string msg =
            "Msbuild failed when processing the file 'Foo.csproj' with message: "
            + "Package 'Microsoft.VisualStudio.SDK' 17.0.0 was restored using '.NETFramework,Version=v4.7.2' "
            + "instead of the project target framework '.NETCoreApp,Version=v10.0'. "
            + "This package may not be fully compatible with your project.";
        var formatted = WorkspaceDiagnosticFormatter.Format("Failure", msg);
        Assert.StartsWith("Warning (NuGet compat):", formatted, StringComparison.Ordinal);
        Assert.False(WorkspaceDiagnosticFormatter.IsBlockingLoadFailure(formatted));
    }

    [Fact]
    public void IsBlockingLoadFailure_false_for_warning_kind_msbuild_failed_wrapper()
    {
        const string formatted =
            "Warning: Msbuild failed when processing the file 'Foo.csproj' with message: "
            + "warning NU1701: Package 'X' 1.0.0 was restored using '.NETFramework,Version=v4.7.2' "
            + "instead of the project target framework '.NETCoreApp,Version=v10.0'. "
            + "This package may not be fully compatible with your project.";
        Assert.False(WorkspaceDiagnosticFormatter.IsBlockingLoadFailure(formatted));
    }

    [Fact]
    public void IsBlockingLoadFailure_false_for_wrapped_warning_without_remap()
    {
        const string formatted =
            "Failure: Msbuild failed when processing the file 'Foo.csproj' with message: "
            + "warning NU1603: Foo depends on Bar (>= 2.0.0) but Bar 1.0.0 was resolved.";
        Assert.False(WorkspaceDiagnosticFormatter.IsBlockingLoadFailure(formatted));
    }

    [Fact]
    public void IsBlockingLoadFailure_true_for_wrapped_msbuild_error()
    {
        const string formatted =
            "Failure: Msbuild failed when processing the file 'Foo.csproj' with message: "
            + "error MSB4019: The imported project was not found.";
        Assert.True(WorkspaceDiagnosticFormatter.IsBlockingLoadFailure(formatted));
    }

    [Fact]
    public void IsMissingTargetFrameworkEvaluation_still_blocking()
    {
        const string msg =
            "Msbuild failed when processing the file 'D:\\m\\src\\generated\\Foo.csproj' with message: "
            + "The \"ResolvePackageAssets\" task was not given a value for the required parameter \"TargetFramework\".";
        var formatted = WorkspaceDiagnosticFormatter.Format("Failure", msg);
        Assert.StartsWith("Failure:", formatted, StringComparison.Ordinal);
        Assert.True(WorkspaceDiagnosticFormatter.IsMissingTargetFrameworkEvaluation(formatted));
        Assert.True(WorkspaceDiagnosticFormatter.IsBlockingLoadFailure(formatted));
        Assert.Equal(
            @"D:\m\src\generated\Foo.csproj",
            WorkspaceDiagnosticFormatter.TryGetProcessedProjectPath(formatted));
    }
}
