using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// Semantic view of a declared <see cref="SourceSetScope"/>: one <see cref="CSharpCompilation"/> built
/// from the scope sources and references, without MSBuild project loading. Symbol queries answer by
/// identity, not by name: a field, an overload or a property of a user type is a different symbol from
/// a same-named member of another type.
/// <para>
/// Every blocking condition of the input is collected in <see cref="InputDiagnostics"/> and must be
/// reported by the caller: a compilation error, a conditional directive whose inactive branch is
/// outside the declared scope, or an empty scope. Absence of a forbidden symbol is only evidence when
/// the input compiled.
/// </para>
/// </summary>
internal sealed class SourceSetAnalysis
{
    public SourceSetScope Scope { get; }

    public CSharpCompilation Compilation { get; }

    /// <summary>Blocking input conditions: compilation errors, conditional directives, an empty scope.</summary>
    public IReadOnlyList<string> InputDiagnostics { get; }

    /// <summary>True when the scope compiled without errors and contains no out-of-scope conditional branch.</summary>
    public bool IsUsable => InputDiagnostics.Count == 0;

    private readonly Dictionary<SourceFile, SyntaxTree> _trees;
    private readonly Dictionary<SyntaxTree, SourceFile> _files;
    private readonly Dictionary<SyntaxTree, SemanticModel> _models = new();

    private SourceSetAnalysis(
        SourceSetScope scope,
        CSharpCompilation compilation,
        Dictionary<SourceFile, SyntaxTree> trees,
        Dictionary<SyntaxTree, SourceFile> files,
        IReadOnlyList<string> inputDiagnostics)
    {
        Scope = scope;
        Compilation = compilation;
        _trees = trees;
        _files = files;
        InputDiagnostics = inputDiagnostics;
    }

    public static SourceSetAnalysis Create(SourceSetScope scope)
    {
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(scope.LanguageVersion)
            .WithPreprocessorSymbols(scope.PreprocessorSymbols);
        var trees = new Dictionary<SourceFile, SyntaxTree>();
        var files = new Dictionary<SyntaxTree, SourceFile>();
        foreach (var file in scope.Files)
        {
            var tree = CSharpSyntaxTree.ParseText(file.Text, parseOptions, path: file.Path);
            trees.Add(file, tree);
            files.Add(tree, file);
        }

        var compilation = CSharpCompilation.Create(
            scope.AssemblyName,
            trees.Values,
            scope.References,
            new CSharpCompilationOptions(scope.OutputKind));

        return new SourceSetAnalysis(scope, compilation, trees, files, CollectInputDiagnostics(scope, compilation));
    }

    public SyntaxTree TreeFor(SourceFile file) => _trees[file];

    public SemanticModel ModelFor(SourceFile file) => ModelFor(_trees[file]);

    public SemanticModel ModelFor(SyntaxTree tree)
    {
        if (_models.TryGetValue(tree, out var cached))
        {
            return cached;
        }

        var model = Compilation.GetSemanticModel(tree);
        _models.Add(tree, model);
        return model;
    }

    public SourceFile? FileFor(SyntaxTree tree) => _files.TryGetValue(tree, out var file) ? file : null;

    /// <summary>
    /// Type by metadata name: partial declarations of one type are merged into a single symbol, while a
    /// same-named type in another namespace stays a different type. A missing type is a diagnostic.
    /// </summary>
    public INamedTypeSymbol? FindType(string namespaceName, string typeName, List<string> diagnostics)
    {
        var metadataName = string.IsNullOrEmpty(namespaceName) ? typeName : $"{namespaceName}.{typeName}";
        var type = Compilation.GetTypeByMetadataName(metadataName);
        if (type is not null)
        {
            return type;
        }

        diagnostics.Add($"{metadataName}: type not found in the declared scope ({Scope.Description})"
            + DescribeSameNamedTypes(typeName));
        return null;
    }

    /// <summary>
    /// Declared method by name. Overloads produce a diagnostic instead of an arbitrary pick: a check must
    /// not verify a different overload than the one it names.
    /// </summary>
    public IMethodSymbol? FindDeclaredMethod(INamedTypeSymbol type, string methodName, List<string> diagnostics)
    {
        var candidates = type.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .ToList();
        if (candidates.Count == 0)
        {
            diagnostics.Add($"{type.ToDisplayString()}.{methodName}: method not found");
            return null;
        }

        if (candidates.Count > 1)
        {
            var signatures = string.Join(", ", candidates.Select(candidate => candidate.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            diagnostics.Add($"{type.ToDisplayString()}.{methodName}: {candidates.Count} declarations with this name ({signatures}); an unambiguous signature is required");
            return null;
        }

        return candidates[0];
    }

    /// <summary>
    /// Name references of the whole scope, pre-filtered by identifier text and decided by symbol
    /// binding. The text filter only narrows the candidates: a reference to a member must be written
    /// with the member name, so the filter cannot hide one, and the verdict still comes from the
    /// semantic model.
    /// </summary>
    public IReadOnlyList<SourceReference> FindReferences(string memberName)
    {
        var references = new List<SourceReference>();
        foreach (var tree in Compilation.SyntaxTrees)
        {
            var model = ModelFor(tree);
            foreach (var name in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
            {
                if (!string.Equals(name.Identifier.ValueText, memberName, StringComparison.Ordinal) || IsDeclarationName(name))
                {
                    continue;
                }

                references.Add(SourceReference.From(model, name));
            }
        }

        return references;
    }

    /// <summary>
    /// Members declared in the scope's own sources with the given name. Used by prohibitions of the
    /// form "this helper must not exist": a declaration is reported instead of being missed.
    /// </summary>
    public IReadOnlyList<ISymbol> FindDeclaredMembers(string memberName)
    {
        var declared = new List<ISymbol>();
        foreach (var tree in Compilation.SyntaxTrees)
        {
            var model = ModelFor(tree);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<MemberDeclarationSyntax>())
            {
                if (declaration is not MethodDeclarationSyntax and not PropertyDeclarationSyntax and not FieldDeclarationSyntax
                    and not EventDeclarationSyntax and not IndexerDeclarationSyntax)
                {
                    continue;
                }

                if (!DeclarationName(declaration).Equals(memberName, StringComparison.Ordinal))
                {
                    continue;
                }

                var symbol = model.GetDeclaredSymbol(declaration);
                if (symbol is not null)
                {
                    declared.Add(symbol);
                }
            }
        }

        return declared;
    }

    /// <summary>Site text of a node as <c>path:line</c>, for violation messages.</summary>
    public static string Describe(SyntaxNode node)
    {
        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        return $"{node.SyntaxTree.FilePath}:{line}";
    }

    private static IReadOnlyList<string> CollectInputDiagnostics(SourceSetScope scope, CSharpCompilation compilation)
    {
        var diagnostics = new List<string>();
        if (scope.Files.Count == 0)
        {
            diagnostics.Add($"the declared scope contains no files ({scope.Description})");
        }

        foreach (var error in compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            diagnostics.Add(DescribeDiagnostic(error));
        }

        foreach (var tree in compilation.SyntaxTrees)
        {
            foreach (var directive in ConditionalDirectives(tree))
            {
                var line = directive.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                diagnostics.Add(
                    $"{tree.FilePath}:{line} conditional directive `{directive.DirectiveNameToken.Text}`: inactive branches are "
                    + $"outside the declared scope (symbols: {string.Join(", ", scope.PreprocessorSymbols)})");
            }
        }

        return diagnostics;
    }

    private static IEnumerable<DirectiveTriviaSyntax> ConditionalDirectives(SyntaxTree tree)
    {
        return tree.GetRoot()
            .DescendantNodes(descendIntoTrivia: true)
            .OfType<DirectiveTriviaSyntax>()
            .Where(directive => directive.IsActive
                && directive is IfDirectiveTriviaSyntax or ElifDirectiveTriviaSyntax or ElseDirectiveTriviaSyntax);
    }

    private static string DescribeDiagnostic(Diagnostic diagnostic)
    {
        if (!diagnostic.Location.IsInSource)
        {
            return $"compilation error {diagnostic.Id} {diagnostic.GetMessage()}";
        }

        var line = diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1;
        return $"{diagnostic.Location.SourceTree?.FilePath}:{line} compilation error {diagnostic.Id} {diagnostic.GetMessage()}";
    }

    private string DescribeSameNamedTypes(string typeName)
    {
        var sameNamed = Compilation.GetSymbolsWithName(typeName, SymbolFilter.Type)
            .OfType<INamedTypeSymbol>()
            .Where(candidate => candidate.DeclaringSyntaxReferences.Length > 0)
            .Select(candidate => candidate.ToDisplayString())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return sameNamed.Count == 0
            ? string.Empty
            : $" (declared as: {string.Join(", ", sameNamed)})";
    }

    /// <summary>A declaration name is not a reference site: the declaration is the symbol itself.</summary>
    private static bool IsDeclarationName(SimpleNameSyntax node) =>
        node.Parent is MemberDeclarationSyntax or VariableDeclaratorSyntax or LocalFunctionStatementSyntax
            or ParameterSyntax or TypeParameterSyntax or TypeDeclarationSyntax;

    private static string DeclarationName(MemberDeclarationSyntax declaration) => declaration switch
    {
        MethodDeclarationSyntax method => method.Identifier.ValueText,
        PropertyDeclarationSyntax property => property.Identifier.ValueText,
        EventDeclarationSyntax @event => @event.Identifier.ValueText,
        IndexerDeclarationSyntax => "this[]",
        FieldDeclarationSyntax field => field.Declaration.Variables.Count == 1
            ? field.Declaration.Variables[0].Identifier.ValueText
            : string.Empty,
        _ => string.Empty,
    };
}
