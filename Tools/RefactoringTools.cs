using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tools;

public sealed class RefactoringTools
{
    private readonly SolutionManager _solutionManager;
    private readonly ILogger<RefactoringTools> _logger;

    public RefactoringTools(SolutionManager solutionManager, ILogger<RefactoringTools> logger)
    {
        _solutionManager = solutionManager;
        _logger = logger;
    }

    [McpServerTool(Name = "extract_interface", Title = "Extract interface from class")]
    [Description(
        "Extracts a public interface from a class. Writes files. Requires load_workspace. previewOnly=true writes nothing.")]
    public async Task<string> ExtractInterface(
        [Description("Path to the .cs file containing the class.")] string filePath,
        [Description("Class to extract from.")] string className,
        [Description("Interface name. Default is I plus className.")] string? interfaceName = null,
        [Description("When true (default), write the interface to a new file.")] bool createNewFile = true,
        [Description("When true, preview without writing.")] bool previewOnly = false,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(ExtractInterface);

        try
        {
            var resolvedPath = _solutionManager.ResolvePathAgainstWorkspace(filePath);
            var document = await ResolveDocumentAsync(filePath, cancellationToken);
            if (document is null)
            {
                return ToolTelemetry.TraceAndReturn(toolName, $"Document was not found in the active workspace: `{resolvedPath}`.");
            }

            var baseSolution = document.Project.Solution;
            var (newSolution, preview) = await StructuralRefactoringHelper.ExtractInterfaceAsync(
                document, className, interfaceName, createNewFile, cancellationToken);

            if (previewOnly)
            {
                return ToolTelemetry.TraceAndReturn(toolName, "Preview only — no files written." + Environment.NewLine + StructuralRefactoringHelper.FormatPreview(preview));
            }

            var write = await _solutionManager.ApplySolutionChangesToDiskAsync(baseSolution, newSolution, cancellationToken);
            if (!write.IsFullSuccess)
            {
                return ToolTelemetry.TraceAndReturn(toolName, write.FormatAdapterMessage("Extract interface was not fully applied."));
            }

            return ToolTelemetry.TraceAndReturn(
                toolName,
                $"Extract interface applied. Files touched: {write.SavedPaths.Count}{Environment.NewLine}{StructuralRefactoringHelper.FormatPreview(preview)}");
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(toolName, "extract_interface was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExtractInterface failed for {ClassName} in {FilePath}", className, filePath);
            return ToolTelemetry.TraceAndReturn(toolName, $"Failed to extract interface: {ex.Message}");
        }
    }

    [McpServerTool(Name = "move_type_to_new_file", Title = "Move type to its own file")]
    [Description(
        "Moves top-level types into separate files named {TypeName}.cs. Writes files. previewOnly=true writes nothing.")]
    public async Task<string> MoveTypeToNewFile(
        [Description("Path to the .cs file containing the type(s).")] string filePath,
        [Description("Optional type name to move. Omit to move types that do not match the file name.")] string? typeName = null,
        [Description("When true, preview without writing.")] bool previewOnly = false,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(MoveTypeToNewFile);

        try
        {
            var resolvedPath = _solutionManager.ResolvePathAgainstWorkspace(filePath);
            var document = await ResolveDocumentAsync(filePath, cancellationToken);
            if (document is null)
            {
                return ToolTelemetry.TraceAndReturn(toolName, $"Document was not found in the active workspace: `{resolvedPath}`.");
            }

            var baseSolution = document.Project.Solution;
            var (newSolution, preview) = await StructuralRefactoringHelper.MoveTypesToNewFilesAsync(
                document, typeName, cancellationToken);

            if (previewOnly)
            {
                return ToolTelemetry.TraceAndReturn(toolName, "Preview only — no files written." + Environment.NewLine + StructuralRefactoringHelper.FormatPreview(preview));
            }

            var write = await _solutionManager.ApplySolutionChangesToDiskAsync(baseSolution, newSolution, cancellationToken);
            if (!write.IsFullSuccess)
            {
                return ToolTelemetry.TraceAndReturn(toolName, write.FormatAdapterMessage("Move type was not fully applied."));
            }

            return ToolTelemetry.TraceAndReturn(
                toolName,
                $"Move type applied. Files touched: {write.SavedPaths.Count}{Environment.NewLine}{StructuralRefactoringHelper.FormatPreview(preview)}");
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(toolName, "move_type_to_new_file was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MoveTypeToNewFile failed for {TypeName} in {FilePath}", typeName, filePath);
            return ToolTelemetry.TraceAndReturn(toolName, $"Failed to move type: {ex.Message}");
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
