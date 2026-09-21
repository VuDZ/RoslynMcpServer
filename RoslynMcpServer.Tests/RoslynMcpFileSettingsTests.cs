using RoslynMcpServer.Config;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class RoslynMcpJsoncParserTests
{
    [Fact]
    public void StripComments_allows_line_and_block_comments()
    {
        const string jsonc = """
            {
              // line comment
              "workspace-path": "App.sln",
              /* block
                 comment */
              "preview": true
            }
            """;

        using var doc = RoslynMcpJsoncParser.Parse(jsonc);
        Assert.Equal("App.sln", doc.RootElement.GetProperty("workspace-path").GetString());
        Assert.True(doc.RootElement.GetProperty("preview").GetBoolean());
    }

    [Fact]
    public void StripComments_does_not_strip_slashes_inside_strings()
    {
        const string jsonc = """
            { "ripgrep-path": "C:/tools/rg.exe // keep" }
            """;

        using var doc = RoslynMcpJsoncParser.Parse(jsonc);
        Assert.Equal("C:/tools/rg.exe // keep", doc.RootElement.GetProperty("ripgrep-path").GetString());
    }
}

public sealed class RoslynMcpFileSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "RoslynMcpFileSettingsTests-" + Guid.NewGuid().ToString("N"));
    private readonly string _exeDir;
    private readonly string _cwdDir;

    public RoslynMcpFileSettingsTests()
    {
        _exeDir = Path.Combine(_root, "exe");
        _cwdDir = Path.Combine(_root, "cwd");
        Directory.CreateDirectory(_exeDir);
        Directory.CreateDirectory(_cwdDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }

    [Fact]
    public void LoadFromDirectories_cwd_overrides_exe_keys()
    {
        File.WriteAllText(
            Path.Combine(_exeDir, RoslynMcpFileSettings.FileName),
            """
            {
              "workspace-path": "FromExe.sln",
              "configuration": "Debug",
              "platform": "x86",
              "max-results": 10
            }
            """);
        File.WriteAllText(
            Path.Combine(_cwdDir, RoslynMcpFileSettings.FileName),
            """
            {
              // cwd wins for overlapping keys
              "workspace-path": "FromCwd.sln",
              "configuration": "Release",
              "preview": true
            }
            """);

        var settings = RoslynMcpFileSettings.LoadFromDirectories(_exeDir, _cwdDir);
        Assert.Equal("FromCwd.sln", settings.WorkspacePath);
        Assert.Equal("Release", settings.Configuration);
        Assert.Equal("x86", settings.Platform);
        Assert.Equal(10, settings.MaxResults);
        Assert.True(settings.Preview);
        Assert.True(settings.ExecutableFilePresent);
        Assert.True(settings.WorkingDirectoryFilePresent);
    }

    [Fact]
    public void LoadFromDirectories_no_file_returns_empty_settings()
    {
        var settings = RoslynMcpFileSettings.LoadFromDirectories(_exeDir, _cwdDir);
        Assert.False(settings.HasAnyFile);
        Assert.Null(settings.WorkspacePath);
        Assert.Empty(settings.ParseFailures);
    }

    [Fact]
    public void LoadFromDirectories_records_parse_failure_without_throwing()
    {
        File.WriteAllText(
            Path.Combine(_cwdDir, RoslynMcpFileSettings.FileName),
            "{ this is not json");

        var settings = RoslynMcpFileSettings.LoadFromDirectories(_exeDir, _cwdDir);
        Assert.Null(settings.WorkspacePath);
        Assert.NotEmpty(settings.ParseFailures);
        Assert.Contains(settings.ParseFailures, f => f.Path.Contains("cwd", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadFromDirectories_broken_sibling_keeps_valid_path_and_records_failure()
    {
        File.WriteAllText(
            Path.Combine(_exeDir, RoslynMcpFileSettings.FileName),
            "{ broken");
        File.WriteAllText(
            Path.Combine(_cwdDir, RoslynMcpFileSettings.FileName),
            """{ "workspace-path": "FromCwd.sln", "configuration": "Release" }""");

        var settings = RoslynMcpFileSettings.LoadFromDirectories(_exeDir, _cwdDir);
        Assert.Equal("FromCwd.sln", settings.WorkspacePath);
        Assert.Equal("Release", settings.Configuration);
        Assert.NotEmpty(settings.ParseFailures);
        Assert.Contains(settings.ParseFailures, f => f.Path.Contains("exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveWorkspacePathAgainstWorkingDirectory_uses_process_cwd()
    {
        var previous = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = _cwdDir;
            File.WriteAllText(
                Path.Combine(_exeDir, RoslynMcpFileSettings.FileName),
                """{ "workspace-path": "Rel.sln" }""");

            var settings = RoslynMcpFileSettings.LoadFromDirectories(_exeDir, _cwdDir);
            var resolved = settings.ResolveWorkspacePathAgainstWorkingDirectory();
            Assert.Equal(Path.GetFullPath(Path.Combine(_cwdDir, "Rel.sln")), resolved);
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }
}

public sealed class MsBuildWorkspacePropertiesPassedArgsTests
{
    [Fact]
    public void MatchesPassedLoadArguments_omitted_platform_does_not_miss_cache()
    {
        Assert.True(
            MsBuildWorkspaceProperties.MatchesPassedLoadArguments(
                @"C:\src\A.sln",
                "Sit-Debug",
                "x64",
                "net10.0",
                @"C:\src\A.sln",
                passedConfiguration: null,
                passedPlatform: null,
                passedTargetFramework: null,
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MatchesPassedLoadArguments_different_configuration_misses_cache()
    {
        Assert.False(
            MsBuildWorkspaceProperties.MatchesPassedLoadArguments(
                @"C:\src\A.sln",
                "Sit-Debug",
                "x64",
                "net10.0",
                @"C:\src\A.sln",
                passedConfiguration: "Release",
                passedPlatform: null,
                passedTargetFramework: null,
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MatchesPassedLoadArguments_matching_passed_platform_hits_cache()
    {
        Assert.True(
            MsBuildWorkspaceProperties.MatchesPassedLoadArguments(
                @"C:\src\A.sln",
                "Sit-Debug",
                "x64",
                "net10.0",
                @"C:\src\A.sln",
                passedConfiguration: null,
                passedPlatform: "x64",
                passedTargetFramework: null,
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MatchesPassedLoadArguments_different_path_misses_cache()
    {
        Assert.False(
            MsBuildWorkspaceProperties.MatchesPassedLoadArguments(
                @"C:\src\A.sln",
                "Sit-Debug",
                "x64",
                "net10.0",
                @"C:\src\B.sln",
                passedConfiguration: null,
                passedPlatform: null,
                passedTargetFramework: null,
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IsSameLoadCache_still_treats_omitted_as_unequal()
    {
        Assert.False(
            MsBuildWorkspaceProperties.IsSameLoadCache(
                @"C:\src\A.sln",
                "Sit-Debug",
                "x64",
                "net10.0",
                @"C:\src\A.sln",
                configuration: null,
                platform: null,
                targetFramework: null,
                StringComparison.OrdinalIgnoreCase));
    }
}
