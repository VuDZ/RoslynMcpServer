using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.TestRunner;

public sealed class SolutionFilterToolTests
{
    [Theory]
    [InlineData(".sln")]
    [InlineData(".slnx")]
    public async Task Filter_build_and_test_inherit_solution_settings_and_exclude_unlisted_project(string extension)
    {
        var root = Directory.CreateTempSubdirectory("mcp-solution-filter-");
        var manager = SolutionManagerTestFactory.Create();
        try
        {
            var solution = WriteSolution(root.FullName, extension);
            var filter = WriteFilter(root, extension);
            WriteProject(root, "Included");
            WriteProject(root, "Excluded", failIfBuilt: true);
            var restore = await DotNetCliRunner.RunWithMetadataAsync(
                $"restore \"{solution}\"",
                root.FullName,
                CancellationToken.None,
                TimeSpan.FromMinutes(2));
            Assert.True(restore.ExitCode == 0, restore.CombinedOutput);

            MsBuildBootstrapper.Register();
            await manager.LoadAsync(solution, CancellationToken.None, configuration: "Release", platform: "Any CPU");
            var published = manager.GetCurrentSolution();
            Assert.NotNull(published);

            var change = await Assert.ThrowsAsync<ArgumentException>(
                () => manager.LoadAsync(filter, CancellationToken.None));
            Assert.Contains("backing `.sln` or `.slnx`", change.Message, StringComparison.Ordinal);
            Assert.Same(published, manager.GetCurrentSolution());
            Assert.Equal(solution, manager.GetLoadedWorkspacePath());

            var build = new BuildTools(manager, NullLogger<BuildTools>.Instance);
            var tests = new TestTools(manager, NullLogger<TestTools>.Instance);
            var buildReport = await build.RunDotNetBuild(filter, noIncremental: false);
            Assert.Equal("## Build succeeded", buildReport);
            Assert.True(File.Exists(Path.Combine(root.FullName, "Included", "bin", "Release", "net10.0", "Included.dll")));

            var report = await tests.RunDotNetTest(filter, noBuild: false, noRestore: true);
            Assert.True(report.Contains("**1** · Passed: **1** · Failed: **0**", StringComparison.Ordinal), $"{extension}: {report}");
            report = await tests.RunSpecificTest(filter, "Included.IncludedCases", "Passes", noBuild: true, noRestore: true);
            Assert.True(report.Contains("**1** · Passed: **1** · Failed: **0**", StringComparison.Ordinal), $"{extension}: {report}");
            report = await tests.RunTestByFilter(filter, "FullyQualifiedName~Included.IncludedCases", noBuild: true, noRestore: true);
            Assert.True(report.Contains("**1** · Passed: **1** · Failed: **0**", StringComparison.Ordinal), $"{extension}: {report}");
            Assert.False(File.Exists(Path.Combine(root.FullName, "Excluded", "bin", "Release", "net10.0", "Excluded.dll")));

            var excluded = await tests.RunSpecificTest(filter, "Excluded.ExcludedCases", "Passes", noBuild: true, noRestore: true);
            Assert.Contains("no matching tests", excluded, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("filter's selected projects and their dependencies", excluded, StringComparison.Ordinal);
            Assert.DoesNotContain("**Mismatch:**", excluded, StringComparison.Ordinal);

            var projectTarget = await build.RunDotNetBuild(filter, projectName: "Included");
            Assert.Contains("`projectName` cannot select a target from a solution filter", projectTarget, StringComparison.Ordinal);
            var dllTarget = await tests.RunDotNetTest(filter, binariesPath: root.FullName);
            Assert.Contains("To test a solution filter, omit `binariesPath`", dllTarget, StringComparison.Ordinal);
        }
        finally
        {
            await manager.ClearWorkspaceAsync();
            await manager.Baseline.CaptureTask.WaitAsync(TimeSpan.FromSeconds(30));
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{")]
    [InlineData("""{"solution":{"path":"missing.slnx","projects":false}}""")]
    public async Task Missing_or_invalid_filter_reports_the_input_and_error(string? content)
    {
        var root = Directory.CreateTempSubdirectory("mcp-invalid-filter-");
        try
        {
            var path = Path.Combine(root.FullName, "Broken.slnf");
            if (content is not null)
            {
                File.WriteAllText(path, content);
            }

            var manager = SolutionManagerTestFactory.Create();
            var buildReport = await new BuildTools(manager, NullLogger<BuildTools>.Instance).RunDotNetBuild(path);
            var testReport = await new TestTools(manager, NullLogger<TestTools>.Instance)
                .RunDotNetTest(path, noBuild: true, noRestore: true);
            var expectedError = content is null ? "not found" : "MSB";

            Assert.Contains(path, buildReport, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(path, testReport, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(expectedError, buildReport, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(expectedError, testReport, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(".sln")]
    [InlineData(".slnx")]
    public async Task Filter_with_missing_backing_solution_reports_both_paths(string extension)
    {
        var root = Directory.CreateTempSubdirectory("mcp-missing-filter-backing-");
        try
        {
            var filter = WriteFilter(root, extension);
            var backingSolution = Path.Combine(root.FullName, "App" + extension);
            var expected = $"Error: solution filter `{filter}` references a backing solution that was not found: `{backingSolution}`.";
            var manager = SolutionManagerTestFactory.Create();
            var build = new BuildTools(manager, NullLogger<BuildTools>.Instance);
            var tests = new TestTools(manager, NullLogger<TestTools>.Instance);

            Assert.Equal(expected, await build.RunDotNetBuild(filter));
            Assert.Equal(expected, await tests.RunDotNetTest(filter, noBuild: false));
            Assert.Equal(expected, await tests.RunSpecificTest(filter, className: "IncludedCases", noBuild: true));
            Assert.Equal(expected, await tests.RunTestByFilter(filter, "FullyQualifiedName~IncludedCases"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static string WriteSolution(string root, string extension)
    {
        var path = Path.Combine(root, "App" + extension);
        File.WriteAllText(path, extension == ".slnx" ? """
            <Solution>
              <Project Path="Included/Included.csproj" />
              <Project Path="Excluded/Excluded.csproj" />
            </Solution>
            """ : """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Included", "Included\Included.csproj", "{11111111-1111-1111-1111-111111111111}"
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Excluded", "Excluded\Excluded.csproj", "{22222222-2222-2222-2222-222222222222}"
            EndProject
            Global
              GlobalSection(SolutionConfigurationPlatforms) = preSolution
                Release|Any CPU = Release|Any CPU
              EndGlobalSection
              GlobalSection(ProjectConfigurationPlatforms) = postSolution
                {11111111-1111-1111-1111-111111111111}.Release|Any CPU.ActiveCfg = Release|Any CPU
                {11111111-1111-1111-1111-111111111111}.Release|Any CPU.Build.0 = Release|Any CPU
                {22222222-2222-2222-2222-222222222222}.Release|Any CPU.ActiveCfg = Release|Any CPU
                {22222222-2222-2222-2222-222222222222}.Release|Any CPU.Build.0 = Release|Any CPU
              EndGlobalSection
            EndGlobal
            """);
        return path;
    }

    private static string WriteFilter(DirectoryInfo root, string extension)
    {
        var path = Path.Combine(root.CreateSubdirectory("filters").FullName, "Included.slnf");
        File.WriteAllText(path, $$"""
            {"solution":{"path":"..\\App{{extension}}","projects":["Included\\Included.csproj"]} }
            """);
        return path;
    }

    private static void WriteProject(DirectoryInfo root, string name, bool failIfBuilt = false)
    {
        var project = root.CreateSubdirectory(name);
        var errorTarget = failIfBuilt ? """
            <Target Name="RejectBuild" BeforeTargets="CoreCompile" Condition="'$(DesignTimeBuild)' != 'true'">
              <Error Text="The excluded test project must stay out of a filter build." />
            </Target>
            """ : string.Empty;
        File.WriteAllText(Path.Combine(project.FullName, name + ".csproj"), $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <IsTestProject>true</IsTestProject>
                <NuGetAudit>false</NuGetAudit>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
                <PackageReference Include="xunit" Version="2.9.3" />
                <PackageReference Include="xunit.runner.visualstudio" Version="3.1.0" />
              </ItemGroup>
              {{errorTarget}}
            </Project>
            """);
        File.WriteAllText(Path.Combine(project.FullName, "Cases.cs"), $$"""
            using Xunit;
            namespace {{name}};
            public class {{name}}Cases
            {
                [Fact]
                public void Passes() => Assert.True(true);
            }
            """);
    }
}
