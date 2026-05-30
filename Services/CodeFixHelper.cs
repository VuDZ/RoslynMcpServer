using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.Text;

namespace RoslynMcpServer.Services;

public sealed record CodeFixDescriptor(
    int FixIndex,
    string Title,
    string DiagnosticId,
    int Line,
    int Column,
    IReadOnlyList<string> DiagnosticIds);

internal sealed record ResolvedCodeFix(
    CodeFixDescriptor Descriptor,
    CodeAction Action,
    Document Document);

public sealed class CodeFixHelper
{
    private const int MaxFixes = 20;

    public static async Task<IReadOnlyList<CodeFixDescriptor>> GetFixesAsync(
        Document document,
        int line,
        int column,
        string diagnosticId,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveFixesAsync(document, line, column, diagnosticId, cancellationToken);
        return resolved.Select(r => r.Descriptor).ToList();
    }

    public static async Task<(Solution NewSolution, IReadOnlyList<string> ChangedFilePaths)> ApplyFixAsync(
        Document document,
        int line,
        int column,
        string diagnosticId,
        int fixIndex,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveFixesAsync(document, line, column, diagnosticId, cancellationToken);
        var match = resolved.FirstOrDefault(r => r.Descriptor.FixIndex == fixIndex);
        if (match is null)
        {
            throw new InvalidOperationException(
                $"Fix index {fixIndex} was not found for diagnostic `{diagnosticId}` at line {line}, column {column}. Call get_code_fixes first.");
        }

        var baseSolution = document.Project.Solution;
        var operations = await match.Action.GetOperationsAsync(cancellationToken).ConfigureAwait(false);
        var newSolution = ApplyOperations(baseSolution, operations);
        if (newSolution is null)
        {
            throw new InvalidOperationException($"Fix `{match.Descriptor.Title}` produced no solution changes.");
        }

        var changedPaths = await GetChangedFilePathsAsync(baseSolution, newSolution, cancellationToken);
        return (newSolution, changedPaths);
    }

    public static async Task<string> BuildPreviewAsync(
        Document document,
        int line,
        int column,
        string diagnosticId,
        int fixIndex,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveFixesAsync(document, line, column, diagnosticId, cancellationToken);
        var match = resolved.FirstOrDefault(r => r.Descriptor.FixIndex == fixIndex);
        if (match is null)
        {
            throw new InvalidOperationException(
                $"Fix index {fixIndex} was not found for diagnostic `{diagnosticId}` at line {line}, column {column}.");
        }

        var baseSolution = document.Project.Solution;
        var operations = await match.Action.GetOperationsAsync(cancellationToken).ConfigureAwait(false);
        var newSolution = ApplyOperations(baseSolution, operations);
        if (newSolution is null)
        {
            return "No changes would be applied.";
        }

        var sb = new StringBuilder();
        foreach (var project in newSolution.Projects)
        {
            foreach (var newDoc in project.Documents)
            {
                var oldDoc = baseSolution.GetDocument(newDoc.Id);
                if (oldDoc is null || newDoc.FilePath is null)
                {
                    continue;
                }

                var oldText = await oldDoc.GetTextAsync(cancellationToken);
                var newText = await newDoc.GetTextAsync(cancellationToken);
                if (string.Equals(oldText.ToString(), newText.ToString(), StringComparison.Ordinal))
                {
                    continue;
                }

                sb.AppendLine($"--- {newDoc.FilePath}");
                sb.AppendLine(BuildUnifiedDiffPreview(oldText.ToString(), newText.ToString(), maxLines: 40));
                sb.AppendLine();
            }
        }

        return sb.Length == 0 ? "No changes would be applied." : sb.ToString().TrimEnd();
    }

    private static async Task<IReadOnlyList<ResolvedCodeFix>> ResolveFixesAsync(
        Document document,
        int line,
        int column,
        string diagnosticId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(diagnosticId))
        {
            throw new ArgumentException("Diagnostic id cannot be empty.", nameof(diagnosticId));
        }

        if (line < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(line), "Line must be >= 1.");
        }

        if (column < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(column), "Column must be >= 1.");
        }

        if (!RoslynCodeFixBridge.IsAvailable)
        {
            throw new InvalidOperationException(
                "Roslyn code fix service is unavailable. Ensure Microsoft.CodeAnalysis.Features packages are loaded.");
        }

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        if (semanticModel is null)
        {
            throw new InvalidOperationException("Could not obtain semantic model for the document.");
        }

        var text = await document.GetTextAsync(cancellationToken);
        if (line > text.Lines.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(line), $"Line {line} is out of range (file has {text.Lines.Count} lines).");
        }

        var position = text.Lines[line - 1].Start + Math.Min(column - 1, text.Lines[line - 1].Span.Length);
        var matchingDiagnostics = semanticModel.GetDiagnostics()
            .Where(d => string.Equals(d.Id, diagnosticId, StringComparison.Ordinal))
            .Where(d => d.Location.IsInSource)
            .Where(d => IsNearPosition(d, line, position))
            .OrderBy(d => DistanceToPosition(d, position))
            .ToList();

        if (matchingDiagnostics.Count == 0)
        {
            throw new InvalidOperationException(
                $"No diagnostic `{diagnosticId}` found near line {line}, column {column}. Run get_diagnostics_for_file first.");
        }

        var seenTitles = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<ResolvedCodeFix>();

        foreach (var diagnostic in matchingDiagnostics)
        {
            if (results.Count >= MaxFixes)
            {
                break;
            }

            var span = diagnostic.Location.SourceSpan;
            var fixes = await RoslynCodeFixBridge.GetFixesAsync(document, span, cancellationToken);
            foreach (var (title, action) in fixes)
            {
                if (results.Count >= MaxFixes)
                {
                    break;
                }

                if (!seenTitles.Add(title))
                {
                    continue;
                }

                var fixLine = diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1;
                var fixColumn = diagnostic.Location.GetLineSpan().StartLinePosition.Character + 1;
                var descriptor = new CodeFixDescriptor(
                    FixIndex: results.Count,
                    Title: title,
                    DiagnosticId: diagnosticId,
                    Line: fixLine,
                    Column: fixColumn,
                    DiagnosticIds: [diagnosticId]);

                results.Add(new ResolvedCodeFix(descriptor, action, document));
            }

            if (results.Count > 0)
            {
                break;
            }
        }

        return results;
    }

    private static bool IsNearPosition(Diagnostic diagnostic, int line, int position)
    {
        var span = diagnostic.Location.SourceSpan;
        if (span.Contains(position))
        {
            return true;
        }

        var diagnosticLine = diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1;
        return diagnosticLine == line;
    }

    private static int DistanceToPosition(Diagnostic diagnostic, int position)
    {
        var span = diagnostic.Location.SourceSpan;
        if (span.Contains(position))
        {
            return 0;
        }

        return Math.Min(Math.Abs(span.Start - position), Math.Abs(span.End - position));
    }

    private static Solution? ApplyOperations(Solution baseSolution, IEnumerable<CodeActionOperation> operations)
    {
        Solution? current = baseSolution;
        foreach (var operation in operations)
        {
            if (operation is ApplyChangesOperation applyChanges)
            {
                current = applyChanges.ChangedSolution;
            }
        }

        return ReferenceEquals(current, baseSolution) ? null : current;
    }

    private static async Task<IReadOnlyList<string>> GetChangedFilePathsAsync(
        Solution oldSolution,
        Solution newSolution,
        CancellationToken cancellationToken)
    {
        var changed = new List<string>();
        foreach (var project in newSolution.Projects)
        {
            foreach (var newDoc in project.Documents)
            {
                if (newDoc.FilePath is null)
                {
                    continue;
                }

                var oldDoc = oldSolution.GetDocument(newDoc.Id);
                if (oldDoc is null)
                {
                    changed.Add(newDoc.FilePath);
                    continue;
                }

                var oldText = await oldDoc.GetTextAsync(cancellationToken);
                var newText = await newDoc.GetTextAsync(cancellationToken);
                if (!string.Equals(oldText.ToString(), newText.ToString(), StringComparison.Ordinal))
                {
                    changed.Add(newDoc.FilePath);
                }
            }
        }

        return changed;
    }

    private static string BuildUnifiedDiffPreview(string oldText, string newText, int maxLines)
    {
        var oldLines = oldText.Split('\n');
        var newLines = newText.Split('\n');
        var sb = new StringBuilder();
        var emitted = 0;

        var max = Math.Max(oldLines.Length, newLines.Length);
        for (var i = 0; i < max && emitted < maxLines; i++)
        {
            var oldLine = i < oldLines.Length ? oldLines[i].TrimEnd('\r') : null;
            var newLine = i < newLines.Length ? newLines[i].TrimEnd('\r') : null;
            if (string.Equals(oldLine, newLine, StringComparison.Ordinal))
            {
                continue;
            }

            emitted++;
            sb.AppendLine($"@@ line {i + 1} @@");
            if (oldLine is not null)
            {
                sb.AppendLine($"- {oldLine}");
            }

            if (newLine is not null)
            {
                sb.AppendLine($"+ {newLine}");
            }
        }

        if (emitted == 0)
        {
            return "(text changed but line-based diff is empty)";
        }

        return sb.ToString().TrimEnd();
    }
}
