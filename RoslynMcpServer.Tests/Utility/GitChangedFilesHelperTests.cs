using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Services;
using RoslynMcpServer.Tests.Support;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.Utility;

public sealed class GitChangedFilesHelperTests
{
    [Fact]
    public void ParsePorcelainStatus_parses_modified_and_untracked()
    {
        const string output = """
            M  src/Foo.cs
            ?? docs/new.md
            """;

        var files = GitChangedFilesHelper.ParsePorcelainStatus(output);
        Assert.Equal(2, files.Count);
        Assert.Equal("src/Foo.cs", files[0].Path);
        Assert.Equal("M", files[0].Status);
        Assert.Equal("docs/new.md", files[1].Path);
    }

    [Theory]
    [InlineData("R ")]
    [InlineData(" C")]
    public void ParsePorcelainStatus_consumes_the_source_path_of_a_rename_or_copy(string status)
    {
        const string Destination = "  literal -> \"destination\"\n.cs ";
        const string NextPath = "next\tfile.cs";
        var output = $"{status} {Destination}\0?? looks like another record\0 M {NextPath}\0";

        var files = GitChangedFilesHelper.ParsePorcelainStatus(output);

        Assert.Collection(
            files,
            first => Assert.Equal(new GitChangedFilesHelper.ChangedFile(Destination, status.Trim()), first),
            next => Assert.Equal(new GitChangedFilesHelper.ChangedFile(NextPath, "M"), next));
    }

    [Fact]
    public void Default_execution_budget_is_180_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(180), GitChangedFilesHelper.DefaultExecutionTimeout);
    }

    [Fact]
    public async Task RunGitAsync_reads_a_megabyte_of_stderr_without_hanging()
    {
        var marker = NewMarker();
        using var directory = NewWorkDirectory();
        var script = Path.Combine(directory.FullName, "write-stderr.ps1");
        var start = StartInfoForMegabyteStderr(script, marker);
        try
        {
            var result = await FinishWithin(
                GitChangedFilesHelper.RunGitAsync(
                    directory.FullName,
                    arguments: "",
                    CancellationToken.None,
                    TimeSpan.FromSeconds(20),
                    start),
                TimeSpan.FromSeconds(30),
                () => KillByMarkerAsync(marker));

            Assert.True(result.Success, result.Error);
            Assert.Contains("PIPE-OK", result.Output, StringComparison.Ordinal);
        }
        finally
        {
            await KillByMarkerAsync(marker);
        }
    }

    [Fact]
    public async Task RunGitAsync_timeout_kills_the_process()
    {
        var marker = NewMarker();
        using var directory = NewWorkDirectory();
        var start = StartInfoForSleep(marker);
        try
        {
            var result = await FinishWithin(
                GitChangedFilesHelper.RunGitAsync(
                    directory.FullName,
                    arguments: "",
                    CancellationToken.None,
                    TimeSpan.FromSeconds(2),
                    start),
                TimeSpan.FromSeconds(15),
                () => KillByMarkerAsync(marker));

            Assert.False(result.Success);
            Assert.Contains("MCP_GIT_TIMEOUT", result.Error, StringComparison.Ordinal);
            Assert.Contains("process tree was killed", result.Error, StringComparison.Ordinal);
            Assert.Empty(await PidsWithMarkerAsync(marker));
        }
        finally
        {
            await KillByMarkerAsync(marker);
        }
    }

    [Fact]
    public async Task RunGitAsync_cancel_kills_parent_and_child()
    {
        var marker = NewMarker();
        using var directory = NewWorkDirectory();
        var start = StartInfoForParentAndChild(marker);
        using var cts = new CancellationTokenSource();
        var run = GitChangedFilesHelper.RunGitAsync(
            directory.FullName,
            arguments: "",
            cts.Token,
            TimeSpan.FromSeconds(30),
            start);
        try
        {
            var (shellPids, children) = await WaitForParentAndChildAsync(marker, TimeSpan.FromSeconds(15));
            Assert.NotEmpty(shellPids);
            Assert.NotEmpty(children);

            cts.Cancel();
            await FinishWithin(
                Assert.ThrowsAnyAsync<OperationCanceledException>(() => run),
                TimeSpan.FromSeconds(15),
                () => KillByMarkerAsync(marker));

            Assert.Empty(await PidsWithMarkerAsync(marker));
            foreach (var child in children)
            {
                Assert.False(PidAlive(child), $"child {child} still running");
            }
        }
        finally
        {
            await KillByMarkerAsync(marker);
        }
    }

    [Fact]
    public async Task RunGitAsync_returns_when_parent_exits_and_child_holds_pipes()
    {
        var marker = NewMarker();
        using var directory = NewWorkDirectory();
        var pidFile = Path.Combine(directory.FullName, "holder.pid");
        var start = StartInfoForPipeHolder(pidFile, marker);
        try
        {
            var result = await FinishWithin(
                GitChangedFilesHelper.RunGitAsync(
                    directory.FullName,
                    arguments: "",
                    CancellationToken.None,
                    TimeSpan.FromSeconds(5),
                    start),
                TimeSpan.FromSeconds(20),
                () => KillHoldersAsync(marker, pidFile));

            Assert.False(result.Success);
            Assert.Contains("MCP_GIT_TIMEOUT", result.Error, StringComparison.Ordinal);
            var holderAlive = HolderAlive(pidFile);
            if (holderAlive)
            {
                Assert.DoesNotContain("process tree was killed", result.Error, StringComparison.Ordinal);
            }
        }
        finally
        {
            await KillHoldersAsync(marker, pidFile);
        }
    }

    [Fact]
    public async Task RunGitAsync_status_on_this_repo_returns()
    {
        var root = FindRepositoryRoot();
        var result = await FinishWithin(
            GitChangedFilesHelper.RunGitAsync(root, "status --porcelain", CancellationToken.None),
            TimeSpan.FromSeconds(30),
            static () => Task.CompletedTask);

        Assert.True(result.Success, result.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetChangedFiles_reports_the_inner_checkout_for_file_and_directory_anchors(bool linkedWorktree)
    {
        using var directory = NewWorkDirectory();
        var source = Path.Combine(directory.FullName, "source");
        var outer = Path.Combine(directory.FullName, "outer");
        const string checkoutName = "nested-checkout";
        var checkout = Path.Combine(outer, checkoutName);
        await InitializeRepositoryAsync(source);
        await InitializeRepositoryAsync(outer);
        if (linkedWorktree)
        {
            await RunFixtureGitAsync(source, "worktree", "add", "--detach", checkout);
        }
        else
        {
            await RunFixtureGitAsync(outer, "-c", "protocol.file.allow=always", "submodule", "add", source, checkoutName);
        }

        Assert.True(File.Exists(Path.Combine(checkout, ".git")));
        await File.WriteAllTextAsync(Path.Combine(outer, "outer-change.txt"), "Outer repository");
        var nested = Path.Combine(checkout, "nested");
        var changedFile = Path.Combine(nested, "inner-change.txt");
        await File.WriteAllTextAsync(changedFile, "Inner checkout");

        var tools = new UtilityTools(NullLogger<UtilityTools>.Instance, SolutionManagerTestFactory.Create());
        // Git can resolve the macOS temp directory through its /private path.
        var checkoutSuffix = Path.Combine(Path.GetFileName(directory.FullName), "outer", checkoutName);
        foreach (var anchor in new[] { checkout, nested, changedFile })
        {
            var report = await tools.GetChangedFiles(anchor);

            Assert.Contains($"{checkoutSuffix}`", report, StringComparison.Ordinal);
            Assert.Contains("**Changed/untracked:** 1", report, StringComparison.Ordinal);
            Assert.Contains("| ?? | `nested/inner-change.txt` |", report, StringComparison.Ordinal);
            Assert.DoesNotContain("outer-change.txt", report, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("checkout\r\n", false, "checkout\r")]
    [InlineData("checkout\n\n", false, "checkout\n")]
    [InlineData("checkout \n", false, "checkout ")]
    [InlineData("checkout\r\n", true, "checkout")]
    [InlineData("checkout\n", true, "checkout")]
    public void TrimCheckoutLineTerminator_preserves_checkout_characters(string output, bool windows, string expected)
    {
        Assert.Equal(expected, GitChangedFilesHelper.TrimCheckoutLineTerminator(output, windows));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task GetChangedFiles_preserves_the_native_checkout_name(string whitespace)
    {
        using var directory = NewWorkDirectory();
        var outer = Path.Combine(directory.FullName, "outer");
        var checkoutName = OperatingSystem.IsWindows() ? "checkout" : "checkout" + whitespace;
        var checkout = Path.Combine(outer, checkoutName);
        await InitializeRepositoryAsync(outer);
        await InitializeRepositoryAsync(checkout);
        await File.WriteAllTextAsync(Path.Combine(outer, "outer-change.txt"), "Outer repository");
        var nested = Path.Combine(checkout, "nested");
        await File.WriteAllTextAsync(Path.Combine(nested, "changed.txt"), "Inner checkout");
        var tools = new UtilityTools(NullLogger<UtilityTools>.Instance, SolutionManagerTestFactory.Create());

        // On Windows these characters are input padding rather than part of the checkout name.
        var anchor = OperatingSystem.IsWindows() ? checkout + whitespace : checkout;
        var report = await tools.GetChangedFiles(anchor);
        var nestedReport = await tools.GetChangedFiles(nested);

        var checkoutSuffix = Path.Combine(Path.GetFileName(directory.FullName), "outer", checkoutName);
        Assert.Contains($"{checkoutSuffix}`", report, StringComparison.Ordinal);
        Assert.Contains("**Changed/untracked:** 1", report, StringComparison.Ordinal);
        Assert.Contains("| ?? | `nested/changed.txt` |", report, StringComparison.Ordinal);
        Assert.DoesNotContain("outer-change.txt", report, StringComparison.Ordinal);
        Assert.Equal(report, nestedReport);
    }

    [Fact]
    public async Task GetChangedFiles_outside_a_repository_returns_the_discovery_error()
    {
        using var directory = NewWorkDirectory();
        var tools = new UtilityTools(NullLogger<UtilityTools>.Instance, SolutionManagerTestFactory.Create());

        var report = await tools.GetChangedFiles(directory.FullName);

        Assert.StartsWith($"Error: No git repository found from `{directory.FullName}`.", report, StringComparison.Ordinal);
        Assert.DoesNotContain("## Git changed files", report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetChangedFiles_returns_literal_names_that_open_in_the_checkout(bool includeMarkdownDelimiters)
    {
        using var directory = NewWorkDirectory();
        await RunFixtureGitAsync(directory.FullName, "init", "--template=");
        await RunFixtureGitAsync(directory.FullName, "config", "--local", "core.quotepath", "true");
        Directory.CreateDirectory(Path.Combine(directory.FullName, "new"));
        var paths = new List<string> { "new/тест.cs", "new/ space [1].cs" };
        if (includeMarkdownDelimiters)
        {
            paths.Add("new/contains ``` fence.cs");
            if (!OperatingSystem.IsWindows())
            {
                paths.Add("new/literal -> \"quote\"\t` |\n| ?? | `next ");
            }
        }

        foreach (var path in paths)
        {
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, path), path);
        }

        var tools = new UtilityTools(NullLogger<UtilityTools>.Instance, SolutionManagerTestFactory.Create());
        var report = await tools.GetChangedFiles(directory.FullName);

        Assert.Contains($"**Changed/untracked:** {paths.Count}", report, StringComparison.Ordinal);
        Assert.Contains("new/тест.cs", report, StringComparison.Ordinal);
        string[] reportedPaths;
        if (includeMarkdownDelimiters)
        {
            var lines = report.Split(Environment.NewLine);
            var fence = Array.IndexOf(lines, "```json");
            Assert.InRange(fence, 0, lines.Length - 3);
            Assert.Equal("```", lines[fence + 2]);
            using var entries = JsonDocument.Parse(lines[fence + 1]);
            reportedPaths = entries.RootElement.EnumerateArray().Select(entry =>
            {
                Assert.Equal("??", entry.GetProperty("status").GetString());
                return entry.GetProperty("path").GetString()!;
            }).ToArray();
        }
        else
        {
            reportedPaths = report.Split("| ?? | `", StringSplitOptions.None)
                .Skip(1)
                .Select(row => row[..row.IndexOf("` |", StringComparison.Ordinal)])
                .ToArray();
        }

        Assert.Equal(paths.Order(StringComparer.Ordinal), reportedPaths);
        foreach (var path in reportedPaths)
        {
            Assert.Equal(path, await File.ReadAllTextAsync(Path.Combine(directory.FullName, path)));
        }
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("no")]
    public async Task GetChangedFiles_reports_the_total_and_shows_only_the_first_80_paths(string untrackedMode)
    {
        using var directory = NewWorkDirectory();
        await RunFixtureGitAsync(directory.FullName, "init", "--template=");
        await RunFixtureGitAsync(directory.FullName, "config", "--local", "status.showUntrackedFiles", untrackedMode);
        var info = Directory.CreateDirectory(Path.Combine(directory.FullName, ".git", "info"));
        await File.WriteAllTextAsync(Path.Combine(info.FullName, "exclude"), "ignored.txt\n");
        var nested = Directory.CreateDirectory(Path.Combine(directory.FullName, "new"));
        await File.WriteAllTextAsync(Path.Combine(nested.FullName, "ignored.txt"), "Ignored");
        for (var index = 0; index < 81; index++)
        {
            await File.WriteAllTextAsync(Path.Combine(nested.FullName, $"file-{index:000}.txt"), "Change");
        }

        var tools = new UtilityTools(NullLogger<UtilityTools>.Instance, SolutionManagerTestFactory.Create());
        var report = await tools.GetChangedFiles(directory.FullName);

        var rows = report.Split(Environment.NewLine).Where(line => line.StartsWith("| ?? |", StringComparison.Ordinal));
        var expectedRows = Enumerable.Range(0, 80).Select(index => $"| ?? | `new/file-{index:000}.txt` |");
        Assert.Equal(expectedRows, rows);
        Assert.Contains("**Changed/untracked:** 81", report, StringComparison.Ordinal);
        Assert.Contains("Showing first 80 of 81 paths.", report, StringComparison.Ordinal);
        Assert.DoesNotContain("ignored.txt", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetChangedFiles_preserves_non_utf8_paths_from_the_git_index()
    {
        using var directory = NewWorkDirectory();
        await InitializeRepositoryAsync(directory.FullName);
        var blob = await GitChangedFilesHelper.RunGitAsync(directory.FullName, "rev-parse HEAD:nested/seed.txt", CancellationToken.None);
        Assert.True(blob.Success, blob.Error);
        byte[][] paths = [[0x61, 0xFE, 0x2E, 0x63, 0x73], [0x61, 0xFF, 0x2E, 0x63, 0x73]];
        var entryHeader = System.Text.Encoding.ASCII.GetBytes($"100644 {blob.Output}\t");
        byte[] indexRecords = [.. entryHeader, .. paths[0], 0, .. entryHeader, .. paths[1], 0];
        var start = new ProcessStartInfo("git", "update-index -z --index-info")
        {
            WorkingDirectory = directory.FullName,
            UseShellExecute = false,
            RedirectStandardInput = true,
            StandardInputEncoding = System.Text.Encoding.ASCII,
            RedirectStandardError = true,
        };
        using var git = Process.Start(start)!;
        var error = git.StandardError.ReadToEndAsync();
        await git.StandardInput.BaseStream.WriteAsync(indexRecords);
        git.StandardInput.Close();
        await git.WaitForExitAsync();
        Assert.True(git.ExitCode == 0, await error);

        // A literal hexadecimal name could otherwise be mistaken for the escaped path.
        var literalName = Convert.ToHexString(paths[0]);
        await File.WriteAllTextAsync(Path.Combine(directory.FullName, literalName), "Plain text name");
        var tools = new UtilityTools(NullLogger<UtilityTools>.Instance, SolutionManagerTestFactory.Create());
        var report = await tools.GetChangedFiles(directory.FullName);

        var lines = report.Split(Environment.NewLine);
        var fence = Array.IndexOf(lines, "```json");
        Assert.InRange(fence, 0, lines.Length - 3);
        using var entries = JsonDocument.Parse(lines[fence + 1]);
        var files = entries.RootElement.EnumerateArray().ToArray();
        Assert.Equal(3, files.Length);
        var encoded = files.Where(file => file.TryGetProperty("pathEncoding", out _)).ToArray();
        Assert.Equal(paths.Length, encoded.Length);
        for (var i = 0; i < encoded.Length; i++)
        {
            Assert.Equal("hex", encoded[i].GetProperty("pathEncoding").GetString());
            Assert.Equal(paths[i], Convert.FromHexString(encoded[i].GetProperty("path").GetString()!));
        }
        var literal = Assert.Single(files, file => !file.TryGetProperty("pathEncoding", out _));
        Assert.Equal(literalName, literal.GetProperty("path").GetString());
    }

    private static async Task InitializeRepositoryAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        await RunFixtureGitAsync(directory, "init", "--template=");
        var nested = Directory.CreateDirectory(Path.Combine(directory, "nested"));
        await File.WriteAllTextAsync(Path.Combine(nested.FullName, "seed.txt"), "Base file");
        await RunFixtureGitAsync(directory, "add", "nested");
        await RunFixtureGitAsync(
            directory,
            "-c",
            "user.name=Fixture",
            "-c",
            "user.email=fixture@example.invalid",
            "-c",
            "commit.gpgsign=false",
            "commit",
            "-m",
            "Fixture");
    }

    private static async Task RunFixtureGitAsync(string directory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var result = await GitChangedFilesHelper.RunGitAsync(
            directory,
            arguments: "",
            CancellationToken.None,
            TimeSpan.FromSeconds(20),
            startInfo);
        Assert.True(result.Success, result.Error ?? result.Output);
    }

    private static ProcessStartInfo StartInfoForMegabyteStderr(string scriptPath, string marker)
    {
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(
                scriptPath,
                """
                1..1024 | ForEach-Object { [Console]::Error.Write(('E' * 1024)) }
                [Console]::Error.Flush()
                [Console]::Out.Write('PIPE-OK')
                [Console]::Out.Flush()
                """);
            return new ProcessStartInfo(
                _windowsPowerShell,
                $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" {marker}");
        }

        return new ProcessStartInfo(
            "/bin/sh",
            $"-c \"dd if=/dev/zero bs=1048576 count=1 >&2; printf PIPE-OK # {marker}\"");
    }

    private static ProcessStartInfo StartInfoForSleep(string marker)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo(
                _windowsPowerShell,
                $"-NoProfile -Command \"Start-Sleep -Seconds 120 # {marker}\"");
        }

        return new ProcessStartInfo("/bin/sh", $"-c \"sleep 120 # {marker}\"");
    }

    private static ProcessStartInfo StartInfoForParentAndChild(string marker)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo(
                "cmd.exe",
                $"/d /s /c \"\"{_windowsPowerShell}\" -NoProfile -Command \"Start-Sleep -Seconds 120 # {marker}\"\"");
        }

        // A final external command can replace the shell on macOS; wait keeps the parent alive.
        return new ProcessStartInfo("/bin/sh", $"-c \"sleep 120 & wait # {marker}\"");
    }

    private static ProcessStartInfo StartInfoForPipeHolder(string pidFile, string marker)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo(
                "cmd.exe",
                $"/d /c start /b \"\" \"{_windowsPowerShell}\" -NoProfile -Command \""
                + $"Set-Content -LiteralPath '{pidFile}' -Value $PID; Start-Sleep -Seconds 120 # {marker}\"");
        }

        var quoted = pidFile.Replace("'", "'\\''", StringComparison.Ordinal);
        return new ProcessStartInfo(
            "/bin/sh",
            $"-c \"sleep 120 & echo $! > '{quoted}' # {marker}\"");
    }

    private static async Task<T> FinishWithin<T>(Task<T> work, TimeSpan watchdog, Func<Task> kill)
    {
        var finished = await Task.WhenAny(work, Task.Delay(watchdog)).ConfigureAwait(false);
        if (!ReferenceEquals(finished, work))
        {
            await kill().ConfigureAwait(false);
            throw new TimeoutException($"helper did not return within {watchdog.TotalSeconds:0}s.");
        }

        return await work.ConfigureAwait(false);
    }

    private static async Task<(IReadOnlyList<int> ShellPids, IReadOnlyList<int> Children)> WaitForParentAndChildAsync(
        string marker,
        TimeSpan budget)
    {
        var deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline)
        {
            var shellPids = await PidsWithMarkerAsync(marker).ConfigureAwait(false);
            if (shellPids.Count > 0)
            {
                var children = await ChildPidsAsync(shellPids).ConfigureAwait(false);
                if (children.Count > 0)
                {
                    return (shellPids, children);
                }
            }

            await Task.Delay(200).ConfigureAwait(false);
        }

        return ([], []);
    }

    private static async Task<IReadOnlyList<int>> PidsWithMarkerAsync(string marker)
    {
        var rows = await ProcessRowsAsync().ConfigureAwait(false);
        return rows
            .Where(row => row.Command.Contains(marker, StringComparison.Ordinal))
            .Select(row => row.Pid)
            .Distinct()
            .ToList();
    }

    private static async Task<IReadOnlyList<int>> ChildPidsAsync(IReadOnlyList<int> parentPids)
    {
        var parents = parentPids.ToHashSet();
        var rows = await ProcessRowsAsync().ConfigureAwait(false);
        return rows
            .Where(row => parents.Contains(row.ParentPid))
            .Where(row => !row.Command.Contains("conhost.exe", StringComparison.OrdinalIgnoreCase))
            .Select(row => row.Pid)
            .Distinct()
            .ToList();
    }

    private static async Task<IReadOnlyList<ProcessRow>> ProcessRowsAsync()
    {
        ProcessStartInfo psi;
        if (OperatingSystem.IsWindows())
        {
            // EncodedCommand avoids quoting. [char]9 is a real tab; a single-quoted `t is not.
            const string script =
                "Get-CimInstance Win32_Process -Filter \"Name = 'cmd.exe' OR Name = 'powershell.exe' OR Name = 'pwsh.exe'\" | ForEach-Object { $_.ProcessId.ToString() + [char]9 + $_.ParentProcessId.ToString() + [char]9 + ([string]$_.CommandLine) }";
            var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
            psi = new ProcessStartInfo(_windowsPowerShell, "-NoProfile -EncodedCommand " + encoded);
        }
        else
        {
            psi = new ProcessStartInfo("/bin/ps", "-ax -o pid=,ppid=,command=");
        }

        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        using var process = Process.Start(psi);
        if (process is null)
        {
            return [];
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var exitTask = process.WaitForExitAsync();
        var drain = Task.WhenAll(stdoutTask, stderrTask, exitTask);
        var finished = await Task.WhenAny(drain, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
        if (!ReferenceEquals(finished, drain))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Snapshot helper already exited.
            }

            return [];
        }

        var text = await stdoutTask.ConfigureAwait(false);
        var rows = new List<ProcessRow>();
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (OperatingSystem.IsWindows())
            {
                var windowsParts = line.Split('\t', 3);
                if (windowsParts.Length >= 2
                    && int.TryParse(windowsParts[0], out var pid)
                    && int.TryParse(windowsParts[1], out var parentPid))
                {
                    rows.Add(new ProcessRow(pid, parentPid, windowsParts.Length == 3 ? windowsParts[2] : string.Empty));
                }

                continue;
            }

            var unixParts = line.Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
            if (unixParts.Length >= 2
                && int.TryParse(unixParts[0], out var unixPid)
                && int.TryParse(unixParts[1], out var unixParent))
            {
                rows.Add(new ProcessRow(unixPid, unixParent, unixParts.Length == 3 ? unixParts[2] : string.Empty));
            }
        }

        return rows;
    }

    private static bool PidAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool HolderAlive(string pidFile)
    {
        if (!File.Exists(pidFile))
        {
            return false;
        }

        var text = File.ReadAllText(pidFile).Trim();
        return int.TryParse(text, out var pid) && PidAlive(pid);
    }

    private static async Task KillHoldersAsync(string marker, string pidFile)
    {
        await KillByMarkerAsync(marker).ConfigureAwait(false);
        if (!File.Exists(pidFile))
        {
            return;
        }

        var text = await File.ReadAllTextAsync(pidFile).ConfigureAwait(false);
        if (int.TryParse(text.Trim(), out var pid))
        {
            KillPid(pid);
        }
    }

    private static async Task KillByMarkerAsync(string marker)
    {
        IReadOnlyList<int> pids;
        try
        {
            pids = await PidsWithMarkerAsync(marker).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return;
        }

        foreach (var pid in pids)
        {
            KillPid(pid);
        }
    }

    private static void KillPid(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone.
        }
    }

    private static string NewMarker() => "gitpipe" + Guid.NewGuid().ToString("N");

    private static WorkDirectory NewWorkDirectory() => WorkDirectory.Create();

    private sealed class WorkDirectory : IDisposable
    {
        private WorkDirectory(string fullName) => FullName = fullName;

        public string FullName { get; }

        public static WorkDirectory Create()
        {
            var path = Path.Combine(Path.GetTempPath(), "gitpipe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new WorkDirectory(path);
        }

        public void Dispose()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // Git can leave read-only objects in worktree and submodule metadata.
                    foreach (var file in new DirectoryInfo(FullName).EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        if (file.IsReadOnly)
                        {
                            file.IsReadOnly = false;
                        }
                    }
                }

                Directory.Delete(FullName, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A holder may still have the directory open. The test already killed it.
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            // In a linked worktree .git is a file that points at the main repository.
            var gitEntry = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(gitEntry) || File.Exists(gitEntry))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root was not found from the test output directory.");
    }

    private readonly record struct ProcessRow(int Pid, int ParentPid, string Command);

    // The process fixtures must still launch a real child when cmd lacks PowerShell on PATH.
    private static readonly string _windowsPowerShell =
        Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
}
