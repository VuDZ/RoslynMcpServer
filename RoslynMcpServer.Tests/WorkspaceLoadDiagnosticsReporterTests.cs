using RoslynMcpServer.Diagnostics;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class WorkspaceLoadDiagnosticsReporterTests
{
    [Fact]
    public void FormatSection_empty_returns_empty()
    {
        Assert.Equal(string.Empty, WorkspaceLoadDiagnosticsReporter.FormatSection([], briefOutput: true));
        Assert.Equal(string.Empty, WorkspaceLoadDiagnosticsReporter.FormatSection([], briefOutput: false));
    }

    [Fact]
    public void FormatBrief_groups_categories_and_codes()
    {
        var diagnostics = new[]
        {
            "Warning (MSBuild design-time): Msbuild failed when processing the file 'A.csproj' with message: warning MSB3270: architecture mismatch.",
            "Warning (MSBuild design-time): Msbuild failed when processing the file 'B.csproj' with message: warning MSB3270: architecture mismatch.",
            "Warning (MSBuild design-time): warning ASPDEPR007: IncludeOpenAPIAnalyzers is deprecated.",
            "Warning (NuGet compat): warning NU1701: Package 'X' was restored using .NETFramework.",
            "Warning (NuGet compat): warning NU1701: Package 'Y' was restored using .NETFramework.",
            "Warning (NuGet prune): PackageReference Foo will not be pruned.",
        };

        var brief = WorkspaceLoadDiagnosticsReporter.FormatBrief(diagnostics);

        Assert.StartsWith("Workspace diagnostics (brief): 6 warning(s).", brief, StringComparison.Ordinal);
        Assert.Contains("Categories: MSBuild design-time 3, NuGet compat 2, NuGet prune 1.", brief, StringComparison.Ordinal);
        Assert.Contains("Codes: MSB3270×2, NU1701×2, ASPDEPR007×1.", brief, StringComparison.Ordinal);
        Assert.Contains("Pass briefOutput=false for full messages.", brief, StringComparison.Ordinal);
        Assert.DoesNotContain("Workspace diagnostics:", brief, StringComparison.Ordinal);
        Assert.DoesNotContain("**Note:**", brief, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatBrief_single_warning_uses_singular()
    {
        var brief = WorkspaceLoadDiagnosticsReporter.FormatBrief(
            ["Warning (NuGet audit): Package 'Foo' 1.0.0 has a known vulnerability, https://github.com/advisories/GHSA-rxg9-xrhp-64gj"]);

        Assert.Contains("1 warning.", brief, StringComparison.Ordinal);
        Assert.Contains("Categories: NuGet audit 1.", brief, StringComparison.Ordinal);
        Assert.Contains("GHSA-rxg9-xrhp-64gj×1", brief, StringComparison.Ordinal);
        Assert.Contains("Pass briefOutput=false for full messages.", brief, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatBrief_unknown_kind_is_other_without_codes()
    {
        var brief = WorkspaceLoadDiagnosticsReporter.FormatBrief(
            ["Warning: some design-time advisory without an MSBuild code."]);

        Assert.Contains("Categories: other 1.", brief, StringComparison.Ordinal);
        Assert.DoesNotContain("Codes:", brief, StringComparison.Ordinal);
        Assert.Contains("Pass briefOutput=false for full messages.", brief, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatBrief_caps_codes_at_max()
    {
        var diagnostics = Enumerable.Range(1, WorkspaceLoadDiagnosticsReporter.MaxBriefCodes + 2)
            .Select(i => $"Warning (MSBuild design-time): warning MSB{3000 + i}: item {i}.")
            .ToArray();

        var brief = WorkspaceLoadDiagnosticsReporter.FormatBrief(diagnostics);
        var codeSection = brief[(brief.IndexOf("Codes:", StringComparison.Ordinal) + "Codes:".Length)..];
        var codeSectionEnd = codeSection.IndexOf(". Pass", StringComparison.Ordinal);
        var listed = codeSection[..codeSectionEnd].Split(',', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(WorkspaceLoadDiagnosticsReporter.MaxBriefCodes, listed.Length);
        Assert.Contains("MSB3001×1", brief, StringComparison.Ordinal);
        Assert.DoesNotContain($"MSB{3000 + WorkspaceLoadDiagnosticsReporter.MaxBriefCodes + 2}×", brief, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatVerbose_lists_messages_and_notes()
    {
        var diagnostics = new[]
        {
            "Warning (MSBuild design-time): warning ASPDEPR007: IncludeOpenAPIAnalyzers is deprecated.",
            "Warning (NuGet compat): warning NU1701: Package 'X' was restored using .NETFramework.",
        };

        var verbose = WorkspaceLoadDiagnosticsReporter.FormatVerbose(diagnostics);

        Assert.StartsWith("Workspace diagnostics:", verbose, StringComparison.Ordinal);
        Assert.Contains("- Warning (MSBuild design-time):", verbose, StringComparison.Ordinal);
        Assert.Contains("- Warning (NuGet compat):", verbose, StringComparison.Ordinal);
        Assert.Contains("**Note:** Design-time MSBuild warnings", verbose, StringComparison.Ordinal);
        Assert.Contains("**Note:** NuGet TFM-compat advisories", verbose, StringComparison.Ordinal);
        Assert.DoesNotContain("briefOutput=false", verbose, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatSection_routes_brief_and_verbose()
    {
        var diagnostics = new[] { "Warning (NuGet prune): PackageReference Foo will not be pruned." };

        var brief = WorkspaceLoadDiagnosticsReporter.FormatSection(diagnostics, briefOutput: true);
        var verbose = WorkspaceLoadDiagnosticsReporter.FormatSection(diagnostics, briefOutput: false);

        Assert.Contains("Workspace diagnostics (brief):", brief, StringComparison.Ordinal);
        Assert.StartsWith("Workspace diagnostics:", verbose, StringComparison.Ordinal);
        Assert.Contains("**Note:** NuGet prune", verbose, StringComparison.Ordinal);
    }
}
