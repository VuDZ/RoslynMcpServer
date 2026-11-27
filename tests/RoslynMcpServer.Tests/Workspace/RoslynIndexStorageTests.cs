using System.Diagnostics;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Navigation;
using RoslynMcpServer.Tests.Support;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

/// <summary>
/// Roslyn keeps syntax indexes in a SQLite database between processes once the SQLite library it references
/// is present. Without it every server process rebuilt all indexes for the first search.
/// </summary>
public sealed class RoslynIndexStorageTests
{
    [Fact]
    public void MacOS_temp_directory_matches_the_native_working_directory()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var temp = Path.GetTempPath();
        using var process = Process.Start(new ProcessStartInfo("/bin/pwd", "-P")
        {
            WorkingDirectory = temp,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;
        var physicalPath = process.StandardOutput.ReadToEnd().Trim();
        Assert.True(process.WaitForExit(5000), "Native working directory probe did not exit.");
        Assert.Equal(0, process.ExitCode);
        Assert.Equal(Path.TrimEndingDirectorySeparator(temp), physicalPath);
    }

    [Fact]
    public void Unix_cache_is_private_and_the_user_data_folder_is_restored()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var relativeCache = Path.GetRelativePath(Path.GetTempPath(), RoslynIndexCache.ProcessFolder);
        Assert.StartsWith($"RoslynMcpIndexCache-{Environment.ProcessId}-", relativeCache);

        var processBase = Path.Combine(Path.GetTempPath(), relativeCache.Split(Path.DirectorySeparatorChar)[0]);
        var userData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        Assert.False(string.IsNullOrEmpty(userData));
        Assert.False(userData.StartsWith(processBase, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("SQLitePCLRaw.batteries_v2")]
    [InlineData("SQLitePCLRaw.core")]
    public void Shipped_sqlite_is_the_build_Roslyn_references(string assemblyName)
    {
        var reference = typeof(Microsoft.CodeAnalysis.Workspace).Assembly.GetReferencedAssemblies()
            .Single(name => name.Name == assemblyName);

        var shipped = AssemblyName.GetAssemblyName(Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll"));

        Assert.Equal(reference.Version, shipped.Version);
        Assert.Equal(Convert.ToHexString(reference.GetPublicKeyToken()!), Convert.ToHexString(shipped.GetPublicKeyToken()!));
    }

    [Fact]
    public async Task Indexes_are_stored_and_a_later_session_sees_changed_sources()
    {
        MsBuildBootstrapper.Register();
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpIndexStorage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "App"));
        try
        {
            var solutionPath = Path.Combine(root, "App.slnx");
            await File.WriteAllTextAsync(solutionPath, """<Solution><Project Path="App/App.csproj" /></Solution>""", BomFree);
            await File.WriteAllTextAsync(Path.Combine(root, "App", "App.csproj"), Project, BomFree);
            var source = Path.Combine(root, "App", "Types.cs");
            await File.WriteAllTextAsync(source, "namespace App; public class Alpha { }", BomFree);

            Assert.Equal(1, await CountDeclarationsInNewSessionAsync(solutionPath, "Alpha"));
            Assert.NotNull(RoslynIndexCache.FindDatabase(solutionPath));

            // Sources change while no server runs: the stored index of the file must not hide the change.
            await File.WriteAllTextAsync(source, "namespace App; public class Beta { }", BomFree);

            Assert.Equal(0, await CountDeclarationsInNewSessionAsync(solutionPath, "Alpha"));
            Assert.Equal(1, await CountDeclarationsInNewSessionAsync(solutionPath, "Beta"));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // A locked file only affects cleanup of a temporary directory.
            }
        }
    }

    /// <summary>A fresh workspace each time, like a restarted server; only the storage database is shared.</summary>
    private static async Task<int> CountDeclarationsInNewSessionAsync(string solutionPath, string name)
    {
        var manager = SolutionManagerTestFactory.Create();
        try
        {
            await manager.LoadAndPrepareAsync(solutionPath, shadowCopyInSolutionAnalyzers: false, CancellationToken.None);
            var solution = await manager.GetPublishedSolutionAsync();
            Assert.NotNull(solution);
            var result = await SymbolDeclarationResolver.ResolveDeclarationsAsync(solution, name, SymbolFilter.Type);
            Assert.Null(result.Error);
            return result.Symbols.Count;
        }
        finally
        {
            await manager.ClearWorkspaceAsync();
        }
    }

    private const string Project = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """;

    private static readonly UTF8Encoding BomFree = new(encoderShouldEmitUTF8Identifier: false);
}
