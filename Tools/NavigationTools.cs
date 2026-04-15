using System.ComponentModel;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;

namespace RoslynMcpServer.Tools;

public sealed class NavigationTools
{
    private const int MaxReferences = 20;
    private const int MaxDefinitionLocations = 200;
    private const int MaxFindUsagesReferences = 30;
    private const int MaxFindUsagesSourceLineChars = 400;
    private const int MaxAmbiguousCandidatesListed = 10;

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

    [McpServerTool(Name = "find_symbol_definition", Title = "Find symbol definitions in workspace")]
    [Description(
        "Searches the **currently loaded Roslyn solution** (semantic workspace index) for declarations whose name matches `symbolName`. "
        + "For each match it returns the symbol display string, every **source** definition file path, and the **1-based** starting line number. "
        + "Use this when you need to know **where a C# type or member is defined** (class, interface, struct, enum, method, property, etc.). "
        + "**Do not** answer “where is it **declared**?” with plain-text search or by running grep/findstr/Select-String from a terminal over the tree—those walk `bin/`, `obj/`, and generated trees, are easy to mis-read, and can trigger access violations or lock contention. "
        + "For arbitrary text search across files, use your environment’s built-in **`grep`** tool (not `bash`/`PowerShell` pipelines). "
        + "Call `load_workspace` first so the solution is loaded; then call this tool with the exact identifier text (matching is case-insensitive). "
        + "Do not invent generic tool names like `search` for this task. "
        + "For finding *usages*, use `find_usages` (solution-wide by simple name) or `find_symbol_references` when you already know the declaring `.cs` file.")]
    public async Task<string> FindSymbolDefinition(
        [Description("Exact identifier of the type or member to locate (e.g. `IRunAvpCommand`).")]
        string symbolName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(symbolName))
            {
                return ToolTelemetry.TraceAndReturn(nameof(FindSymbolDefinition), "Error: `symbolName` is empty.");
            }

            var solution = _solutionManager.GetCurrentSolution();
            if (solution is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(FindSymbolDefinition),
                    "Error: No active workspace. Call `load_workspace` with your .sln or .csproj first.");
            }

            var trimmedName = symbolName.Trim();
            var declarations = new List<ISymbol>();
            foreach (var projectId in solution.ProjectIds)
            {
                var project = solution.GetProject(projectId);
                if (project is null)
                {
                    continue;
                }

                var found = await SymbolFinder.FindDeclarationsAsync(
                    project,
                    trimmedName,
                    ignoreCase: true,
                    SymbolFilter.Type | SymbolFilter.Member,
                    cancellationToken).ConfigureAwait(false);
                declarations.AddRange(found);
            }

            var symbols = declarations.Distinct(SymbolEqualityComparer.Default).ToList();
            if (symbols.Count == 0)
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(FindSymbolDefinition),
                    $"Symbol `{trimmedName}` was not found in the current solution (no matching type or member declarations).");
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Found {symbols.Count} declaration symbol(s) matching `{trimmedName}`:");
            sb.AppendLine();

            var emitted = 0;
            foreach (var symbol in symbols)
            {
                var display = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                var sourceLocations = symbol.Locations.Where(l => l.IsInSource && l.SourceTree?.FilePath is not null).ToList();
                if (sourceLocations.Count == 0)
                {
                    sb.AppendLine($"Symbol: {display}");
                    sb.AppendLine("  (no in-source locations — metadata or implicit declaration only)");
                    sb.AppendLine();
                    continue;
                }

                foreach (var location in sourceLocations)
                {
                    if (emitted >= MaxDefinitionLocations)
                    {
                        sb.AppendLine(
                            $"[!] Output truncated after {MaxDefinitionLocations} source location(s). Narrow the symbol name or use `find_symbol_references` from a known file.");
                        return ToolTelemetry.TraceAndReturn(nameof(FindSymbolDefinition), sb.ToString().TrimEnd());
                    }

                    var path = location.SourceTree!.FilePath!;
                    var line = location.GetLineSpan().StartLinePosition.Line + 1;
                    sb.AppendLine($"Symbol: {display}");
                    sb.AppendLine($"  File: {path}");
                    sb.AppendLine($"  Line: {line}");
                    sb.AppendLine();
                    emitted++;
                }
            }

            return ToolTelemetry.TraceAndReturn(nameof(FindSymbolDefinition), sb.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find definition locations for {SymbolName}", symbolName);
            return ToolTelemetry.TraceAndReturn(
                nameof(FindSymbolDefinition),
                $"Failed to find definitions for `{symbolName}`: {ex.Message}");
        }
    }

    [McpServerTool(Name = "find_usages", Title = "Find symbol usages across solution")]
    [Description(
        "Semantically searches the **entire loaded Roslyn solution** for references and invocations of a symbol whose declared name matches `symbolName` (case-insensitive). "
        + "Returns grouped **file paths**, **1-based line numbers**, and the **actual source line text** at each reference so you can see how project utilities, types, or members are used in context. "
        + "Use this to learn usage patterns for classes, methods, properties, etc. Call `load_workspace` first. "
        + "If several declarations share the same simple name, the tool picks a single primary symbol (types preferred over methods/properties, then stable ordering by fully-qualified name); narrow `symbolName` or use `find_symbol_definition` / `find_symbol_references` with a known file when needed. "
        + "Output is capped at 30 references to limit token use.")]
    public async Task<string> FindUsages(
        [Description("Declared name of the type or member whose references to find (e.g. `Guard`, `JsonExtensions`, `Format`).")]
        string symbolName,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(FindUsages);

        try
        {
            if (string.IsNullOrWhiteSpace(symbolName))
            {
                return ToolTelemetry.TraceAndReturn(toolName, "Error: `symbolName` is empty.");
            }

            var solution = _solutionManager.GetCurrentSolution();
            if (solution is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    "Error: No active workspace. Call `load_workspace` with your .sln or .csproj first.");
            }

            var trimmedName = symbolName.Trim();
            var declarations = new List<ISymbol>();
            foreach (var projectId in solution.ProjectIds)
            {
                var project = solution.GetProject(projectId);
                if (project is null)
                {
                    continue;
                }

                var found = await SymbolFinder.FindDeclarationsAsync(
                    project,
                    trimmedName,
                    ignoreCase: true,
                    SymbolFilter.Type | SymbolFilter.Member,
                    cancellationToken).ConfigureAwait(false);
                declarations.AddRange(found);
            }

            var symbols = declarations.Distinct(SymbolEqualityComparer.Default).ToList();
            if (symbols.Count == 0)
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    $"No declarations named `{trimmedName}` were found in the current solution.");
            }

            var targetSymbol = PickPrimarySymbol(symbols);
            var chosenDisplay = targetSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var chosenFqn = targetSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            var references = await SymbolFinder.FindReferencesAsync(targetSymbol, solution, cancellationToken)
                .ConfigureAwait(false);

            var refLocations = references
                .SelectMany(r => r.Locations)
                .Where(l => l.Location.IsInSource && l.Document.FilePath is not null)
                .OrderBy(l => l.Document.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(l => l.Location.GetLineSpan().StartLinePosition.Line)
                .ThenBy(l => l.Location.SourceSpan.Start)
                .ToList();

            var totalFound = refLocations.Count;
            var truncated = totalFound > MaxFindUsagesReferences;
            var limited = refLocations.Take(MaxFindUsagesReferences).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"## Usages for `{trimmedName}`");
            sb.AppendLine();
            sb.AppendLine($"**Primary symbol:** `{chosenDisplay}`");
            sb.AppendLine($"`{chosenFqn}`");
            sb.AppendLine();

            if (symbols.Count > 1)
            {
                sb.AppendLine(
                    $"[!] {symbols.Count} declarations match this name; references are for the primary symbol above. Other candidates:");
                foreach (var s in symbols
                             .Where(s => !SymbolEqualityComparer.Default.Equals(s, targetSymbol))
                             .OrderBy(s => s.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
                             .Take(MaxAmbiguousCandidatesListed))
                {
                    sb.AppendLine($"  - {s.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}");
                }

                if (symbols.Count - 1 > MaxAmbiguousCandidatesListed)
                {
                    sb.AppendLine($"  … and {symbols.Count - 1 - MaxAmbiguousCandidatesListed} more.");
                }

                sb.AppendLine();
            }

            if (limited.Count == 0)
            {
                sb.AppendLine("No in-source references were returned for this symbol.");
                return ToolTelemetry.TraceAndReturn(toolName, sb.ToString().TrimEnd());
            }

            sb.AppendLine(
                $"Showing **{limited.Count}** reference location(s)" +
                (truncated ? $" of **{totalFound}** total (capped at {MaxFindUsagesReferences})." : "."));
            sb.AppendLine();

            var textByDocument = new Dictionary<DocumentId, SourceText>();
            foreach (var group in limited.GroupBy(l => l.Document.FilePath!, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"### `{group.Key}`");
                sb.AppendLine();
                foreach (var refLoc in group)
                {
                    var line1Based = refLoc.Location.GetLineSpan().StartLinePosition.Line + 1;
                    var lineText = await GetReferenceSourceLineAsync(refLoc, textByDocument, cancellationToken)
                        .ConfigureAwait(false);
                    if (string.IsNullOrEmpty(lineText))
                    {
                        lineText = "(source line unavailable)";
                    }

                    sb.AppendLine($"- **Line {line1Based}:** `{EscapeMdBackticks(lineText)}`");
                }

                sb.AppendLine();
            }

            if (truncated)
            {
                sb.AppendLine($"[!] Truncated to {MaxFindUsagesReferences} references; narrow `symbolName` or use `find_symbol_references` from a declaring file.");
            }

            return ToolTelemetry.TraceAndReturn(toolName, sb.ToString().TrimEnd());
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(toolName, "`find_usages` was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FindUsages failed for {SymbolName}", symbolName);
            return ToolTelemetry.TraceAndReturn(toolName, $"Failed to find usages for `{symbolName}`: {ex.Message}");
        }
    }

    private static ISymbol PickPrimarySymbol(IReadOnlyList<ISymbol> symbols)
    {
        return symbols
            .OrderByDescending(SymbolPriority)
            .ThenBy(s => s is IMethodSymbol m ? m.Parameters.Length : 0)
            .ThenBy(s => s.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
            .First();
    }

    private static int SymbolPriority(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol => 400,
        IMethodSymbol => 300,
        IPropertySymbol => 250,
        IEventSymbol => 200,
        IFieldSymbol => 150,
        _ => 100
    };

    private static async Task<string> GetReferenceSourceLineAsync(
        ReferenceLocation refLoc,
        Dictionary<DocumentId, SourceText> textByDocument,
        CancellationToken cancellationToken)
    {
        var document = refLoc.Document;
        if (!textByDocument.TryGetValue(document.Id, out var text))
        {
            text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            textByDocument[document.Id] = text;
        }

        var lineIndex = refLoc.Location.GetLineSpan().StartLinePosition.Line;
        if (lineIndex < 0 || lineIndex >= text.Lines.Count)
        {
            return string.Empty;
        }

        var raw = text.Lines[lineIndex].ToString().TrimEnd();
        if (raw.Length > MaxFindUsagesSourceLineChars)
        {
            return raw[..MaxFindUsagesSourceLineChars] + "…";
        }

        return raw;
    }

    private static string EscapeMdBackticks(string s) => s.Replace('`', '\'');

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
