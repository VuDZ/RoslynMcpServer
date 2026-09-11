using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tools;

public sealed class CodeFixTools
{
    private readonly SolutionManager _solutionManager;
    private readonly ILogger<CodeFixTools> _logger;

    public CodeFixTools(SolutionManager solutionManager, ILogger<CodeFixTools> logger)
    {
        _solutionManager = solutionManager;
        _logger = logger;
    }

    [McpServerTool(Name = "get_code_fixes", Title = "Get code fixes for diagnostic")]
    [Description(
        "Lists Roslyn code fixes for a diagnostic at a location. Requires load_workspace. Use fixIndex with apply_code_fix.")]
    public async Task<string> GetCodeFixes(
        [Description("Path to the target .cs file.")] string filePath,
        [Description("Diagnostic id from get_diagnostics_for_file.")] string diagnosticId,
        [Description("1-based line where the diagnostic starts.")] int line,
        [Description("1-based column where the diagnostic starts.")] int column = 1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var resolvedPath = _solutionManager.ResolvePathAgainstWorkspace(filePath);
            var document = await ResolveDocumentAsync(filePath, cancellationToken);
            if (document is null)
            {
                return ToolTelemetry.TraceAndReturn(nameof(GetCodeFixes), $"Document was not found in the active workspace: `{resolvedPath}`.");
            }

            var fixes = await CodeFixHelper.GetFixesAsync(document, line, column, diagnosticId, cancellationToken);
            if (fixes.Count == 0)
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(GetCodeFixes),
                    $"No code fixes found for diagnostic `{diagnosticId}` at line {line}, column {column} in `{resolvedPath}`.");
            }

            var sb = new StringBuilder();
            sb.AppendLine($"File: `{resolvedPath}`");
            sb.AppendLine($"Diagnostic: `{diagnosticId}` at line {line}, column {column}");
            sb.AppendLine($"Available fixes: {fixes.Count}");
            sb.AppendLine();

            foreach (var fix in fixes)
            {
                sb.AppendLine($"[{fix.FixIndex}] {fix.Title}");
                if (fix.DiagnosticIds.Count > 0)
                {
                    sb.AppendLine($"    DiagnosticIds: {string.Join(", ", fix.DiagnosticIds)}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("Apply a fix with apply_code_fix using the same filePath, diagnosticId, line, column, and fixIndex.");

            return ToolTelemetry.TraceAndReturn(nameof(GetCodeFixes), sb.ToString().TrimEnd());
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(nameof(GetCodeFixes), "GetCodeFixes was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetCodeFixes failed for {FilePath} diagnostic {DiagnosticId}", filePath, diagnosticId);
            return ToolTelemetry.TraceAndReturn(nameof(GetCodeFixes), $"Failed to get code fixes: {ex.Message}");
        }
    }

    [McpServerTool(Name = "apply_code_fix", Title = "Apply Roslyn code fix")]
    [Description(
        "Applies a Roslyn code fix listed by get_code_fixes. Writes files. previewOnly=true returns a diff without writing.")]
    public async Task<string> ApplyCodeFix(
        [Description("Path to the target .cs file.")] string filePath,
        [Description("Diagnostic id from get_code_fixes.")] string diagnosticId,
        [Description("fixIndex from get_code_fixes, 0-based.")] int fixIndex,
        [Description("1-based line number.")] int line,
        [Description("1-based column number.")] int column = 1,
        [Description("When true, preview a diff without writing.")] bool previewOnly = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var resolvedPath = _solutionManager.ResolvePathAgainstWorkspace(filePath);
            var document = await ResolveDocumentAsync(filePath, cancellationToken);
            if (document is null)
            {
                return ToolTelemetry.TraceAndReturn(nameof(ApplyCodeFix), $"Document was not found in the active workspace: `{resolvedPath}`.");
            }

            if (previewOnly)
            {
                var preview = await CodeFixHelper.BuildPreviewAsync(
                    document, line, column, diagnosticId, fixIndex, cancellationToken);
                return ToolTelemetry.TraceAndReturn(
                    nameof(ApplyCodeFix),
                    $"Preview for fix [{fixIndex}] on `{diagnosticId}` at line {line}:{column}{Environment.NewLine}{Environment.NewLine}{preview}");
            }

            var baseSolution = document.Project.Solution;
            var (newSolution, changedPaths) = await CodeFixHelper.ApplyFixAsync(
                document, line, column, diagnosticId, fixIndex, cancellationToken);

            var write = await _solutionManager.ApplySolutionChangesToDiskAsync(
                baseSolution, newSolution, cancellationToken);
            if (!write.IsFullSuccess)
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(ApplyCodeFix),
                    write.FormatAdapterMessage($"Applied fix [{fixIndex}] for `{diagnosticId}` at line {line}, column {column}."));
            }

            var writtenPaths = write.SavedPaths;
            var sb = new StringBuilder();
            sb.AppendLine($"Applied fix [{fixIndex}] for `{diagnosticId}` at line {line}, column {column}.");
            sb.AppendLine($"Updated files ({writtenPaths.Count}):");
            foreach (var path in writtenPaths)
            {
                sb.AppendLine($"- {path}");
            }

            if (writtenPaths.Count == 0 && changedPaths.Count > 0)
            {
                sb.AppendLine("[!] Fix reported changes but no files were written.");
            }

            sb.AppendLine();
            sb.AppendLine("Run get_diagnostics_for_file to verify remaining issues.");

            return ToolTelemetry.TraceAndReturn(nameof(ApplyCodeFix), sb.ToString().TrimEnd());
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(nameof(ApplyCodeFix), "ApplyCodeFix was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ApplyCodeFix failed for {FilePath} diagnostic {DiagnosticId} fix {FixIndex}", filePath, diagnosticId, fixIndex);
            return ToolTelemetry.TraceAndReturn(nameof(ApplyCodeFix), $"Failed to apply code fix: {ex.Message}");
        }
    }

    private async Task<Microsoft.CodeAnalysis.Document?> ResolveDocumentAsync(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("filePath is empty.");
        }

        var fullPath = _solutionManager.ResolvePathAgainstWorkspace(filePath);
        if (!string.Equals(Path.GetExtension(fullPath), ".cs", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Path must point to a .cs file: `{fullPath}`.");
        }

        return await _solutionManager.FindDocumentAsync(fullPath, cancellationToken);
    }
}
