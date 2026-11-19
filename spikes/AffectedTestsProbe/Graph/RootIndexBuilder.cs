using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AffectedTestsProbe;

internal sealed class RootIndexBuilder
{
    private readonly Solution _solution;
    private readonly ProjectMap _map;
    private readonly IReadOnlyList<string> _testProjects;
    private readonly List<string> _limitations = new();
    private readonly List<UnmappedType> _unmapped = new();
    private readonly List<TestClassModel> _classes = new();

    public RootIndexBuilder(Solution solution, ProjectMap map, IReadOnlyList<string> testProjects)
    {
        _solution = solution;
        _map = map;
        _testProjects = testProjects;
    }

    public static async Task<RootIndex> BuildAsync(
        Solution solution,
        ProjectMap map,
        IReadOnlyList<string> testProjects,
        CancellationToken cancellationToken)
    {
        var builder = new RootIndexBuilder(solution, map, testProjects);
        await builder.CollectAsync(cancellationToken).ConfigureAwait(false);
        return builder.Freeze();
    }

    private async Task CollectAsync(CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var collections = new Dictionary<string, (ProjectId ProjectId, string DocumentationId)>(StringComparer.Ordinal);
        var binding = new ProjectCompilations(_solution);
        await binding.VisitAsync(
            bindBodies: null,
            async (project, compilation, token) =>
            {
                foreach (var tree in compilation.SyntaxTrees)
                {
                    var root = await tree.GetRootAsync(token).ConfigureAwait(false);
                    var model = compilation.GetSemanticModel(tree);
                    foreach (var declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                    {
                        if (model.GetDeclaredSymbol(declaration, token) is INamedTypeSymbol type)
                        {
                            RememberCollection(collections, project, type);
                        }
                    }
                }

                if (!NameMatches(project, _testProjects))
                {
                    return;
                }

                foreach (var tree in compilation.SyntaxTrees)
                {
                    var root = await tree.GetRootAsync(token).ConfigureAwait(false);
                    var model = compilation.GetSemanticModel(tree);
                    foreach (var declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                    {
                        if (model.GetDeclaredSymbol(declaration, token) is not INamedTypeSymbol type)
                        {
                            continue;
                        }

                        var id = type.OriginalDefinition.GetDocumentationCommentId();
                        if (type.TypeKind != TypeKind.Class || type.IsAbstract || id is null
                            || !seen.Add(project.Id + "|" + id))
                        {
                            continue;
                        }

                        AddClass(type, _map.Key(project.Id), project, compilation, binding, collections);
                    }
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static void RememberCollection(
        Dictionary<string, (ProjectId ProjectId, string DocumentationId)> collections,
        Project project,
        INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.Name != "CollectionDefinitionAttribute")
            {
                continue;
            }

            var name = attribute.ConstructorArguments.FirstOrDefault().Value as string;
            var id = type.GetDocumentationCommentId();
            if (name is not null && id is not null && !collections.ContainsKey(name))
            {
                collections.Add(name, (project.Id, id));
            }
        }
    }

    private void AddClass(
        INamedTypeSymbol type,
        ProjectKey project,
        Project source,
        Compilation compilation,
        ProjectCompilations binding,
        Dictionary<string, (ProjectId ProjectId, string DocumentationId)> collections)
    {
        var tests = TestsOf(type);
        if (tests.Count == 0)
        {
            return;
        }

        var runtime = RuntimeNames.TryName(type, out var reason);
        if (runtime is null)
        {
            _unmapped.Add(new UnmappedType(project, type.ToDisplayString(), reason ?? "No runtime name."));
            return;
        }

        var model = new TestClassModel(project, runtime);
        foreach (var (method, node) in tests)
        {
            var unit = new TestUnit(project, runtime, method.Name, node);
            model.Units.Add(unit);
            AddDataRoots(method, unit, model);
        }

        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            AddLifecycle(current, model.ClassWideRoots);
        }

        AddFixtures(type, model.ClassWideRoots, source, compilation, binding, collections);
        _classes.Add(model);
    }

    private List<(IMethodSymbol Method, MemberNode Node)> TestsOf(INamedTypeSymbol type)
    {
        var result = new List<(IMethodSymbol Method, MemberNode Node)>();
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.MethodKind != MethodKind.Ordinary || !seen.Add(method.OriginalDefinition))
                {
                    continue;
                }

                if (!TestMarkers.IsTestMethod(method.OriginalDefinition))
                {
                    continue;
                }

                if (SymbolIds.TryNode(method.OriginalDefinition, _solution, _map.Keys) is { } node)
                {
                    result.Add((method.OriginalDefinition, node));
                }
            }
        }

        return result;
    }

    private void AddLifecycle(INamedTypeSymbol type, List<MemberNode> roots)
    {
        foreach (var constructor in type.InstanceConstructors)
        {
            AddNode(roots, constructor);
        }

        foreach (var member in type.GetMembers())
        {
            switch (member)
            {
                case IFieldSymbol field when !field.IsStatic && !field.IsConst && HasInitializer(field):
                    AddNode(roots, field);
                    break;
                case IPropertySymbol property when !property.IsStatic && HasInitializer(property):
                    AddNode(roots, property);
                    break;
                case IMethodSymbol method when IsLifecycleMethod(method):
                    AddNode(roots, method);
                    break;
            }
        }
    }

    private void AddFixtures(
        INamedTypeSymbol testClass,
        List<MemberNode> roots,
        Project source,
        Compilation compilation,
        ProjectCompilations binding,
        Dictionary<string, (ProjectId ProjectId, string DocumentationId)> collections)
    {
        foreach (var iface in testClass.AllInterfaces)
        {
            if (iface.Name == "IClassFixture"
                && iface.TypeArguments.Length == 1
                && iface.TypeArguments[0] is INamedTypeSymbol fixture)
            {
                AddFixture(fixture, roots);
            }
        }

        foreach (var attribute in testClass.GetAttributes())
        {
            if (attribute.AttributeClass?.Name != "CollectionAttribute")
            {
                continue;
            }

            var name = attribute.ConstructorArguments.FirstOrDefault().Value as string;
            if (name is null)
            {
                continue;
            }

            if (!collections.TryGetValue(name, out var defined))
            {
                _limitations.Add("Collection fixture definition was not found: " + name);
                continue;
            }

            var definition = CollectionType(defined, source, compilation, binding);
            if (definition is null)
            {
                _limitations.Add("Collection fixture definition was not found: " + name);
                continue;
            }

            foreach (var iface in definition.AllInterfaces)
            {
                if (iface.Name == "ICollectionFixture"
                    && iface.TypeArguments.Length == 1
                    && iface.TypeArguments[0] is INamedTypeSymbol fixture)
                {
                    AddFixture(fixture, roots);
                }
            }
        }
    }

    private static INamedTypeSymbol? CollectionType(
        (ProjectId ProjectId, string DocumentationId) defined,
        Project source,
        Compilation compilation,
        ProjectCompilations binding)
    {
        var target = defined.ProjectId == source.Id ? compilation : binding.Kept(defined.ProjectId);
        return target is null
            ? null
            : DocumentationCommentId.GetFirstSymbolForDeclarationId(defined.DocumentationId, target) as INamedTypeSymbol;
    }

    private void AddFixture(INamedTypeSymbol fixture, List<MemberNode> roots)
    {
        // The fixture's own IClassFixture is not a root of the test class. Walking it would also cycle.
        for (var current = fixture; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            AddLifecycle(current, roots);
        }
    }

    private void AddDataRoots(IMethodSymbol method, TestUnit unit, TestClassModel model)
    {
        foreach (var attribute in method.GetAttributes())
        {
            var kind = TestMarkers.DataAttributeName(attribute.AttributeClass);
            if (kind is null)
            {
                continue;
            }

            if (!model.DataRoots.TryGetValue(unit, out var roots))
            {
                roots = new List<MemberNode>();
                model.DataRoots.Add(unit, roots);
            }

            foreach (var member in ResolveDataMembers(method, attribute, kind))
            {
                AddNode(roots, member);
            }
        }
    }

    private IEnumerable<ISymbol> ResolveDataMembers(IMethodSymbol method, AttributeData attribute, string kind)
    {
        if (kind == "ClassData")
        {
            return DataClass(attribute.ConstructorArguments.FirstOrDefault().Value as INamedTypeSymbol, method);
        }

        var owner = NamedType(attribute, "MemberType") ?? NamedType(attribute, "SourceType") ?? method.ContainingType;
        if (attribute.ConstructorArguments.FirstOrDefault().Value is string name)
        {
            var found = FindMember(owner, name);
            if (found is null)
            {
                _limitations.Add("Data source " + name + " was not found for " + method.ToDisplayString() + ".");
                return [];
            }

            return [found];
        }

        if (attribute.ConstructorArguments.FirstOrDefault().Value is INamedTypeSymbol dataClass)
        {
            return DataClass(dataClass, method);
        }

        _limitations.Add("Data source did not bind for " + method.ToDisplayString() + ".");
        return [];
    }

    private IEnumerable<ISymbol> DataClass(INamedTypeSymbol? dataClass, IMethodSymbol method)
    {
        if (dataClass is null)
        {
            _limitations.Add("Class data type did not bind for " + method.ToDisplayString() + ".");
            return [];
        }

        return dataClass.InstanceConstructors.Cast<ISymbol>()
            .Concat(dataClass.GetMembers("GetEnumerator").OfType<IMethodSymbol>());
    }

    private static INamedTypeSymbol? NamedType(AttributeData attribute, string name) =>
        attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value as INamedTypeSymbol;

    private static ISymbol? FindMember(INamedTypeSymbol owner, string name)
    {
        for (var current = owner; current is not null; current = current.BaseType)
        {
            var found = current.GetMembers(name).FirstOrDefault(member => member is IMethodSymbol or IPropertySymbol or IFieldSymbol);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static bool IsLifecycleMethod(IMethodSymbol method)
    {
        if (method.GetAttributes().Any(attribute => TestMarkers.IsLifecycleAttribute(attribute.AttributeClass)))
        {
            return true;
        }

        if (method.Parameters.Length != 0 || method.IsStatic || method.IsGenericMethod)
        {
            return false;
        }

        var overloads = method.ContainingType.GetMembers(method.Name).OfType<IMethodSymbol>()
            .Count(candidate => candidate.MethodKind == MethodKind.Ordinary);
        if (overloads > 1 && method.ExplicitInterfaceImplementations.Length == 0)
        {
            return false;
        }

        return method.Name switch
        {
            "InitializeAsync" => Implements(method.ContainingType, "IAsyncLifetime"),
            "DisposeAsync" => Implements(method.ContainingType, "IAsyncLifetime")
                || Implements(method.ContainingType, "IAsyncDisposable"),
            "Dispose" => Implements(method.ContainingType, "IDisposable"),
            _ => false,
        };
    }

    private static bool Implements(INamedTypeSymbol type, string interfaceName) =>
        type.AllInterfaces.Any(iface => iface.Name == interfaceName);

    private static bool HasInitializer(ISymbol symbol) =>
        symbol.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is VariableDeclaratorSyntax { Initializer: not null }
            or PropertyDeclarationSyntax { Initializer: not null });

    private void AddNode(List<MemberNode> nodes, ISymbol symbol)
    {
        if (SymbolIds.TryNode(symbol, _solution, _map.Keys) is { } node && !nodes.Contains(node))
        {
            nodes.Add(node);
        }
    }

    private RootIndex Freeze()
    {
        var unitsByRoot = new Dictionary<MemberNode, HashSet<TestUnit>>();
        foreach (var testClass in _classes)
        {
            foreach (var root in testClass.ClassWideRoots)
            {
                AddRoot(unitsByRoot, root, testClass.Units);
            }

            foreach (var unit in testClass.Units)
            {
                AddRoot(unitsByRoot, unit.Method, [unit]);
                if (testClass.DataRoots.TryGetValue(unit, out var data))
                {
                    foreach (var root in data)
                    {
                        AddRoot(unitsByRoot, root, [unit]);
                    }
                }
            }
        }

        var frozen = unitsByRoot.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<TestUnit>)pair.Value.ToArray());
        return new RootIndex(_classes, frozen, _limitations, _unmapped);
    }

    public static bool NameMatches(Project project, IReadOnlyList<string> filters)
    {
        if (filters.Count == 0)
        {
            return true;
        }

        var (stripped, _) = ProjectNameSuffix.Split(project.Name);
        return filters.Any(filter =>
            string.Equals(filter, project.Name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(filter, stripped, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddRoot(Dictionary<MemberNode, HashSet<TestUnit>> map, MemberNode node, IEnumerable<TestUnit> units)
    {
        if (!map.TryGetValue(node, out var set))
        {
            set = new HashSet<TestUnit>();
            map.Add(node, set);
        }

        foreach (var unit in units)
        {
            set.Add(unit);
        }
    }
}
