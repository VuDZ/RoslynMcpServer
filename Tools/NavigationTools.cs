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
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tools;

public sealed class NavigationTools
{
    private const int MaxReferences = 20;
    private const int MaxDefinitionLocations = 200;
    private const int MaxFindUsagesReferences = 30;
    private const int MaxFindUsagesSourceLineChars = 400;
    private const int MaxAmbiguousCandidatesListed = 10;
    private const int MaxImplementations = 50;

    private readonly SolutionManager _solutionManager;
    private readonly ILogger<NavigationTools> _logger;

    public NavigationTools(SolutionManager solutionManager, ILogger<NavigationTools> logger)
    {
        _solutionManager = solutionManager;
        _logger = logger;
    }

    [McpServerTool(Name = "find_symbol_references", Title = "Find symbol references")]
    [Description(
        "Finds semantic references when the declaring .cs file is known. Requires load_workspace. "
        + "For solution-wide search by name use find_usages.")]
    public async Task<string> FindSymbolReferences(
        [Description("Path to the declaring .cs file.")]
        string filePath,
        [Description("Symbol name declared in that file.")]
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

            var fullPath = _solutionManager.ResolvePathAgainstWorkspace(filePath);
            var document = await _solutionManager.FindDocumentAsync(fullPath, cancellationToken);
            if (document is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(FindSymbolReferences),
                    $"Could not resolve Roslyn document for file: `{fullPath}`.");
            }

            var solution = document.Project.Solution;
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
            if (semanticModel is null || syntaxRoot is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(FindSymbolReferences),
                    $"Could not build semantic model for file: `{fullPath}`.");
            }

            var declaration = FindMatchingDeclaration(syntaxRoot, symbolName);
            if (declaration is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(FindSymbolReferences),
                    $"Symbol `{symbolName}` was not found as a class/interface/method declaration in `{fullPath}`.");
            }

            var symbol = semanticModel.GetDeclaredSymbol(declaration, cancellationToken);
            if (symbol is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(FindSymbolReferences),
                    $"Unable to resolve declared symbol for `{symbolName}` in `{fullPath}`.");
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
                WorkspaceLoadGuidance.FormatCaughtException(
                    ex,
                    $"Failed to find references for `{symbolName}`: {ex.Message}",
                    filePath));
        }
    }

    [McpServerTool(Name = "find_symbol_definition", Title = "Find symbol definitions in workspace")]
    [Description(
        "Finds declarations of a type or member in the loaded workspace. Requires load_workspace. "
        + "Do not use text search for where a symbol is declared. For usages use find_usages; when the declaring file is known use find_symbol_references.")]
    public async Task<string> FindSymbolDefinition(
        [Description("Exact identifier of the type or member.")]
        string symbolName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(symbolName))
            {
                return ToolTelemetry.TraceAndReturn(nameof(FindSymbolDefinition), "Error: `symbolName` is empty.");
            }

            var solution = await _solutionManager.GetPublishedSolutionAfterDiskSyncAsync(cancellationToken).ConfigureAwait(false);
            if (solution is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(FindSymbolDefinition),
                    WorkspaceLoadGuidance.FormatNoWorkspaceLoadedMessage(
                        "Error: No active workspace."));
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
                    _solutionManager.WithDiskSyncNotes(
                        $"Symbol `{trimmedName}` was not found in the current solution (no matching type or member declarations)."));
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
                        return ToolTelemetry.TraceAndReturn(
                            nameof(FindSymbolDefinition),
                            _solutionManager.WithDiskSyncNotes(sb.ToString().TrimEnd()));
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

            return ToolTelemetry.TraceAndReturn(
                nameof(FindSymbolDefinition),
                _solutionManager.WithDiskSyncNotes(sb.ToString().TrimEnd()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find definition locations for {SymbolName}", symbolName);
            return ToolTelemetry.TraceAndReturn(
                nameof(FindSymbolDefinition),
                WorkspaceLoadGuidance.FormatCaughtException(
                    ex,
                    $"Failed to find definitions for `{symbolName}`: {ex.Message}"));
        }
    }

    [McpServerTool(Name = "find_usages", Title = "Find symbol usages across solution")]
    [Description(
        "Finds solution-wide semantic references by declared name. Requires load_workspace. "
        + "When the declaring file is known use find_symbol_references. For interface or base hierarchy use find_implementations.")]
    public async Task<string> FindUsages(
        [Description("Declared name of the type or member.")]
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

            var solution = await _solutionManager.GetPublishedSolutionAfterDiskSyncAsync(cancellationToken).ConfigureAwait(false);
            if (solution is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    WorkspaceLoadGuidance.FormatNoWorkspaceLoadedMessage(
                        "Error: No active workspace."));
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
                    _solutionManager.WithDiskSyncNotes(
                        $"No declarations named `{trimmedName}` were found in the current solution."));
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
                return ToolTelemetry.TraceAndReturn(toolName, _solutionManager.WithDiskSyncNotes(sb.ToString().TrimEnd()));
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

            return ToolTelemetry.TraceAndReturn(toolName, _solutionManager.WithDiskSyncNotes(sb.ToString().TrimEnd()));
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(toolName, "`find_usages` was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FindUsages failed for {SymbolName}", symbolName);
            return ToolTelemetry.TraceAndReturn(
                toolName,
                WorkspaceLoadGuidance.FormatCaughtException(
                    ex,
                    $"Failed to find usages for `{symbolName}`: {ex.Message}"));
        }
    }

    [McpServerTool(Name = "find_implementations", Title = "Find interface implementations or derived types")]
    [Description(
        "Finds types that implement an interface or derive from a base type. Requires load_workspace. "
        + "Do not use find_usages or text search for this.")]
    public async Task<string> FindImplementations(
        [Description("Interface or base type name.")]
        string symbolName,
        [Description("When true (default), include indirect implementations and derived types.")]
        bool transitive = true,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(FindImplementations);

        try
        {
            if (string.IsNullOrWhiteSpace(symbolName))
            {
                return ToolTelemetry.TraceAndReturn(toolName, "Error: `symbolName` is empty.");
            }

            var solution = await _solutionManager.GetPublishedSolutionAfterDiskSyncAsync(cancellationToken).ConfigureAwait(false);
            if (solution is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    WorkspaceLoadGuidance.FormatNoWorkspaceLoadedMessage(
                        "Error: No active workspace."));
            }

            var trimmedName = symbolName.Trim();
            var typeSymbols = await FindNamedTypeDeclarationsAsync(solution, trimmedName, cancellationToken);
            if (typeSymbols.Count == 0)
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    $"No type declaration named `{trimmedName}` was found in the current solution.");
            }

            var targetType = PickPrimaryTypeSymbol(typeSymbols);
            var kindLabel = GetTypeKindLabel(targetType);
            var chosenDisplay = targetType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var chosenFqn = targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            IEnumerable<INamedTypeSymbol> relatedTypes;
            string searchMode;
            switch (targetType.TypeKind)
            {
                case TypeKind.Interface:
                    relatedTypes = await SymbolFinder.FindImplementationsAsync(
                        targetType, solution, transitive, projects: null, cancellationToken).ConfigureAwait(false);
                    searchMode = transitive ? "implementations (transitive)" : "direct implementations";
                    break;
                case TypeKind.Class:
                case TypeKind.Struct:
                    relatedTypes = await SymbolFinder.FindDerivedClassesAsync(
                        targetType, solution, transitive, projects: null, cancellationToken).ConfigureAwait(false);
                    searchMode = transitive ? "derived types (transitive)" : "direct derived types";
                    break;
                default:
                    return ToolTelemetry.TraceAndReturn(
                        toolName,
                        $"Symbol `{trimmedName}` is a {targetType.TypeKind}; only interfaces, classes, and structs are supported.");
            }

            var results = relatedTypes
                .Distinct(SymbolEqualityComparer.Default)
                .Cast<INamedTypeSymbol>()
                .OrderBy(t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"## {searchMode} for `{trimmedName}`");
            sb.AppendLine();
            sb.AppendLine($"**Base symbol:** `{chosenDisplay}` ({kindLabel})");
            sb.AppendLine($"`{chosenFqn}`");
            sb.AppendLine();

            if (typeSymbols.Count > 1)
            {
                sb.AppendLine(
                    $"[!] {typeSymbols.Count} type declarations match this name; results are for the primary symbol above. Other candidates:");
                foreach (var candidate in typeSymbols
                             .Where(t => !SymbolEqualityComparer.Default.Equals(t, targetType))
                             .OrderBy(t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
                             .Take(MaxAmbiguousCandidatesListed))
                {
                    sb.AppendLine($"  - {candidate.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}");
                }

                if (typeSymbols.Count - 1 > MaxAmbiguousCandidatesListed)
                {
                    sb.AppendLine($"  … and {typeSymbols.Count - 1 - MaxAmbiguousCandidatesListed} more.");
                }

                sb.AppendLine();
            }

            if (results.Count == 0)
            {
                sb.AppendLine($"No {searchMode} were found in the solution.");
                return ToolTelemetry.TraceAndReturn(toolName, sb.ToString().TrimEnd());
            }

            var truncated = results.Count > MaxImplementations;
            var limited = results.Take(MaxImplementations).ToList();

            sb.AppendLine($"Found **{results.Count}** type(s)" + (truncated ? $" (showing first {MaxImplementations}):" : ":"));
            sb.AppendLine();

            var index = 1;
            foreach (var type in limited)
            {
                var display = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                var location = type.Locations.FirstOrDefault(l => l.IsInSource && l.SourceTree?.FilePath is not null);
                if (location is null)
                {
                    sb.AppendLine($"{index}. `{display}` — (no in-source location)");
                }
                else
                {
                    var path = location.SourceTree!.FilePath!;
                    var line = location.GetLineSpan().StartLinePosition.Line + 1;
                    sb.AppendLine($"{index}. `{display}` — `{path}:{line}`");
                }

                index++;
            }

            if (truncated)
            {
                sb.AppendLine();
                sb.AppendLine($"[!] Truncated to {MaxImplementations} types. Narrow `symbolName` or use `find_symbol_definition` to disambiguate.");
            }

            return ToolTelemetry.TraceAndReturn(toolName, sb.ToString().TrimEnd());
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(toolName, "`find_implementations` was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FindImplementations failed for {SymbolName}", symbolName);
            return ToolTelemetry.TraceAndReturn(
                toolName,
                WorkspaceLoadGuidance.FormatCaughtException(
                    ex,
                    $"Failed to find implementations for `{symbolName}`: {ex.Message}"));
        }
    }

    private static async Task<List<INamedTypeSymbol>> FindNamedTypeDeclarationsAsync(
        Solution solution,
        string trimmedName,
        CancellationToken cancellationToken)
    {
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
                SymbolFilter.Type,
                cancellationToken).ConfigureAwait(false);
            declarations.AddRange(found);
        }

        return declarations
            .OfType<INamedTypeSymbol>()
            .Distinct(SymbolEqualityComparer.Default)
            .Cast<INamedTypeSymbol>()
            .ToList();
    }

    private static INamedTypeSymbol PickPrimaryTypeSymbol(IReadOnlyList<INamedTypeSymbol> types)
    {
        return types
            .OrderByDescending(t => t.TypeKind == TypeKind.Interface ? 300 : t.TypeKind == TypeKind.Class ? 200 : 100)
            .ThenBy(t => t.IsAbstract ? 0 : 1)
            .ThenBy(t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
            .First();
    }

    private static string GetTypeKindLabel(INamedTypeSymbol type) => type.TypeKind switch
    {
        TypeKind.Interface => "interface",
        TypeKind.Struct => "struct",
        TypeKind.Class when type.IsRecord => "record",
        TypeKind.Class => type.IsAbstract ? "abstract class" : "class",
        _ => type.TypeKind.ToString().ToLowerInvariant()
    };

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

    [McpServerTool(Name = "get_call_graph", Title = "Get method call graph")]
    [Description("Lists callers and callees of a method in the loaded workspace. Requires load_workspace.")]
    public async Task<string> GetCallGraph(
        [Description("Path to the .cs file containing the method.")] string filePath,
        [Description("Class containing the method.")] string className,
        [Description("Method name.")] string methodName,
        [Description("Max nodes per callers/callees list.")] int maxNodes = 25,
        [Description("When true, include callees outside the loaded solution.")] bool includeExternalCallees = false,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(GetCallGraph);
        try
        {
            var fullPath = _solutionManager.ResolvePathAgainstWorkspace(filePath);
            var document = await _solutionManager.FindDocumentAsync(fullPath, cancellationToken).ConfigureAwait(false);
            if (document is null)
            {
                return ToolTelemetry.TraceAndReturn(toolName, $"Document not in workspace: `{fullPath}`. Call `load_workspace` first.");
            }

            var solution = document.Project.Solution;
            var graph = await CallGraphHelper.BuildCallGraphAsync(
                solution, document, className, methodName, maxNodes, includeExternalCallees, cancellationToken)
                .ConfigureAwait(false);

            return ToolTelemetry.TraceAndReturn(toolName, CallGraphHelper.FormatMarkdown(graph));
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(toolName, "`get_call_graph` was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetCallGraph failed for {ClassName}.{MethodName}", className, methodName);
            return ToolTelemetry.TraceAndReturn(
                toolName,
                WorkspaceLoadGuidance.FormatCaughtException(ex, $"Failed: {ex.Message}"));
        }
    }
}
