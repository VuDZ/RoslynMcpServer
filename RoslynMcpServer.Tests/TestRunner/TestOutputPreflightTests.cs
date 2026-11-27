using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Build.Evaluation;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Build;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.TestRunner;

[Collection("DotNetSdkEnvironment")]
public sealed class TestOutputPreflightTests
{
    [Fact]
    public async Task Ordinary_no_build_tools_name_the_evaluated_assembly_before_starting_vstest()
    {
        using var files = new TestFiles();
        var project = files.Write("Sample.csproj", CustomOutputProject);
        var tools = new TestTools(SolutionManagerTestFactory.Create(), NullLogger<TestTools>.Instance);

        var results = new[]
        {
            await tools.RunDotNetTest(project, noBuild: true),
            await tools.RunSpecificTest(project, className: "Cases", noBuild: true),
            await tools.RunTestByFilter(project, "FullyQualifiedName~Cases", noBuild: true),
        };
        var output = Path.Combine(files.Root, "payload", "Debug", "AnyCPU", "RenamedTests.dll");
        Assert.All(results, result =>
        {
            Assert.Contains("Error: test assembly not found:", result, StringComparison.Ordinal);
            Assert.Contains(output, result, StringComparison.Ordinal);
            Assert.Contains("Configuration: `Debug`", result, StringComparison.Ordinal);
            Assert.Contains("Tests were not started.", result, StringComparison.Ordinal);
            Assert.Contains("`noBuild=false`", result, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Microsoft_test_sdk_program_generation_allows_the_missing_assembly_check()
    {
        using var files = new TestFiles();
        const string content = """
            <Project>
              <Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />
              <PropertyGroup>
                <TargetFramework>net11.0</TargetFramework>
                <ProgramHook>GenerateProgramFile</ProgramHook>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
              </ItemGroup>
              <Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" />
              <!-- CUSTOM TARGET -->
            </Project>
            """;
        var path = files.Write("Sample.csproj", content);
        var restore = await DotNetCliRunner.RunWithMetadataAsync(
            $"restore \"{path}\"", files.Root, CancellationToken.None, TimeSpan.FromMinutes(2));
        Assert.True(restore.ExitCode == 0, restore.CombinedOutput);
        var tools = new TestTools(SolutionManagerTestFactory.Create(), NullLogger<TestTools>.Instance);
        var result = await tools.RunDotNetTest(path, noBuild: true, noRestore: true);
        Assert.Contains("Error: test assembly not found:", result, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(files.Root, "bin", "Debug", "net11.0", "Sample.dll"), result, StringComparison.Ordinal);

        files.Write("actual.dll", "");
        foreach (var attributes in new[] { """Name="ChooseOutput" BeforeTargets="$(ProgramHook)" """, """Name="GenerateProgramFile" """ })
        {
            files.Write("Sample.csproj", content.Replace("<!-- CUSTOM TARGET -->", $"""
                <Target {attributes}>
                  <PropertyGroup>
                    <TargetPath>$(MSBuildProjectDirectory)/actual.dll</TargetPath>
                  </PropertyGroup>
                </Target>
                """, StringComparison.Ordinal));
            Assert.Null(TestOutputPreflight.FindMissing(path, files.Root, "Release", null, files.Sdk, default));
        }
    }

    [Fact]
    public void Solution_uses_mapped_outputs_and_filters_out_unselected_projects()
    {
        using var files = new TestFiles();
        files.Write("Selected.csproj", CustomOutputProject.Replace("payload/", "selected/", StringComparison.Ordinal));
        files.Write("Excluded.csproj", CustomOutputProject.Replace("payload/", "excluded/", StringComparison.Ordinal));
        files.Write("Skipped.csproj", CustomOutputProject.Replace("payload/", "skipped/", StringComparison.Ordinal));
        var solution = files.Write("Sample.sln", SolutionWithMappings);
        var filter = files.Write("subset/Sample.slnf", """
            {"solution":{"path":"../Sample.sln","projects":["Selected.csproj","Skipped.csproj"]}}
            """);

        var missing = TestOutputPreflight.FindMissing(solution, files.Root, "Retail", "x64", files.Sdk, default);
        Assert.NotNull(missing);
        Assert.Equal("Release", missing.Value.Configuration);
        Assert.Equal("AnyCPU", missing.Value.Platform);
        Assert.Equal("Retail|x64", missing.Value.SolutionConfiguration);
        Assert.Equal(Path.Combine(files.Root, "selected", "Release", "AnyCPU", "RenamedTests.dll"), missing.Value.AssemblyPath);

        files.Write("selected/Release/AnyCPU/RenamedTests.dll", "");
        missing = TestOutputPreflight.FindMissing(solution, files.Root, "Retail", "x64", files.Sdk, default);
        Assert.Equal(Path.Combine(files.Root, "excluded", "Release", "AnyCPU", "RenamedTests.dll"), missing?.AssemblyPath);
        Assert.Null(TestOutputPreflight.FindMissing(filter, files.Root, "Retail", "x64", files.Sdk, default));
    }

    [Fact]
    public void Invalid_solution_filter_leaves_the_diagnostic_to_msbuild()
    {
        using var files = new TestFiles();
        files.Write("Selected.csproj", CustomOutputProject);
        files.Write("Sample.sln", SolutionWithMappings);
        var filter = files.Write("Sample.slnf", """
            {"solution":{"path":"Sample.sln","projects":["Selected.csproj","NotInSolution.csproj"]}}
            """);

        Assert.Null(TestOutputPreflight.FindMissing(filter, files.Root, "Retail", "x64", files.Sdk, default));
    }

    [Fact]
    public void Multi_target_project_checks_only_test_frameworks()
    {
        using var files = new TestFiles();
        var path = files.Write("Sample.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net10.0;net11.0</TargetFrameworks>
                <IsTestProject Condition="'$(TargetFramework)' == 'net11.0'">true</IsTestProject>
              </PropertyGroup>
            </Project>
            """);

        var missing = TestOutputPreflight.FindMissing(path, files.Root, "Release", null, files.Sdk, default);
        Assert.NotNull(missing);
        Assert.Equal("net11.0", missing.Value.Framework);
        Assert.Equal(Path.Combine(files.Root, "bin", "Release", "net11.0", "Sample.dll"), missing.Value.AssemblyPath);

        files.Write("bin/Release/net11.0/Sample.dll", "");
        Assert.Null(TestOutputPreflight.FindMissing(path, files.Root, "Release", null, files.Sdk, default));
    }

    [Theory]
    [InlineData("BeforeTargets=\"VSTest\"", "SelectAssembly")]
    [InlineData("", "_VSTestConsole")]
    public void Custom_vstest_execution_leaves_the_assembly_decision_to_the_runner(string hook, string name)
    {
        using var files = new TestFiles();
        var path = files.Write("Sample.csproj", $$"""
            <Project>
              <Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />
              <PropertyGroup>
                <TargetFramework>net11.0</TargetFramework>
                <IsTestProject>true</IsTestProject>
              </PropertyGroup>
              <Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" />
              <Target Name="{{name}}" {{hook}}>
                <PropertyGroup>
                  <TargetPath>$(MSBuildProjectDirectory)/actual.dll</TargetPath>
                </PropertyGroup>
              </Target>
            </Project>
            """);
        files.Write("actual.dll", "");

        Assert.Null(TestOutputPreflight.FindMissing(path, files.Root, "Release", null, files.Sdk, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Custom_initial_targets_leave_the_assembly_decision_to_the_runner(bool imported)
    {
        using var files = new TestFiles();
        const string target = """
            <Target Name="SelectExistingOutput">
              <PropertyGroup>
                <TargetPath>$(MSBuildProjectDirectory)/actual.dll</TargetPath>
              </PropertyGroup>
            </Target>
            """;
        var path = files.Write("Sample.csproj", CustomOutputProject);
        Assert.NotNull(TestOutputPreflight.FindMissing(path, files.Root, "Release", null, files.Sdk, default));

        files.Write("actual.dll", "");
        var project = imported
            ? CustomOutputProject.Replace("</Project>", "<Import Project=\"Custom.targets\" /></Project>", StringComparison.Ordinal)
            : CustomOutputProject.Replace("Sdk=\"Microsoft.NET.Sdk\">",
                "Sdk=\"Microsoft.NET.Sdk\" InitialTargets=\"SelectExistingOutput\">", StringComparison.Ordinal)
                .Replace("</Project>", target + "</Project>", StringComparison.Ordinal);
        if (imported)
        {
            files.Write("Custom.targets", "<Project InitialTargets=\"SelectExistingOutput\">" + target + "</Project>");
        }

        files.Write("Sample.csproj", project);
        Assert.Null(TestOutputPreflight.FindMissing(path, files.Root, "Release", null, files.Sdk, default));
    }

    [Theory]
    [InlineData("Directory.Build.rsp")]
    [InlineData("project/Directory.Build.rsp")]
    [InlineData("work/Directory.Build.rsp")]
    public void Auto_response_arguments_leave_the_assembly_decision_to_the_runner(string responseFile)
    {
        using var files = new TestFiles();
        var project = files.Write("project/Sample.csproj", CustomOutputProject);
        var workingDirectory = Directory.CreateDirectory(Path.Combine(files.Root, "work")).FullName;

        files.Write(responseFile, "# Empty response file.\n");
        Assert.NotNull(TestOutputPreflight.FindMissing(project, workingDirectory, "Release", null, files.Sdk, default));

        files.Write(responseFile, "-p:OutDir=payload/\n");
        Assert.Null(TestOutputPreflight.FindMissing(project, workingDirectory, "Release", null, files.Sdk, default));
    }

    [Theory]
    [InlineData("BeforeTargets=\"_ComputeTargetFrameworkItems\"", "SelectFrameworks")]
    [InlineData("", "_ComputeTargetFrameworkItems")]
    public void Custom_cross_targeting_dispatch_leaves_the_assembly_decision_to_the_runner(string hook, string name)
    {
        using var files = new TestFiles();
        const string project = """
            <Project>
              <Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />
              <PropertyGroup>
                <TargetFrameworks>net10.0;net11.0</TargetFrameworks>
                <IsTestProject>true</IsTestProject>
              </PropertyGroup>
              <Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" />
              <!-- CUSTOM TARGET -->
            </Project>
            """;
        var path = files.Write("Sample.csproj", project);
        files.Write("bin/Release/net10.0/Sample.dll", "");

        var missing = TestOutputPreflight.FindMissing(path, files.Root, "Release", null, files.Sdk, default);
        Assert.Equal("net11.0", missing?.Framework);

        files.Write("Sample.csproj", project.Replace("<!-- CUSTOM TARGET -->", $"""
            <Target Name="{name}" {hook}>
              <PropertyGroup>
                <TargetFrameworks>net10.0</TargetFrameworks>
              </PropertyGroup>
            </Target>
            """, StringComparison.Ordinal));
        Assert.Null(TestOutputPreflight.FindMissing(path, files.Root, "Release", null, files.Sdk, default));
    }

    [Theory]
    [InlineData("VSTestTestCaseFilter")]
    [InlineData("VSTestLogger")]
    [InlineData("VSTestResultsDirectory")]
    [InlineData("MSBuildStartupDirectory")]
    public void Cli_only_properties_in_imports_leave_the_assembly_decision_to_the_runner(string property)
    {
        using var files = new TestFiles();
        var project = files.Write("Sample.csproj", CustomOutputProject.Replace("</Project>",
            "<Import Project=\"Output.props\" /></Project>", StringComparison.Ordinal));
        files.Write("Output.props", "<Project />");
        Assert.NotNull(TestOutputPreflight.FindMissing(project, files.Root, "Release", null, files.Sdk, default));

        files.Write("existing/RenamedTests.dll", "");
        var condition = property == "MSBuildStartupDirectory"
            ? $"'$(MSBuildStartupDirectory)' == '{files.Root}'"
            : $"'$({property})' != ''";
        files.Write("Output.props", $"""
            <Project>
              <PropertyGroup>
                <OutputPath Condition="{condition}">existing/</OutputPath>
              </PropertyGroup>
            </Project>
            """);

        Assert.Null(TestOutputPreflight.FindMissing(project, files.Root, "Release", null, files.Sdk, default));
    }

    [Theory]
    [InlineData("Directory.Solution.props")]
    [InlineData("Directory.Solution.targets")]
    [InlineData("nested/before.Sample.sln.targets")]
    [InlineData("nested/after.Sample.sln.targets")]
    public void Custom_solution_imports_leave_the_assembly_decision_to_the_runner(string customImport)
    {
        using var files = new TestFiles();
        files.Write("nested/Selected.csproj", CustomOutputProject);
        var solution = files.Write("nested/Sample.sln", SolutionWithMappings);
        Assert.NotNull(TestOutputPreflight.FindMissing(solution, files.Root, "Retail", "x64", files.Sdk, default));

        files.Write(customImport, """
            <Project>
              <Target Name="ChooseOutputs" BeforeTargets="VSTest" />
            </Project>
            """);

        Assert.Null(TestOutputPreflight.FindMissing(solution, files.Root, "Retail", "x64", files.Sdk, default));
    }

    [Theory]
    [InlineData("CoNfIgUrAtIoN", null, "x64")]
    [InlineData("PLATFORM", "Retail", null)]
    public void Inherited_solution_environment_leaves_implicit_mapping_to_msbuild(
        string name, string? configuration, string? platform)
    {
        using var files = new TestFiles();
        files.Write("Selected.csproj", CustomOutputProject);
        var solution = files.Write("Sample.sln", SolutionWithMappings);
        var original = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, "Overridden");
            Assert.Null(TestOutputPreflight.FindMissing(solution, files.Root, configuration, platform, files.Sdk, default));
            Assert.NotNull(TestOutputPreflight.FindMissing(solution, files.Root, "Retail", "x64", files.Sdk, default));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, original);
        }
    }

    [Fact]
    public void Sdk_mismatch_leaves_the_assembly_decision_to_the_runner()
    {
        using var files = new TestFiles();
        var path = files.Write("Sample.csproj", CustomOutputProject);
        Assert.NotNull(TestOutputPreflight.FindMissing(path, files.Root, null, null, files.Sdk, default));

        var anotherSdk = files.Sdk with { SdkDirectory = Path.Combine(files.Root, "OtherSdk") };
        Assert.Null(TestOutputPreflight.FindMissing(path, files.Root, null, null, anotherSdk, default));

        var nestedProject = files.Write("nested/Sample.csproj", CustomOutputProject);
        files.Write("nested/global.json", "{}");
        Assert.Null(TestOutputPreflight.FindMissing(nestedProject, files.Root, null, null, files.Sdk, default));
    }

    private const string CustomOutputProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net11.0</TargetFramework>
            <IsTestProject>true</IsTestProject>
            <AssemblyName>RenamedTests</AssemblyName>
            <OutputPath>payload/$(Configuration)/$(Platform)</OutputPath>
            <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
            <AssemblyName Condition="'$(BuildingSolutionFile)' == 'true' and '$(CurrentSolutionConfigurationContents)' == ''">Wrong.dll</AssemblyName>
          </PropertyGroup>
        </Project>
        """;

    private const string SolutionWithMappings = """
        Microsoft Visual Studio Solution File, Format Version 12.00
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Selected", "Selected.csproj", "{11111111-1111-1111-1111-111111111111}"
        EndProject
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Excluded", "Excluded.csproj", "{22222222-2222-2222-2222-222222222222}"
        EndProject
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Skipped", "Skipped.csproj", "{33333333-3333-3333-3333-333333333333}"
        EndProject
        Global
          GlobalSection(SolutionConfigurationPlatforms) = preSolution
            Retail|x64 = Retail|x64
          EndGlobalSection
          GlobalSection(ProjectConfigurationPlatforms) = postSolution
            {11111111-1111-1111-1111-111111111111}.Retail|x64.ActiveCfg = Release|Any CPU
            {11111111-1111-1111-1111-111111111111}.Retail|x64.Build.0 = Release|Any CPU
            {22222222-2222-2222-2222-222222222222}.Retail|x64.ActiveCfg = Release|Any CPU
            {22222222-2222-2222-2222-222222222222}.Retail|x64.Build.0 = Release|Any CPU
            {33333333-3333-3333-3333-333333333333}.Retail|x64.ActiveCfg = Release|Any CPU
          EndGlobalSection
        EndGlobal
        """;

    private sealed class TestFiles : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("mcp-test-outputs-").FullName;
        public DotNetSdkEnvironment.SdkPinInfo Sdk { get; }

        public TestFiles()
        {
            MsBuildBootstrapper.Register();
            var directory = GetSdkDirectory();
            var version = Path.GetFileName(directory);
            var dotnetRoot = Directory.GetParent(directory)!.Parent!.FullName;
            var globalJson = Write("global.json", JsonSerializer.Serialize(new
            {
                sdk = new { version, rollForward = "disable", paths = new[] { dotnetRoot } },
            }));
            Sdk = new(version, directory, Path.Combine(directory, "MSBuild.dll"), Path.Combine(directory, "Sdks"), dotnetRoot)
            {
                GlobalJsonPath = globalJson,
                ResolvedVersion = version,
            };
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);

        public string Write(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string GetSdkDirectory() => Path.GetDirectoryName(typeof(Project).Assembly.Location)!;
    }
}
