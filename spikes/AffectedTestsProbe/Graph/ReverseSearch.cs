using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace AffectedTestsProbe;

/// <summary>
/// Walks predecessors through <see cref="SymbolFinder"/> and the inverse edge rules.
/// It does not take a <see cref="FactGraph"/>: the forward graph is neither an index nor a cache.
/// </summary>
internal static class ReverseSearch
{
    public static Task<ReverseOutcome> SearchAsync(
        Solution solution,
        ProjectMap map,
        RootIndex roots,
        MemberNode start,
        ProbeLimits limits,
        CancellationToken cancellationToken) =>
        new Search(solution, map, roots, limits, cancellationToken).RunAsync([start]);

    /// <summary>
    /// One walk from several changed members, as an edit of a whole file would start it.
    /// The members share one visited set and one set of limits.
    /// </summary>
    public static Task<ReverseOutcome> SearchAsync(
        Solution solution,
        ProjectMap map,
        RootIndex roots,
        IReadOnlyCollection<MemberNode> starts,
        ProbeLimits limits,
        CancellationToken cancellationToken) =>
        new Search(solution, map, roots, limits, cancellationToken).RunAsync(starts);

    private sealed class Search
    {
        private readonly Solution _solution;
        private readonly ProjectMap _map;
        private readonly RootIndex _roots;
        private readonly ProbeLimits _limits;
        private readonly CancellationToken _cancellation;
        private readonly Dictionary<SyntaxTree, SemanticModel?> _models = new();
        private readonly HashSet<MemberNode> _visited = new();
        private readonly Queue<(MemberNode Node, int Depth)> _queue = new();
        private ReverseOutcome _outcome = new();

        public Search(Solution solution, ProjectMap map, RootIndex roots, ProbeLimits limits, CancellationToken cancellation)
        {
            _solution = solution;
            _map = map;
            _roots = roots;
            _limits = limits;
            _cancellation = cancellation;
        }

        public async Task<ReverseOutcome> RunAsync(IEnumerable<MemberNode> starts)
        {
            var watch = Stopwatch.StartNew();
            _outcome = new ReverseOutcome();
            foreach (var start in starts)
            {
                _queue.Enqueue((start, 0));
            }

            while (_queue.Count > 0 && _outcome.Limit is null)
            {
                _cancellation.ThrowIfCancellationRequested();
                if (watch.Elapsed > _limits.PerMember)
                {
                    _outcome.Limit = "time";
                    break;
                }

                var (node, depth) = _queue.Dequeue();
                if (!_visited.Add(node))
                {
                    continue;
                }

                _outcome.Visited = _visited.Count;
                if (_outcome.Visited > _limits.Members)
                {
                    _outcome.Limit = "members";
                    break;
                }

                if (depth > _limits.Depth)
                {
                    _outcome.Limit = "depth";
                    break;
                }

                _outcome.Depth = Math.Max(_outcome.Depth, depth);
                if (_roots.UnitsByRoot.TryGetValue(node, out var units))
                {
                    foreach (var unit in units)
                    {
                        _outcome.Units.Add(unit);
                    }

                    // Framework roots bind to tests. Their callers are the framework, not more source.
                    continue;
                }

                var symbol = await ResolveAsync(node).ConfigureAwait(false);
                if (symbol is null)
                {
                    continue;
                }

                await ExpandReferencesAsync(symbol, depth).ConfigureAwait(false);
                if (_outcome.Limit is null)
                {
                    await ExpandSpecialAsync(symbol, depth).ConfigureAwait(false);
                }
            }

            _outcome.ElapsedMilliseconds = (long)watch.Elapsed.TotalMilliseconds;
            return _outcome;
        }

        private async Task ExpandSpecialAsync(ISymbol symbol, int depth)
        {
            if (symbol is IMethodSymbol method)
            {
                for (var current = method.OverriddenMethod; current is not null; current = current.OverriddenMethod)
                {
                    await ExpandReferencesAsync(current, depth).ConfigureAwait(false);
                }

                foreach (var implemented in InterfaceMethods(method))
                {
                    await ExpandReferencesAsync(implemented, depth).ConfigureAwait(false);
                }

                if (method is { MethodKind: MethodKind.Constructor, IsImplicitlyDeclared: true })
                {
                    await ExpandDerivedBaseCallsAsync(method, depth).ConfigureAwait(false);
                }
            }

            if (IsStaticInitializer(symbol))
            {
                await ExpandTypeUseAsync(symbol.ContainingType, depth).ConfigureAwait(false);
            }

            if (IsInstanceInitializer(symbol))
            {
                foreach (var constructor in symbol.ContainingType.InstanceConstructors)
                {
                    if (SymbolIds.TryNode(constructor, _solution, _map.Keys) is { } node)
                    {
                        Enqueue(node, depth + 1);
                    }
                }
            }
        }

        private async Task ExpandTypeUseAsync(INamedTypeSymbol type, int depth)
        {
            foreach (var member in type.GetMembers())
            {
                if (member.IsStatic && member is not IMethodSymbol { MethodKind: MethodKind.StaticConstructor })
                {
                    await ExpandReferencesAsync(member, depth).ConfigureAwait(false);
                }
            }

            foreach (var constructor in type.InstanceConstructors)
            {
                await ExpandReferencesAsync(constructor, depth).ConfigureAwait(false);
            }
        }

        private async Task ExpandDerivedBaseCallsAsync(IMethodSymbol constructor, int depth)
        {
            var derived = await SymbolFinder.FindDerivedClassesAsync(
                    constructor.ContainingType,
                    _solution,
                    transitive: true,
                    projects: null,
                    cancellationToken: _cancellation)
                .ConfigureAwait(false);
            foreach (var child in derived)
            {
                foreach (var childConstructor in child.InstanceConstructors)
                {
                    if (SymbolIds.TryNode(childConstructor, _solution, _map.Keys) is { } childNode
                        && await CallsBaseAsync(childConstructor, constructor).ConfigureAwait(false))
                    {
                        Enqueue(childNode, depth + 1);
                    }
                }
            }
        }

        private async Task<bool> CallsBaseAsync(IMethodSymbol derived, IMethodSymbol baseConstructor)
        {
            if (derived.IsImplicitlyDeclared)
            {
                return baseConstructor.Parameters.Length == 0;
            }

            if (derived.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not ConstructorDeclarationSyntax syntax)
            {
                return false;
            }

            if (syntax.Initializer is null)
            {
                return baseConstructor.Parameters.Length == 0;
            }

            if (syntax.Initializer.ThisOrBaseKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ThisKeyword))
            {
                return false;
            }

            var model = await ModelAsync(syntax.SyntaxTree).ConfigureAwait(false);
            return model?.GetOperation(syntax.Initializer) is IInvocationOperation invocation
                && SymbolEqualityComparer.Default.Equals(
                    invocation.TargetMethod.OriginalDefinition,
                    baseConstructor.OriginalDefinition);
        }

        private async Task ExpandReferencesAsync(ISymbol symbol, int depth)
        {
            if (_outcome.Limit is not null)
            {
                return;
            }

            var references = await SymbolFinder.FindReferencesAsync(symbol, _solution, _cancellation).ConfigureAwait(false);
            foreach (var reference in references)
            {
                foreach (var location in reference.Locations)
                {
                    _cancellation.ThrowIfCancellationRequested();
                    var tree = location.Location.SourceTree;
                    if (tree is null)
                    {
                        continue;
                    }

                    var node = await FindNodeAsync(tree, location.Location.SourceSpan).ConfigureAwait(false);
                    if (node is null)
                    {
                        continue;
                    }

                    if (ParameterDefault(node) is { } parameterSyntax)
                    {
                        var model = await ModelAsync(tree).ConfigureAwait(false);
                        if (model?.GetDeclaredSymbol(parameterSyntax, _cancellation) is IParameterSymbol parameter)
                        {
                            await ExpandOmittedAsync(parameter, depth).ConfigureAwait(false);
                        }

                        continue;
                    }

                    var enclosing = await EnclosingMemberAsync(tree, node).ConfigureAwait(false);
                    if (enclosing is { } member)
                    {
                        Enqueue(member, depth + 1);
                    }
                }
            }
        }

        private async Task ExpandOmittedAsync(IParameterSymbol parameter, int depth)
        {
            if (parameter.ContainingSymbol is not IMethodSymbol method)
            {
                return;
            }

            var references = await SymbolFinder.FindReferencesAsync(method, _solution, _cancellation).ConfigureAwait(false);
            foreach (var reference in references)
            {
                foreach (var location in reference.Locations)
                {
                    var sourceTree = location.Location.SourceTree;
                    if (sourceTree is null)
                    {
                        continue;
                    }

                    var node = await FindNodeAsync(sourceTree, location.Location.SourceSpan).ConfigureAwait(false);
                    if (node is null)
                    {
                        continue;
                    }

                    var model = await ModelAsync(sourceTree).ConfigureAwait(false);
                    if (model is null || InvocationAt(model, node) is not { } invocation || !Omits(invocation, parameter))
                    {
                        continue;
                    }

                    var enclosing = await EnclosingMemberAsync(sourceTree, node).ConfigureAwait(false);
                    if (enclosing is { } member)
                    {
                        Enqueue(member, depth + 1);
                    }
                }
            }
        }

        private async Task<MemberNode?> EnclosingMemberAsync(SyntaxTree tree, SyntaxNode node)
        {
            var model = await ModelAsync(tree).ConfigureAwait(false);
            if (model is null)
            {
                return null;
            }

            for (var current = node; current is not null; current = current.Parent)
            {
                if (current is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
                {
                    continue;
                }

                // Only a field's declarator is a member. A local's declarator (`var result = M();`) is inside a body:
                // stopping there gave a local symbol without a documentation id and dropped the reference silently.
                if (current is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax
                    or VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax }
                    or PropertyDeclarationSyntax or IndexerDeclarationSyntax)
                {
                    var symbol = model.GetDeclaredSymbol(current, _cancellation);
                    return symbol is null ? null : SymbolIds.TryNode(symbol, _solution, _map.Keys);
                }
            }

            return null;
        }

        private async Task<ISymbol?> ResolveAsync(MemberNode node)
        {
            if (!_map.Projects.TryGetValue(node.Project, out var project))
            {
                return null;
            }

            var compilation = await project.GetCompilationAsync(_cancellation).ConfigureAwait(false);
            return compilation is null
                ? null
                : DocumentationCommentId.GetFirstSymbolForDeclarationId(node.DocumentationId, compilation);
        }

        private async Task<SemanticModel?> ModelAsync(SyntaxTree tree)
        {
            if (_models.TryGetValue(tree, out var cached))
            {
                return cached;
            }

            var document = _solution.GetDocument(tree);
            var model = document is null ? null : await document.GetSemanticModelAsync(_cancellation).ConfigureAwait(false);
            _models.Add(tree, model);
            return model;
        }

        private async Task<SyntaxNode?> FindNodeAsync(SyntaxTree tree, TextSpan span)
        {
            var root = await tree.GetRootAsync(_cancellation).ConfigureAwait(false);
            return root.FindNode(span, getInnermostNodeForTie: true);
        }

        private void Enqueue(MemberNode node, int depth) => _queue.Enqueue((node, depth));

        private static bool Omits(IInvocationOperation invocation, IParameterSymbol parameter)
        {
            var match = invocation.Arguments.FirstOrDefault(argument =>
                SymbolEqualityComparer.Default.Equals(argument.Parameter?.OriginalDefinition, parameter.OriginalDefinition));
            return match is null || match.IsImplicit;
        }

        private static ParameterSyntax? ParameterDefault(SyntaxNode node)
        {
            for (var current = node; current is not null; current = current.Parent)
            {
                if (current is EqualsValueClauseSyntax && current.Parent is ParameterSyntax parameter)
                {
                    return parameter;
                }

                if (current is MemberDeclarationSyntax or StatementSyntax)
                {
                    return null;
                }
            }

            return null;
        }

        private static IInvocationOperation? InvocationAt(SemanticModel model, SyntaxNode node)
        {
            for (var current = node; current is not null; current = current.Parent)
            {
                if (model.GetOperation(current) is IInvocationOperation invocation)
                {
                    return invocation;
                }

                if (current is StatementSyntax or MemberDeclarationSyntax)
                {
                    return null;
                }
            }

            return null;
        }

        private static IEnumerable<IMethodSymbol> InterfaceMethods(IMethodSymbol method)
        {
            foreach (var explicitMethod in method.ExplicitInterfaceImplementations)
            {
                yield return explicitMethod;
            }

            var type = method.ContainingType;
            if (type is null)
            {
                yield break;
            }

            foreach (var iface in type.AllInterfaces)
            {
                foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
                {
                    if (type.FindImplementationForInterfaceMember(member) is IMethodSymbol implemented
                        && SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, method.OriginalDefinition))
                    {
                        yield return member;
                    }
                }
            }
        }

        private static bool IsStaticInitializer(ISymbol symbol) =>
            symbol is IMethodSymbol { MethodKind: MethodKind.StaticConstructor }
            || symbol is IFieldSymbol { IsStatic: true, IsConst: false } && HasInitializer(symbol)
            || symbol is IPropertySymbol { IsStatic: true } && HasInitializer(symbol);

        private static bool IsInstanceInitializer(ISymbol symbol) =>
            symbol is IFieldSymbol { IsStatic: false, IsConst: false } && HasInitializer(symbol)
            || symbol is IPropertySymbol { IsStatic: false } && HasInitializer(symbol);

        private static bool HasInitializer(ISymbol symbol) =>
            symbol.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is VariableDeclaratorSyntax { Initializer: not null }
                or PropertyDeclarationSyntax { Initializer: not null });
    }
}
