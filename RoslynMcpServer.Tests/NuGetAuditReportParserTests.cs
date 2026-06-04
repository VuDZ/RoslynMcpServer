using RoslynMcpServer.Diagnostics;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class NuGetAuditReportParserTests
{
    [Fact]
    public void Parse_extracts_transitive_vulnerability()
    {
        const string json = """
            {
              "projects": [
                {
                  "path": "C:/app/src/App.csproj",
                  "frameworks": [
                    {
                      "framework": "net10.0",
                      "transitivePackages": [
                        {
                          "id": "System.Drawing.Common",
                          "resolvedVersion": "5.0.0",
                          "vulnerabilities": [
                            {
                              "severity": "Critical",
                              "advisoryurl": "https://github.com/advisories/GHSA-rxg9-xrhp-64gj"
                            }
                          ]
                        }
                      ]
                    }
                  ]
                }
              ]
            }
            """;

        var entries = NuGetAuditReportParser.Parse(json);
        Assert.Single(entries);
        Assert.Equal("System.Drawing.Common", entries[0].PackageId);
        Assert.True(entries[0].IsTransitive);
        Assert.Equal("Critical", entries[0].Vulnerabilities[0].Severity);
    }

    [Fact]
    public void FormatMarkdownReport_empty()
    {
        var md = NuGetAuditReportParser.FormatMarkdownReport("C:/x.sln", []);
        Assert.Contains("No packages with known vulnerabilities", md, StringComparison.Ordinal);
    }
}
