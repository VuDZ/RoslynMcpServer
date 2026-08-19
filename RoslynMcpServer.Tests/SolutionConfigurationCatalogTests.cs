using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class SolutionConfigurationCatalogTests
{
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
