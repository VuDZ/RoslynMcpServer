using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services;

/// <summary>Git status/diff helpers for commit-scoped agent workflows.</summary>
public static class GitChangedFilesHelper
{
    public sealed record ChangedFile(string Path, string Status);

    public static string? FindRepositoryRoot(string startPath)
    {
        var full = Path.GetFullPath(startPath);
        if (File.Exists(full))
        {
            full = Path.GetDirectoryName(full) ?? full;
        }

        var current = new DirectoryInfo(full);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    public static Task<(bool Success, string Output, string? Error)> RunGitAsync(
        string repositoryRoot,
        string arguments,
        CancellationToken cancellationToken)
        => RunGitAsync(repositoryRoot, arguments, cancellationToken, DefaultExecutionTimeout, startInfo: null);

    /// <summary>
    /// Runs a git command that does not read stdin. The only caller is <c>git status</c>.
    /// Stdin is closed immediately, so this method must not be used for commands that read it
    /// (<c>git apply</c>, <c>git commit -F -</c>).
    /// </summary>
    /// <remarks>
    /// Both redirected pipes are read together with process exit. Reading stdout to EOF and only
    /// then stderr deadlocks when the child fills the stderr buffer and never closes stdout.
    /// The same finite pipe exists on Windows, Linux, and macOS.
    /// Waiting out <see cref="Process.WaitForExitAsync(CancellationToken)"/> alone is also not
    /// enough: the process that was started can exit while a child still holds the pipe handles.
    /// <paramref name="executionTimeout"/> covers exit and both EOFs. Stopping the tree uses
    /// <see cref="Process.Kill(bool)"/> with <c>entireProcessTree: true</c> (supported on all
    /// three OS) and one shared cleanup budget, not a second unlimited read and not a
    /// Windows-only job object.
    /// </remarks>
    internal static Task<(bool Success, string Output, string? Error)> RunGitAsync(
        string repositoryRoot,
        string arguments,
        CancellationToken cancellationToken,
        TimeSpan executionTimeout,
        ProcessStartInfo? startInfo)
        => RunCoreAsync(repositoryRoot, arguments, cancellationToken, executionTimeout, startInfo);

    public static IReadOnlyList<ChangedFile> ParsePorcelainStatus(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var files = new List<ChangedFile>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 4)
            {
                continue;
            }

            var status = line[..2].Trim();
            var path = line[3..].Trim().Trim('"');
            if (path.Contains(" -> ", StringComparison.Ordinal))
            {
                path = path.Split(" -> ", 2, StringSplitOptions.TrimEntries)[^1];
            }

            files.Add(new ChangedFile(path, status));
        }

        return files;
    }

    public static IReadOnlyList<string> SuggestTestProjects(Solution? solution, IReadOnlyList<string> changedRelativePaths)
    {
        if (solution is null || changedRelativePaths.Count == 0)
        {
            return [];
        }

        var changedSet = new HashSet<string>(changedRelativePaths, StringComparer.OrdinalIgnoreCase);
        var suggestions = new List<string>();

        foreach (var project in solution.Projects)
        {
            var name = project.Name;
            if (!name.Contains("test", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var doc in project.Documents)
            {
                if (doc.FilePath is null)
                {
                    continue;
                }

                var rel = GetRelativePathSafe(solution, doc.FilePath);
                if (rel is null)
                {
                    continue;
                }

                var stem = Path.GetFileNameWithoutExtension(rel);
                if (changedSet.Any(p => p.Contains(stem, StringComparison.OrdinalIgnoreCase)
                                        || stem.Contains(Path.GetFileNameWithoutExtension(p), StringComparison.OrdinalIgnoreCase)))
                {
                    suggestions.Add(project.FilePath ?? name);
                    break;
                }
            }
        }

        return suggestions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? GetRelativePathSafe(Solution solution, string filePath)
    {
        var roots = solution.Projects
            .Select(p => p.FilePath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetDirectoryName(p!)!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(r => r.Length);

        foreach (var root in roots)
        {
            if (filePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetRelativePath(root, filePath);
            }
        }

        return Path.GetFileName(filePath);
    }

    private static async Task<(bool Success, string Output, string? Error)> RunCoreAsync(
        string repositoryRoot,
        string arguments,
        CancellationToken cancellationToken,
        TimeSpan executionTimeout,
        ProcessStartInfo? startInfo)
    {
        if (executionTimeout <= TimeSpan.Zero)
        {
            executionTimeout = DefaultExecutionTimeout;
        }

        var psi = PrepareStartInfo(repositoryRoot, arguments, startInfo);
        using var process = new Process { StartInfo = psi };
        if (!TryStart(process, out var startError))
        {
            return (false, string.Empty, startError);
        }

        // Close the write end so a child blocked on stdin gets EOF. GIT_TERMINAL_PROMPT does not
        // cover every prompt a child program can wait on.
        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The child had already exited.
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        var exitTask = process.WaitForExitAsync(CancellationToken.None);
        var finished = Task.WhenAll(Silence(exitTask), Silence(stdoutTask), Silence(stderrTask));

        try
        {
            var completed = await WaitForRunAsync(finished, executionTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (!completed)
            {
                var cleanup = await CleanupAsync(process, exitTask, stdoutTask, stderrTask)
                    .ConfigureAwait(false);
                return (false, ReadCompleted(stdoutTask), FormatTimeout(executionTimeout, cleanup));
            }
        }
        catch (OperationCanceledException)
        {
            await CleanupAsync(process, exitTask, stdoutTask, stderrTask).ConfigureAwait(false);
            throw;
        }
        finally
        {
            KillIfStillRunning(process);
        }

        if (stdoutTask.IsFaulted || stderrTask.IsFaulted || !process.HasExited)
        {
            var error = stdoutTask.Exception?.GetBaseException().Message
                ?? stderrTask.Exception?.GetBaseException().Message
                ?? "git did not exit";
            return (false, ReadCompleted(stdoutTask), error);
        }

        var stdout = ReadCompleted(stdoutTask);
        var stderr = ReadCompleted(stderrTask);
        if (process.ExitCode != 0)
        {
            return (false, stdout, string.IsNullOrWhiteSpace(stderr) ? $"git exit {process.ExitCode}" : stderr);
        }

        return (true, stdout, null);
    }

    private static ProcessStartInfo PrepareStartInfo(
        string repositoryRoot,
        string arguments,
        ProcessStartInfo? startInfo)
    {
        var psi = startInfo ?? new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
        };

        if (string.IsNullOrWhiteSpace(psi.WorkingDirectory))
        {
            psi.WorkingDirectory = repositoryRoot;
        }

        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.StandardOutputEncoding ??= Encoding.UTF8;
        psi.StandardErrorEncoding ??= Encoding.UTF8;
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        return psi;
    }

    private static bool TryStart(Process process, out string? error)
    {
        try
        {
            if (!process.Start())
            {
                error = "Failed to start `git`. Ensure Git is on PATH.";
                return false;
            }
        }
        catch (Win32Exception ex)
        {
            error = $"Failed to start `git`. Ensure Git is on PATH. {ex.Message}";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Returns <c>true</c> when exit and both EOFs happened inside the execution budget.
    /// Cancellation of <paramref name="cancellationToken"/> propagates.
    /// </summary>
    private static async Task<bool> WaitForRunAsync(
        Task finished,
        TimeSpan executionTimeout,
        CancellationToken cancellationToken)
    {
        using var executionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        executionCts.CancelAfter(executionTimeout);
        try
        {
            await finished.WaitAsync(executionCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (finished.IsCompletedSuccessfully)
        {
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task<PipeCleanup> CleanupAsync(
        Process process,
        Task exitTask,
        Task<string> stdoutTask,
        Task<string> stderrTask)
    {
        var killAttempted = false;
        Exception? killError = null;
        if (!process.HasExited)
        {
            killAttempted = true;
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                if (!process.HasExited)
                {
                    killError = ex;
                }
            }
        }

        // One budget for exit and both EOFs. Sequential drains would add the timeout twice.
        var drain = Task.WhenAll(Silence(exitTask), Silence(stdoutTask), Silence(stderrTask));
        var finishedInBudget = await Task.WhenAny(drain, Task.Delay(CleanupTimeout)).ConfigureAwait(false);
        if (finishedInBudget != drain)
        {
            _ = Silence(drain);
        }

        return new PipeCleanup(
            process.HasExited,
            stdoutTask.IsCompletedSuccessfully && stderrTask.IsCompletedSuccessfully,
            killError,
            killAttempted);
    }

    private static string FormatTimeout(TimeSpan executionTimeout, PipeCleanup cleanup)
    {
        var seconds = (int)Math.Round(executionTimeout.TotalSeconds, MidpointRounding.AwayFromZero);
        var cleanupSeconds = (int)Math.Round(CleanupTimeout.TotalSeconds, MidpointRounding.AwayFromZero);
        if (cleanup.ProcessExited && cleanup.StreamsClosed && cleanup.KillAttempted)
        {
            return $"MCP_GIT_TIMEOUT: exceeded {seconds}s and the process tree was killed.";
        }

        if (cleanup.KillError is not null && !cleanup.ProcessExited)
        {
            return $"MCP_GIT_TIMEOUT: exceeded {seconds}s. Kill failed: {cleanup.KillError.Message}";
        }

        if (cleanup.ProcessExited && !cleanup.StreamsClosed)
        {
            return $"MCP_GIT_TIMEOUT: exceeded {seconds}s. The process exited but stdout/stderr stayed open after {cleanupSeconds}s (a child may still hold the pipes).";
        }

        if (!cleanup.ProcessExited)
        {
            return $"MCP_GIT_TIMEOUT: exceeded {seconds}s. Kill was requested but the process was still running after {cleanupSeconds}s.";
        }

        return $"MCP_GIT_TIMEOUT: exceeded {seconds}s. The process exited during cleanup.";
    }

    private static void KillIfStillRunning(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Already gone, or this OS refused the tree kill. The caller still returns.
        }
    }

    private static string ReadCompleted(Task<string> task)
    {
        if (!task.IsCompletedSuccessfully)
        {
            return string.Empty;
        }

        return task.Result.TrimEnd();
    }

    private static Task Silence(Task task)
    {
        return task.ContinueWith(
            static completed => { _ = completed.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private sealed record PipeCleanup(
        bool ProcessExited,
        bool StreamsClosed,
        Exception? KillError,
        bool KillAttempted);

    internal static readonly TimeSpan DefaultExecutionTimeout = TimeSpan.FromSeconds(180);

    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(3);

}
