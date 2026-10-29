using RoslynMcpServer.Config;
using Xunit;

namespace RoslynMcpServer.Tests.Config;

public sealed class RoslynMcpUserFileSettingsTests : IDisposable
{
    public RoslynMcpUserFileSettingsTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Temporary files can remain locked by a concurrent scanner.
        }
    }

    [Fact]
    public void User_settings_override_exe_and_cwd_overrides_user_per_key()
    {
        var exe = Write("exe", """{"configuration":"Debug","platform":"x64","plugins":["exe"]}""");
        var user = Write("user", """{"configuration":"User","preview":true,"plugins":["user"]}""");
        var cwd = Write("cwd", """{"configuration":"Release","max-results":123,"plugins":[]}""");

        var result = RoslynMcpFileSettings.LoadFromDirectories(exe, cwd, user, logger: null);

        Assert.Equal("Release", result.Configuration);
        Assert.Equal("x64", result.Platform);
        Assert.True(result.Preview);
        Assert.Equal(123, result.MaxResults);
        Assert.Empty(result.Plugins);
        Assert.True(result.ExecutableFilePresent);
        Assert.True(result.UserFilePresent);
        Assert.True(result.WorkingDirectoryFilePresent);
    }

    [Fact]
    public void A_user_file_alone_counts_as_configuration()
    {
        var user = Write("user", """{"workspace-path":"App.sln"}""");

        var result = RoslynMcpFileSettings.LoadFromDirectories(null, null, user, logger: null);

        Assert.True(result.HasAnyFile);
        Assert.True(result.UserFilePresent);
        Assert.Equal(Path.Combine(user, RoslynMcpFileSettings.FileName), result.UserFilePath);
        Assert.Equal("App.sln", result.WorkspacePath);
    }

    [Fact]
    public void A_broken_user_file_preserves_the_valid_sibling_sources()
    {
        var exe = Write("exe", """{"platform":"x64"}""");
        var user = Write("user", "{broken");
        var cwd = Write("cwd", """{"configuration":"Release"}""");

        var result = RoslynMcpFileSettings.LoadFromDirectories(exe, cwd, user, logger: null);

        Assert.Equal("x64", result.Platform);
        Assert.Equal("Release", result.Configuration);
        Assert.Equal(Path.Combine(user, RoslynMcpFileSettings.FileName), Assert.Single(result.ParseFailures).Path);
    }

    [Fact]
    public void Reusing_a_file_at_the_highest_priority_reuses_its_parse_but_retains_that_priority()
    {
        var exeAndCwd = Write("exe", """{"configuration":"ExeAndCwd"}""");
        var user = Write("user", """{"configuration":"User"}""");

        var result = RoslynMcpFileSettings.LoadFromDirectories(exeAndCwd, exeAndCwd, user, logger: null);

        Assert.Equal("ExeAndCwd", result.Configuration);
        Assert.Empty(result.ParseFailures);
    }

    [Fact]
    public void A_broken_file_shared_by_all_sources_is_reported_once()
    {
        var shared = Write("shared", "{broken");

        var result = RoslynMcpFileSettings.LoadFromDirectories(shared, Path.Combine(shared, "."), shared, logger: null);

        Assert.Single(result.ParseFailures);
    }

    [Fact]
    public void A_user_location_that_is_not_a_directory_keeps_the_exe_configuration()
    {
        var exe = Write("exe", """{"configuration":"Debug"}""");
        var inaccessibleDirectory = Path.Combine(_root, "not-a-directory");
        File.WriteAllText(inaccessibleDirectory, "file");

        var result = RoslynMcpFileSettings.LoadFromDirectories(exe, null, inaccessibleDirectory, logger: null);

        Assert.Equal("Debug", result.Configuration);
        Assert.False(result.UserFilePresent);
    }

    private string Write(string name, string content)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, RoslynMcpFileSettings.FileName), content);
        return directory;
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "RoslynMcpUserSettings-" + Guid.NewGuid().ToString("N"));
}
