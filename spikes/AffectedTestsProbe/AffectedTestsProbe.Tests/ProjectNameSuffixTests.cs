using Microsoft.CodeAnalysis;
using Xunit;

namespace AffectedTestsProbe.Tests;

public sealed class ProjectNameSuffixTests
{
    [Theory]
    [InlineData("Prod(netstandard2.0)", "Prod", "netstandard2.0")]
    [InlineData("Prod (net10.0)", "Prod", "net10.0")]
    [InlineData("Tests(net9.0)", "Tests", "net9.0")]
    [InlineData("Tests", "Tests", "unspecified")]
    public void Split_AcceptsTheWorkspaceSuffixWithOrWithoutASpace(string projectName, string name, string framework)
    {
        var split = ProjectNameSuffix.Split(projectName);

        Assert.Equal(name, split.Name);
        Assert.Equal(framework, split.TargetFramework);
        Assert.Equal(framework, ProjectKey.TargetFrameworkOf(projectName));
    }

    [Fact]
    public void NameMatches_StripsTheUnspacedFrameworkSuffix()
    {
        using var workspace = new AdhocWorkspace();
        var id = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution.AddProject(id, "Tests(net10.0)", "Tests", LanguageNames.CSharp);
        Assert.True(workspace.TryApplyChanges(solution));
        var project = workspace.CurrentSolution.GetProject(id);

        Assert.NotNull(project);
        Assert.True(RootIndexBuilder.NameMatches(project, ["Tests"]));
        Assert.True(RootIndexBuilder.NameMatches(project, ["Tests(net10.0)"]));
        Assert.False(RootIndexBuilder.NameMatches(project, ["Other"]));
    }
}
