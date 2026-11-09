namespace AffectedTestsProbe;

internal static class ForwardReachability
{
    public static Reachability Compute(FactGraph facts, RootIndex roots, ProjectMap map)
    {
        var reach = new Reachability();
        foreach (var testClass in roots.Classes)
        {
            var (created, mocked, fellBack) = Fix(testClass, facts, map);
            if (fellBack)
            {
                reach.Limitations.Add("RTA did not converge for " + testClass.RuntimeName + "; CHA was used for that class.");
            }

            foreach (var unit in testClass.Units)
            {
                var starts = Starts(testClass, unit);
                Record(unit, starts, DispatchKind.Cha, created, mocked, facts, map, reach.Cha, reach.ShortestCha, reach, recordFallback: false);
                Record(
                    unit,
                    starts,
                    fellBack ? DispatchKind.Cha : DispatchKind.Rta,
                    created,
                    mocked,
                    facts,
                    map,
                    reach.Rta,
                    reach.ShortestRta,
                    reach,
                    recordFallback: !fellBack);
            }
        }

        return reach;
    }

    private static List<MemberNode> Starts(TestClassModel testClass, TestUnit unit)
    {
        var starts = new List<MemberNode>(testClass.ClassWideRoots.Count + 4);
        starts.AddRange(testClass.ClassWideRoots);
        starts.Add(unit.Method);
        if (testClass.DataRoots.TryGetValue(unit, out var data))
        {
            starts.AddRange(data);
        }

        return starts;
    }

    private static (HashSet<string> Created, HashSet<string> Mocked, bool FellBack) Fix(
        TestClassModel testClass,
        FactGraph facts,
        ProjectMap map)
    {
        var starts = new List<MemberNode>(testClass.ClassWideRoots);
        foreach (var unit in testClass.Units)
        {
            starts.Add(unit.Method);
            if (testClass.DataRoots.TryGetValue(unit, out var data))
            {
                starts.AddRange(data);
            }
        }

        var created = new HashSet<string>(StringComparer.Ordinal);
        var mocked = new HashSet<string>(StringComparer.Ordinal);
        HashSet<MemberNode>? previous = null;
        var seen = new List<HashSet<MemberNode>>();
        for (var iteration = 0; iteration < 32; iteration++)
        {
            var reachable = Walk(starts, DispatchKind.Rta, created, mocked, facts, map, limitations: null);
            var nextCreated = new HashSet<string>(StringComparer.Ordinal);
            var nextMocked = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in reachable)
            {
                if (!facts.Facts.TryGetValue(member, out var fact))
                {
                    continue;
                }

                nextCreated.UnionWith(fact.CreatedTypeKeys);
                nextMocked.UnionWith(fact.MockedTypeKeys);
            }

            if (previous is not null
                && previous.SetEquals(reachable)
                && nextCreated.SetEquals(created)
                && nextMocked.SetEquals(mocked))
            {
                return (created, mocked, false);
            }

            if (seen.Any(set => set.SetEquals(reachable)) && !nextCreated.SetEquals(created))
            {
                return (created, mocked, true);
            }

            seen.Add(reachable);
            previous = reachable;
            created = nextCreated;
            mocked = nextMocked;
        }

        return (created, mocked, true);
    }

    private static void Record(
        TestUnit unit,
        IReadOnlyList<MemberNode> starts,
        DispatchKind kind,
        IReadOnlySet<string> created,
        IReadOnlySet<string> mocked,
        FactGraph facts,
        ProjectMap map,
        Dictionary<MemberNode, HashSet<TestUnit>> units,
        Dictionary<MemberNode, int> shortest,
        Reachability reach,
        bool recordFallback)
    {
        var limitations = recordFallback ? new HashSet<string>(StringComparer.Ordinal) : null;
        var visited = new HashSet<MemberNode>();
        var queue = new Queue<(MemberNode Node, int Depth)>();
        foreach (var start in starts)
        {
            queue.Enqueue((start, 0));
        }

        while (queue.Count > 0)
        {
            var (node, depth) = queue.Dequeue();
            if (!visited.Add(node))
            {
                continue;
            }

            if (!units.TryGetValue(node, out var set))
            {
                set = new HashSet<TestUnit>();
                units.Add(node, set);
            }

            set.Add(unit);
            if (!shortest.TryGetValue(node, out var known) || depth < known)
            {
                shortest[node] = depth;
            }

            if (!facts.Facts.TryGetValue(node, out var fact))
            {
                continue;
            }

            foreach (var call in fact.Calls)
            {
                foreach (var target in Targets(node, call, kind, created, mocked, facts.Implementations, map.Visible, limitations))
                {
                    queue.Enqueue((target, depth + 1));
                }
            }
        }

        if (limitations is not null)
        {
            foreach (var limitation in limitations)
            {
                if (!reach.Limitations.Contains(limitation))
                {
                    reach.Limitations.Add(limitation);
                }
            }
        }
    }

    private static HashSet<MemberNode> Walk(
        IReadOnlyList<MemberNode> starts,
        DispatchKind kind,
        IReadOnlySet<string> created,
        IReadOnlySet<string> mocked,
        FactGraph facts,
        ProjectMap map,
        HashSet<string>? limitations)
    {
        var visited = new HashSet<MemberNode>();
        var queue = new Queue<MemberNode>(starts);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!visited.Add(node) || !facts.Facts.TryGetValue(node, out var fact))
            {
                continue;
            }

            foreach (var call in fact.Calls)
            {
                foreach (var target in Targets(node, call, kind, created, mocked, facts.Implementations, map.Visible, limitations))
                {
                    queue.Enqueue(target);
                }
            }
        }

        return visited;
    }

    private static IEnumerable<MemberNode> Targets(
        MemberNode caller,
        CallSite call,
        DispatchKind kind,
        IReadOnlySet<string> created,
        IReadOnlySet<string> mocked,
        ImplementationIndex index,
        IReadOnlyDictionary<ProjectKey, HashSet<ProjectKey>> visible,
        HashSet<string>? limitations)
    {
        if (!call.IsVirtual)
        {
            if (call.Direct is { } direct)
            {
                yield return direct;
            }

            yield break;
        }

        if (call.Direct is { } declared)
        {
            yield return declared;
        }

        if (call.VirtualDocumentationId is null)
        {
            yield break;
        }

        var implementations = index.Find(call.VirtualDocumentationId, caller.Project, visible);
        if (kind == DispatchKind.Cha)
        {
            foreach (var implementation in implementations)
            {
                yield return implementation.Node;
            }

            yield break;
        }

        var hits = 0;
        foreach (var implementation in implementations)
        {
            if (created.Contains(implementation.ContainingTypeKey))
            {
                hits++;
                yield return implementation.Node;
            }
        }

        if (hits > 0 || call.DeclaredTypeKey is not null && mocked.Contains(call.DeclaredTypeKey))
        {
            yield break;
        }

        if (implementations.Count == 0)
        {
            yield break;
        }

        limitations?.Add("CHA fallback for " + call.VirtualDocumentationId);
        foreach (var implementation in implementations)
        {
            yield return implementation.Node;
        }
    }
}
