using RoslynMcpServer.Diagnostics;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class DotNetBuildDiagnosticParserTests
{
    [Fact]
    public void Parse_matches_msbuild_file_line_format()
    {
        const string line = @"C:\src\Foo.cs(10,5): error CS0246: The type or namespace name 'Bar' could not be found";
        var parsed = DotNetBuildDiagnosticParser.Parse(line);
        Assert.Single(parsed);
        Assert.Equal("CS0246", parsed[0].Code);
        Assert.Equal("error", parsed[0].Severity, ignoreCase: true);
    }

    [Fact]
    public void Parse_matches_nuget_audit_error_without_path()
    {
        const string line =
            "error NU1904: Warning As Error: Package 'System.Drawing.Common' 5.0.0 has a known critical severity vulnerability, https://github.com/advisories/GHSA-rxg9-xrhp-64gj";
        var parsed = DotNetBuildDiagnosticParser.Parse(line);
        Assert.Single(parsed);
        Assert.Equal("NU1904", parsed[0].Code);
        Assert.Equal("error", parsed[0].Severity, ignoreCase: true);
        Assert.Contains("System.Drawing.Common", parsed[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_matches_nuget_warning_with_project_path()
    {
        const string line =
            @"C:\repo\src\App\App.csproj : error NU1903: Package 'System.Security.Cryptography.Xml' 5.0.0 has a known high severity vulnerability";
        var parsed = DotNetBuildDiagnosticParser.Parse(line);
        Assert.Single(parsed);
        Assert.Equal("NU1903", parsed[0].Code);
        Assert.Contains("App.csproj", parsed[0].Location, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_matches_colon_prefixed_nuget_error()
    {
        const string line = ": error NU1904: Warning As Error: Package 'System.Drawing.Common' 5.0.0 has a known critical severity vulnerability";
        var parsed = DotNetBuildDiagnosticParser.Parse(line);
        Assert.Single(parsed);
        Assert.Equal("NU1904", parsed[0].Code);
        Assert.Equal("(msbuild)", parsed[0].Location);
    }

    [Fact]
    public void Parse_matches_msbuild_task_prefix_nuget_error()
    {
        const string line = "2> : error NU1902: Package 'Foo' 1.0.0 has a known moderate severity vulnerability";
        var parsed = DotNetBuildDiagnosticParser.Parse(line);
        Assert.Single(parsed);
        Assert.Equal("NU1902", parsed[0].Code);
    }

    [Fact]
    public void Parse_matches_msb_project_line()
    {
        const string line = @"C:\repo\App.csproj : error MSB4236: The SDK 'Microsoft.NET.Sdk' specified could not be found.";
        var parsed = DotNetBuildDiagnosticParser.Parse(line);
        Assert.Single(parsed);
        Assert.Equal("MSB4236", parsed[0].Code);
    }

    [Fact]
    public void Parse_skips_section_headers()
    {
        const string text = """
            --- dotnet build -v:minimal (exit 1, stdout 12 chars, stderr 0 chars) ---
            Build FAILED.
            error NU1904: audit message
            """;
        var parsed = DotNetBuildDiagnosticParser.Parse(text);
        Assert.Single(parsed);
        Assert.Equal("NU1904", parsed[0].Code);
    }
}
