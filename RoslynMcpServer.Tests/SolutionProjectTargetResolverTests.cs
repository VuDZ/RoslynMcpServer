using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class SolutionProjectTargetResolverTests
{
    private const string NestedSln = """
        Microsoft Visual Studio Solution File, Format Version 12.00
        Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "src", "src", "{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}"
        EndProject
        Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "tools", "tools", "{BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB}"
        EndProject
        Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "tests", "tests", "{CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC}"
        EndProject
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "src\App\App.csproj", "{11111111-1111-1111-1111-111111111111}"
        EndProject
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "My.Project", "lib\My.Project.csproj", "{22222222-2222-2222-2222-222222222222}"
        EndProject
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Cli", "tools\Cli\Cli.csproj", "{33333333-3333-3333-3333-333333333333}"
        EndProject
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "tests\App\App.csproj", "{44444444-4444-4444-4444-444444444444}"
        EndProject
        Global
        	GlobalSection(NestedProjects) = preSolution
        		{BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB} = {AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}
        		{11111111-1111-1111-1111-111111111111} = {AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}
        		{33333333-3333-3333-3333-333333333333} = {BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB}
        		{44444444-4444-4444-4444-444444444444} = {CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC}
        	EndGlobalSection
        EndGlobal
        """;

    [Fact]
    public void ListFromSln_root_project_keeps_display_name()
    {
        var projects = SolutionProjectTargetResolver.ListFromSln(NestedSln);
        var mine = Assert.Single(projects, p => p.DisplayName == "My.Project");
        Assert.Equal("My.Project", mine.VirtualPath);
        Assert.Equal("My_Project", mine.TargetName);
        Assert.Equal("My.Project", mine.FileNameWithoutExtension);
    }

    [Fact]
    public void ListFromSln_nested_folders_build_virtual_path()
    {
        var projects = SolutionProjectTargetResolver.ListFromSln(NestedSln);
        var cli = Assert.Single(projects, p => p.DisplayName == "Cli");
        Assert.Equal(@"src\tools\Cli", cli.VirtualPath);
        Assert.Equal(@"src\tools\Cli", cli.TargetName);
    }

    [Fact]
    public void SanitizeProjectNameForTarget_replaces_msbuild_reserved_chars()
    {
        Assert.Equal("My_Project", SolutionProjectTargetResolver.SanitizeProjectNameForTarget("My.Project"));
        Assert.Equal("Foo_Bar_", SolutionProjectTargetResolver.SanitizeProjectNameForTarget("Foo(Bar)"));
        Assert.Equal("A_B_C_D_E_F_G", SolutionProjectTargetResolver.SanitizeProjectNameForTarget("A%B$C@D;E.F'G"));
    }

    [Fact]
    public void ListFromSlnx_folders_and_absolute_names()
    {
        const string slnx = """
            <Solution>
              <Folder Name="/src/">
                <Folder Name="/src/tools/">
                  <Project Path="tools/Cli/Cli.csproj" />
                </Folder>
                <Project Path="src/App/App.csproj" />
              </Folder>
              <Project Path="lib/My.Project/My.Project.csproj" />
            </Solution>
            """;

        var projects = SolutionProjectTargetResolver.ListFromSlnx(slnx);
        Assert.Equal(3, projects.Count);
        Assert.Contains(projects, p => p.DisplayName == "Cli" && p.VirtualPath == @"src\tools\Cli");
        Assert.Contains(projects, p => p.DisplayName == "App" && p.VirtualPath == @"src\App");
        var mine = Assert.Single(projects, p => p.DisplayName == "My.Project");
        Assert.Equal("My.Project", mine.VirtualPath);
        Assert.Equal("My_Project", mine.TargetName);
    }

    [Fact]
    public void TryResolve_matches_display_name_when_unique()
    {
        var path = WriteTempSln(NestedSln, ".sln");
        try
        {
            var result = SolutionProjectTargetResolver.TryResolve(path, "Cli");
            Assert.True(result.Success);
            Assert.Equal("Cli", result.DisplayName);
            Assert.Equal(@"src\tools\Cli", result.TargetName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryResolve_matches_virtual_path_when_display_name_is_ambiguous()
    {
        var path = WriteTempSln(NestedSln, ".sln");
        try
        {
            var ambiguous = SolutionProjectTargetResolver.TryResolve(path, "App");
            Assert.False(ambiguous.Success);
            Assert.Contains("matches 2 projects", ambiguous.ErrorMessage, StringComparison.Ordinal);
            Assert.Contains(@"src\App", ambiguous.ErrorMessage, StringComparison.Ordinal);
            Assert.Contains(@"tests\App", ambiguous.ErrorMessage, StringComparison.Ordinal);

            var byPath = SolutionProjectTargetResolver.TryResolve(path, @"src\App");
            Assert.True(byPath.Success);
            Assert.Equal(@"src\App", byPath.VirtualPath);

            var byForward = SolutionProjectTargetResolver.TryResolve(path, "src/App");
            Assert.True(byForward.Success);
            Assert.Equal(@"src\App", byForward.TargetName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryResolve_missing_name_lists_projects()
    {
        var path = WriteTempSln(NestedSln, ".sln");
        try
        {
            var result = SolutionProjectTargetResolver.TryResolve(path, "Missing");
            Assert.False(result.Success);
            Assert.Contains("was not found", result.ErrorMessage, StringComparison.Ordinal);
            Assert.Contains("Cli → src\\tools\\Cli", result.ErrorMessage, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryResolve_rejects_csproj_extension()
    {
        var path = WriteTempSln("<Project Sdk=\"Microsoft.NET.Sdk\" />", ".csproj");
        try
        {
            var result = SolutionProjectTargetResolver.TryResolve(path, "App");
            Assert.False(result.Success);
            Assert.Contains(".sln", result.ErrorMessage, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryResolve_skips_solution_folders_and_items()
    {
        const string sln = """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "docs", "docs", "{AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA}"
            EndProject
            Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "Solution Items", "Solution Items", "{BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB}"
            ProjectSection(SolutionItems) = preProject
            	README.md = README.md
            EndProjectSection
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "App\App.csproj", "{11111111-1111-1111-1111-111111111111}"
            EndProject
            """;

        var projects = SolutionProjectTargetResolver.ListFromSln(sln);
        var app = Assert.Single(projects);
        Assert.Equal("App", app.DisplayName);
        Assert.Equal("App", app.TargetName);
    }

    private static string WriteTempSln(string content, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), "roslyn-mcp-sln-" + Guid.NewGuid().ToString("N") + extension);
        File.WriteAllText(path, content);
        return path;
    }
}
