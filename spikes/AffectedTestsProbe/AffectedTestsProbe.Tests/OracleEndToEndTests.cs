using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace AffectedTestsProbe.Tests;

public sealed class OracleEndToEndTests
{
    [Fact]
    public async Task SmallSolution_OracleContainsOnlyTheTestsThatCallTheMember()
    {
        var root = Path.Combine(Path.GetTempPath(), "tia-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Prod"));
        Directory.CreateDirectory(Path.Combine(root, "Tests"));
        var source = Path.Combine(root, "Prod", "Calc.cs");
        const string original =
            """
            namespace Prod;

            public class Calc
            {
                public int Add(int a, int b)
                {
                    return a + b;
                }
            }
            """;
        await File.WriteAllTextAsync(source, original);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Prod", "Prod.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Tests", "CalcTests.cs"),
            """
            using Prod;
            using Xunit;

            namespace Tests;

            public class CalcTests
            {
                [Fact]
                public void Adds()
                {
                    _ = new Calc().Add(1, 2);
                }

                [Theory]
                [InlineData(1, 2)]
                [InlineData(3, 4)]
                public void Rows(int a, int b)
                {
                    _ = new Calc().Add(a, b);
                }

                [Fact]
                public void Unrelated()
                {
                }
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Tests", "Tests.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
                <PackageReference Include="xunit" Version="2.9.3" />
                <PackageReference Include="xunit.runner.visualstudio" Version="3.1.0" />
              </ItemGroup>
              <ItemGroup>
                <ProjectReference Include="..\Prod\Prod.csproj" />
              </ItemGroup>
            </Project>
            """);

        try
        {
            var created = await RunAsync("dotnet", ["new", "sln", "-n", "E2E", "-o", root, "--force"], root);
            Assert.Equal(0, created.ExitCode);
            var solution = Directory.EnumerateFiles(root, "E2E.sln*").Single();
            var added = await RunAsync(
                "dotnet",
                ["sln", solution, "add", Path.Combine(root, "Prod", "Prod.csproj"), Path.Combine(root, "Tests", "Tests.csproj")],
                root);
            Assert.Equal(0, added.ExitCode);

            var probe = ProbeDll();
            var oracle = await RunAsync(
                "dotnet",
                [
                    probe,
                    "oracle",
                    "--solution",
                    solution,
                    "--member",
                    "M:Prod.Calc.Add(System.Int32,System.Int32)",
                    "--test-project",
                    "Tests",
                    "--clone-ack",
                    "--configuration",
                    "Release",
                ],
                root);
            Assert.True(oracle.ExitCode == 0, oracle.Error + oracle.Output);
            using var json = JsonDocument.Parse(oracle.Output);
            var report = json.RootElement;
            Assert.Equal(JsonValueKind.Null, report.GetProperty("invalid").ValueKind);
            Assert.Equal("Valid", report.GetProperty("judgment").GetString());
            Assert.Equal(0, report.GetProperty("unmappedExecuted").GetInt32());
            Assert.True(report.GetProperty("publishCompleteness").GetBoolean());
            var names = report.GetProperty("oracle").EnumerateArray().Select(item => item.GetString()!).ToArray();
            Assert.Contains(names, name => name.Contains(".Adds", StringComparison.Ordinal));
            Assert.Contains(names, name => name.Contains(".Rows", StringComparison.Ordinal));
            Assert.DoesNotContain(names, name => name.Contains(".Unrelated", StringComparison.Ordinal));
            Assert.Equal(original.Replace("\r\n", "\n"), (await File.ReadAllTextAsync(source)).Replace("\r\n", "\n"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string ProbeDll()
    {
        var configuration = AppContext.BaseDirectory.Contains(
            Path.DirectorySeparatorChar + "Debug" + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase)
            ? "Debug"
            : "Release";
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "bin",
            configuration,
            "net10.0",
            "AffectedTestsProbe.dll"));
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string file,
        string[] arguments,
        string directory)
    {
        var start = new ProcessStartInfo(file)
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start " + file);
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output, await error);
    }
}
