using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Workspace;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

public sealed class UnloadedSourceReaderBudgetTests
{
    public UnloadedSourceReaderBudgetTests()
    {
        MsBuildBootstrapper.Register();
    }

    [Fact]
    public void Large_checkout_finds_the_owner_and_a_later_linked_reader()
    {
        var root = Directory.CreateTempSubdirectory("mcp-source-readers-");
        try
        {
            root.CreateSubdirectory(".git");
            var owner = root.CreateSubdirectory("Owner");
            var source = Path.Combine(owner.FullName, "Owner.cs");
            File.WriteAllText(source, "class Owner { }");
            File.WriteAllText(Path.Combine(owner.FullName, "Owner.csproj"), Project);

            for (var index = 0; index < 280; index++)
            {
                root.CreateSubdirectory($"Other/Directory{index:D3}");
            }

            for (var index = 0; index < 33; index++)
            {
                File.WriteAllText(
                    Path.Combine(root.FullName, "Other", $"Directory{index:D3}", $"Library{index:D3}.csproj"),
                    Project);
            }

            Assert.Equal([0], UnloadedSourceReaders.GetCodePages(source, CancellationToken.None));

            var linkedProject = Path.Combine(root.FullName, "Other", "Directory032", "Library032.csproj");
            File.WriteAllText(linkedProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>
                  <PropertyGroup Condition="'$(Configuration)' == 'Release'">
                    <CodePage>28591</CodePage>
                  </PropertyGroup>
                  <ItemGroup Condition="'$(Configuration)' == 'Release'">
                    <Compile Include="../../Owner/Owner.cs" />
                  </ItemGroup>
                </Project>
                """);

            var pages = UnloadedSourceReaders.GetCodePages(source, CancellationToken.None);
            Assert.Equal([0, 28591], pages.Order());
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Different_solution_configurations_can_share_one_project_evaluation()
    {
        var root = Directory.CreateTempSubdirectory("mcp-source-readers-");
        try
        {
            var source = Path.Combine(root.FullName, "Owner.cs");
            File.WriteAllText(source, "class Owner { }");
            File.WriteAllText(Path.Combine(root.FullName, "Owner.csproj"), Project);
            File.WriteAllText(Path.Combine(root.FullName, "Owner.sln"), """
                Microsoft Visual Studio Solution File, Format Version 12.00
                Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Owner", "Owner.csproj", "{4C43D3A1-45AE-437E-955B-53699FDA39A3}"
                EndProject
                Global
                    GlobalSection(SolutionConfigurationPlatforms) = preSolution
                        Debug|Any CPU = Debug|Any CPU
                        Retail|Any CPU = Retail|Any CPU
                    EndGlobalSection
                    GlobalSection(ProjectConfigurationPlatforms) = postSolution
                        {4C43D3A1-45AE-437E-955B-53699FDA39A3}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                        {4C43D3A1-45AE-437E-955B-53699FDA39A3}.Debug|Any CPU.Build.0 = Debug|Any CPU
                        {4C43D3A1-45AE-437E-955B-53699FDA39A3}.Retail|Any CPU.ActiveCfg = Debug|Any CPU
                        {4C43D3A1-45AE-437E-955B-53699FDA39A3}.Retail|Any CPU.Build.0 = Debug|Any CPU
                    EndGlobalSection
                EndGlobal
                """);

            Assert.Equal([0], UnloadedSourceReaders.GetCodePages(source, CancellationToken.None));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private const string Project = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net11.0</TargetFramework>
            <Configurations>Debug;Release</Configurations>
          </PropertyGroup>
        </Project>
        """;
}
