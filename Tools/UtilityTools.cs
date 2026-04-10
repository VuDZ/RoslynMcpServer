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
    [Description("Executes `dotnet {command}` in the target directory, waits for process completion, and returns formatted combined output from standard output and standard error.")]
    public async Task<string> ExecuteDotNetCommand(
        [Description("Arguments passed after `dotnet`, for example: `test`, `build`, or `add package Moq`.")] string command,
        [Description("Optional working directory for the command. If omitted or empty, `Environment.CurrentDirectory` is used.")] string? workingDirectory = null,
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

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = command,
                WorkingDirectory = fullWorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi };
            if (!process.Start())
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(ExecuteDotNetCommand),
                    "Failed to start `dotnet` process. Ensure .NET SDK is on PATH.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var stdout = (await stdoutTask).TrimEnd();
            var stderr = (await stderrTask).TrimEnd();

            var sb = new StringBuilder();
            sb.AppendLine($"Command: `dotnet {command}`");
            sb.AppendLine($"WorkingDirectory: `{fullWorkingDirectory}`");
            sb.AppendLine($"ExitCode: `{process.ExitCode}`");
            sb.AppendLine(process.ExitCode == 0
                ? "Status: SUCCESS"
                : "Status: FAILED (non-zero exit code)");
            if (!string.IsNullOrWhiteSpace(stdout))
            {
                sb.AppendLine();
                sb.AppendLine("StdOut:");
                sb.AppendLine(stdout);
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                sb.AppendLine();
                sb.AppendLine("StdErr:");
                sb.AppendLine(stderr);
            }

            if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(stderr))
            {
                sb.AppendLine();
                sb.AppendLine("StdErr: (empty)");
            }

            return ToolTelemetry.TraceAndReturn(nameof(ExecuteDotNetCommand), sb.ToString().TrimEnd());
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(nameof(ExecuteDotNetCommand), "`dotnet` command was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExecuteDotNetCommand failed for command {Command} in {WorkingDirectory}", command, workingDirectory);
            return ToolTelemetry.TraceAndReturn(nameof(ExecuteDotNetCommand), $"Failed to run `dotnet {command}`: {ex.Message}");
        }
    }

    [McpServerTool(Name = "list_directory_tree", Title = "ListDirectoryTree")]
    [Description("Recursively lists files and directories as a tree, excluding bin, obj, and .git folders.")]
    public Task<string> ListDirectoryTree(
        [Description("Root directory to list (same idea as `directoryPath` in search_code).")] string directoryPath,
        [Description("Maximum recursion depth (default 2)")] int maxDepth = 2,
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
    [Description("Returns the source of one method in a named class (disambiguates overloads and duplicate names across types). Prefer this over reading the whole file for large sources.")]
    public async Task<string> GetMethodBody(
        [Description("Absolute or relative path to the C# source file (same parameter name as get_file_content / read_file_range).")] string filePath,
        [Description("Class name containing the method")] string className,
        [Description("Method name to extract")] string methodName,
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

            var fullPath = Path.GetFullPath(filePath);
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
    [Description("Reads the tail of a log file for LLM-safe diagnostics. Optionally filters lines by keyword (case-insensitive) and returns only the last N matching lines to avoid context overflow.")]
    public Task<string> ReadLogTail(
        [Description("Absolute or relative path to the log file (same parameter name as get_file_content).")] string filePath,
        [Description("How many lines from the end of the result to return. Default is 200.")] int lastNLines = 200,
        [Description("Optional case-insensitive keyword to filter lines before taking the tail. Pass null or empty string to disable filtering.")] string? filterKeyword = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(ReadLogTail), "Error: `filePath` is empty."));
            }

            if (lastNLines <= 0)
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(ReadLogTail), "`lastNLines` must be greater than 0."));
            }

            var fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(ReadLogTail), $"File not found: `{fullPath}`"));
            }

            var hasFilter = !string.IsNullOrWhiteSpace(filterKeyword);
            var keyword = filterKeyword ?? string.Empty;
            var tail = new Queue<string>(lastNLines);

            foreach (var line in File.ReadLines(fullPath))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (hasFilter && line.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (tail.Count == lastNLines)
                {
                    _ = tail.Dequeue();
                }

                tail.Enqueue(line);
            }

            if (tail.Count == 0)
            {
                var filterInfo = hasFilter ? $" for filter `{keyword}`" : string.Empty;
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(ReadLogTail), $"No lines found{filterInfo} in `{fullPath}`."));
            }

            var result = string.Join(Environment.NewLine, tail);
            return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(ReadLogTail), result));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(ReadLogTail), "Log tail read was cancelled."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReadLogTail failed for {FilePath}", filePath);
            return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(ReadLogTail), $"Error: {ex.Message}"));
        }
    }

    [McpServerTool(Name = "read_file_range", Title = "ReadFileRange")]
    [Description("Reads a specific chunk of a text file to reduce LLM context usage. Returns up to `lineCount` lines starting from the 1-based `startLine`, with original line numbers included in the output.")]
    public Task<string> ReadFileRange(
        [Description("Absolute or relative path to the target file (same parameter name as get_file_content).")] string filePath,
        [Description("1-based line number where reading should start (first line is 1).")] int startLine,
        [Description("Number of lines to read from the starting line. Must be greater than 0.")] int lineCount,
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

            var fullPath = Path.GetFullPath(filePath);
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
    [Description("Searches source files like a lightweight ripgrep for LLM workflows. Returns matching lines with file path and line number, while limiting output to prevent context overflow.")]
    public Task<string> SearchCode(
        [Description("Search pattern used to match lines. Interpreted as plain text when `useRegex=false`, or as a regular expression when `useRegex=true`.")] string pattern,
        [Description("Optional root directory to search. If null or empty, `Environment.CurrentDirectory` is used.")] string? directoryPath = null,
        [Description("When true, interprets `pattern` as a .NET regular expression. When false, performs case-insensitive text search using Contains.")] bool useRegex = false,
        [Description("Maximum number of matched lines to return. Limits output for LLM context protection. Default is 50.")] int maxResults = 50,
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

            var rootDirectory = string.IsNullOrWhiteSpace(directoryPath)
                ? Environment.CurrentDirectory
                : Path.GetFullPath(directoryPath);

            if (!Directory.Exists(rootDirectory))
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(SearchCode), $"Error: Directory not found: `{rootDirectory}`"));
            }

            Regex? regex = null;
            if (useRegex)
            {
                try
                {
                    regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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

            while (directoriesStack.Count > 0 && matches.Count < maxResults)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                    if (matches.Count >= maxResults)
                    {
                        break;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    filesScanned++;

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
                            : line.Contains(pattern, StringComparison.OrdinalIgnoreCase);

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

            result.AppendLine();
            foreach (var match in matches)
            {
                result.AppendLine(match);
            }

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

    [McpServerTool(Name = "apply_patch", Title = "ApplyPatch")]
    [Description(
        "Replaces `oldString` with `newString` in a file (the only search-and-replace tool; use `replaceAll=false` for a single occurrence, `replaceAll=true` for all matches). Line endings are normalized for matching (`\\r\\n` → `\\n`); output preserves CRLF when the file used it. Tries exact match on normalized text first, then whitespace-tolerant token matching (string literals with internal spaces may not match).")]
    public async Task<string> ApplyPatch(
        [Description("Absolute or relative path to the file that should be patched.")] string filePath,
        [Description("Source fragment to find (exact or whitespace-tolerant; see tool description).")] string oldString,
        [Description("Replacement text that will be inserted in place of the matched fragment(s).")] string newString,
        [Description("When true, replaces all occurrences. When false, replaces only the first occurrence for safer edits.")] bool replaceAll = false,
        CancellationToken cancellationToken = default)
    {
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

            var fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
            {
                return ToolTelemetry.TraceAndReturn(nameof(ApplyPatch), $"Error: File not found: `{fullPath}`");
            }

            var sourceRaw = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var sourceNorm = PatchMatchHelper.NormalizeLineEndings(sourceRaw);
            var oldNorm = PatchMatchHelper.NormalizeLineEndings(oldString);
            var newNorm = PatchMatchHelper.NormalizeLineEndings(newString ?? string.Empty);

            string? updatedNorm;
            var usedFlexible = false;
            try
            {
                updatedNorm = PatchMatchHelper.ApplyPatchWithFlexibleFallback(
                    sourceNorm,
                    oldNorm,
                    newNorm,
                    replaceAll,
                    out usedFlexible,
                    out var matched);
                if (!matched)
                {
                    _logger.LogWarning(
                        "ApplyPatch: could not match oldString in {FilePath}. {Diagnostic}",
                        fullPath,
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
                    "ApplyPatch: regex match timeout for {FilePath}. {Diagnostic}",
                    fullPath,
                    PatchMatchHelper.BuildPatchFailureDiagnostic(oldNorm));
                return ToolTelemetry.TraceAndReturn(
                    nameof(ApplyPatch),
                    "Error: Patch match timed out; try a shorter or more specific `oldString`.");
            }

            var updatedRaw = PatchMatchHelper.RestorePreferredLineEndings(sourceRaw, updatedNorm!);
            if (string.Equals(sourceRaw, updatedRaw, StringComparison.Ordinal))
            {
                return ToolTelemetry.TraceAndReturn(nameof(ApplyPatch), "No changes were applied.");
            }

            await File.WriteAllTextAsync(fullPath, updatedRaw, cancellationToken);
            await _solutionManager.UpdateDocumentInMemoryAsync(fullPath, updatedRaw, cancellationToken);
            var note = usedFlexible ? " (whitespace-tolerant match)" : string.Empty;
            return ToolTelemetry.TraceAndReturn(nameof(ApplyPatch), $"Patch applied successfully to `{fullPath}`.{note}");
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(nameof(ApplyPatch), "ApplyPatch was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ApplyPatch failed for {FilePath}", filePath);
            return ToolTelemetry.TraceAndReturn(nameof(ApplyPatch), $"Error: {ex.Message}");
        }
    }

    [McpServerTool(Name = "run_format", Title = "RunFormat")]
    [Description("Runs `dotnet format` to stabilize code style after edits. Supports verify-only mode to report formatting drift without modifying files.")]
    public async Task<string> RunFormat(
        [Description("Path to a .sln, .csproj, or directory — same parameter name and shape as `load_workspace` / `run_dotnet_build`.")] string workspacePath,
        [Description("When true, checks formatting without changing files (`--verify-no-changes`).")] bool verifyOnly = false,
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
                ? fullPath
                : Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;

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

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = args.ToString(),
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi };
            if (!process.Start())
            {
                return ToolTelemetry.TraceAndReturn(nameof(RunFormat), "Error: Failed to start `dotnet format`.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var stdout = (await stdoutTask).TrimEnd();
            var stderr = (await stderrTask).TrimEnd();
            var result = new StringBuilder();
            result.AppendLine($"ExitCode: {process.ExitCode}");
            result.AppendLine($"Mode: {(verifyOnly ? "verify-only" : "apply")}");
            if (!string.IsNullOrWhiteSpace(stdout))
            {
                result.AppendLine().AppendLine("StdOut:").AppendLine(stdout);
            }
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                result.AppendLine().AppendLine("StdErr:").AppendLine(stderr);
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
    [Description("Performs semantic symbol rename using Roslyn. Can preview impacted locations before applying changes, and can scope updates to a project or entire solution.")]
    public async Task<string> RenameSymbol(
        [Description("Path to a C# file containing the target symbol declaration or usage.")] string filePath,
        [Description("Current symbol name to rename.")] string symbolName,
        [Description("New symbol name that should replace the current name.")] string newName,
        [Description("Rename scope: `project` (default) or `solution`.")] string scope = "project",
        [Description("When true, returns preview only and does not write any changes.")] bool previewOnly = true,
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

            var document = await _solutionManager.FindDocumentAsync(filePath, cancellationToken);
            if (document is null)
            {
                _logger.LogWarning(
                    "RenameSymbol: document not in workspace for `{SymbolName}` -> `{NewName}` (file `{FilePath}`).",
                    symbolName,
                    newName,
                    filePath);
                return ToolTelemetry.TraceAndReturn(nameof(RenameSymbol), $"Error: Document not found in workspace: `{filePath}`");
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
    [Description("Lists projects from the active workspace solution, including target frameworks, output type, and project references.")]
    public async Task<string> ListProjects(
        [Description("Optional path to a .sln or .csproj. When provided, workspace is loaded/reloaded before listing projects.")] string? workspacePath = null,
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
                return ToolTelemetry.TraceAndReturn(nameof(ListProjects), "Error: No workspace loaded.");
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
    [Description("Builds a project-to-project dependency graph from the active workspace solution.")]
    public async Task<string> GetProjectGraph(
        [Description("Optional path to a .sln or .csproj. When provided, workspace is loaded/reloaded before building the graph.")] string? workspacePath = null,
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
                return ToolTelemetry.TraceAndReturn(nameof(GetProjectGraph), "Error: No workspace loaded.");
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
    [Description("Reads the latest MCP tool/server log file under `logs/mcp-*.log` as a shortcut over ReadLogTail.")]
    public Task<string> TailToolLog(
        [Description("Number of lines to return from the end of the latest log file. Default is 200.")] int lastNLines = 200,
        [Description("Optional case-insensitive keyword filter applied before taking the tail.")] string? filterKeyword = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var logsDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
            if (!Directory.Exists(logsDirectory))
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(TailToolLog), $"Error: Logs directory not found: `{logsDirectory}`"));
            }

            var latestLog = Directory.EnumerateFiles(logsDirectory, "mcp-*.log", SearchOption.TopDirectoryOnly)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (latestLog is null)
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(TailToolLog), "No `mcp-*.log` files found."));
            }

            return ReadLogTail(latestLog.FullName, lastNLines, filterKeyword, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TailToolLog failed");
            return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(TailToolLog), $"Error: {ex.Message}"));
        }
    }

    [McpServerTool(Name = "manage_agent_scratchpad", Title = "ManageAgentScratchpad")]
    [Description("Manages the agent's long-term memory scratchpad stored at `.agent_memory/scratchpad.md`. Supports read, write, append, and clear actions for persistent notes across sessions.")]
    public async Task<string> ManageAgentScratchpad(
        [Description("Action for the agent's long-term memory scratchpad. Allowed values: `read`, `write`, `append`, `clear`.")] string action,
        [Description("Optional text payload for the agent's long-term memory scratchpad. Used by `write` and `append`; ignored by `read` and `clear`.")] string? content = null,
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
