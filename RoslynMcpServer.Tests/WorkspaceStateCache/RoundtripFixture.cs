using System.Diagnostics;
using System.Text;
using RoslynMcpServer.Tests.SourceStructure;
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
        File.Copy(Path.Combine(RepositoryRoot.Find(), "global.json"), Path.Combine(DirectoryPath, "global.json"));
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
        // Reused build servers can inherit redirected output and keep it open after dotnet exits.
        // https://github.com/VuDZ/RoslynMcpServer/actions/runs/43707408649
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        foreach (var argument in new[] { "build", ProjectPath, "-c", "Debug", "--nologo", "--disable-build-servers" })
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)!;
        using var outputCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var output = new StringBuilder();
        var error = new StringBuilder();
        var readTasks = Task.WhenAll(
            ReadOutputAsync(process.StandardOutput, output, outputCancellation.Token),
            ReadOutputAsync(process.StandardError, error, outputCancellation.Token));
        var outputTimedOut = false;
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            try
            {
                await readTasks.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (TimeoutException)
            {
                outputTimedOut = true;
            }
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
        finally
        {
            await outputCancellation.CancelAsync();
            try
            {
                await readTasks;
            }
            catch (OperationCanceledException) when (outputCancellation.IsCancellationRequested)
            {
            }
        }
        if (outputTimedOut)
        {
            throw new TimeoutException(
                $"The build exited with code {process.ExitCode}, but its output streams did not close within five seconds.\n{output}\n{error}");
        }
        Assert.True(process.ExitCode == 0, output + "\n" + error);
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

    internal static async Task ReadOutputAsync(
        StreamReader reader,
        StringBuilder output,
        CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            output.AppendLine(line);
        }
    }
}
