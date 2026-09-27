using System.Diagnostics;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

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
            await KillByMarkerAsync(marker).ConfigureAwait(false);
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
            Assert.Empty(await PidsWithMarkerAsync(marker).ConfigureAwait(false));
        }
        finally
        {
            await KillByMarkerAsync(marker).ConfigureAwait(false);
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
            var (shellPids, children) = await WaitForParentAndChildAsync(marker, TimeSpan.FromSeconds(15))
                .ConfigureAwait(false);
            Assert.NotEmpty(shellPids);
            Assert.NotEmpty(children);

            cts.Cancel();
            await FinishWithin(
                Assert.ThrowsAnyAsync<OperationCanceledException>(() => run),
                TimeSpan.FromSeconds(15),
                () => KillByMarkerAsync(marker));

            Assert.Empty(await PidsWithMarkerAsync(marker).ConfigureAwait(false));
            foreach (var child in children)
            {
                Assert.False(PidAlive(child), $"child {child} still running");
            }
        }
        finally
        {
            await KillByMarkerAsync(marker).ConfigureAwait(false);
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
            await KillHoldersAsync(marker, pidFile).ConfigureAwait(false);
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
                "powershell.exe",
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
                "powershell.exe",
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
                $"/c powershell.exe -NoProfile -Command \"Start-Sleep -Seconds 120 # {marker}\"");
        }

        return new ProcessStartInfo("/bin/sh", $"-c \"sleep 120 # {marker}\"");
    }

    private static ProcessStartInfo StartInfoForPipeHolder(string pidFile, string marker)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo(
                "cmd.exe",
                "/c start /b \"\" powershell.exe -NoProfile -Command \""
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
            psi = new ProcessStartInfo("powershell.exe", "-NoProfile -EncodedCommand " + encoded);
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
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root was not found from the test output directory.");
    }

    private readonly record struct ProcessRow(int Pid, int ParentPid, string Command);
}
