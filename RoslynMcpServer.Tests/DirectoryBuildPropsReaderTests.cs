using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class DirectoryBuildPropsReaderTests
{
    [Fact]
    public void Parse_splits_target_frameworks()
    {
        const string props = """
            <Project>
              <PropertyGroup>
                <TargetFrameworks>netstandard2.0;net10.0</TargetFrameworks>
              </PropertyGroup>
            </Project>
            """;

        var tfms = DirectoryBuildPropsReader.Parse(props);
        Assert.Equal(2, tfms.Count);
        Assert.Equal("netstandard2.0", tfms[0]);
        Assert.Equal("net10.0", tfms[1]);
    }

    [Fact]
    public void Parse_prefers_target_frameworks_over_singular()
    {
        const string props = """
            <Project>
              <TargetFramework>net8.0</TargetFramework>
              <TargetFrameworks>net10.0</TargetFrameworks>
            </Project>
            """;

        var tfms = DirectoryBuildPropsReader.Parse(props);
        Assert.Equal(["net10.0"], tfms);
    }

    [Fact]
    public void Parse_singular_target_framework()
    {
        var tfms = DirectoryBuildPropsReader.Parse("<TargetFramework>net10.0</TargetFramework>");
        Assert.Equal(["net10.0"], tfms);
    }

    [Fact]
    public void ListTargetFrameworks_walks_up_to_directory_build_props()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpTfmProps", Guid.NewGuid().ToString("N"));
        var src = Path.Combine(root, "Src");
        Directory.CreateDirectory(src);
        try
        {
            File.WriteAllText(
                Path.Combine(root, "Directory.Build.props"),
                "<Project><TargetFrameworks>netstandard2.0;net10.0</TargetFrameworks></Project>");
            var sln = Path.Combine(src, "App.sln");
            File.WriteAllText(sln, string.Empty);

            var tfms = DirectoryBuildPropsReader.ListTargetFrameworks(sln);
            Assert.Equal(["netstandard2.0", "net10.0"], tfms);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
