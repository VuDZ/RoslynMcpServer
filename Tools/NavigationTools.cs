using System.ComponentModel;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;

namespace RoslynMcpServer.Tools;

public sealed class NavigationTools
{
    private const int MaxReferences = 20;

    private readonly SolutionManager _solutionManager;
    private readonly ILogger<NavigationTools> _logger;

    public NavigationTools(SolutionManager solutionManager, ILogger<NavigationTools> logger)
    {
        _solutionManager = solutionManager;
        _logger = logger;
    }

    [McpServerTool(Name = "find_symbol_references", Title = "Find symbol references")]
    [Description("Finds all usages of a class, interface, or method across the entire solution. CRITICAL for safe refactoring.")]
    public async Task<string> FindSymbolReferences(
        [Description("Path to a .cs file (same JSON key `filePath` as get_file_content / get_diagnostics_for_file).")]
        string filePath,
        [Description("Symbol name (class/interface/method)")]
        string symbolName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return ToolTelemetry.TraceAndReturn(nameof(FindSymbolReferences), "Error: `filePath` is empty.");
            }

            if (string.IsNullOrWhiteSpace(symbolName))
            {
                return ToolTelemetry.TraceAndReturn(nameof(FindSymbolReferences), "Symbol name is empty.");
            }

            var document = await _solutionManager.FindDocumentAsync(filePath, cancellationToken);
            if (document is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(FindSymbolReferences),
                    $"Could not resolve Roslyn document for file: `{filePath}`.");
            }

            var solution = _solutionManager.GetCurrentSolution() ?? document.Project.Solution;
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
            if (semanticModel is null || syntaxRoot is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(FindSymbolReferences),
                    $"Could not build semantic model for file: `{filePath}`.");
            }

            var declaration = FindMatchingDeclaration(syntaxRoot, symbolName);
            if (declaration is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(FindSymbolReferences),
                    $"Symbol `{symbolName}` was not found as a class/interface/method declaration in `{filePath}`.");
            }

            var symbol = semanticModel.GetDeclaredSymbol(declaration, cancellationToken);
            if (symbol is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(FindSymbolReferences),
                    $"Unable to resolve declared symbol for `{symbolName}` in `{filePath}`.");
            }

            var references = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken);
            var locations = references
                .SelectMany(r => r.Locations)
                .Where(l => l.Location.IsInSource)
                .ToList();

            if (locations.Count == 0)
            {
                return ToolTelemetry.TraceAndReturn(nameof(FindSymbolReferences), $"No usages found for `{symbolName}`.");
            }

            var truncated = locations.Count > MaxReferences;
            var limited = locations.Take(MaxReferences).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"Found {limited.Count} usages for '{symbolName}':");
            sb.AppendLine();

            foreach (var docGroup in limited.GroupBy(l => l.Document.Id))
            {
                var refDoc = solution.GetDocument(docGroup.Key);
                var docPath = refDoc?.FilePath ?? "(unknown file)";
                sb.AppendLine($"File: {docPath}");

                if (refDoc is null)
                {
                    sb.AppendLine("- Inside: (document unavailable)");
                    sb.AppendLine();
                    continue;
                }

                var root = await refDoc.GetSyntaxRootAsync(cancellationToken);
                if (root is null)
                {
                    sb.AppendLine("- Inside: (syntax tree unavailable)");
                    sb.AppendLine();
                    continue;
                }

                foreach (var location in docGroup)
                {
                    var node = root.FindNode(location.Location.SourceSpan, getInnermostNodeForTie: true);
                    sb.AppendLine($"- Inside: {GetEnclosingContext(node)}");
                }

                sb.AppendLine();
            }

            if (truncated)
            {
                sb.AppendLine("[!] More than 20 usages found. Truncated to protect LLM context.");
            }

            return ToolTelemetry.TraceAndReturn(nameof(FindSymbolReferences), sb.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find references for {SymbolName} in {FilePath}", symbolName, filePath);
            return ToolTelemetry.TraceAndReturn(
                nameof(FindSymbolReferences),
                $"Failed to find references for `{symbolName}`: {ex.Message}");
        }
    }

    private static SyntaxNode? FindMatchingDeclaration(SyntaxNode root, string symbolName)
    {
        return root.DescendantNodes().FirstOrDefault(node =>
            node switch
            {
                ClassDeclarationSyntax c => string.Equals(c.Identifier.ValueText, symbolName, StringComparison.Ordinal),
                InterfaceDeclarationSyntax i => string.Equals(i.Identifier.ValueText, symbolName, StringComparison.Ordinal),
                MethodDeclarationSyntax m => string.Equals(m.Identifier.ValueText, symbolName, StringComparison.Ordinal),
                _ => false
            });
    }

    private static string GetEnclosingContext(SyntaxNode? referenceNode)
    {
        if (referenceNode is null)
        {
            return "(context not available)";
        }

        var method = referenceNode.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method is not null)
        {
            return ToSingleLine(method.WithBody(null).WithExpressionBody(null).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));
        }

        var ctor = referenceNode.AncestorsAndSelf().OfType<ConstructorDeclarationSyntax>().FirstOrDefault();
        if (ctor is not null)
        {
            return ToSingleLine(ctor.WithBody(null).WithExpressionBody(null).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));
        }

        var property = referenceNode.AncestorsAndSelf().OfType<PropertyDeclarationSyntax>().FirstOrDefault();
        if (property is not null)
        {
            return ToSingleLine(SanitizeProperty(property));
        }

        var field = referenceNode.AncestorsAndSelf().OfType<FieldDeclarationSyntax>().FirstOrDefault();
        if (field is not null)
        {
            return ToSingleLine(field);
        }

        var classDecl = referenceNode.AncestorsAndSelf().OfType<ClassDeclarationSyntax>().FirstOrDefault();
        if (classDecl is not null)
        {
            return $"class {classDecl.Identifier.ValueText}";
        }

        var interfaceDecl = referenceNode.AncestorsAndSelf().OfType<InterfaceDeclarationSyntax>().FirstOrDefault();
        if (interfaceDecl is not null)
        {
            return $"interface {interfaceDecl.Identifier.ValueText}";
        }

        return ToSingleLine(referenceNode);
    }

    private static PropertyDeclarationSyntax SanitizeProperty(PropertyDeclarationSyntax property)
    {
        if (property.AccessorList is null)
        {
            return property;
        }

        var sanitizedAccessors = property.AccessorList.Accessors.Select(accessor =>
            accessor
                .WithBody(null)
                .WithExpressionBody(null)
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));

        return property.WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.List(sanitizedAccessors)));
    }

    private static string ToSingleLine(SyntaxNode node)
    {
        return node.NormalizeWhitespace(eol: " ", indentation: string.Empty)
            .ToFullString()
            .Trim();
    }
}
