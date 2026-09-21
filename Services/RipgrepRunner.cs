using System.Diagnostics;
using System.Text;

namespace RoslynMcpServer.Services;

/// <summary>
/// Launches <c>rg</c>/<c>rg.exe</c> and parses <c>path:line:content</c> stdout.
/// Does not decide when to use ripgrep — callers must opt in.
/// </summary>
internal static class RipgrepRunner
{
    /// <summary>Soft Windows CreateProcess argument budget used when packing many roots.</summary>
    internal const int WindowsCommandLineSoftLimit = 24_000;

    /// <summary>
    /// Test seam: when set, <see cref="ResolveBinary"/> delegates here instead of probing the filesystem/PATH.
    /// Signature is <c>(argumentPath, configPath) => resolvedPathOrNull</c>.
    /// </summary>
    internal static Func<string?, string?, string?>? ResolveBinaryOverride { get; set; }

    /// <summary>
    /// Test seam: when set, PATH lookup is skipped and this delegate is used for the bare <c>rg</c> name.
    /// </summary>
    internal static Func<string>? FindOnPathOverride { get; set; }

    internal sealed record MatchLine(string FilePath, int LineNumber, string Text);

    internal sealed record RunResult(
        IReadOnlyList<MatchLine> Matches,
        bool TimedOut,
        bool Cancelled,
        bool MaxResultsReached,
        int ExitCode,
        string StdErr);

    /// <summary>
    /// Resolves the ripgrep binary. Prefers <paramref name="argumentPath"/>, then <paramref name="configPath"/>,
    /// then <c>rg</c> on PATH. Returns <c>null</c> when nothing usable is found.
    /// </summary>
    internal static string? ResolveBinary(string? argumentPath, string? configPath)
    {
        if (ResolveBinaryOverride is not null)
        {
            return ResolveBinaryOverride(argumentPath, configPath);
        }

        if (!string.IsNullOrWhiteSpace(argumentPath))
        {
            var trimmed = argumentPath.Trim();
            // Explicit path wins: do not fall through to config or PATH when missing.
            return File.Exists(trimmed) ? Path.GetFullPath(trimmed) : null;
        }

        if (!string.IsNullOrWhiteSpace(configPath))
        {
            var trimmed = configPath.Trim();
            // Configured path wins when the argument is omitted: no PATH fallback.
            return File.Exists(trimmed) ? Path.GetFullPath(trimmed) : null;
        }

        return FindOnPath("rg");
    }

    internal static string? FindOnPath(string fileName)
    {
        if (FindOnPathOverride is not null)
        {
            return FindOnPathOverride();
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(dir.Trim().Trim('"'), fileName);
            }
            catch
            {
                continue;
            }

            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }

            if (OperatingSystem.IsWindows())
            {
                var withExe = candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? candidate
                    : candidate + ".exe";
                if (File.Exists(withExe))
                {
                    return Path.GetFullPath(withExe);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Parses one ripgrep stdout line (<c>path:line:content</c>), including Windows drive letters.
    /// </summary>
    internal static bool TryParseMatchLine(string line, out MatchLine? match)
    {
        match = null;
        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        // Prefer an explicit scan so "C:\a\b.cs:12:text" keeps the drive letter in the path.
        var start = 0;
        if (line.Length >= 3
            && char.IsAsciiLetter(line[0])
            && line[1] == ':'
            && (line[2] == '\\' || line[2] == '/'))
        {
            start = 2;
        }

        for (var i = start; i < line.Length - 1; i++)
        {
            if (line[i] != ':')
            {
                continue;
            }

            var digitStart = i + 1;
            if (digitStart >= line.Length || !char.IsAsciiDigit(line[digitStart]))
            {
                continue;
            }

            var digitEnd = digitStart;
            while (digitEnd < line.Length && char.IsAsciiDigit(line[digitEnd]))
            {
                digitEnd++;
            }

            if (digitEnd >= line.Length || line[digitEnd] != ':')
            {
                continue;
            }

            if (!int.TryParse(line.AsSpan(digitStart, digitEnd - digitStart), out var lineNumber)
                || lineNumber <= 0)
            {
                continue;
            }

            match = new MatchLine(line[..i], lineNumber, line[(digitEnd + 1)..]);
            return true;
        }

        return false;
    }

    internal static string FormatManagedMatch(MatchLine match)
        => $"{match.FilePath}:{match.LineNumber} | {match.Text}";

    internal static async Task<RunResult> RunAsync(
        string binaryPath,
        IReadOnlyList<string> roots,
        string pattern,
        bool useRegex,
        bool caseSensitive,
        bool includeAllExtensions,
        IReadOnlyCollection<string> extensions,
        int maxResults,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binaryPath);
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        if (roots.Count == 0)
        {
            throw new ArgumentException("At least one search root is required.", nameof(roots));
        }

        if (maxResults <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults));
        }

        var sharedPrefix = BuildSharedArgumentPrefix(
            pattern,
            useRegex,
            caseSensitive,
            includeAllExtensions,
            extensions);
        var batches = PackRootBatches(sharedPrefix, roots, WindowsCommandLineSoftLimit);
        var matches = new List<MatchLine>(Math.Min(maxResults, 200));
        var stderrParts = new List<string>();
        var timedOut = false;
        var cancelled = false;
        var lastExit = 0;
        var remaining = timeout;

        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (matches.Count >= maxResults)
            {
                break;
            }

            var batchStarted = Stopwatch.StartNew();
            var batchResult = await RunOneBatchAsync(
                    binaryPath,
                    batch,
                    maxResults - matches.Count,
                    remaining,
                    cancellationToken)
                .ConfigureAwait(false);

            lastExit = batchResult.ExitCode;
            if (!string.IsNullOrWhiteSpace(batchResult.StdErr))
            {
                stderrParts.Add(batchResult.StdErr);
            }

            matches.AddRange(batchResult.Matches);
            if (batchResult.TimedOut)
            {
                timedOut = true;
                break;
            }

            if (batchResult.Cancelled)
            {
                cancelled = true;
                break;
            }

            if (remaining is { } budget)
            {
                var left = budget - batchStarted.Elapsed;
                if (left <= TimeSpan.Zero)
                {
                    timedOut = true;
                    break;
                }

                remaining = left;
            }
        }

        return new RunResult(
            matches,
            timedOut,
            cancelled,
            matches.Count >= maxResults,
            lastExit,
            string.Join(Environment.NewLine, stderrParts).Trim());
    }

    internal static IReadOnlyList<string> PackRootBatches(
        string sharedPrefix,
        IReadOnlyList<string> roots,
        int commandLineSoftLimit)
    {
        if (roots.Count == 0)
        {
            return Array.Empty<string>();
        }

        var batches = new List<string>();
        var current = new StringBuilder(sharedPrefix);
        var used = Encoding.UTF8.GetByteCount(sharedPrefix);
        var hasRoot = false;

        foreach (var root in roots)
        {
            var quoted = QuoteArgument(root);
            var extra = 1 + Encoding.UTF8.GetByteCount(quoted); // leading space
            if (hasRoot && used + extra > commandLineSoftLimit)
            {
                batches.Add(current.ToString());
                current = new StringBuilder(sharedPrefix);
                used = Encoding.UTF8.GetByteCount(sharedPrefix);
                hasRoot = false;
            }

            current.Append(' ');
            current.Append(quoted);
            used += extra;
            hasRoot = true;
        }

        if (hasRoot)
        {
            batches.Add(current.ToString());
        }

        return batches;
    }

    internal static string BuildSharedArgumentPrefix(
        string pattern,
        bool useRegex,
        bool caseSensitive,
        bool includeAllExtensions,
        IReadOnlyCollection<string> extensions)
    {
        var sb = new StringBuilder(256);
        sb.Append("--line-number --with-filename --no-heading --color never");
        if (!useRegex)
        {
            sb.Append(" --fixed-strings");
        }

        if (!caseSensitive)
        {
            sb.Append(" -i");
        }

        if (!includeAllExtensions)
        {
            foreach (var extension in extensions.OrderBy(static e => e, StringComparer.OrdinalIgnoreCase))
            {
                var normalized = extension.StartsWith('.') ? extension : "." + extension;
                sb.Append(" --glob ");
                sb.Append(QuoteArgument("*" + normalized));
            }
        }

        sb.Append(" -- ");
        sb.Append(QuoteArgument(pattern));
        return sb.ToString();
    }

    private static async Task<RunResult> RunOneBatchAsync(
        string binaryPath,
        string arguments,
        int maxResults,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = binaryPath,
            Arguments = arguments,
            WorkingDirectory = Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start `{binaryPath}`.");
        }

        var matches = new List<MatchLine>(Math.Min(maxResults, 200));
        var stdoutTask = ReadMatchesAsync(process, matches, maxResults, CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            if (!await WaitForExitOrTimeoutAsync(process, timeout, cancellationToken).ConfigureAwait(false))
            {
                await stdoutTask.ConfigureAwait(false);
                var stderr = (await stderrTask.ConfigureAwait(false)).TrimEnd();
                return new RunResult(
                    matches,
                    TimedOut: false,
                    Cancelled: false,
                    MaxResultsReached: matches.Count >= maxResults,
                    ExitCode: process.HasExited ? process.ExitCode : 0,
                    StdErr: stderr);
            }

            TryKillProcessTree(process);
            await DrainAsync(stdoutTask).ConfigureAwait(false);
            var timedOutStderr = (await DrainStderrAsync(stderrTask).ConfigureAwait(false)).TrimEnd();
            return new RunResult(
                matches,
                TimedOut: true,
                Cancelled: false,
                MaxResultsReached: matches.Count >= maxResults,
                ExitCode: process.HasExited ? process.ExitCode : -1,
                StdErr: timedOutStderr);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            await DrainAsync(stdoutTask).ConfigureAwait(false);
            _ = await DrainStderrAsync(stderrTask).ConfigureAwait(false);
            return new RunResult(
                matches,
                TimedOut: false,
                Cancelled: true,
                MaxResultsReached: matches.Count >= maxResults,
                ExitCode: process.HasExited ? process.ExitCode : -1,
                StdErr: string.Empty);
        }
        catch
        {
            TryKillProcessTree(process);
            throw;
        }
    }

    private static async Task ReadMatchesAsync(
        Process process,
        List<MatchLine> matches,
        int maxResults,
        CancellationToken cancellationToken)
    {
        while (matches.Count < maxResults)
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (!TryParseMatchLine(line, out var match) || match is null)
            {
                continue;
            }

            matches.Add(match);
            if (matches.Count >= maxResults)
            {
                TryKillProcessTree(process);
                break;
            }
        }
    }

    private static async Task<bool> WaitForExitOrTimeoutAsync(
        Process process,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var exitTask = process.WaitForExitAsync(CancellationToken.None);
        if (timeout is null && !cancellationToken.CanBeCanceled)
        {
            await exitTask.ConfigureAwait(false);
            return false;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout is { } t && t > TimeSpan.Zero)
        {
            linked.CancelAfter(t);
        }

        var delayTask = Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
        var completed = await Task.WhenAny(exitTask, delayTask).ConfigureAwait(false);
        if (completed == exitTask)
        {
            await linked.CancelAsync().ConfigureAwait(false);
            await exitTask.ConfigureAwait(false);
            return false;
        }

        // Delay won: either timeout or caller cancel.
        cancellationToken.ThrowIfCancellationRequested();
        return true;
    }

    private static async Task DrainAsync(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch
        {
            // best effort
        }
    }

    private static async Task<string> DrainStderrAsync(Task<string> stderrTask)
    {
        try
        {
            return await stderrTask.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // best effort
        }
    }

    private static string QuoteArgument(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        var needsQuotes = false;
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch) || ch is '"' or '\'')
            {
                needsQuotes = true;
                break;
            }
        }

        if (!needsQuotes)
        {
            return value;
        }

        var sb = new StringBuilder(value.Length + 8);
        sb.Append('"');
        foreach (var ch in value)
        {
            if (ch == '"')
            {
                sb.Append('\\');
            }

            sb.Append(ch);
        }

        sb.Append('"');
        return sb.ToString();
    }
}
