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

    [Fact]
    public void IsMissingCompileTarget_still_blocking()
    {
        const string msg =
            "Msbuild failed when processing the file 'C:\\src\\Contracts.csproj' with message: "
            + "Project does not contain 'Compile' target.";
        var formatted = WorkspaceDiagnosticFormatter.Format("Failure", msg);
        Assert.StartsWith("Failure:", formatted, StringComparison.Ordinal);
        Assert.True(WorkspaceDiagnosticFormatter.IsMissingCompileTarget(formatted));
        Assert.True(WorkspaceDiagnosticFormatter.IsBlockingLoadFailure(formatted));
        Assert.Equal(
            @"C:\src\Contracts.csproj",
            WorkspaceDiagnosticFormatter.TryGetProcessedProjectPath(formatted));
    }

    [Fact]
    public void Format_include_openapi_analyzers_deprecation_as_design_time_warning()
    {
        const string msg =
            "Msbuild failed when processing the file "
            + "'C:\\data\\ast\\monorepo\\src\\infrastructure\\kpc\\eis\\Src\\EcomIntegrationService.TrustedParty.Api\\EcomIntegrationService.TrustedParty.Api.csproj' "
            + "with message: The IncludeOpenAPIAnalyzers property and its associated MVC API analyzers are deprecated and will be removed in a future release.";
        var formatted = WorkspaceDiagnosticFormatter.Format("Failure", msg);
        Assert.StartsWith("Warning (MSBuild design-time):", formatted, StringComparison.Ordinal);
        Assert.False(WorkspaceDiagnosticFormatter.IsBlockingLoadFailure(formatted));
    }

    [Fact]
    public void IsBlockingLoadFailure_false_for_aspdepr007_warning_code()
    {
        const string msg =
            "Msbuild failed when processing the file 'Foo.Api.csproj' with message: "
            + "warning ASPDEPR007: The IncludeOpenAPIAnalyzers property and its associated MVC API analyzers "
            + "are deprecated and will be removed in a future release.";
        var formatted = WorkspaceDiagnosticFormatter.Format("Failure", msg);
        Assert.StartsWith("Warning (MSBuild design-time):", formatted, StringComparison.Ordinal);
        Assert.False(WorkspaceDiagnosticFormatter.IsBlockingLoadFailure(formatted));
    }

    [Fact]
    public void Format_processor_architecture_mismatch_as_design_time_warning()
    {
        const string msg =
            "Msbuild failed when processing the file "
            + "'C:\\data\\ast\\monorepo\\src\\infrastructure\\kpc\\eis\\Src\\EcomIntegrationService.Consumers.Tests\\EcomIntegrationService.Consumers.Tests.csproj' "
            + "with message: There was a mismatch between the processor architecture of the project being built \"MSIL\" "
            + "and the processor architecture of the reference "
            + "\"C:\\data\\ast\\monorepo\\src\\infrastructure\\kpc\\eis\\Src\\EcomIntegrationService.Consumers\\bin\\Debug\\net10.0\\ProducerConsumer.Eis.Consumers.dll\", "
            + "\"AMD64\". This mismatch may cause runtime failures. Please consider changing the targeted processor architecture "
            + "of your project through the Configuration Manager so as to align the processor architectures between your project and references, "
            + "or take a dependency on references with a processor architecture that matches the targeted processor architecture of your project.";
        var formatted = WorkspaceDiagnosticFormatter.Format("Failure", msg);
        Assert.StartsWith("Warning (MSBuild design-time):", formatted, StringComparison.Ordinal);
        Assert.False(WorkspaceDiagnosticFormatter.IsBlockingLoadFailure(formatted));
    }

    [Fact]
    public void Format_analyzer_project_without_metadata_reference_as_design_time_warning()
    {
        const string msg =
            "Found project reference without a matching metadata reference: "
            + @"C:\data\ast\monorepo\src\infrastructure\kpc\eis\Src\EcomIntegrationService.Analyzers\EcomIntegrationService.Analyzers.csproj";
        var formatted = WorkspaceDiagnosticFormatter.Format("Failure", msg);
        Assert.StartsWith("Warning (MSBuild design-time):", formatted, StringComparison.Ordinal);
        Assert.False(WorkspaceDiagnosticFormatter.IsBlockingLoadFailure(formatted));
    }

    [Fact]
    public void Format_unknown_wrapped_message_without_error_code_as_design_time_warning()
    {
        const string msg =
            "Msbuild failed when processing the file 'Foo.csproj' with message: "
            + "Some future SDK advisory without an MSBuild code.";
        var formatted = WorkspaceDiagnosticFormatter.Format("Failure", msg);
        Assert.StartsWith("Warning (MSBuild design-time):", formatted, StringComparison.Ordinal);
        Assert.False(WorkspaceDiagnosticFormatter.IsBlockingLoadFailure(formatted));
    }

    [Fact]
    public void IsBlockingLoadFailure_true_for_wrapped_sdk_not_found()
    {
        const string msg =
            "Msbuild failed when processing the file 'Foo.csproj' with message: "
            + "The SDK 'Microsoft.NET.Sdk' specified could not be found.";
        var formatted = WorkspaceDiagnosticFormatter.Format("Failure", msg);
        Assert.StartsWith("Failure:", formatted, StringComparison.Ordinal);
        Assert.True(WorkspaceDiagnosticFormatter.IsBlockingLoadFailure(formatted));
    }

    [Fact]
    public void IsBlockingLoadFailure_true_for_xmakeelements_buildhost_crash()
    {
        const string msg =
            "An exception of type System.TypeInitializationException was thrown: "
            + "The type initializer for 'Microsoft.Build.Shared.XMakeElements' threw an exception.";
        var formatted = WorkspaceDiagnosticFormatter.Format("Failure", msg);
        Assert.StartsWith("Failure:", formatted, StringComparison.Ordinal);
        Assert.True(WorkspaceDiagnosticFormatter.IsBlockingLoadFailure(formatted));
        Assert.True(WorkspaceDiagnosticFormatter.IsHardMsBuildLoadFailure(msg));
    }

    [Fact]
    public void IsBlockingLoadFailure_true_for_unwrapped_generic_failure()
    {
        var formatted = WorkspaceDiagnosticFormatter.Format("Failure", "Unable to open the solution cache.");
        Assert.StartsWith("Failure:", formatted, StringComparison.Ordinal);
        Assert.True(WorkspaceDiagnosticFormatter.IsBlockingLoadFailure(formatted));
    }
}
