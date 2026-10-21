using System.Diagnostics;
using System.Text;
using Xunit;

namespace RoslynMcpServer.Tests.WorkspaceStateCache;

internal sealed class RoundtripFixture : IDisposable
{
    public string DirectoryPath { get; }

    public string ProjectPath => Path.Combine(DirectoryPath, "App", "App.csproj");

    public string SharedPath => Path.Combine(DirectoryPath, "Shared.cs");

    public RoundtripFixture()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), "task06-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(Path.Combine(DirectoryPath, "NuGet.Config"),
            "<configuration><packageSources><clear /></packageSources></configuration>");
    }

    public async Task PrepareAsync(string encodingName, CancellationToken cancellationToken)
    {
        WriteShared("/// <summary>Grüße 世界</summary>\npublic static class Shared { public const int Value = 7; }\n",
            EncodingFor(encodingName));
        foreach (var name in new[] { "Library", "App" })
        {
            var directory = Path.Combine(DirectoryPath, name);
            Directory.CreateDirectory(directory);
            var references = name == "App"
                ? "<ProjectReference Include=\"../Library/Library.csproj\" /><AdditionalFiles Include=\"../Shared.cs\" />"
                : "";
            File.WriteAllText(Path.Combine(directory, name + ".csproj"), $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>
                    <CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>
                    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                  </PropertyGroup>
                  <ItemGroup><Compile Include="../Shared.cs" Link="Linked/Shared.cs" />{{references}}</ItemGroup>
                </Project>
                """);
        }
        await BuildAsync(cancellationToken);
    }

    public async Task BuildAsync(CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WorkingDirectory = DirectoryPath,
        };
        // A reused MSBuild node inherits this redirected stdout. ReadToEndAsync then waits until
        // that node exits, which is how the multitarget build hung for the whole test budget.
        // DotNetCliRunner sets the same variable on every production invocation.
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        foreach (var argument in new[] { "build", ProjectPath, "-c", "Debug", "--nologo" })
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        Assert.True(process.ExitCode == 0, await output + "\n" + await error);
    }

    public void WriteShared(string text, Encoding encoding) =>
        File.WriteAllBytes(SharedPath, [.. encoding.GetPreamble(), .. encoding.GetBytes(text)]);

    public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);

    public static Encoding EncodingFor(string name) => name switch
    {
        "utf8" => new UTF8Encoding(false),
        "utf8-bom" => new UTF8Encoding(true),
        "utf16-bom" => new UnicodeEncoding(false, true),
        _ => throw new ArgumentException("Unknown encoding control.", nameof(name)),
    };
}
