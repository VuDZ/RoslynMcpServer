using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class GitChangedFilesHelperTests
{
    [Fact]
    public void ParsePorcelainStatus_parses_modified_and_untracked()
    {
        const string output = """
            M  src/Foo.cs
            ?? docs/new.md
            """;

        var files = GitChangedFilesHelper.ParsePorcelainStatus(output);
        Assert.Equal(2, files.Count);
        Assert.Equal("src/Foo.cs", files[0].Path);
        Assert.Equal("M", files[0].Status);
        Assert.Equal("docs/new.md", files[1].Path);
    }
}
