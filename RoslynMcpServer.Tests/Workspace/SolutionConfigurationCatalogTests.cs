using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

public sealed class SolutionConfigurationCatalogTests
{
    [Theory]
    [InlineData(".sln", "Microsoft Visual Studio Solution File, Format Version 12.00\nGlobal\nGlobalSection(SolutionConfigurationPlatforms) = preSolution\nReview|Any CPU = Review|Any CPU\nEndGlobalSection\nEndGlobal")]
    [InlineData(".slnx", """<Solution><Configurations><BuildType Name="Review" /><Platform Name="Any CPU" /></Configurations></Solution>""")]
    public void Filter_configuration_guidance_uses_backing_solution(string extension, string solution)
    {
        var root = Directory.CreateTempSubdirectory("mcp-filter-config-");
        try
        {
            var solutionPath = Path.Combine(root.FullName, "App" + extension);
            var filterPath = Path.Combine(root.CreateSubdirectory("filters").FullName, "App.SLNF");
            File.WriteAllText(solutionPath, solution);
            File.WriteAllText(filterPath, $$"""
                {"solution":{"path":"..\\App{{extension}}","projects":[]} }
                """);

            Assert.Equal(["Review|Any CPU"], SolutionConfigurationCatalog.ListConfigurationPlatforms(filterPath));
            var guidance = WorkspaceLoadGuidance.FormatNoMatchingTestsAgentHint(solutionPath, null, filterPath);
            Assert.DoesNotContain("**Mismatch:**", guidance, StringComparison.Ordinal);

            File.WriteAllText(filterPath, "{");
            Assert.Empty(SolutionConfigurationCatalog.ListConfigurationPlatforms(filterPath));
            var invalidGuidance = WorkspaceLoadGuidance.FormatNoMatchingTestsAgentHint(solutionPath, null, filterPath);
            Assert.Contains("filter's backing `.sln` or `.slnx`", invalidGuidance, StringComparison.Ordinal);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void ListFromSln_reads_configuration_platforms()
    {
        const string sln = """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Global
            	GlobalSection(SolutionConfigurationPlatforms) = preSolution
            		Debug|Any CPU = Debug|Any CPU
            		kart|x64 = kart|x64
            	EndGlobalSection
            EndGlobal
            """;

        var entries = SolutionConfigurationCatalog.ListFromSln(sln);
        Assert.Equal(2, entries.Count);
        Assert.Contains("Debug|Any CPU", entries);
        Assert.Contains("kart|x64", entries);
    }

    [Fact]
    public void ListFromSlnx_cartesian_buildtype_and_platform()
    {
        const string slnx = """
            <Solution>
              <Configurations>
                <BuildType Name="Debug" />
                <BuildType Name="kart" />
                <Platform Name="Any CPU" />
                <Platform Name="x64" />
              </Configurations>
            </Solution>
            """;

        var entries = SolutionConfigurationCatalog.ListFromSlnx(slnx);
        Assert.Contains("Debug|Any CPU", entries);
        Assert.Contains("kart|x64", entries);
        Assert.Equal(4, entries.Count);
    }
}
