using System.Diagnostics;
using RoslynMcpServer.Diagnostics;
using Xunit;

namespace RoslynMcpServer.Tests.Logging;

public sealed class HarnessSessionIdTests
{
    [Theory]
    [InlineData(@"C:\cursor\Cursor.exe", "--type=utility node.mojom @deepseek-ai/dsh", "cursor")]
    [InlineData(@"C:\cursor\CURSOR.EXE", null, "cursor")]
    [InlineData("cursor-agent", null, "cursor")]
    [InlineData(@"C:\Program Files\nodejs\node.exe", @"C:\pkg\node_modules\@deepseek-ai\dsh\lib\bin.js web", "dsh")]
    [InlineData("/usr/bin/node", "/usr/lib/node_modules/@anthropic-ai/claude-code/cli.js", "claude")]
    [InlineData("/usr/bin/node", "/opt/claude/cli.js", "claude")]
    [InlineData("/usr/bin/nodejs", "/usr/lib/node_modules/@openai/codex/bin.js", "codex")]
    [InlineData("/usr/bin/node", "/opt/codex/bin.js", "codex")]
    [InlineData(@"C:\Tools\opencode.exe", null, "opencode")]
    [InlineData("/usr/local/bin/opencode", null, "opencode")]
    [InlineData("/usr/bin/node", "/usr/lib/node_modules/opencode-ai/bin/opencode", "opencode")]
    [InlineData("/usr/bin/node", "/opt/opencode/index.js", "opencode")]
    [InlineData("/usr/bin/node", "/opt/cursor-agent/index.js", "cursor")]
    [InlineData("/usr/bin/node", "/opt/tools/echo.js", "cli")]
    [InlineData("/usr/bin/node", null, "cli")]
    [InlineData(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", @"node_modules\@deepseek-ai\dsh\lib\bin.js", "cli")]
    [InlineData(null, null, "cli")]
    public void Resolve_maps_immediate_parent(string? imagePath, string? commandLine, string expected)
    {
        Assert.Equal(expected, HarnessNameResolver.Resolve(imagePath, commandLine));
    }

    [Fact]
    public void Format_joins_harness_name_and_suffix()
    {
        Assert.Equal("[dsh-p9qx]", HarnessSessionId.Format(HarnessNameResolver.Dsh, "p9qx"));
    }

    [Fact]
    public void Suffix_is_four_characters_from_the_unambiguous_alphabet()
    {
        var suffix = HarnessSessionId.CreateSuffix();

        Assert.Equal(HarnessSessionId.SuffixLength, suffix.Length);
        Assert.All(suffix, character => Assert.Contains(character, HarnessSessionId.SuffixAlphabet));
    }

    [Fact]
    public void Current_process_session_id_has_harness_prefix_and_suffix()
    {
        var id = HarnessSessionId.CreateForCurrentProcess();
        var names = string.Join(
            '|',
            HarnessNameResolver.Cursor,
            HarnessNameResolver.Dsh,
            HarnessNameResolver.Claude,
            HarnessNameResolver.Codex,
            HarnessNameResolver.OpenCode,
            HarnessNameResolver.Unknown);
        var pattern = $@"^\[({names})-[{HarnessSessionId.SuffixAlphabet}]{{{HarnessSessionId.SuffixLength}}}\]$";

        Assert.Matches(pattern, id);
    }

    [Fact]
    public void Windows_reads_this_process_image_and_command_line()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var image = WindowsParentProcess.TryGetImagePath(Environment.ProcessId);
        Assert.False(string.IsNullOrWhiteSpace(image));
        var moduleName = Process.GetCurrentProcess().MainModule?.ModuleName;
        Assert.NotNull(moduleName);
        Assert.Equal(moduleName, Path.GetFileName(image), StringComparer.OrdinalIgnoreCase);

        var commandLine = WindowsParentProcess.TryReadCommandLine(Environment.ProcessId);
        Assert.NotNull(commandLine);
        Assert.Contains("testhost", commandLine, StringComparison.OrdinalIgnoreCase);
        Assert.True(WindowsParentProcess.TryGetParentProcessId() > 0);
    }

    [Fact]
    public void Linux_reads_parent_process_id()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        Assert.True(LinuxParentProcess.TryGetParentProcessId() > 0);
    }
}
