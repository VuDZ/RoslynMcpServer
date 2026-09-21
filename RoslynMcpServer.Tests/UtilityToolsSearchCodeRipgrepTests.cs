using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Config;
using RoslynMcpServer.Services;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class UtilityToolsSearchCodeRipgrepTests : IDisposable
{
    public UtilityToolsSearchCodeRipgrepTests()
    {
        RipgrepRunner.ResolveBinaryOverride = null;
        RipgrepRunner.FindOnPathOverride = null;
    }

    public void Dispose()
    {
        RipgrepRunner.ResolveBinaryOverride = null;
        RipgrepRunner.FindOnPathOverride = null;
    }

    [Fact]
    public async Task SearchCode_useRipgrep_false_does_not_resolve_binary_or_emit_ripgrep_header()
    {
        var resolveCalls = 0;
        RipgrepRunner.ResolveBinaryOverride = (_, _) =>
        {
            resolveCalls++;
            return @"C:\fake\rg.exe";
        };
        RipgrepRunner.FindOnPathOverride = () =>
        {
            resolveCalls++;
            return @"C:\fake\rg.exe";
        };

        var workspaceRoot = CreateTempRoot();
        try
        {
            File.WriteAllText(Path.Combine(workspaceRoot, "App.sln"), string.Empty);
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "src"));
            File.WriteAllText(Path.Combine(workspaceRoot, "src", "a.cs"), "// next version line");

            var tool = new UtilityTools(NullLogger<UtilityTools>.Instance, CreateManagerWithLoadedPath(Path.Combine(workspaceRoot, "App.sln")));
            var result = await tool.SearchCode("next version");

            Assert.Equal(0, resolveCalls);
            Assert.DoesNotContain("Search engine: ripgrep", result, StringComparison.Ordinal);
            Assert.Contains("Found 1 match(es)", result, StringComparison.Ordinal);
            Assert.Contains("Scanned files:", result, StringComparison.Ordinal);
        }
        finally
        {
            DeleteQuietly(workspaceRoot);
        }
    }

    [Fact]
    public async Task SearchCode_useRipgrep_true_without_binary_returns_error_not_managed_walk()
    {
        RipgrepRunner.ResolveBinaryOverride = (_, _) => null;

        var workspaceRoot = CreateTempRoot();
        try
        {
            File.WriteAllText(Path.Combine(workspaceRoot, "App.sln"), string.Empty);
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "src"));
            File.WriteAllText(Path.Combine(workspaceRoot, "src", "a.cs"), "// next version line");

            var tool = new UtilityTools(NullLogger<UtilityTools>.Instance, CreateManagerWithLoadedPath(Path.Combine(workspaceRoot, "App.sln")));
            var result = await tool.SearchCode("next version", useRipgrep: true);

            Assert.Contains("useRipgrep is enabled but rg was not found", result, StringComparison.Ordinal);
            Assert.Contains("Omit useRipgrep", result, StringComparison.Ordinal);
            Assert.DoesNotContain("Found 1 match(es)", result, StringComparison.Ordinal);
            Assert.DoesNotContain("Scanned files:", result, StringComparison.Ordinal);
            Assert.DoesNotContain("Search engine: ripgrep", result, StringComparison.Ordinal);
        }
        finally
        {
            DeleteQuietly(workspaceRoot);
        }
    }

    [Fact]
    public void TryParseMatchLine_formats_to_managed_output()
    {
        var ok = RipgrepRunner.TryParseMatchLine(
            @"E:\Devel\App\src\Foo.cs:42:    public void Bar()",
            out var match);

        Assert.True(ok);
        Assert.NotNull(match);
        Assert.Equal(@"E:\Devel\App\src\Foo.cs", match!.FilePath);
        Assert.Equal(42, match.LineNumber);
        Assert.Equal("    public void Bar()", match.Text);
        Assert.Equal(@"E:\Devel\App\src\Foo.cs:42 |     public void Bar()", RipgrepRunner.FormatManagedMatch(match));
    }

    [Fact]
    public async Task SearchCode_ripgrepPath_without_useRipgrep_is_reported_unused()
    {
        var resolveCalls = 0;
        RipgrepRunner.ResolveBinaryOverride = (_, _) =>
        {
            resolveCalls++;
            return @"C:\fake\rg.exe";
        };

        var workspaceRoot = CreateTempRoot();
        try
        {
            File.WriteAllText(Path.Combine(workspaceRoot, "App.sln"), string.Empty);
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "src"));
            File.WriteAllText(Path.Combine(workspaceRoot, "src", "a.cs"), "// next version line");

            var tool = new UtilityTools(NullLogger<UtilityTools>.Instance, CreateManagerWithLoadedPath(Path.Combine(workspaceRoot, "App.sln")));
            var result = await tool.SearchCode(
                "next version",
                useRipgrep: false,
                ripgrepPath: @"C:\tools\rg.exe");

            Assert.Equal(0, resolveCalls);
            Assert.Contains("Note: ripgrepPath was ignored because useRipgrep is false.", result, StringComparison.Ordinal);
            Assert.Contains("Found 1 match(es)", result, StringComparison.Ordinal);
            Assert.DoesNotContain("Search engine: ripgrep", result, StringComparison.Ordinal);
        }
        finally
        {
            DeleteQuietly(workspaceRoot);
        }
    }

    [Fact]
    public void ResolveBinary_uses_config_only_when_argument_omitted()
    {
        var configPath = Path.Combine(CreateTempRoot(), "rg.exe");
        File.WriteAllText(configPath, string.Empty);
        var argumentPath = Path.Combine(Path.GetDirectoryName(configPath)!, "other-rg.exe");
        File.WriteAllText(argumentPath, string.Empty);

        try
        {
            RipgrepRunner.FindOnPathOverride = () => throw new InvalidOperationException("PATH must not be probed when config is set.");

            var fromConfig = RipgrepRunner.ResolveBinary(argumentPath: null, configPath: configPath);
            Assert.Equal(Path.GetFullPath(configPath), fromConfig);

            var fromArgument = RipgrepRunner.ResolveBinary(argumentPath, configPath);
            Assert.Equal(Path.GetFullPath(argumentPath), fromArgument);
        }
        finally
        {
            DeleteQuietly(Path.GetDirectoryName(configPath)!);
        }
    }

    [Fact]
    public async Task SearchCode_useRipgrep_true_omitted_path_passes_config_to_resolver()
    {
        string? seenArgument = "unset";
        string? seenConfig = "unset";
        RipgrepRunner.ResolveBinaryOverride = (argumentPath, configPath) =>
        {
            seenArgument = argumentPath;
            seenConfig = configPath;
            return null;
        };

        var cwd = CreateTempRoot();
        try
        {
            File.WriteAllText(
                Path.Combine(cwd, RoslynMcpFileSettings.FileName),
                """{ "ripgrep-path": "C:\\configured\\rg.exe" }""");
            var settings = RoslynMcpFileSettings.LoadFromDirectories(executableDirectory: null, workingDirectory: cwd);
            Assert.Equal(@"C:\configured\rg.exe", settings.RipgrepPath);

            File.WriteAllText(Path.Combine(cwd, "App.sln"), string.Empty);
            Directory.CreateDirectory(Path.Combine(cwd, "src"));
            File.WriteAllText(Path.Combine(cwd, "src", "a.cs"), "// next version line");

            var manager = CreateManagerWithLoadedPath(Path.Combine(cwd, "App.sln"), settings);
            var tool = new UtilityTools(NullLogger<UtilityTools>.Instance, manager);
            var result = await tool.SearchCode("next version", useRipgrep: true);

            Assert.Null(seenArgument);
            Assert.Equal(@"C:\configured\rg.exe", seenConfig);
            Assert.Contains("useRipgrep is enabled but rg was not found", result, StringComparison.Ordinal);
        }
        finally
        {
            DeleteQuietly(cwd);
        }
    }

    private static SolutionManager CreateManagerWithLoadedPath(string loadedPath, RoslynMcpFileSettings? fileSettings = null)
    {
        var manager = SolutionManagerTestFactory.Create(fileSettings);
        typeof(SolutionManager)
            .GetField("_loadedPath", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, loadedPath);
        return manager;
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpRipgrepTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }
}
