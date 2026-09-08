using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tools;

public sealed class UtilityTools
{
    private const string AgentMemoryDirectoryName = ".agent_memory";
    private const string ScratchpadFileName = "scratchpad.md";

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        ".git",
        ".vs"
    };

    private readonly ILogger<UtilityTools> _logger;
    private readonly SolutionManager _solutionManager;

    public UtilityTools(ILogger<UtilityTools> logger, SolutionManager solutionManager)
    {
        _logger = logger;
        _solutionManager = solutionManager;
    }

    [McpServerTool(Name = "execute_dotnet_command", Title = "ExecuteDotNetCommand")]
    [Description(
        "Runs raw dotnet {command}. Executes a process. Prefer specialized build/test/run tools.")]
    public async Task<string> ExecuteDotNetCommand(
        [Description("Arguments after dotnet, for example test or add package Moq.")] string command,
        [Description("Working directory. Omit to use process CWD then nearest global.json root.")] string? workingDirectory = null,
        [Description("Process timeout in seconds. 0 disables timeout.")]
        int timeoutSeconds = DotNetCliRunner.DefaultTimeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return ToolTelemetry.TraceAndReturn(nameof(ExecuteDotNetCommand), "Command is empty.");
            }

            var fullWorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? Environment.CurrentDirectory
                : Path.GetFullPath(workingDirectory);
            if (!Directory.Exists(fullWorkingDirectory))
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(ExecuteDotNetCommand),
                    $"Working directory not found: `{fullWorkingDirectory}`");
            }

            var workDir = WorkspaceRootResolver.ResolveDotNetWorkingDirectory(fullWorkingDirectory);
            TimeSpan? timeout = timeoutSeconds > 0 ? TimeSpan.FromSeconds(timeoutSeconds) : null;
            var run = await DotNetCliRunner.RunSeparatedAsync(command.Trim(), workDir, timeout, cancellationToken)
                .ConfigureAwait(false);

            var stdoutExcerpt = ProcessOutputExcerpt.BuildStdoutExcerpt(run.StdOut, 6000);
            var stderrExcerpt = ProcessOutputExcerpt.BuildStderrExcerpt(run.StdErr, 2000);

            var sb = new StringBuilder();
            sb.AppendLine("## dotnet command");
            sb.AppendLine();
            foreach (var line in run.RunMetadata.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                sb.AppendLine(line);
            }

            sb.AppendLine($"- **Command:** `dotnet {command.Trim()}`");
            sb.AppendLine($"- **Exit code:** `{run.ExitCode}`");
            if (run.TimedOut)
            {
                sb.AppendLine("- **Timed out:** yes (process tree killed)");
                sb.AppendLine();
                sb.AppendLine(DotNetCliRunner.FormatHangHints(timedOut: true, cancelled: false));
            }

            sb.AppendLine();
            sb.AppendLine("### StdOut");
            sb.AppendLine(string.IsNullOrEmpty(stdoutExcerpt) ? "(empty)" : "```text\n" + stdoutExcerpt + "\n```");
            sb.AppendLine();
            sb.AppendLine("### StdErr");
            sb.AppendLine(string.IsNullOrEmpty(stderrExcerpt) ? "(empty)" : "```text\n" + stderrExcerpt + "\n```");

            return ToolTelemetry.TraceAndReturn(nameof(ExecuteDotNetCommand), sb.ToString().TrimEnd());
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(
                nameof(ExecuteDotNetCommand),
                "Command was cancelled." + Environment.NewLine + Environment.NewLine
                + DotNetCliRunner.FormatHangHints(timedOut: false, cancelled: true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExecuteDotNetCommand failed for command {Command} in {WorkingDirectory}", command, workingDirectory);
            return ToolTelemetry.TraceAndReturn(nameof(ExecuteDotNetCommand), $"Failed to run `dotnet {command}`: {ex.Message}");
        }
    }

    [McpServerTool(Name = "get_changed_files", Title = "Get changed files (git)")]
    [Description("Lists git changed and untracked files. Executes git. Does not return diffs.")]
    public async Task<string> GetChangedFiles(
        [Description("Path to a .sln/.slnx/.csproj or repo directory. Omit to use loaded workspace or current directory.")]
        string? workspacePath = null,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(GetChangedFiles);

        try
        {
            var anchor = ResolveGitAnchorPath(workspacePath);
            var repoRoot = GitChangedFilesHelper.FindRepositoryRoot(anchor);
            if (repoRoot is null)
            {
                return ToolTelemetry.TraceAndReturn(toolName, $"Error: No git repository found from `{anchor}`.");
            }

            var status = await GitChangedFilesHelper.RunGitAsync(repoRoot, "status --porcelain", cancellationToken)
                .ConfigureAwait(false);
            if (!status.Success)
            {
                return ToolTelemetry.TraceAndReturn(toolName, $"git status failed: {status.Error}");
            }

            var changed = GitChangedFilesHelper.ParsePorcelainStatus(status.Output);
            var solution = _solutionManager.GetCurrentSolution();
            var relativePaths = changed.Select(c => c.Path).ToList();
            var testSuggestions = GitChangedFilesHelper.SuggestTestProjects(solution, relativePaths);

            var sb = new StringBuilder();
            sb.AppendLine("## Git changed files");
            sb.AppendLine();
            sb.AppendLine($"- **Repository:** `{repoRoot}`");
            sb.AppendLine($"- **Changed/untracked:** {changed.Count}");
            sb.AppendLine();

            if (changed.Count == 0)
            {
                sb.AppendLine("Working tree clean (no porcelain entries).");
            }
            else
            {
                sb.AppendLine("| Status | Path |");
                sb.AppendLine("| --- | --- |");
                foreach (var file in changed.Take(80))
                {
                    sb.AppendLine($"| {file.Status} | `{file.Path}` |");
                }

                if (changed.Count > 80)
                {
                    sb.AppendLine();
                    sb.AppendLine($"[!] Showing first 80 of {changed.Count} paths.");
                }
            }

            if (testSuggestions.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("### Suggested test projects (heuristic)");
                foreach (var testProj in testSuggestions)
                {
                    sb.AppendLine($"- `{testProj}`");
                }
            }

            sb.AppendLine();
            sb.AppendLine("> On Windows PowerShell 5.x chain commands with `;`, not `&&`.");
            return ToolTelemetry.TraceAndReturn(toolName, sb.ToString().TrimEnd());
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(toolName, "`get_changed_files` was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetChangedFiles failed");
            return ToolTelemetry.TraceAndReturn(toolName, $"Error: {ex.Message}");
        }
    }

    private string ResolveGitAnchorPath(string? workspacePath)
    {
        if (!string.IsNullOrWhiteSpace(workspacePath))
        {
            return Path.GetFullPath(workspacePath.Trim());
        }

        var solution = _solutionManager.GetCurrentSolution();
        var firstProject = solution?.Projects.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.FilePath));
        if (firstProject?.FilePath is not null)
        {
            return firstProject.FilePath;
        }

        return Environment.CurrentDirectory;
    }

    [McpServerTool(Name = "list_directory_tree", Title = "ListDirectoryTree")]
    [Description("Lists files and directories as a tree, skipping bin, obj, .git, and .vs.")]
    public Task<string> ListDirectoryTree(
        [Description("Root directory to list.")] string directoryPath,
        [Description("Maximum recursion depth.")] int maxDepth = 2,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        try
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(ListDirectoryTree), "Error: `directoryPath` is empty."));
            }

            var rootPath = Path.GetFullPath(directoryPath);
            if (!Directory.Exists(rootPath))
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(ListDirectoryTree), $"Directory not found: `{rootPath}`"));
            }

            var depth = Math.Max(0, maxDepth);
            var rootInfo = new DirectoryInfo(rootPath);
            var sb = new StringBuilder();
            sb.AppendLine(rootInfo.Name);
            AppendDirectoryTree(sb, rootInfo, 0, depth);
            return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(ListDirectoryTree), sb.ToString().TrimEnd()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ListDirectoryTree failed for {DirectoryPath}", directoryPath);
            return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(ListDirectoryTree), $"Failed to list directory tree for `{directoryPath}`: {ex.Message}"));
        }
    }

    [McpServerTool(Name = "get_method_body", Title = "GetMethodBody")]
    [Description("Returns the first matching method source from a disk .cs file. First match wins; no overload selection.")]
    public async Task<string> GetMethodBody(
        [Description("Path to the C# source file.")] string filePath,
        [Description("Class containing the method.")] string className,
        [Description("Method name to extract.")] string methodName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return ToolTelemetry.TraceAndReturn(nameof(GetMethodBody), "Error: `filePath` is empty.");
            }

            if (string.IsNullOrWhiteSpace(className))
            {
                return ToolTelemetry.TraceAndReturn(nameof(GetMethodBody), "Class name is empty.");
            }

            if (string.IsNullOrWhiteSpace(methodName))
            {
                return ToolTelemetry.TraceAndReturn(nameof(GetMethodBody), "Method name is empty.");
            }

            var fullPath = _solutionManager.ResolvePathAgainstWorkspace(filePath);
            if (!File.Exists(fullPath))
            {
                return ToolTelemetry.TraceAndReturn(nameof(GetMethodBody), $"File not found: `{fullPath}`");
            }

            var source = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
            var root = await syntaxTree.GetRootAsync(cancellationToken);

            var classNode = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(c => string.Equals(c.Identifier.Text, className, StringComparison.Ordinal));

            if (classNode is null)
            {
                return ToolTelemetry.TraceAndReturn(nameof(GetMethodBody), $"Class `{className}` not found in `{fullPath}`.");
            }

            var method = classNode.Members
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => string.Equals(m.Identifier.Text, methodName, StringComparison.Ordinal));

            if (method is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(GetMethodBody),
                    $"Method `{methodName}` was not found in class `{className}` (`{fullPath}`).");
            }

            return ToolTelemetry.TraceAndReturn(nameof(GetMethodBody), method.ToFullString().Trim());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetMethodBody failed for {FilePath} {ClassName}.{MethodName}", filePath, className, methodName);
            return ToolTelemetry.TraceAndReturn(
                nameof(GetMethodBody),
                $"Failed to extract `{className}.{methodName}` from `{filePath}`: {ex.Message}");
        }
    }

    [McpServerTool(Name = "read_log_tail", Title = "ReadLogTail")]
    [Description("Reads the tail of a log file. Omit filePath to read the latest MCP server log.")]
    public async Task<string> ReadLogTail(
        [Description("Path to the log file. Omit for the latest logs/mcp-*.log.")] string? filePath = null,
        [Description("Lines to return from the end.")] int lastNLines = 200,
        [Description("Case-insensitive keyword filter applied before the tail.")] string? filterKeyword = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return await TailToolLog(lastNLines, filterKeyword, cancellationToken).ConfigureAwait(false);
            }

            if (lastNLines <= 0)
            {
                return ToolTelemetry.TraceAndReturn(nameof(ReadLogTail), "`lastNLines` must be greater than 0.");
            }

            var fullPath = _solutionManager.ResolvePathAgainstWorkspace(filePath);
            if (!File.Exists(fullPath))
            {
                return ToolTelemetry.TraceAndReturn(nameof(ReadLogTail), $"File not found: `{fullPath}`");
            }

            var result = LogTailReader.ReadTail(fullPath, lastNLines, filterKeyword, cancellationToken);
            if (string.IsNullOrEmpty(result))
            {
                var hasFilter = !string.IsNullOrWhiteSpace(filterKeyword);
                var filterInfo = hasFilter ? $" for filter `{filterKeyword}`" : string.Empty;
                return ToolTelemetry.TraceAndReturn(nameof(ReadLogTail), $"No lines found{filterInfo} in `{fullPath}`.");
            }

            return ToolTelemetry.TraceAndReturn(nameof(ReadLogTail), result);
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(nameof(ReadLogTail), "Log tail read was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReadLogTail failed for {FilePath}", filePath);
            return ToolTelemetry.TraceAndReturn(
                nameof(ReadLogTail),
                $"Error ({ex.GetType().Name}): {ex.Message}");
        }
    }

    [McpServerTool(Name = "read_file_range", Title = "ReadFileRange")]
    [Description("Reads a line range from a disk file with original line numbers.")]
    public Task<string> ReadFileRange(
        [Description("Path to the file.")] string filePath,
        [Description("1-based start line.")] int startLine,
        [Description("Number of lines to read. Must be greater than 0.")] int lineCount,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(ReadFileRange), "Error: `filePath` is empty."));
            }

            if (startLine <= 0)
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(ReadFileRange), "Error: `startLine` must be >= 1."));
            }

            if (lineCount <= 0)
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(ReadFileRange), "Error: `lineCount` must be > 0."));
            }

            var fullPath = _solutionManager.ResolvePathAgainstWorkspace(filePath);
            if (!File.Exists(fullPath))
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(ReadFileRange), $"Error: File not found: `{fullPath}`"));
            }

            var result = new List<string>(lineCount);
            var currentLineNumber = 0;
            var endLine = checked(startLine + lineCount - 1);

            foreach (var line in File.ReadLines(fullPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                currentLineNumber++;

                if (currentLineNumber < startLine)
                {
                    continue;
                }

                if (currentLineNumber > endLine)
                {
                    break;
                }

                result.Add($"{currentLineNumber} | {line}");
            }

            if (result.Count == 0)
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(
                    nameof(ReadFileRange),
                    $"Start line `{startLine}` is out of bounds for `{fullPath}` (file has {currentLineNumber} lines)."));
            }

            if (result.Count < lineCount)
            {
                result.Add($"[!] Reached end of file. Returned {result.Count} of requested {lineCount} lines.");
            }

            return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(ReadFileRange), string.Join(Environment.NewLine, result)));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(ReadFileRange), "ReadFileRange was cancelled."));
        }
        catch (OverflowException)
        {
            return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(ReadFileRange), "Error: `startLine + lineCount` is too large."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReadFileRange failed for {FilePath} from {StartLine} count {LineCount}", filePath, startLine, lineCount);
            return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(ReadFileRange), $"Error: {ex.Message}"));
        }
    }

    [McpServerTool(Name = "search_code", Title = "SearchCode")]
    [Description(
        "Text-searches source files. No workspace required. Default .cs, case-insensitive. "
        + "Not for finding symbol declarations — use find_symbol_definition.")]
    public Task<string> SearchCode(
        [Description("Search text, or a regex when useRegex is true.")] string pattern,
        [Description("Root directory. Omit for loaded workspace root or process CWD.")] string? directoryPath = null,
        [Description("File extensions to scan, comma-separated. Use * for all files.")] string? includeExtensions = ".cs",
        [Description("When true, treat pattern as a .NET regular expression.")] bool useRegex = false,
        [Description("When true, matching is case-sensitive.")] bool caseSensitive = false,
        [Description("Maximum matched lines to return.")] int maxResults = 50,
        [Description("Maximum scan time in seconds. 0 disables timeout.")] int maxScanSeconds = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(SearchCode), "Error: `pattern` is empty."));
            }

            if (maxResults <= 0)
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(SearchCode), "Error: `maxResults` must be greater than 0."));
            }

            var rootDirectory = ResolveSearchRootDirectory(directoryPath);
            var extensionFilter = ParseExtensionFilter(includeExtensions);

            if (!Directory.Exists(rootDirectory))
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(SearchCode), $"Error: Directory not found: `{rootDirectory}`"));
            }

            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            Regex? regex = null;
            if (useRegex)
            {
                try
                {
                    var regexOptions = RegexOptions.Compiled | RegexOptions.CultureInvariant;
                    if (!caseSensitive)
                    {
                        regexOptions |= RegexOptions.IgnoreCase;
                    }

                    regex = new Regex(pattern, regexOptions);
                }
                catch (ArgumentException ex)
                {
                    return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(SearchCode), $"Error: Invalid regex pattern: {ex.Message}"));
                }
            }

            var matches = new List<string>(Math.Min(maxResults, 200));
            var filesScanned = 0;
            var directoriesStack = new Stack<string>();
            directoriesStack.Push(rootDirectory);
            var stopwatch = Stopwatch.StartNew();
            var scanTimeout = maxScanSeconds > 0 ? TimeSpan.FromSeconds(maxScanSeconds) : Timeout.InfiniteTimeSpan;
            var timedOut = false;

            _logger.LogInformation(
                "SearchCode started: pattern={Pattern} root={RootDirectory} useRegex={UseRegex} caseSensitive={CaseSensitive} maxResults={MaxResults} maxScanSeconds={MaxScanSeconds}",
                pattern,
                rootDirectory,
                useRegex,
                caseSensitive,
                maxResults,
                maxScanSeconds);
            _logger.LogInformation(
                "SearchCode filter: includeExtensions={IncludeExtensions}",
                extensionFilter.IncludeAll ? "*" : string.Join(",", extensionFilter.Extensions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)));

            while (directoriesStack.Count > 0 && matches.Count < maxResults && !timedOut)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (scanTimeout != Timeout.InfiniteTimeSpan && stopwatch.Elapsed >= scanTimeout)
                {
                    timedOut = true;
                    break;
                }

                var currentDirectory = directoriesStack.Pop();

                IEnumerable<string> subDirectories;
                try
                {
                    subDirectories = Directory.EnumerateDirectories(currentDirectory);
                }
                catch
                {
                    continue;
                }

                foreach (var subDirectory in subDirectories)
                {
                    var name = Path.GetFileName(subDirectory);
                    if (ExcludedDirectories.Contains(name))
                    {
                        continue;
                    }

                    directoriesStack.Push(subDirectory);
                }

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(currentDirectory);
                }
                catch
                {
                    continue;
                }

                foreach (var file in files)
                {
                    if (matches.Count >= maxResults || timedOut)
                    {
                        break;
                    }

                    if (!extensionFilter.IncludeAll && !extensionFilter.Extensions.Contains(Path.GetExtension(file)))
                    {
                        continue;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    if (scanTimeout != Timeout.InfiniteTimeSpan && stopwatch.Elapsed >= scanTimeout)
                    {
                        timedOut = true;
                        break;
                    }

                    filesScanned++;
                    if (filesScanned % 1000 == 0)
                    {
                        _logger.LogInformation(
                            "SearchCode progress: scanned={FilesScanned} matches={Matches} elapsedMs={ElapsedMs} root={RootDirectory}",
                            filesScanned,
                            matches.Count,
                            stopwatch.ElapsedMilliseconds,
                            rootDirectory);
                    }

                    int lineNumber = 0;
                    IEnumerable<string> lines;
                    try
                    {
                        lines = File.ReadLines(file);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var line in lines)
                    {
                        lineNumber++;
                        var isMatch = useRegex
                            ? regex!.IsMatch(line)
                            : line.Contains(pattern, comparison);

                        if (!isMatch)
                        {
                            continue;
                        }

                        matches.Add($"{file}:{lineNumber} | {line}");
                        if (matches.Count >= maxResults)
                        {
                            break;
                        }
                    }
                }
            }

            if (matches.Count == 0)
            {
                if (timedOut)
                {
                    _logger.LogWarning(
                        "SearchCode timed out with no matches: pattern={Pattern} scanned={FilesScanned} elapsedMs={ElapsedMs} root={RootDirectory}",
                        pattern,
                        filesScanned,
                        stopwatch.ElapsedMilliseconds,
                        rootDirectory);
                    return Task.FromResult(ToolTelemetry.TraceAndReturn(
                        nameof(SearchCode),
                        $"No matches found for `{pattern}` in `{rootDirectory}` before timeout ({maxScanSeconds}s). Scanned files: {filesScanned}."));
                }

                return Task.FromResult(ToolTelemetry.TraceAndReturn(
                    nameof(SearchCode),
                    $"No matches found for `{pattern}` in `{rootDirectory}`."));
            }

            var result = new StringBuilder();
            result.AppendLine($"Found {matches.Count} match(es) for `{pattern}` in `{rootDirectory}`.");
            result.AppendLine($"Scanned files: {filesScanned}.");
            if (matches.Count >= maxResults)
            {
                result.AppendLine($"[!] Reached maxResults limit ({maxResults}).");
            }

            if (timedOut)
            {
                result.AppendLine($"[!] Search timed out after {maxScanSeconds}s. Results are partial.");
            }

            result.AppendLine();
            foreach (var match in matches)
            {
                result.AppendLine(match);
            }

            _logger.LogInformation(
                "SearchCode completed: pattern={Pattern} root={RootDirectory} matches={Matches} scanned={FilesScanned} timedOut={TimedOut} elapsedMs={ElapsedMs}",
                pattern,
                rootDirectory,
                matches.Count,
                filesScanned,
                timedOut,
                stopwatch.ElapsedMilliseconds);

            return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(SearchCode), result.ToString().TrimEnd()));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(SearchCode), "SearchCode was cancelled."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SearchCode failed for pattern {Pattern} in {DirectoryPath}", pattern, directoryPath);
            return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(SearchCode), $"Error: {ex.Message}"));
        }
    }

    private string ResolveSearchRootDirectory(string? directoryPath)
    {
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            return Path.GetFullPath(directoryPath);
        }

        var loadedWorkspaceDirectory = _solutionManager.GetLoadedWorkspaceDirectory();
        if (!string.IsNullOrWhiteSpace(loadedWorkspaceDirectory))
        {
            return loadedWorkspaceDirectory;
        }

        return Environment.CurrentDirectory;
    }

    private static (bool IncludeAll, HashSet<string> Extensions) ParseExtensionFilter(string? includeExtensions)
    {
        var raw = string.IsNullOrWhiteSpace(includeExtensions) ? ".cs" : includeExtensions.Trim();
        if (string.Equals(raw, "*", StringComparison.Ordinal))
        {
            return (true, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        var values = raw.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length == 0)
        {
            values = [".cs"];
        }

        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var normalized = value.Length > 0 && value[0] == '.' ? value : "." + value;
            extensions.Add(normalized);
        }

        return (false, extensions);
    }

    [McpServerTool(Name = "apply_patch", Title = "ApplyPatch")]
    [Description("Replaces oldString with newString in a file. Writes the file. Default replaceAll=false.")]
    public async Task<string> ApplyPatch(
        [Description("Path to the file to patch.")] string filePath,
        [Description("Source fragment to find.")] string oldString,
        [Description("Replacement text.")] string newString,
        [Description("When true, replace all matches; when false, only the first.")] bool replaceAll = false,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var fullPath = filePath;
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return ToolTelemetry.TraceAndReturn(nameof(ApplyPatch), "Error: `filePath` is empty.");
            }

            if (string.IsNullOrEmpty(oldString))
            {
                return ToolTelemetry.TraceAndReturn(nameof(ApplyPatch), "Error: `oldString` is empty.");
            }

            fullPath = _solutionManager.ResolvePathAgainstWorkspace(filePath);
            if (!File.Exists(fullPath))
            {
                return ToolTelemetry.TraceAndReturn(nameof(ApplyPatch), $"Error: File not found: `{fullPath}`");
            }

            var sourceRaw = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var sourceNorm = PatchMatchHelper.NormalizeLineEndings(sourceRaw);
            var oldNorm = PatchMatchHelper.NormalizeLineEndings(oldString);
            var newNorm = PatchMatchHelper.NormalizeLineEndings(newString ?? string.Empty);
            var newContainsOld = oldNorm.Length > 0
                && newNorm.Contains(oldNorm, StringComparison.Ordinal);

            _logger.LogInformation(
                "ApplyPatch start file={FilePath} oldLen={OldLen} newLen={NewLen} sourceLen={SourceLen} replaceAll={ReplaceAll} newContainsOld={NewContainsOld}",
                fullPath,
                oldNorm.Length,
                newNorm.Length,
                sourceNorm.Length,
                replaceAll,
                newContainsOld);

            string? updatedNorm;
            var usedFlexible = false;
            var replacementCount = 0;
            try
            {
                updatedNorm = PatchMatchHelper.ApplyPatchWithFlexibleFallback(
                    sourceNorm,
                    oldNorm,
                    newNorm,
                    replaceAll,
                    out usedFlexible,
                    out var matched,
                    out replacementCount,
                    cancellationToken);
                if (!matched)
                {
                    _logger.LogWarning(
                        "ApplyPatch: could not match oldString in {FilePath} ({ElapsedMs}ms). {Diagnostic}",
                        fullPath,
                        started.ElapsedMilliseconds,
                        PatchMatchHelper.BuildPatchFailureDiagnostic(oldNorm));
                    return ToolTelemetry.TraceAndReturn(
                        nameof(ApplyPatch),
                        "Error: `oldString` was not found in the file (exact or whitespace-tolerant). Copy from read_file/read_file_range when possible.");
                }
            }
            catch (RegexMatchTimeoutException ex)
            {
                _logger.LogWarning(
                    ex,
                    "ApplyPatch: regex match timeout for {FilePath} ({ElapsedMs}ms). {Diagnostic}",
                    fullPath,
                    started.ElapsedMilliseconds,
                    PatchMatchHelper.BuildPatchFailureDiagnostic(oldNorm));
                return ToolTelemetry.TraceAndReturn(
                    nameof(ApplyPatch),
                    "Error: Patch match timed out; try a shorter or more specific `oldString`.");
            }

            _logger.LogInformation(
                "ApplyPatch matched file={FilePath} replacements={ReplacementCount} usedFlexible={UsedFlexible} matchMs={ElapsedMs}",
                fullPath,
                replacementCount,
                usedFlexible,
                started.ElapsedMilliseconds);

            var updatedRaw = PatchMatchHelper.RestorePreferredLineEndings(sourceRaw, updatedNorm!);
            if (string.Equals(sourceRaw, updatedRaw, StringComparison.Ordinal))
            {
                return ToolTelemetry.TraceAndReturn(nameof(ApplyPatch), "No changes were applied.");
            }

            _solutionManager.SuppressDiskWatchForPath(fullPath);
            await File.WriteAllTextAsync(fullPath, updatedRaw, cancellationToken);
            var writeMs = started.ElapsedMilliseconds;
            await _solutionManager.UpdateDocumentInMemoryAsync(fullPath, updatedRaw, cancellationToken);
            _logger.LogInformation(
                "ApplyPatch wrote file={FilePath} replacements={ReplacementCount} writeMs={WriteMs} workspaceMs={TotalMs}",
                fullPath,
                replacementCount,
                writeMs,
                started.ElapsedMilliseconds);
            var note = usedFlexible ? " (whitespace-tolerant match)" : string.Empty;
            return ToolTelemetry.TraceAndReturn(
                nameof(ApplyPatch),
                $"Patch applied successfully to `{fullPath}` ({replacementCount} replacement(s)).{note}");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "ApplyPatch cancelled file={FilePath} after {ElapsedMs}ms",
                fullPath,
                started.ElapsedMilliseconds);
            return ToolTelemetry.TraceAndReturn(nameof(ApplyPatch), "ApplyPatch was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ApplyPatch failed for {FilePath} after {ElapsedMs}ms", fullPath, started.ElapsedMilliseconds);
            return ToolTelemetry.TraceAndReturn(nameof(ApplyPatch), $"Error: {ex.Message}");
        }
    }

    [McpServerTool(Name = "run_format", Title = "RunFormat")]
    [Description("Runs dotnet format. Executes a process. Directories are allowed. verifyOnly checks without writing.")]
    public async Task<string> RunFormat(
        [Description("Path to a .sln, .slnx, .csproj, or directory.")] string workspacePath,
        [Description("When true, check formatting without changing files.")] bool verifyOnly = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return ToolTelemetry.TraceAndReturn(nameof(RunFormat), "Error: `workspacePath` is empty.");
            }

            var fullPath = Path.GetFullPath(workspacePath);
            var workingDirectory = Directory.Exists(fullPath)
                ? WorkspaceRootResolver.ResolveDotNetWorkingDirectory(
                    WorkspaceRootResolver.FindSolutionOrProjectInDirectory(fullPath) ?? fullPath)
                : WorkspaceRootResolver.ResolveDotNetWorkingDirectory(fullPath);

            if (!Directory.Exists(workingDirectory))
            {
                return ToolTelemetry.TraceAndReturn(nameof(RunFormat), $"Error: Working directory not found: `{workingDirectory}`");
            }

            var args = new StringBuilder("format ");
            args.Append('"').Append(fullPath).Append('"');
            if (verifyOnly)
            {
                args.Append(" --verify-no-changes");
            }

            var run = await DotNetCliRunner.RunWithMetadataAsync(
                args.ToString(),
                workingDirectory,
                cancellationToken,
                TimeSpan.FromSeconds(DotNetCliRunner.DefaultTimeoutSeconds)).ConfigureAwait(false);

            var stdout = run.CombinedOutput;
            var processExitCode = run.ExitCode;
            var result = new StringBuilder();
            result.AppendLine(run.RunMetadata);
            result.AppendLine($"ExitCode: {processExitCode}");
            result.AppendLine($"Mode: {(verifyOnly ? "verify-only" : "apply")}");
            if (!string.IsNullOrWhiteSpace(stdout))
            {
                result.AppendLine().AppendLine("Output:").AppendLine(stdout);
            }

            return ToolTelemetry.TraceAndReturn(nameof(RunFormat), result.ToString().TrimEnd());
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(nameof(RunFormat), "RunFormat was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RunFormat failed for {WorkspacePath}", workspacePath);
            return ToolTelemetry.TraceAndReturn(nameof(RunFormat), $"Error: {ex.Message}");
        }
    }

    [McpServerTool(Name = "rename_symbol", Title = "RenameSymbol")]
    [Description(
        "Semantic C# symbol rename. Default previewOnly=true. For project folders use rename_project.")]
    public async Task<string> RenameSymbol(
        [Description("Path to a C# file containing the symbol.")] string filePath,
        [Description("Current symbol name.")] string symbolName,
        [Description("New symbol name.")] string newName,
        [Description("Rename scope: project (default) or solution.")] string scope = "project",
        [Description("When true (default), preview only and do not write.")] bool previewOnly = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(symbolName) || string.IsNullOrWhiteSpace(newName))
            {
                _logger.LogWarning(
                    "RenameSymbol rejected: missing arguments (filePath empty={NoPath}, symbolName empty={NoSym}, newName empty={NoNew}).",
                    string.IsNullOrWhiteSpace(filePath),
                    string.IsNullOrWhiteSpace(symbolName),
                    string.IsNullOrWhiteSpace(newName));
                return ToolTelemetry.TraceAndReturn(nameof(RenameSymbol), "Error: `filePath`, `symbolName`, and `newName` are required.");
            }

            if (_solutionManager.GetCurrentSolution() is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(RenameSymbol),
                    WorkspaceLoadGuidance.FormatNoWorkspaceLoadedMessage("Error: No workspace loaded."));
            }

            var normalizedScope = scope.Trim().ToLowerInvariant();
            if (normalizedScope is not ("project" or "solution"))
            {
                _logger.LogWarning(
                    "RenameSymbol rejected: invalid scope `{Scope}` for `{SymbolName}` -> `{NewName}` in `{FilePath}`.",
                    scope,
                    symbolName,
                    newName,
                    filePath);
                return ToolTelemetry.TraceAndReturn(nameof(RenameSymbol), "Error: `scope` must be either `project` or `solution`.");
            }

            var fullPath = _solutionManager.ResolvePathAgainstWorkspace(filePath);
            var document = await _solutionManager.FindDocumentAsync(fullPath, cancellationToken);
            if (document is null)
            {
                _logger.LogWarning(
                    "RenameSymbol: document not in workspace for `{SymbolName}` -> `{NewName}` (file `{FilePath}`).",
                    symbolName,
                    newName,
                    filePath);
                return ToolTelemetry.TraceAndReturn(nameof(RenameSymbol), $"Error: Document not found in workspace: `{fullPath}`");
            }

            var root = await document.GetSyntaxRootAsync(cancellationToken);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            if (root is null || semanticModel is null)
            {
                _logger.LogWarning(
                    "RenameSymbol: no syntax/semantic model for `{SymbolName}` -> `{NewName}` in `{FilePath}`.",
                    symbolName,
                    newName,
                    filePath);
                return ToolTelemetry.TraceAndReturn(nameof(RenameSymbol), "Error: Failed to obtain syntax root or semantic model.");
            }

            var targetSymbol = ExtractTargetSymbol(root, semanticModel, symbolName, cancellationToken);
            if (targetSymbol is null)
            {
                _logger.LogWarning(
                    "RenameSymbol: symbol `{SymbolName}` not found for rename to `{NewName}` in `{FilePath}`.",
                    symbolName,
                    newName,
                    filePath);
                return ToolTelemetry.TraceAndReturn(nameof(RenameSymbol), $"Error: Symbol `{symbolName}` not found.");
            }

            var baseSolution = _solutionManager.GetCurrentSolution() ?? document.Project.Solution;
            var references = await SymbolFinder.FindReferencesAsync(targetSymbol, baseSolution, cancellationToken);
            var affectedLocations = references
                .SelectMany(r => r.Locations)
                .Where(l => l.Location.IsInSource)
                .ToList();

            var targetProjectId = document.Project.Id;
            if (normalizedScope == "project")
            {
                affectedLocations = affectedLocations
                    .Where(l => l.Document.Project.Id == targetProjectId)
                    .ToList();
            }

            if (previewOnly)
            {
                var preview = new StringBuilder();
                preview.AppendLine($"Symbol: `{symbolName}` -> `{newName}`");
                preview.AppendLine($"Scope: {normalizedScope}");
                preview.AppendLine($"Affected locations: {affectedLocations.Count}");
                foreach (var location in affectedLocations.Take(100))
                {
                    var line = location.Location.GetLineSpan().StartLinePosition.Line + 1;
                    preview.AppendLine($"- {location.Document.FilePath}:{line}");
                }
                if (affectedLocations.Count > 100)
                {
                    preview.AppendLine("[!] Showing first 100 locations only.");
                }
                return ToolTelemetry.TraceAndReturn(nameof(RenameSymbol), preview.ToString().TrimEnd());
            }

            var renameOptions = new SymbolRenameOptions();
            var renamedSolution = await Renamer.RenameSymbolAsync(
                baseSolution,
                targetSymbol,
                renameOptions,
                newName,
                cancellationToken);

            if (normalizedScope == "project")
            {
                foreach (var project in renamedSolution.Projects.Where(p => p.Id != targetProjectId))
                {
                    foreach (var doc in project.Documents)
                    {
                        var originalDoc = baseSolution.GetDocument(doc.Id);
                        if (originalDoc is null)
                        {
                            continue;
                        }

                        var originalText = await originalDoc.GetTextAsync(cancellationToken);
                        renamedSolution = renamedSolution.WithDocumentText(doc.Id, originalText);
                    }
                }
            }

            var changedDocs = new List<(Document Doc, string Text)>();
            foreach (var newProject in renamedSolution.Projects)
            {
                foreach (var newDoc in newProject.Documents)
                {
                    var oldDoc = baseSolution.GetDocument(newDoc.Id);
                    if (oldDoc is null || newDoc.FilePath is null)
                    {
                        continue;
                    }

                    var oldText = await oldDoc.GetTextAsync(cancellationToken);
                    var newText = await newDoc.GetTextAsync(cancellationToken);
                    if (!string.Equals(oldText.ToString(), newText.ToString(), StringComparison.Ordinal))
                    {
                        changedDocs.Add((newDoc, newText.ToString()));
                    }
                }
            }

            foreach (var (doc, text) in changedDocs)
            {
                _solutionManager.SuppressDiskWatchForPath(doc.FilePath!);
                await File.WriteAllTextAsync(doc.FilePath!, text, cancellationToken);
                await _solutionManager.UpdateDocumentInMemoryAsync(doc.FilePath!, text, cancellationToken);
            }

            return ToolTelemetry.TraceAndReturn(
                nameof(RenameSymbol),
                $"Rename applied: `{symbolName}` -> `{newName}`. Updated files: {changedDocs.Count}.");
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(nameof(RenameSymbol), "RenameSymbol was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RenameSymbol failed for {SymbolName} in {FilePath}", symbolName, filePath);
            return ToolTelemetry.TraceAndReturn(nameof(RenameSymbol), $"Error: {ex.Message}");
        }
    }

    [McpServerTool(Name = "list_projects", Title = "ListProjects")]
    [Description("Lists projects from the loaded workspace, including TFMs and project references.")]
    public async Task<string> ListProjects(
        [Description("Optional .sln/.slnx/.csproj. When set, the workspace is loaded first.")] string? workspacePath = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(workspacePath))
            {
                await _solutionManager.LoadAsync(workspacePath, cancellationToken);
            }

            var solution = _solutionManager.GetCurrentSolution();
            if (solution is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(ListProjects),
                    WorkspaceLoadGuidance.FormatNoWorkspaceLoadedMessage("Error: No workspace loaded."));
            }

            var sb = new StringBuilder();
            foreach (var project in solution.Projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                var tfm = "(unknown)";
                var outputType = "(unknown)";
                var projectPath = project.FilePath ?? "(unknown path)";

                if (project.FilePath is not null && File.Exists(project.FilePath))
                {
                    try
                    {
                        var xml = await File.ReadAllTextAsync(project.FilePath, cancellationToken);
                        tfm = ExtractSimpleCsprojValue(xml, "TargetFramework")
                            ?? ExtractSimpleCsprojValue(xml, "TargetFrameworks")
                            ?? tfm;
                        outputType = ExtractSimpleCsprojValue(xml, "OutputType") ?? outputType;
                    }
                    catch
                    {
                        // keep unknown metadata if csproj parsing fails
                    }
                }

                var refs = project.ProjectReferences
                    .Select(r => solution.GetProject(r.ProjectId)?.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();

                sb.AppendLine($"- {project.Name}");
                sb.AppendLine($"  Path: {projectPath}");
                sb.AppendLine($"  TFM: {tfm}");
                sb.AppendLine($"  OutputType: {outputType}");
                sb.AppendLine($"  References: {(refs.Count == 0 ? "(none)" : string.Join(", ", refs))}");
            }

            return ToolTelemetry.TraceAndReturn(nameof(ListProjects), sb.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ListProjects failed for {WorkspacePath}", workspacePath);
            return ToolTelemetry.TraceAndReturn(nameof(ListProjects), $"Error: {ex.Message}");
        }
    }

    [McpServerTool(Name = "get_project_graph", Title = "GetProjectGraph")]
    [Description("Builds a project-to-project dependency graph from the loaded workspace.")]
    public async Task<string> GetProjectGraph(
        [Description("Optional .sln/.slnx/.csproj. When set, the workspace is loaded first.")] string? workspacePath = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(workspacePath))
            {
                await _solutionManager.LoadAsync(workspacePath, cancellationToken);
            }

            var solution = _solutionManager.GetCurrentSolution();
            if (solution is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(GetProjectGraph),
                    WorkspaceLoadGuidance.FormatNoWorkspaceLoadedMessage("Error: No workspace loaded."));
            }

            var sb = new StringBuilder();
            foreach (var project in solution.Projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                var deps = project.ProjectReferences
                    .Select(r => solution.GetProject(r.ProjectId)?.Name ?? r.ProjectId.ToString())
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                sb.AppendLine($"{project.Name} -> {(deps.Count == 0 ? "(none)" : string.Join(", ", deps))}");
            }

            return ToolTelemetry.TraceAndReturn(nameof(GetProjectGraph), sb.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetProjectGraph failed for {WorkspacePath}", workspacePath);
            return ToolTelemetry.TraceAndReturn(nameof(GetProjectGraph), $"Error: {ex.Message}");
        }
    }

    [McpServerTool(Name = "tail_tool_log", Title = "TailToolLog")]
    [Description("Reads the latest MCP server log under logs/mcp-*.log.")]
    public async Task<string> TailToolLog(
        [Description("Lines to return from the end.")] int lastNLines = 200,
        [Description("Case-insensitive keyword filter applied before the tail.")] string? filterKeyword = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var logsDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
            if (!Directory.Exists(logsDirectory))
            {
                return ToolTelemetry.TraceAndReturn(nameof(TailToolLog), $"Error: Logs directory not found: `{logsDirectory}`");
            }

            var latestLog = Directory.EnumerateFiles(logsDirectory, "mcp-*.log", SearchOption.TopDirectoryOnly)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (latestLog is null)
            {
                return ToolTelemetry.TraceAndReturn(nameof(TailToolLog), "No `mcp-*.log` files found.");
            }

            return await ReadLogTail(latestLog.FullName, lastNLines, filterKeyword, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TailToolLog failed");
            return ToolTelemetry.TraceAndReturn(nameof(TailToolLog), $"Error ({ex.GetType().Name}): {ex.Message}");
        }
    }

    [McpServerTool(Name = "manage_agent_scratchpad", Title = "ManageAgentScratchpad")]
    [Description("Reads, writes, appends, or clears .agent_memory/scratchpad.md under process CWD. Writes files.")]
    public async Task<string> ManageAgentScratchpad(
        [Description("Action: read, write, append, or clear.")] string action,
        [Description("Text payload for write and append.")] string? content = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return ToolTelemetry.TraceAndReturn(nameof(ManageAgentScratchpad), "Error: `action` is empty.");
            }

            var normalizedAction = action.Trim().ToLowerInvariant();
            if (normalizedAction is not ("read" or "write" or "append" or "clear"))
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(ManageAgentScratchpad),
                    "Error: Invalid `action`. Allowed values are `read`, `write`, `append`, `clear`.");
            }

            var memoryDirectory = Path.Combine(Environment.CurrentDirectory, AgentMemoryDirectoryName);
            Directory.CreateDirectory(memoryDirectory);
            var scratchpadPath = Path.Combine(memoryDirectory, ScratchpadFileName);

            switch (normalizedAction)
            {
                case "read":
                {
                    if (!File.Exists(scratchpadPath))
                    {
                        return ToolTelemetry.TraceAndReturn(
                            nameof(ManageAgentScratchpad),
                            "Agent scratchpad is empty (file does not exist yet).");
                    }

                    var text = await File.ReadAllTextAsync(scratchpadPath, cancellationToken);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return ToolTelemetry.TraceAndReturn(
                            nameof(ManageAgentScratchpad),
                            "Agent scratchpad is empty.");
                    }

                    return ToolTelemetry.TraceAndReturn(nameof(ManageAgentScratchpad), text);
                }

                case "write":
                {
                    var text = content ?? string.Empty;
                    await File.WriteAllTextAsync(scratchpadPath, text, cancellationToken);
                    return ToolTelemetry.TraceAndReturn(
                        nameof(ManageAgentScratchpad),
                        $"Scratchpad saved ({text.Length} chars) to `{scratchpadPath}`.");
                }

                case "append":
                {
                    var text = content ?? string.Empty;
                    if (!File.Exists(scratchpadPath))
                    {
                        await File.WriteAllTextAsync(scratchpadPath, text, cancellationToken);
                        return ToolTelemetry.TraceAndReturn(
                            nameof(ManageAgentScratchpad),
                            $"Scratchpad created and appended ({text.Length} chars) to `{scratchpadPath}`.");
                    }

                    await File.AppendAllTextAsync(scratchpadPath, Environment.NewLine + text, cancellationToken);
                    return ToolTelemetry.TraceAndReturn(
                        nameof(ManageAgentScratchpad),
                        $"Scratchpad appended ({text.Length} chars) to `{scratchpadPath}`.");
                }

                case "clear":
                {
                    if (File.Exists(scratchpadPath))
                    {
                        File.Delete(scratchpadPath);
                    }

                    return ToolTelemetry.TraceAndReturn(
                        nameof(ManageAgentScratchpad),
                        "Agent scratchpad cleared.");
                }
            }

            return ToolTelemetry.TraceAndReturn(nameof(ManageAgentScratchpad), "Error: Unsupported action.");
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(nameof(ManageAgentScratchpad), "Scratchpad operation was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ManageAgentScratchpad failed for action {Action}", action);
            return ToolTelemetry.TraceAndReturn(nameof(ManageAgentScratchpad), $"Error: {ex.Message}");
        }
    }

    private static ISymbol? ExtractTargetSymbol(
        SyntaxNode root,
        SemanticModel semanticModel,
        string symbolName,
        CancellationToken cancellationToken)
    {
        foreach (var node in root.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (node)
            {
                case ClassDeclarationSyntax c when string.Equals(c.Identifier.Text, symbolName, StringComparison.Ordinal):
                    return semanticModel.GetDeclaredSymbol(c, cancellationToken);
                case StructDeclarationSyntax s when string.Equals(s.Identifier.Text, symbolName, StringComparison.Ordinal):
                    return semanticModel.GetDeclaredSymbol(s, cancellationToken);
                case InterfaceDeclarationSyntax i when string.Equals(i.Identifier.Text, symbolName, StringComparison.Ordinal):
                    return semanticModel.GetDeclaredSymbol(i, cancellationToken);
                case EnumDeclarationSyntax e when string.Equals(e.Identifier.Text, symbolName, StringComparison.Ordinal):
                    return semanticModel.GetDeclaredSymbol(e, cancellationToken);
                case MethodDeclarationSyntax m when string.Equals(m.Identifier.Text, symbolName, StringComparison.Ordinal):
                    return semanticModel.GetDeclaredSymbol(m, cancellationToken);
                case PropertyDeclarationSyntax p when string.Equals(p.Identifier.Text, symbolName, StringComparison.Ordinal):
                    return semanticModel.GetDeclaredSymbol(p, cancellationToken);
                case FieldDeclarationSyntax f:
                {
                    var v = f.Declaration.Variables.FirstOrDefault(x =>
                        string.Equals(x.Identifier.Text, symbolName, StringComparison.Ordinal));
                    if (v is not null)
                    {
                        return semanticModel.GetDeclaredSymbol(v, cancellationToken);
                    }

                    break;
                }
            }
        }

        return null;
    }

    private static string? ExtractSimpleCsprojValue(string xml, string elementName)
    {
        var openTag = $"<{elementName}>";
        var closeTag = $"</{elementName}>";
        var start = xml.IndexOf(openTag, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        start += openTag.Length;
        var end = xml.IndexOf(closeTag, start, StringComparison.OrdinalIgnoreCase);
        if (end < 0 || end <= start)
        {
            return null;
        }

        return xml[start..end].Trim();
    }

    private static void AppendDirectoryTree(StringBuilder sb, DirectoryInfo directory, int currentDepth, int maxDepth)
    {
        if (currentDepth >= maxDepth)
        {
            return;
        }

        var childDirectories = directory.GetDirectories()
            .Where(d => !ExcludedDirectories.Contains(d.Name))
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var files = directory.GetFiles()
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var entriesCount = childDirectories.Count + files.Count;
        var index = 0;

        foreach (var dir in childDirectories)
        {
            var isLast = ++index == entriesCount;
            var prefix = isLast ? "└── " : "├── ";
            sb.AppendLine($"{new string(' ', currentDepth * 4)}{prefix}{dir.Name}/");
            AppendDirectoryTree(sb, dir, currentDepth + 1, maxDepth);
        }

        foreach (var file in files)
        {
            var isLast = ++index == entriesCount;
            var prefix = isLast ? "└── " : "├── ";
            sb.AppendLine($"{new string(' ', currentDepth * 4)}{prefix}{file.Name}");
        }
    }
}
