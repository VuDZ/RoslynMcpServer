using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Build;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

public sealed class UnloadedTestSdkSourceReaderTests
{
    [Fact]
    public async Task ApplyPatch_ignores_the_package_entry_point_but_protects_a_redirected_source()
    {
        MsBuildBootstrapper.Register();
        var root = Directory.CreateTempSubdirectory("rmcp-test-sdk-readers-");
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, ".git"));
            var lib = root.CreateSubdirectory("Lib");
            var tests = root.CreateSubdirectory("Tests");
            var source = Path.Combine(lib.FullName, "Sample.cs");
            var testProject = Path.Combine(tests.FullName, "Tests.csproj");
            const string original = "// Café\npublic sealed class Sample { }\n";
            const string replacement = "public sealed class Sample { public int Value => 1; }";
            const string libraryProject = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>
                </Project>
                """;
            const string testProjectContent = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net11.0</TargetFramework>
                    <CodePage>28591</CodePage>
                    <ProgramHook>GenerateProgramFile</ProgramHook>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
                  </ItemGroup>
                  <!-- Customization -->
                </Project>
                """;
            await File.WriteAllTextAsync(Path.Combine(lib.FullName, "Lib.csproj"), libraryProject);
            await File.WriteAllTextAsync(source, original);
            await File.WriteAllTextAsync(testProject, testProjectContent);
            var originalBytes = await File.ReadAllBytesAsync(source);
            var restore = await DotNetCliRunner.RunWithMetadataAsync(
                $"restore \"{testProject}\" -p:NuGetAudit=false", root.FullName, CancellationToken.None, TimeSpan.FromMinutes(2));
            Assert.True(restore.ExitCode == 0, restore.CombinedOutput);

            var tools = new UtilityTools(NullLogger<UtilityTools>.Instance, SolutionManagerTestFactory.Create());
            var result = await tools.ApplyPatch(source, "public sealed class Sample { }", replacement);

            Assert.Contains("Patch applied successfully", result, StringComparison.Ordinal);
            Assert.Equal($"// Café\n{replacement}\n", await File.ReadAllTextAsync(source));

            var placeholder = Path.Combine(tests.FullName, "Microsoft.NET.Test.Sdk.Program.cs");
            await File.WriteAllBytesAsync(placeholder, originalBytes);
            result = await tools.ApplyPatch(placeholder, "public sealed class Sample { }", replacement);
            Assert.Contains("Cannot determine how the owning project reads", result, StringComparison.Ordinal);
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(placeholder));
            File.Delete(placeholder);

            var redirects = new[]
            {
                """
                <PropertyGroup>
                  <GeneratedProgramFile>$(MSBuildProjectDirectory)/../Lib/Sample.cs</GeneratedProgramFile>
                </PropertyGroup>
                """,
                """
                <Target Name="ChooseProgram" BeforeTargets="$(ProgramHook)">
                  <PropertyGroup>
                    <GeneratedProgramFile>$(MSBuildProjectDirectory)/../Lib/Sample.cs</GeneratedProgramFile>
                  </PropertyGroup>
                </Target>
                """,
            };
            foreach (var redirect in redirects)
            {
                await File.WriteAllBytesAsync(source, originalBytes);
                await File.WriteAllTextAsync(
                    testProject, testProjectContent.Replace("<!-- Customization -->", redirect, StringComparison.Ordinal));
                result = await tools.ApplyPatch(source, "public sealed class Sample { }", replacement);

                Assert.Contains("Cannot determine how the owning project reads", result, StringComparison.Ordinal);
                Assert.Equal(originalBytes, await File.ReadAllBytesAsync(source));
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
