using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AffectedTestsProbe;

/// <summary>
/// Frame B is chosen from source shape before either call graph exists.
/// A member is DI before virtual before static before ordinary, so an interface implementation is not also ordinary.
/// An empty DI pool is left empty.
/// </summary>
internal static class SamplePlanner
{
    private const int OrdinaryQuota = 6;
    private const int VirtualQuota = 4;
    private const int DiQuota = 5;
    private const int StaticQuota = 4;

    public static async Task<FrameBResult> FrameBAsync(
        Solution solution,
        ProjectMap map,
        IReadOnlyList<string> testProjects,
        int seed,
        CancellationToken cancellationToken)
    {
        var roots = await RootIndexBuilder.BuildAsync(solution, map, testProjects, cancellationToken).ConfigureAwait(false);
        var connected = Connected(solution, map, roots);
        var testTypes = new HashSet<string>(StringComparer.Ordinal);
        var fixtures = new HashSet<string>(StringComparer.Ordinal);
        var directNew = new HashSet<string>(StringComparer.Ordinal);
        var fixtureNew = new HashSet<string>(StringComparer.Ordinal);
        var genericRegistration = new HashSet<string>(StringComparer.Ordinal);
        var interfaceCalls = new HashSet<string>(StringComparer.Ordinal);
        var implementationCount = new Dictionary<string, int>(StringComparer.Ordinal);
        await ScanAsync(
            solution,
            map,
            roots,
            connected,
            testTypes,
            fixtures,
            directNew,
            fixtureNew,
            genericRegistration,
            interfaceCalls,
            implementationCount,
            cancellationToken).ConfigureAwait(false);

        var ordinary = new List<SampleMember>();
        var virtualMembers = new List<SampleMember>();
        var di = new List<SampleMember>();
        var stat = new List<SampleMember>();
        await new ProjectCompilations(solution).VisitTreesAsync(
            connected,
            (_, model, root, token) =>
            {
                foreach (var node in root.DescendantNodes())
                {
                    var symbol = Declared(model, node);
                    if (symbol is null || !HasBody(node) || symbol.Name.Contains('<') || IsTestOwned(symbol, testTypes, solution))
                    {
                        continue;
                    }

                    if (SymbolIds.TryNode(symbol, solution, map.Keys) is not { } member)
                    {
                        continue;
                    }

                    var display = symbol.ToDisplayString();
                    if (IsDi(symbol, directNew, interfaceCalls, solution, out var mark))
                    {
                        var reason = mark is null ? "interface or abstract call, no direct new in tests" : "di mark: " + mark;
                        if (fixtureNew.Contains(SymbolIds.TypeKey(symbol.ContainingType, solution))
                            || genericRegistration.Contains(SymbolIds.TypeKey(symbol.ContainingType, solution)))
                        {
                            reason = genericRegistration.Contains(SymbolIds.TypeKey(symbol.ContainingType, solution))
                                ? "di mark: generic-registration"
                                : "di mark: fixture";
                        }

                        di.Add(new SampleMember("di", member.Project, member.DocumentationId, display, reason));
                    }
                    else if (IsVirtual(symbol, implementationCount))
                    {
                        virtualMembers.Add(new SampleMember("virtual", member.Project, member.DocumentationId, display, "override or virtual with two implementations"));
                    }
                    else if (symbol.IsStatic)
                    {
                        stat.Add(new SampleMember("static", member.Project, member.DocumentationId, display, "static member"));
                    }
                    else if (symbol is IMethodSymbol { MethodKind: MethodKind.Ordinary or MethodKind.PropertyGet or MethodKind.PropertySet })
                    {
                        ordinary.Add(new SampleMember("ordinary", member.Project, member.DocumentationId, display, "instance method or accessor"));
                    }
                }

                return Task.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);

        var random = new Random(seed);
        var selected = new List<SampleMember>();
        selected.AddRange(Draw(ordinary, OrdinaryQuota, random));
        selected.AddRange(Draw(virtualMembers, VirtualQuota, random));
        selected.AddRange(Draw(di, DiQuota, random));
        selected.AddRange(Draw(stat, StaticQuota, random));
        return new FrameBResult(ordinary, virtualMembers, di, stat, selected, Hash(selected));
    }

    public static string Hash(IReadOnlyList<SampleMember> members)
    {
        var text = string.Join(
            "\n",
            members
                .OrderBy(member => member.DocumentationId, StringComparer.Ordinal)
                .ThenBy(member => member.Project.TargetFramework, StringComparer.Ordinal)
                .Select(member => member.Stratum + "|" + member.Project.ProjectPath + "|" + member.Project.TargetFramework + "|" + member.DocumentationId));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static async Task ScanAsync(
        Solution solution,
        ProjectMap map,
        RootIndex roots,
        HashSet<ProjectId> connected,
        HashSet<string> testTypes,
        HashSet<string> fixtures,
        HashSet<string> directNew,
        HashSet<string> fixtureNew,
        HashSet<string> genericRegistration,
        HashSet<string> interfaceCalls,
        Dictionary<string, int> implementationCount,
        CancellationToken cancellationToken)
    {
        await new ProjectCompilations(solution).VisitTreesAsync(
            connected,
            (_, model, root, token) =>
            {
                RememberTypes(solution, model, root, testTypes, fixtures, implementationCount, token);
                return Task.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);

        await new ProjectCompilations(solution).VisitTreesAsync(
            connected,
            (_, model, root, token) =>
            {
                foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
                {
                    if (model.GetTypeInfo(creation, token).Type is not INamedTypeSymbol created)
                    {
                        continue;
                    }

                    var key = SymbolIds.TypeKey(created, solution);
                    if (key.Length == 0)
                    {
                        continue;
                    }

                    var owner = Owner(model, creation, token);
                    if (owner is not null && (testTypes.Contains(SymbolIds.TypeKey(owner, solution)) || ContainingTest(owner, testTypes, solution)))
                    {
                        directNew.Add(key);
                    }
                    else if (owner is not null && fixtures.Contains(SymbolIds.TypeKey(owner, solution)))
                    {
                        fixtureNew.Add(key);
                    }
                }

                foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (model.GetSymbolInfo(invocation, token).Symbol is not IMethodSymbol method)
                    {
                        continue;
                    }

                    var containing = method.ContainingType;
                    if (containing is not null
                        && (containing.TypeKind == TypeKind.Interface || containing.IsAbstract)
                        && SymbolIds.DocumentationId(method) is { } called)
                    {
                        interfaceCalls.Add(called);
                    }

                    foreach (var argument in method.TypeArguments)
                    {
                        if (argument is INamedTypeSymbol named && named.TypeKind == TypeKind.Class)
                        {
                            var registration = SymbolIds.TypeKey(named, solution);
                            if (registration.Length > 0)
                            {
                                genericRegistration.Add(registration);
                            }
                        }
                    }
                }

                return Task.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);

        // Fixtures discovered while scanning tests are enough for the mark. roots is the scope witness.
        _ = map;
        _ = roots;
    }

    private static void RememberTypes(
        Solution solution,
        SemanticModel model,
        SyntaxNode root,
        HashSet<string> testTypes,
        HashSet<string> fixtures,
        Dictionary<string, int> implementationCount,
        CancellationToken cancellationToken)
    {
        foreach (var declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (model.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol type)
            {
                continue;
            }

            if (type.GetMembers().OfType<IMethodSymbol>().Any(TestMarkers.IsTestMethod))
            {
                for (var current = type; current is not null; current = current.ContainingType)
                {
                    var key = SymbolIds.TypeKey(current, solution);
                    if (key.Length > 0)
                    {
                        testTypes.Add(key);
                    }
                }
            }

            foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
            {
                var id = SymbolIds.DocumentationId(method);
                if (id is not null && !method.IsAbstract && method.DeclaringSyntaxReferences.Length > 0)
                {
                    Increment(implementationCount, id);
                }

                for (var overridden = method.OverriddenMethod; overridden is not null; overridden = overridden.OverriddenMethod)
                {
                    var overriddenId = SymbolIds.DocumentationId(overridden);
                    if (overriddenId is not null)
                    {
                        Increment(implementationCount, overriddenId);
                    }
                }
            }

            foreach (var iface in type.AllInterfaces)
            {
                if (iface.Name is "IClassFixture" or "ICollectionFixture"
                    && iface.TypeArguments.FirstOrDefault() is INamedTypeSymbol fixture)
                {
                    var key = SymbolIds.TypeKey(fixture, solution);
                    if (key.Length > 0)
                    {
                        fixtures.Add(key);
                    }
                }
            }
        }
    }

    private static bool IsDi(
        ISymbol symbol,
        HashSet<string> directNew,
        HashSet<string> interfaceCalls,
        Solution solution,
        out string? mark)
    {
        mark = null;
        if (symbol is not IMethodSymbol method || method.ContainingType is not { } type || !IsConcreteImplementation(type))
        {
            return false;
        }

        var typeKey = SymbolIds.TypeKey(type, solution);
        if (typeKey.Length == 0 || directNew.Contains(typeKey) || !CalledThroughSupertype(method, interfaceCalls))
        {
            return false;
        }

        return true;
    }

    private static bool CalledThroughSupertype(IMethodSymbol method, HashSet<string> interfaceCalls)
    {
        var type = method.ContainingType;
        foreach (var iface in type.AllInterfaces)
        {
            foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
            {
                if (type.FindImplementationForInterfaceMember(member) is IMethodSymbol implementation
                    && SymbolEqualityComparer.Default.Equals(implementation.OriginalDefinition, method.OriginalDefinition)
                    && SymbolIds.DocumentationId(member) is { } id
                    && interfaceCalls.Contains(id))
                {
                    return true;
                }
            }
        }

        for (var current = method.OverriddenMethod; current is not null; current = current.OverriddenMethod)
        {
            if (current.ContainingType.IsAbstract
                && SymbolIds.DocumentationId(current) is { } id
                && interfaceCalls.Contains(id))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsConcreteImplementation(INamedTypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Class || type.IsAbstract)
        {
            return false;
        }

        if (type.AllInterfaces.Any(iface => iface.SpecialType == SpecialType.None))
        {
            return true;
        }

        for (var baseType = type.BaseType; baseType is not null && baseType.SpecialType != SpecialType.System_Object; baseType = baseType.BaseType)
        {
            if (baseType.IsAbstract)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsVirtual(ISymbol symbol, Dictionary<string, int> implementationCount)
    {
        if (symbol is not IMethodSymbol method)
        {
            return false;
        }

        if (method.IsOverride)
        {
            return true;
        }

        var id = SymbolIds.DocumentationId(method);
        return method.IsVirtual && id is not null && implementationCount.TryGetValue(id, out var count) && count >= 2;
    }

    private static bool IsTestOwned(ISymbol symbol, HashSet<string> testTypes, Solution solution)
    {
        if (symbol is IMethodSymbol method && TestMarkers.IsTestMethod(method))
        {
            return true;
        }

        for (var type = symbol.ContainingType; type is not null; type = type.ContainingType)
        {
            if (testTypes.Contains(SymbolIds.TypeKey(type, solution)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainingTest(INamedTypeSymbol type, HashSet<string> testTypes, Solution solution)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (testTypes.Contains(SymbolIds.TypeKey(current, solution)))
            {
                return true;
            }
        }

        return false;
    }

    private static INamedTypeSymbol? Owner(SemanticModel model, SyntaxNode node, CancellationToken cancellationToken)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is TypeDeclarationSyntax declaration)
            {
                return model.GetDeclaredSymbol(declaration, cancellationToken) as INamedTypeSymbol;
            }
        }

        return null;
    }

    private static ISymbol? Declared(SemanticModel model, SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax method => model.GetDeclaredSymbol(method),
        ConstructorDeclarationSyntax constructor => model.GetDeclaredSymbol(constructor),
        AccessorDeclarationSyntax accessor => model.GetDeclaredSymbol(accessor),
        VariableDeclaratorSyntax variable => model.GetDeclaredSymbol(variable),
        PropertyDeclarationSyntax property when property.Initializer is not null || property.ExpressionBody is not null => model.GetDeclaredSymbol(property),
        _ => null,
    };

    private static bool HasBody(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax method => method.Body is not null || method.ExpressionBody is not null,
        ConstructorDeclarationSyntax constructor => constructor.Body is not null || constructor.ExpressionBody is not null,
        AccessorDeclarationSyntax accessor => accessor.Body is not null || accessor.ExpressionBody is not null,
        VariableDeclaratorSyntax { Initializer.Value: not InitializerExpressionSyntax and not null } => true,
        PropertyDeclarationSyntax property => property.ExpressionBody is not null
            || property.Initializer?.Value is not null and not InitializerExpressionSyntax,
        _ => false,
    };

    private static HashSet<ProjectId> Connected(Solution solution, ProjectMap map, RootIndex roots)
    {
        var graph = solution.GetProjectDependencyGraph();
        var connected = new HashSet<ProjectId>();
        foreach (var testClass in roots.Classes)
        {
            if (!map.Projects.TryGetValue(testClass.Project, out var project))
            {
                continue;
            }

            connected.Add(project.Id);
            foreach (var dependency in graph.GetProjectsThatThisProjectTransitivelyDependsOn(project.Id))
            {
                connected.Add(dependency);
            }
        }

        return connected;
    }

    private static void Increment(Dictionary<string, int> counts, string id) =>
        counts[id] = counts.TryGetValue(id, out var count) ? count + 1 : 1;

    private static List<SampleMember> Draw(List<SampleMember> pool, int quota, Random random)
    {
        var ordered = pool.OrderBy(member => member.DocumentationId, StringComparer.Ordinal).ToList();
        for (var index = ordered.Count - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (ordered[index], ordered[swap]) = (ordered[swap], ordered[index]);
        }

        return ordered.Take(Math.Min(quota, ordered.Count)).ToList();
    }
}
