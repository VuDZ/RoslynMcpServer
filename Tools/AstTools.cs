using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tools;

public sealed class AstTools
{
    private readonly SolutionManager _solutionManager;
    private readonly ILogger<AstTools> _logger;

    public AstTools(SolutionManager solutionManager, ILogger<AstTools> logger)
    {
        _solutionManager = solutionManager;
        _logger = logger;
    }

    [McpServerTool(Name = "add_using", Title = "Add using directive")]
    [Description(
        "Adds a `using` directive to a C# file via Roslyn AST insertion and formatting. " +
        "Use instead of `apply_patch` when adding imports — whitespace/formatting differences will not break the edit.")]
    public async Task<string> AddUsing(
        [Description("Path to the .cs file to modify.")] string filePath,
        [Description("Namespace to import, e.g. `System.Linq` or `using System.Collections.Generic;`.")]
        string namespaceName,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(AddUsing);

        try
        {
            var (document, baseSolution) = await ResolveDocumentAsync(filePath, cancellationToken);
            var newDocument = await AstModificationHelper.AddUsingAsync(document, namespaceName, cancellationToken)
                .ConfigureAwait(false);
            var newSolution = newDocument.Project.Solution;
            var writtenPaths = await _solutionManager.ApplySolutionChangesToDiskAsync(
                baseSolution, newSolution, cancellationToken).ConfigureAwait(false);

            return ToolTelemetry.TraceAndReturn(
                toolName,
                $"Added using `{AstModificationHelper.NormalizeNamespaceForDisplay(namespaceName)}` to `{Path.GetFullPath(filePath)}`. " +
                $"Files touched: {writtenPaths.Count}.");
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(toolName, "`add_using` was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddUsing failed for {Namespace} in {FilePath}", namespaceName, filePath);
            return ToolTelemetry.TraceAndReturn(toolName, $"Failed to add using: {ex.Message}");
        }
    }

    [McpServerTool(Name = "add_method_to_class", Title = "Add method to class")]
    [Description(
        "Parses a C# method declaration and inserts it into a class via Roslyn AST (DocumentEditor), then formats the file. " +
        "Use instead of `apply_patch` when adding methods — avoids brittle oldString matching on braces and indentation.")]
    public async Task<string> AddMethodToClass(
        [Description("Path to the .cs file containing the class.")] string filePath,
        [Description("Top-level class name, e.g. `OrderService`.")] string className,
        [Description("Method declaration source (modifiers, signature, body). Example: `public async Task<int> GetCountAsync(CancellationToken ct) { return 0; }`")]
        string methodSource,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(AddMethodToClass);

        try
        {
            var (document, baseSolution) = await ResolveDocumentAsync(filePath, cancellationToken);
            var newDocument = await AstModificationHelper.AddMethodToClassAsync(
                document, className, methodSource, cancellationToken).ConfigureAwait(false);
            var newSolution = newDocument.Project.Solution;
            var writtenPaths = await _solutionManager.ApplySolutionChangesToDiskAsync(
                baseSolution, newSolution, cancellationToken).ConfigureAwait(false);

            var methodName = TryExtractMethodName(methodSource);
            return ToolTelemetry.TraceAndReturn(
                toolName,
                $"Added method `{methodName}` to class `{className}` in `{Path.GetFullPath(filePath)}`. " +
                $"Files touched: {writtenPaths.Count}.");
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(toolName, "`add_method_to_class` was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddMethodToClass failed for {ClassName} in {FilePath}", className, filePath);
            return ToolTelemetry.TraceAndReturn(toolName, $"Failed to add method: {ex.Message}");
        }
    }

    private async Task<(Microsoft.CodeAnalysis.Document Document, Microsoft.CodeAnalysis.Solution BaseSolution)> ResolveDocumentAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("filePath is empty.");
        }

        var fullPath = Path.GetFullPath(filePath);
        if (!string.Equals(Path.GetExtension(fullPath), ".cs", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Path must point to a .cs file: `{fullPath}`.");
        }

        var document = await _solutionManager.FindDocumentAsync(fullPath, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            throw new InvalidOperationException(
                $"Document was not found in the active workspace: `{fullPath}`. Call `load_workspace` first.");
        }

        return (document, document.Project.Solution);
    }

    private static string TryExtractMethodName(string methodSource)
    {
        try
        {
            var member = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseMemberDeclaration(methodSource);
            if (member is Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax method)
            {
                return method.Identifier.Text;
            }
        }
        catch
        {
            // best-effort for success message only
        }

        return "(method)";
    }
}
