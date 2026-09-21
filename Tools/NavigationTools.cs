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
    private readonly SolutionManager _solutionManager;
    private readonly ILogger<NavigationTools> _logger;

    public NavigationTools(SolutionManager solutionManager, ILogger<NavigationTools> logger)
    {
        _solutionManager = solutionManager;
        _logger = logger;
    }

    [McpServerTool(Name = "find_symbol_references", Title = "Find symbol references")]
    [Description(
        "Finds semantic references. Requires load_workspace. "
        + "No filePath: solution-wide name/FQN. "
        + "filePath alone: unique declaration (case-sensitive; not operator/indexer/local function); several → FQN+line:column error. "
        + "filePath+line (±column): positional. Optional maxResults/preview/overflowCursor. "
        + "directOnly (needs filePath): class virtual/override/abstract; cross-project FQN type match. find_usages=name alias.")]
    public async Task<string> FindSymbolReferences(
        [Description("Simple name or exact FQN (no global::, no ()). Solution-wide case-insensitive; file without line ordinal; +line auto-column needle.")]
        string symbolName,
        [Description("Optional .cs. Omit for solution-wide search. Required with line. Without line: unique declaration (error if several).")]
        string? filePath = null,
        [Description("Optional 1-based line. Requires filePath. Resolves the symbol at that position.")]
        int? line = null,
        [Description("Optional 1-based column. Omit with line to pick the unique matching identifier on that line.")]
        int? column = null,
        [Description("Listing cap 1–500. Default 50 or ROSLYN_MCP_MAX_RESULTS; explicit arg wins.")]
        int? maxResults = null,
        [Description("True: append source line (max 400 chars). Default false: path:line:col only.")]
        bool preview = false,
        [Description("Next in-memory overflow chunk; does not start a new search.")]
        string? overflowCursor = null,
        [Description("True: direct calls only (class virtual/override/abstract). Needs filePath.")]
        bool directOnly = false,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(FindSymbolReferences);

        try
        {
            if (!string.IsNullOrWhiteSpace(overflowCursor))
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    NavigationListingHelper.FormatOverflowChunkResponse(
                        NavigationOverflowStore.TryTakeChunk(overflowCursor)));
            }

            if (string.IsNullOrWhiteSpace(symbolName))
            {
                return ToolTelemetry.TraceAndReturn(toolName, "Symbol name is empty.");
            }

            var trimmedName = symbolName.Trim();
            var resolvedMax = NavigationListingHelper.ResolveMaxResults(maxResults);
            var hasFilePath = !string.IsNullOrWhiteSpace(filePath);

            var directOnlyGuard = VirtualReferenceClassifier.ValidateDirectOnlyRequiresFilePath(directOnly, hasFilePath);
            if (directOnlyGuard is not null)
            {
                return ToolTelemetry.TraceAndReturn(toolName, directOnlyGuard);
            }

            if (line is not null && !hasFilePath)
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    "Error: `line` requires `filePath`. Pass the .cs file containing that line, or omit both for name/FQN search.");
            }

            if (!hasFilePath)
            {
                return await FindReferencesByNameAsync(
                        toolName,
                        trimmedName,
                        preview,
                        resolvedMax,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var fullPath = _solutionManager.ResolvePathAgainstWorkspace(filePath!);
            var document = await _solutionManager.FindDocumentAsync(fullPath, cancellationToken);
            if (document is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    $"Could not resolve Roslyn document for file: `{fullPath}`.");
            }

            var publishedDocument = document;
            var solution = await _solutionManager.GetSanitizedPublishedSolutionAsync(cancellationToken).ConfigureAwait(false)
                ?? publishedDocument.Project.Solution;
            document = solution.GetDocument(publishedDocument.Id) ?? publishedDocument;
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
            if (semanticModel is null || syntaxRoot is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    $"Could not build semantic model for file: `{fullPath}`.");
            }

            int? positionAbsolute = null;
            if (line is int lineNumber)
            {
                var (positionSymbol, position, positionError) = await SourcePositionHelper
                    .ResolveSymbolInDocumentAsync(document, trimmedName, lineNumber, column, cancellationToken)
                    .ConfigureAwait(false);
                if (positionError is not null)
                {
                    return ToolTelemetry.TraceAndReturn(toolName, positionError);
                }

                if (positionSymbol is null || position is null)
                {
                    return ToolTelemetry.TraceAndReturn(
                        toolName,
                        $"Unable to resolve symbol `{trimmedName}` at line {lineNumber} in `{fullPath}`.");
                }

                positionAbsolute = position.AbsolutePosition;
            }
            else
            {
                var fileMatches = FileDeclarationCollector.Collect(
                    syntaxRoot,
                    semanticModel,
                    trimmedName,
                    cancellationToken);
                if (fileMatches.Count == 0)
                {
                    return ToolTelemetry.TraceAndReturn(
                        toolName,
                        FileDeclarationCollector.FormatNotFound(trimmedName, fullPath));
                }

                if (fileMatches.Count > 1)
                {
                    return ToolTelemetry.TraceAndReturn(
                        toolName,
                        FileDeclarationCollector.FormatAmbiguity(trimmedName, fullPath, fileMatches));
                }
            }

            ISymbol? requestedSymbol = null;
            var (references, _) = await WorkspaceAnalyzerSanitizer.WithSanitizedRetryAsync(
                async sol =>
                {
                    ISymbol mappedSymbol;
                    if (positionAbsolute is int absolutePosition)
                    {
                        mappedSymbol = await RemapSymbolAtPositionAsync(
                            sol,
                            publishedDocument.Id,
                            absolutePosition,
                            trimmedName,
                            cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        mappedSymbol = await RemapDeclaredSymbolAsync(
                            sol,
                            publishedDocument.Id,
                            trimmedName,
                            (root, model, ct) =>
                            {
                                var matches = FileDeclarationCollector.Collect(root, model, trimmedName, ct);
                                return matches.Count == 1 ? matches[0].Symbol : null;
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    requestedSymbol = mappedSymbol;
                    return await SymbolFinder.FindReferencesAsync(mappedSymbol, sol, cancellationToken)
                        .ConfigureAwait(false);
                },
                () => _solutionManager.GetSanitizedPublishedSolution(),
                solution,
                cancellationToken).ConfigureAwait(false);
            var locations = references
                .SelectMany(r => r.Locations)
                .Where(l => l.Location.IsInSource && l.Document.FilePath is not null)
                .DistinctBy(l => (
                    l.Document.Id,
                    l.Location.SourceSpan.Start,
                    l.Location.SourceSpan.End))
                .OrderBy(l => l.Document.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(l => l.Location.GetLineSpan().StartLinePosition.Line)
                .ThenBy(l => l.Location.SourceSpan.Start)
                .ToList();

            if (locations.Count == 0)
            {
                return ToolTelemetry.TraceAndReturn(toolName, $"No usages found for `{trimmedName}`.");
            }

            if (requestedSymbol is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    $"Unable to resolve symbol `{trimmedName}` for reference classification.");
            }

            var filter = await VirtualReferenceClassifier
                .ApplyAsync(requestedSymbol, locations, directOnly, cancellationToken)
                .ConfigureAwait(false);
            if (filter.Error is not null)
            {
                return ToolTelemetry.TraceAndReturn(toolName, filter.Error);
            }

            locations = filter.Locations.ToList();

            var textByDocument = preview ? new Dictionary<DocumentId, SourceText>() : null;
            var lines = new List<string>(locations.Count);
            foreach (var location in locations)
            {
                var path = location.Document.FilePath!;
                var span = location.Location.GetLineSpan();
                var line1 = span.StartLinePosition.Line + 1;
                var col1 = span.StartLinePosition.Character + 1;
                string? previewLine = null;
                if (preview && textByDocument is not null)
                {
                    previewLine = await GetReferenceSourceLineAsync(location, textByDocument, cancellationToken)
                        .ConfigureAwait(false);
                    if (string.IsNullOrEmpty(previewLine))
                    {
                        previewLine = "(source line unavailable)";
                    }
                }

                lines.Add("- " + NavigationListingHelper.FormatLocationLine(path, line1, col1, previewLine));
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Found {locations.Count} usages for '{trimmedName}':");
            if (filter.Note is not null)
            {
                sb.AppendLine();
                sb.AppendLine(filter.Note);
            }

            sb.AppendLine();
            NavigationListingHelper.AppendCappedLines(sb, lines, resolvedMax, "location(s)");

            return ToolTelemetry.TraceAndReturn(toolName, sb.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find references for {SymbolName} in {FilePath}", symbolName, filePath);
            return ToolTelemetry.TraceAndReturn(
                toolName,
                WorkspaceLoadGuidance.FormatCaughtException(
                    ex,
                    $"Failed to find references for `{symbolName}`: {ex.Message}",
                    filePath));
        }
    }

    [McpServerTool(Name = "find_symbol_definition", Title = "Find symbol definitions in workspace")]
    [Description(
        "Finds declarations. Requires load_workspace. "
        + "Omit filePath/line: solution-wide name (case-insensitive). "
        + "filePath alone: unique declaration (case-sensitive); all in-source places (partials) "
        + "with column+full name; several → FQN+line:column error. "
        + "filePath+line (±column): positional. Optional maxResults/overflowCursor "
        + "(default 50 or ROSLYN_MCP_MAX_RESULTS, 1–500; not dropped). "
        + "Not text search.")]
    public async Task<string> FindSymbolDefinition(
        [Description("Name. Solution-wide case-insensitive; file without line ordinal; +line auto-column.")]
        string symbolName,
        [Description("Optional .cs. Omit for solution-wide. Required with line. Without line: unique declaration.")]
        string? filePath = null,
        [Description("Optional 1-based line; requires filePath.")]
        int? line = null,
        [Description("Optional 1-based column; omit to auto-pick matching identifier on the line.")]
        int? column = null,
        [Description("Cap 1–500. Default 50 or ROSLYN_MCP_MAX_RESULTS; explicit wins.")]
        int? maxResults = null,
        [Description("Next in-memory overflow chunk; no new search.")]
        string? overflowCursor = null,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(FindSymbolDefinition);

        try
        {
            if (!string.IsNullOrWhiteSpace(overflowCursor))
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    NavigationListingHelper.FormatOverflowChunkResponse(
                        NavigationOverflowStore.TryTakeChunk(overflowCursor)));
            }

            if (string.IsNullOrWhiteSpace(symbolName))
            {
                return ToolTelemetry.TraceAndReturn(toolName, "Error: `symbolName` is empty.");
            }

            var trimmedName = symbolName.Trim();
            var resolvedMax = NavigationListingHelper.ResolveMaxResults(maxResults);
            var hasFilePath = !string.IsNullOrWhiteSpace(filePath);

            if (line is not null && !hasFilePath)
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(FindSymbolDefinition),
                    "Error: `line` requires `filePath`. Pass the .cs file containing that line.");
            }

            var solution = await _solutionManager.GetSanitizedPublishedSolutionAsync(cancellationToken).ConfigureAwait(false);
            if (solution is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    nameof(FindSymbolDefinition),
                    _solutionManager.FormatNoPublishedSolutionMessage(
                        "Error: No active workspace."));
            }

            List<ISymbol> symbols;
            if (hasFilePath)
            {
                var fullPath = _solutionManager.ResolvePathAgainstWorkspace(filePath!);
                var document = await _solutionManager.FindDocumentAsync(fullPath, cancellationToken).ConfigureAwait(false);
                if (document is null)
                {
                    return ToolTelemetry.TraceAndReturn(
                        nameof(FindSymbolDefinition),
                        $"Could not resolve Roslyn document for file: `{fullPath}`.");
                }

                var publishedDocument = document;
                document = solution.GetDocument(publishedDocument.Id) ?? publishedDocument;

                if (line is int lineNumber)
                {
                    var (positionSymbol, position, positionError) = await SourcePositionHelper
                        .ResolveSymbolInDocumentAsync(document, trimmedName, lineNumber, column, cancellationToken)
                        .ConfigureAwait(false);
                    if (positionError is not null)
                    {
                        return ToolTelemetry.TraceAndReturn(nameof(FindSymbolDefinition), positionError);
                    }

                    if (positionSymbol is null || position is null)
                    {
                        return ToolTelemetry.TraceAndReturn(
                            nameof(FindSymbolDefinition),
                            $"Unable to resolve symbol `{trimmedName}` at line {lineNumber} in `{fullPath}`.");
                    }

                    var (definitionSymbol, _) = await WorkspaceAnalyzerSanitizer.WithSanitizedRetryAsync(
                        async sol =>
                        {
                            var mapped = await RemapSymbolAtPositionAsync(
                                sol,
                                publishedDocument.Id,
                                position.AbsolutePosition,
                                trimmedName,
                                cancellationToken).ConfigureAwait(false);
                            var sourceDef = await SymbolFinder.FindSourceDefinitionAsync(mapped, sol, cancellationToken)
                                .ConfigureAwait(false);
                            return sourceDef ?? mapped;
                        },
                        () => _solutionManager.GetSanitizedPublishedSolution(),
                        solution,
                        cancellationToken).ConfigureAwait(false);

                    symbols = [definitionSymbol];
                }
                else
                {
                    var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                    var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                    if (semanticModel is null || syntaxRoot is null)
                    {
                        return ToolTelemetry.TraceAndReturn(
                            nameof(FindSymbolDefinition),
                            $"Could not build semantic model for file: `{fullPath}`.");
                    }

                    var fileMatches = FileDeclarationCollector.Collect(
                        syntaxRoot,
                        semanticModel,
                        trimmedName,
                        cancellationToken);
                    if (fileMatches.Count == 0)
                    {
                        return ToolTelemetry.TraceAndReturn(
                            nameof(FindSymbolDefinition),
                            FileDeclarationCollector.FormatNotFound(trimmedName, fullPath));
                    }

                    if (fileMatches.Count > 1)
                    {
                        return ToolTelemetry.TraceAndReturn(
                            nameof(FindSymbolDefinition),
                            FileDeclarationCollector.FormatAmbiguity(trimmedName, fullPath, fileMatches));
                    }

                    symbols = [fileMatches[0].Symbol];
                }
            }
            else
            {
                var (declarations, resolveError) = await ResolveDeclarationsOnSanitizedAsync(
                    solution,
                    trimmedName,
                    SymbolFilter.Type | SymbolFilter.Member,
                    cancellationToken).ConfigureAwait(false);

                if (resolveError is not null)
                {
                    return ToolTelemetry.TraceAndReturn(
                        nameof(FindSymbolDefinition),
                        _solutionManager.WithDiskSyncNotes(resolveError));
                }

                symbols = declarations.ToList();
                if (symbols.Count == 0)
                {
                    return ToolTelemetry.TraceAndReturn(
                        nameof(FindSymbolDefinition),
                        _solutionManager.WithDiskSyncNotes(
                            $"Symbol `{trimmedName}` was not found in the current solution (no matching type or member declarations)."));
                }
            }

            var locationLines = new List<string>();
            foreach (var symbol in symbols)
            {
                var sourceLocations = symbol.Locations
                    .Where(l => l.IsInSource && l.SourceTree?.FilePath is not null)
                    .ToList();
                if (sourceLocations.Count == 0)
                {
                    // Trailing blank keeps the same visual gap AppendLocation used between entries.
                    locationLines.Add(DefinitionLocationFormatter.FormatNoSourceLocations(symbol) + "\n");
                    continue;
                }

                foreach (var location in sourceLocations)
                {
                    locationLines.Add(DefinitionLocationFormatter.FormatLocation(symbol, location) + "\n");
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Found {symbols.Count} declaration symbol(s) matching `{trimmedName}`:");
            sb.AppendLine();
            NavigationListingHelper.AppendCappedLines(sb, locationLines, resolvedMax, "location(s)");

            return ToolTelemetry.TraceAndReturn(
                toolName,
                _solutionManager.WithDiskSyncNotes(sb.ToString().TrimEnd()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find definition locations for {SymbolName}", symbolName);
            return ToolTelemetry.TraceAndReturn(
                toolName,
                WorkspaceLoadGuidance.FormatCaughtException(
                    ex,
                    $"Failed to find definitions for `{symbolName}`: {ex.Message}"));
        }
    }

    [McpServerTool(Name = "find_usages", Title = "Find symbol usages across solution")]
    [Description(
        "Name-based alias of find_symbol_references without filePath. Requires load_workspace. "
        + "Solution-wide simple name or exact FQN; all matching declaration groups are returned (no primary pick). "
        + "Optional maxResults/preview/overflowCursor. For interface or base hierarchy use find_implementations.")]
    public async Task<string> FindUsages(
        [Description("Simple name (case-insensitive) or exact FQN (Namespace.Type or Namespace.Type.Member; no global::, no ()).")]
        string symbolName,
        [Description("Listing cap 1–500. Default 50 or ROSLYN_MCP_MAX_RESULTS; explicit arg wins.")]
        int? maxResults = null,
        [Description("True: append source line (max 400 chars). Default false: path:line:col only.")]
        bool preview = false,
        [Description("Next in-memory overflow chunk; does not start a new search.")]
        string? overflowCursor = null,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(FindUsages);

        try
        {
            if (!string.IsNullOrWhiteSpace(overflowCursor))
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    NavigationListingHelper.FormatOverflowChunkResponse(
                        NavigationOverflowStore.TryTakeChunk(overflowCursor)));
            }

            if (string.IsNullOrWhiteSpace(symbolName))
            {
                return ToolTelemetry.TraceAndReturn(toolName, "Error: `symbolName` is empty.");
            }

            return await FindReferencesByNameAsync(
                    toolName,
                    symbolName.Trim(),
                    preview,
                    NavigationListingHelper.ResolveMaxResults(maxResults),
                    cancellationToken)
                .ConfigureAwait(false);
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
        "Finds interface implementations or derived types. Requires load_workspace. "
        + "Same short name: section per base; exact FQN: one type; FQN miss: resolver text. "
        + "Optional maxResults/preview/overflowCursor. Not find_usages or text search.")]
    public async Task<string> FindImplementations(
        [Description("Simple name (section per match) or exact FQN (one type; no global::).")]
        string symbolName,
        [Description("When true (default), include indirect implementations and derived types.")]
        bool transitive = true,
        [Description("Listing cap 1–500. Default 50 or ROSLYN_MCP_MAX_RESULTS; explicit arg wins.")]
        int? maxResults = null,
        [Description("True: append source line (max 400 chars). Default false: path:line:col only.")]
        bool preview = false,
        [Description("Next in-memory overflow chunk; does not start a new search.")]
        string? overflowCursor = null,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(FindImplementations);

        try
        {
            if (!string.IsNullOrWhiteSpace(overflowCursor))
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    NavigationListingHelper.FormatOverflowChunkResponse(
                        NavigationOverflowStore.TryTakeChunk(overflowCursor)));
            }

            if (string.IsNullOrWhiteSpace(symbolName))
            {
                return ToolTelemetry.TraceAndReturn(toolName, "Error: `symbolName` is empty.");
            }

            var resolvedMax = NavigationListingHelper.ResolveMaxResults(maxResults);
            var solution = await _solutionManager.GetSanitizedPublishedSolutionAsync(cancellationToken).ConfigureAwait(false);
            if (solution is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    _solutionManager.FormatNoPublishedSolutionMessage(
                        "Error: No active workspace."));
            }

            var trimmedName = symbolName.Trim();
            var (declarations, resolveError) = await ResolveDeclarationsOnSanitizedAsync(
                solution,
                trimmedName,
                SymbolFilter.Type,
                cancellationToken).ConfigureAwait(false);

            if (resolveError is not null)
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    _solutionManager.WithDiskSyncNotes(resolveError));
            }

            var typeSymbols = declarations
                .OfType<INamedTypeSymbol>()
                .Distinct(SymbolEqualityComparer.Default)
                .Cast<INamedTypeSymbol>()
                .OrderBy(t => SymbolDeclarationResolver.GetSymbolFqn(t), StringComparer.Ordinal)
                .ToList();

            if (typeSymbols.Count == 0)
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    $"No type declaration named `{trimmedName}` was found in the current solution.");
            }

            var unsupported = typeSymbols
                .Where(t => t.TypeKind is not (TypeKind.Interface or TypeKind.Class or TypeKind.Struct))
                .ToList();
            var supported = typeSymbols
                .Where(t => t.TypeKind is TypeKind.Interface or TypeKind.Class or TypeKind.Struct)
                .ToList();

            if (supported.Count == 0)
            {
                var first = unsupported[0];
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    $"Symbol `{trimmedName}` is a {first.TypeKind}; only interfaces, classes, and structs are supported.");
            }

            // Declaring locations on the resolve snapshot; remap before SymbolFinder on any retry snapshot.
            var baseDeclLocations = supported
                .Select(t => (
                    Fqn: SymbolDeclarationResolver.GetSymbolFqn(t),
                    DeclaringLocation: GetRequiredDeclaringLocation(t, solution, trimmedName)))
                .ToList();

            var (sectionResults, _) = await WorkspaceAnalyzerSanitizer.WithSanitizedRetryAsync(
                    async sol =>
                    {
                        var results = new List<(
                            string Fqn,
                            INamedTypeSymbol MappedType,
                            string SearchMode,
                            List<INamedTypeSymbol> Related)>();

                        foreach (var baseDecl in baseDeclLocations)
                        {
                            var mappedType = await RemapDeclaredSymbolAtSpanAsync<INamedTypeSymbol>(
                                sol,
                                baseDecl.DeclaringLocation.DocumentId,
                                baseDecl.DeclaringLocation.Span,
                                trimmedName,
                                cancellationToken).ConfigureAwait(false);

                            IEnumerable<INamedTypeSymbol> related;
                            string searchMode;
                            switch (mappedType.TypeKind)
                            {
                                case TypeKind.Interface:
                                    related = await SymbolFinder.FindImplementationsAsync(
                                        mappedType, sol, transitive, projects: null, cancellationToken)
                                        .ConfigureAwait(false);
                                    searchMode = ImplementationListingFormatter.GetSearchMode(
                                        TypeKind.Interface, transitive);
                                    break;
                                case TypeKind.Class:
                                case TypeKind.Struct:
                                    related = await SymbolFinder.FindDerivedClassesAsync(
                                        mappedType, sol, transitive, projects: null, cancellationToken)
                                        .ConfigureAwait(false);
                                    searchMode = ImplementationListingFormatter.GetSearchMode(
                                        mappedType.TypeKind, transitive);
                                    break;
                                default:
                                    related = [];
                                    searchMode = ImplementationListingFormatter.GetSearchMode(
                                        mappedType.TypeKind, transitive);
                                    break;
                            }

                            var ordered = related
                                .Distinct(SymbolEqualityComparer.Default)
                                .Cast<INamedTypeSymbol>()
                                .OrderBy(
                                    t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                    StringComparer.Ordinal)
                                .ToList();

                            results.Add((baseDecl.Fqn, mappedType, searchMode, ordered));
                        }

                        return results;
                    },
                    () => _solutionManager.GetSanitizedPublishedSolution(),
                    solution,
                    cancellationToken)
                .ConfigureAwait(false);

            var textByDocument = preview ? new Dictionary<DocumentId, SourceText>() : null;
            var sections = new List<ImplementationListingFormatter.BaseSection>(sectionResults.Count);
            foreach (var (fqn, mappedType, searchMode, related) in sectionResults)
            {
                var relatedLines = new List<ImplementationListingFormatter.RelatedTypeLine>(related.Count);
                foreach (var type in related)
                {
                    string? previewLine = null;
                    if (preview && textByDocument is not null)
                    {
                        var location = type.Locations.FirstOrDefault(
                            l => l.IsInSource && l.SourceTree?.FilePath is not null);
                        if (location is not null)
                        {
                            previewLine = await GetSourceLineAtAsync(
                                    solution,
                                    location,
                                    textByDocument,
                                    cancellationToken)
                                .ConfigureAwait(false);
                            if (string.IsNullOrEmpty(previewLine))
                            {
                                previewLine = "(source line unavailable)";
                            }
                        }
                    }

                    relatedLines.Add(ImplementationListingFormatter.ToRelatedTypeLine(type, previewLine));
                }

                sections.Add(
                    new ImplementationListingFormatter.BaseSection(
                        fqn,
                        ImplementationListingFormatter.GetTypeKindLabel(mappedType),
                        searchMode,
                        mappedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        mappedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        relatedLines));
            }

            var sb = new StringBuilder();
            if (sections.Count == 1)
            {
                var section = sections[0];
                ImplementationListingFormatter.AppendSingleTypeHeader(sb, trimmedName, section);

                if (section.RelatedTypes.Count == 0)
                {
                    sb.AppendLine($"No {section.SearchMode} were found in the solution.");
                    return ToolTelemetry.TraceAndReturn(toolName, sb.ToString().TrimEnd());
                }

                sb.AppendLine($"Found **{section.RelatedTypes.Count}** type(s):");
                sb.AppendLine();
                var lines = ImplementationListingFormatter.BuildNumberedRelatedLines(section.RelatedTypes);
                NavigationListingHelper.AppendCappedLines(sb, lines, resolvedMax, "type(s)");
                return ToolTelemetry.TraceAndReturn(toolName, sb.ToString().TrimEnd());
            }

            var totalRelated = sections.Sum(s => s.RelatedTypes.Count);
            ImplementationListingFormatter.AppendMultiTypeHeader(sb, trimmedName, sections.Count, totalRelated);
            var multiLines = ImplementationListingFormatter.BuildMultiSectionLines(sections);
            NavigationListingHelper.AppendCappedLines(sb, multiLines, resolvedMax, "type(s)");
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

    private async Task<string> FindReferencesByNameAsync(
        string toolName,
        string trimmedName,
        bool preview,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var solution = await _solutionManager.GetSanitizedPublishedSolutionAsync(cancellationToken).ConfigureAwait(false);
        if (solution is null)
        {
            return ToolTelemetry.TraceAndReturn(
                toolName,
                _solutionManager.FormatNoPublishedSolutionMessage(
                    "Error: No active workspace."));
        }

        var (symbols, resolveError) = await ResolveDeclarationsOnSanitizedAsync(
            solution,
            trimmedName,
            SymbolFilter.Type | SymbolFilter.Member,
            cancellationToken).ConfigureAwait(false);

        if (resolveError is not null)
        {
            return ToolTelemetry.TraceAndReturn(toolName, _solutionManager.WithDiskSyncNotes(resolveError));
        }

        if (symbols.Count == 0)
        {
            return ToolTelemetry.TraceAndReturn(
                toolName,
                _solutionManager.WithDiskSyncNotes(
                    $"No declarations named `{trimmedName}` were found in the current solution."));
        }

        // Capture declaring locations on the resolve snapshot; remap before SymbolFinder on any retry snapshot.
        var groups = SymbolDeclarationResolver.GroupByFqn(symbols);
        var groupDeclLocations = groups
            .Select(g => (
                Fqn: g.Key,
                Symbols: g.ToList(),
                DeclaringLocations: g.Select(s => GetRequiredDeclaringLocation(s, solution, trimmedName)).ToList()))
            .ToList();

        var (groupResults, _) = await WorkspaceAnalyzerSanitizer.WithSanitizedRetryAsync(
                async sol =>
                {
                    var results = new List<(string Fqn, IReadOnlyList<ISymbol> Symbols, List<ReferenceLocation> Locations)>();
                    foreach (var group in groupDeclLocations)
                    {
                        var locations = new List<ReferenceLocation>();
                        foreach (var declaringLocation in group.DeclaringLocations)
                        {
                            var mappedSymbol = await RemapDeclaredSymbolAtSpanAsync<ISymbol>(
                                sol,
                                declaringLocation.DocumentId,
                                declaringLocation.Span,
                                trimmedName,
                                cancellationToken).ConfigureAwait(false);
                            var references = await SymbolFinder.FindReferencesAsync(mappedSymbol, sol, cancellationToken)
                                .ConfigureAwait(false);
                            locations.AddRange(
                                references
                                    .SelectMany(r => r.Locations)
                                    .Where(l => l.Location.IsInSource && l.Document.FilePath is not null));
                        }

                        var deduped = locations
                            .DistinctBy(l => (
                                l.Document.Id,
                                l.Location.SourceSpan.Start,
                                l.Location.SourceSpan.End))
                            .OrderBy(l => l.Document.FilePath, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(l => l.Location.GetLineSpan().StartLinePosition.Line)
                            .ThenBy(l => l.Location.SourceSpan.Start)
                            .ToList();

                        results.Add((group.Fqn, group.Symbols, deduped));
                    }

                    return results;
                },
                () => _solutionManager.GetSanitizedPublishedSolution(),
                solution,
                cancellationToken)
            .ConfigureAwait(false);

        var totalLocations = groupResults.Sum(g => g.Locations.Count);
        var sb = new StringBuilder();
        sb.AppendLine($"## References for `{trimmedName}`");
        sb.AppendLine();
        sb.AppendLine($"Found **{groupResults.Count}** declaration group(s), **{totalLocations}** location(s).");
        sb.AppendLine();

        foreach (var (fqn, groupSymbols, locations) in groupResults)
        {
            var kindLabel = groupSymbols[0] switch
            {
                INamedTypeSymbol t => GetTypeKindLabel(t),
                IMethodSymbol => "method",
                IPropertySymbol => "property",
                IEventSymbol => "event",
                IFieldSymbol => "field",
                _ => groupSymbols[0].Kind.ToString().ToLowerInvariant()
            };

            sb.AppendLine(
                $"- `{fqn}` ({kindLabel}"
                + (groupSymbols.Count > 1 ? $", {groupSymbols.Count} overloads" : string.Empty)
                + $", {locations.Count} location(s))");
        }

        sb.AppendLine();

        if (totalLocations == 0)
        {
            sb.AppendLine("No in-source references were returned.");
            return ToolTelemetry.TraceAndReturn(toolName, _solutionManager.WithDiskSyncNotes(sb.ToString().TrimEnd()));
        }

        var textByDocument = preview ? new Dictionary<DocumentId, SourceText>() : null;
        var lines = new List<string>(totalLocations);

        foreach (var (fqn, _, locations) in groupResults)
        {
            foreach (var refLoc in locations)
            {
                var path = refLoc.Document.FilePath!;
                var span = refLoc.Location.GetLineSpan();
                var line1 = span.StartLinePosition.Line + 1;
                var col1 = span.StartLinePosition.Character + 1;
                string? previewLine = null;
                if (preview && textByDocument is not null)
                {
                    previewLine = await GetReferenceSourceLineAsync(refLoc, textByDocument, cancellationToken)
                        .ConfigureAwait(false);
                    if (string.IsNullOrEmpty(previewLine))
                    {
                        previewLine = "(source line unavailable)";
                    }
                }

                lines.Add($"- `{fqn}` {NavigationListingHelper.FormatLocationLine(path, line1, col1, previewLine)}");
            }
        }

        NavigationListingHelper.AppendCappedLines(sb, lines, maxResults, "location(s)");

        return ToolTelemetry.TraceAndReturn(toolName, _solutionManager.WithDiskSyncNotes(sb.ToString().TrimEnd()));
    }

    private async Task<(IReadOnlyList<ISymbol> Symbols, string? Error)> ResolveDeclarationsOnSanitizedAsync(
        Solution solution,
        string trimmedName,
        SymbolFilter filter,
        CancellationToken cancellationToken)
    {
        var (result, _) = await WorkspaceAnalyzerSanitizer.WithSanitizedRetryAsync(
            sol => SymbolDeclarationResolver.ResolveDeclarationsAsync(sol, trimmedName, filter, cancellationToken),
            () => _solutionManager.GetSanitizedPublishedSolution(),
            solution,
            cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static (DocumentId DocumentId, TextSpan Span) GetRequiredDeclaringLocation(
        ISymbol symbol,
        Solution solution,
        string symbolName)
    {
        foreach (var location in symbol.Locations)
        {
            if (!location.IsInSource || location.SourceTree is null)
            {
                continue;
            }

            var document = solution.GetDocument(location.SourceTree);
            if (document is not null)
            {
                return (document.Id, location.SourceSpan);
            }
        }

        throw new InvalidOperationException(
            $"Symbol `{symbolName}` has no in-source declaring document in the current solution snapshot.");
    }

    private static async Task<ISymbol> RemapDeclaredSymbolAsync(
        Solution solution,
        DocumentId documentId,
        string symbolName,
        Func<SyntaxNode, SemanticModel, CancellationToken, ISymbol?> resolve,
        CancellationToken cancellationToken)
    {
        var mapped = solution.GetDocument(documentId)
            ?? throw new InvalidOperationException(
                $"Declaring document for `{symbolName}` is not in the sanitized solution snapshot.");
        var root = await mapped.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Could not build syntax tree for `{symbolName}` in the sanitized solution snapshot.");
        var model = await mapped.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Could not build semantic model for `{symbolName}` in the sanitized solution snapshot.");
        return resolve(root, model, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Symbol `{symbolName}` was not found in the sanitized solution snapshot.");
    }

    private static async Task<ISymbol> RemapSymbolAtPositionAsync(
        Solution solution,
        DocumentId documentId,
        int absolutePosition,
        string symbolName,
        CancellationToken cancellationToken)
    {
        var mapped = solution.GetDocument(documentId)
            ?? throw new InvalidOperationException(
                $"Document for `{symbolName}` is not in the sanitized solution snapshot.");
        var root = await mapped.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Could not build syntax tree for `{symbolName}` in the sanitized solution snapshot.");
        var model = await mapped.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Could not build semantic model for `{symbolName}` in the sanitized solution snapshot.");
        return SourcePositionHelper.GetSymbolAtPosition(model, root, absolutePosition, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Symbol `{symbolName}` was not found at the requested position in the sanitized solution snapshot.");
    }

    private static async Task<TSymbol> RemapDeclaredSymbolAtSpanAsync<TSymbol>(
        Solution solution,
        DocumentId documentId,
        TextSpan span,
        string symbolName,
        CancellationToken cancellationToken)
        where TSymbol : class, ISymbol
    {
        var mapped = solution.GetDocument(documentId)
            ?? throw new InvalidOperationException(
                $"Declaring document for `{symbolName}` is not in the sanitized solution snapshot.");
        var root = await mapped.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Could not build syntax tree for `{symbolName}` in the sanitized solution snapshot.");
        var model = await mapped.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Could not build semantic model for `{symbolName}` in the sanitized solution snapshot.");
        var node = root.FindNode(span, getInnermostNodeForTie: true);
        for (var current = node; current is not null; current = current.Parent)
        {
            if (model.GetDeclaredSymbol(current, cancellationToken) is TSymbol symbol)
            {
                return symbol;
            }
        }

        throw new InvalidOperationException(
            $"Symbol `{symbolName}` was not found in the sanitized solution snapshot.");
    }

    private static string GetTypeKindLabel(INamedTypeSymbol type) =>
        ImplementationListingFormatter.GetTypeKindLabel(type);

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

        return GetSourceLineFromText(text, refLoc.Location.GetLineSpan().StartLinePosition.Line);
    }

    private static async Task<string> GetSourceLineAtAsync(
        Solution solution,
        Location location,
        Dictionary<DocumentId, SourceText> textByDocument,
        CancellationToken cancellationToken)
    {
        if (location.SourceTree is null)
        {
            return string.Empty;
        }

        var document = solution.GetDocument(location.SourceTree);
        if (document is null)
        {
            return string.Empty;
        }

        if (!textByDocument.TryGetValue(document.Id, out var text))
        {
            text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            textByDocument[document.Id] = text;
        }

        return GetSourceLineFromText(text, location.GetLineSpan().StartLinePosition.Line);
    }

    private static string GetSourceLineFromText(SourceText text, int lineIndex)
    {
        if (lineIndex < 0 || lineIndex >= text.Lines.Count)
        {
            return string.Empty;
        }

        return NavigationListingHelper.TruncatePreview(text.Lines[lineIndex].ToString());
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
    [Description(
        "Callers/callees. Requires load_workspace. Overloads: line+column (not first).")]
    public async Task<string> GetCallGraph(
        [Description(".cs file with the method.")] string filePath,
        [Description("Containing class.")] string className,
        [Description("Method name.")] string methodName,
        [Description("Cap per list.")] int maxNodes = 25,
        [Description("Include external callees.")] bool includeExternalCallees = false,
        [Description("1-based; with column.")] int? line = null,
        [Description("1-based; with line.")] int? column = null,
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

            var publishedDocument = document;
            var solution = await _solutionManager.GetSanitizedPublishedSolutionAsync(cancellationToken).ConfigureAwait(false)
                ?? publishedDocument.Project.Solution;
            var (build, _) = await WorkspaceAnalyzerSanitizer.WithSanitizedRetryAsync(
                    sol =>
                    {
                        var mapped = sol.GetDocument(publishedDocument.Id)
                            ?? throw new InvalidOperationException(
                                $"Document `{fullPath}` is not in the sanitized solution snapshot.");
                        return CallGraphHelper.TryBuildCallGraphAsync(
                            sol,
                            mapped,
                            className,
                            methodName,
                            maxNodes,
                            includeExternalCallees,
                            line,
                            column,
                            cancellationToken);
                    },
                    () => _solutionManager.GetSanitizedPublishedSolution(),
                    solution,
                    cancellationToken)
                .ConfigureAwait(false);

            if (build.Error is not null || build.Graph is null)
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    build.Error ?? $"Failed to build call graph for `{className}.{methodName}`.");
            }

            return ToolTelemetry.TraceAndReturn(toolName, CallGraphHelper.FormatMarkdown(build.Graph));
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
