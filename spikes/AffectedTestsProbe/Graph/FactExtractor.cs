using Microsoft.CodeAnalysis;

namespace AffectedTestsProbe;

internal static class FactExtractor
{
    public static async Task<FactGraph> ExtractAsync(Solution solution, ProjectMap map, CancellationToken cancellationToken)
    {
        var graph = new FactGraph();
        var walker = new OperationFacts(graph, solution, map.Keys);
        await new ProjectCompilations(solution).VisitTreesAsync(
            bindBodies: null,
            (_, model, root, _) =>
            {
                walker.Walk(root, model);
                return Task.CompletedTask;
            },
            cancellationToken).ConfigureAwait(false);

        Link(graph, map.Visible);
        return graph;
    }

    private static void Link(FactGraph graph, IReadOnlyDictionary<ProjectKey, HashSet<ProjectKey>> visible)
    {
        foreach (var shape in graph.Shapes.Values)
        {
            foreach (var constructor in shape.InstanceConstructors)
            {
                if (shape.ChainsToThis.Contains(constructor))
                {
                    continue;
                }

                var fact = graph.Fact(constructor);
                foreach (var initializer in shape.InstanceInitializers)
                {
                    fact.Calls.Add(CallSite.To(initializer));
                }
            }

            // Synthesized record members are reached with the record declaration, and the other way around.
            if (shape.IsRecord && shape.Declaration is { } declaration)
            {
                foreach (var synthesized in shape.Synthesized)
                {
                    graph.Fact(synthesized).Calls.Add(CallSite.To(declaration));
                }
            }
        }

        foreach (var (member, fact) in graph.Facts.ToArray())
        {
            if (!visible.TryGetValue(member.Project, out var allowed))
            {
                continue;
            }

            foreach (var (project, typeId) in fact.StaticUses)
            {
                if (!allowed.Contains(project) || !graph.Shapes.TryGetValue((project, typeId), out var shape))
                {
                    continue;
                }

                foreach (var initializer in shape.StaticInitializers)
                {
                    if (initializer != member)
                    {
                        fact.Calls.Add(CallSite.To(initializer));
                    }
                }
            }
        }
    }
}
