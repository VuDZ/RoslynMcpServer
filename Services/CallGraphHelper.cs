using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace RoslynMcpServer.Services;

public sealed record CallGraphNode(string DisplayName, string? FilePath, int? Line);

public sealed record CallGraphResult(
    string TargetDisplay,
    IReadOnlyList<CallGraphNode> Callers,
    IReadOnlyList<CallGraphNode> Callees,
    bool CallersTruncated,
    bool CalleesTruncated);

public static class CallGraphHelper
{
    private const int DefaultMaxNodes = 25;

    public static async Task<CallGraphResult> BuildCallGraphAsync(
        Solution solution,
        Document document,
        string className,
        string methodName,
        int maxNodes,
        bool includeExternalCallees,
        CancellationToken cancellationToken)
    {
        maxNodes = Math.Clamp(maxNodes, 1, 100);

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null)
        {
            throw new InvalidOperationException("Could not obtain syntax tree or semantic model.");
        }

        var classDecl = TypeSyntaxHelper.FindClassDeclaration(root, className.Trim())
            ?? throw new InvalidOperationException($"Class `{className}` not found in `{document.FilePath}`.");

        var methodDecl = classDecl.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => string.Equals(m.Identifier.Text, methodName.Trim(), StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Method `{methodName}` not found in class `{className}`.");

        var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl, cancellationToken) as IMethodSymbol
            ?? throw new InvalidOperationException($"Could not resolve symbol for `{className}.{methodName}`.");

        var targetDisplay = methodSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        var callers = await CollectCallersAsync(solution, methodSymbol, maxNodes, cancellationToken).ConfigureAwait(false);
        var callees = CollectCallees(semanticModel, methodDecl, solution, maxNodes, includeExternalCallees);

        return new CallGraphResult(
            targetDisplay,
            callers.Nodes,
            callees.Nodes,
            callers.Truncated,
            callees.Truncated);
    }

    public static string FormatMarkdown(CallGraphResult graph)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Call graph");
        sb.AppendLine();
        sb.AppendLine($"**Target:** `{graph.TargetDisplay}`");
        sb.AppendLine();

        sb.AppendLine("### Callers (who invokes this method)");
        if (graph.Callers.Count == 0)
        {
            sb.AppendLine("- (none found in loaded solution)");
        }
        else
        {
            foreach (var caller in graph.Callers)
            {
                sb.AppendLine(FormatNode("- ", caller));
            }

            if (graph.CallersTruncated)
            {
                sb.AppendLine("- … truncated");
            }
        }

        sb.AppendLine();
        sb.AppendLine("### Callees (what this method calls)");
        if (graph.Callees.Count == 0)
        {
            sb.AppendLine("- (none found in method body)");
        }
        else
        {
            foreach (var callee in graph.Callees)
            {
                sb.AppendLine(FormatNode("- ", callee));
            }

            if (graph.CalleesTruncated)
            {
                sb.AppendLine("- … truncated");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatNode(string prefix, CallGraphNode node)
    {
        if (node.FilePath is not null && node.Line is not null)
        {
            return $"{prefix}`{node.DisplayName}` — `{node.FilePath}:{node.Line}`";
        }

        return $"{prefix}`{node.DisplayName}`";
    }

    private static async Task<(List<CallGraphNode> Nodes, bool Truncated)> CollectCallersAsync(
        Solution solution,
        IMethodSymbol methodSymbol,
        int maxNodes,
        CancellationToken cancellationToken)
    {
        var nodes = new List<CallGraphNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var truncated = false;

        var callersEnumerable = await SymbolFinder.FindCallersAsync(methodSymbol, solution, cancellationToken).ConfigureAwait(false);
        foreach (var caller in callersEnumerable)
        {
            if (caller.CallingSymbol is null)
            {
                continue;
            }

            var display = caller.CallingSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            if (!seen.Add(display))
            {
                continue;
            }

            string? filePath = null;
            int? line = null;
            foreach (var sourceLocation in caller.Locations)
            {
                if (!sourceLocation.IsInSource)
                {
                    continue;
                }

                var span = sourceLocation.GetLineSpan();
                filePath = span.Path;
                line = span.StartLinePosition.Line + 1;
                break;
            }

            nodes.Add(new CallGraphNode(display, filePath, line));
            if (nodes.Count >= maxNodes)
            {
                truncated = true;
                break;
            }
        }

        return (nodes, truncated);
    }

    private static (List<CallGraphNode> Nodes, bool Truncated) CollectCallees(
        SemanticModel semanticModel,
        MethodDeclarationSyntax methodDecl,
        Solution solution,
        int maxNodes,
        bool includeExternalCallees)
    {
        var nodes = new List<CallGraphNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var truncated = false;

        foreach (var invocation in methodDecl.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var symbolInfo = semanticModel.GetSymbolInfo(invocation.Expression);
            if (symbolInfo.Symbol is not ISymbol symbol)
            {
                continue;
            }

            var method = symbol switch
            {
                IMethodSymbol m => m,
                IPropertySymbol { IsIndexer: true } p => p.GetMethod,
                _ => null
            };

            if (method is null)
            {
                continue;
            }

            if (!includeExternalCallees && !IsSymbolFromLoadedSolution(method, solution))
            {
                continue;
            }

            var display = method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            if (!seen.Add(display))
            {
                continue;
            }

            string? filePath = null;
            int? line = null;
            var declRef = method.DeclaringSyntaxReferences.FirstOrDefault();
            if (declRef?.SyntaxTree?.FilePath is { Length: > 0 } fp)
            {
                filePath = fp;
                line = declRef.Span.Start;
                var lineSpan = declRef.SyntaxTree.GetLineSpan(declRef.Span);
                line = lineSpan.StartLinePosition.Line + 1;
            }

            nodes.Add(new CallGraphNode(display, filePath, line));
            if (nodes.Count >= maxNodes)
            {
                truncated = true;
                break;
            }
        }

        return (nodes.OrderBy(n => n.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(), truncated);
    }

    private static bool IsSymbolFromLoadedSolution(ISymbol symbol, Solution solution)
    {
        if (symbol.Locations.FirstOrDefault()?.SourceTree?.FilePath is not { } path)
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        return solution.Projects
            .SelectMany(p => p.Documents)
            .Any(d => d.FilePath is not null && string.Equals(Path.GetFullPath(d.FilePath), fullPath, StringComparison.OrdinalIgnoreCase));
    }
}
