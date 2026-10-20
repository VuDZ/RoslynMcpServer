using ParallelLoad.Models;

namespace ParallelLoad;

internal static class CoreFirstCluster
{
    public static CoreFirstPlan Create(
        Dictionary<string, HashSet<string>> graph,
        Dictionary<string, double> costs,
        string[][] initialRequests,
        string graphSource,
        string costSource,
        double epsilon)
    {
        var roots = initialRequests.SelectMany(paths => paths).Order(StringComparer.Ordinal).ToArray();
        var closures = roots.ToDictionary(root => root, root => CoreFirstPartition.Closure([root], graph),
            StringComparer.OrdinalIgnoreCase);
        var requests = initialRequests.Select(paths => paths.ToList()).ToArray();
        var rootIndexes = roots.Select((root, index) => (root, index)).ToDictionary(pair => pair.root, pair => pair.index,
            StringComparer.OrdinalIgnoreCase);
        var affinity = new double[roots.Length, roots.Length];
        for (var left = 0; left < roots.Length; left++)
        {
            for (var right = left + 1; right < roots.Length; right++)
            {
                affinity[left, right] = affinity[right, left] = closures[roots[left]]
                    .Intersect(closures[roots[right]], StringComparer.OrdinalIgnoreCase).Sum(path => costs[path]);
            }
        }

        CoreFirstPlan Evaluate() => CoreFirstPartition.BuildPlan(
            graph, costs, requests.Select(paths => paths.ToArray()).ToArray(), graphSource, costSource, epsilon, closures);
        var current = Evaluate();
        // Exact ownership is recomputed for each candidate. Overlap alone misses promotions caused by shared obj directories.
        for (var round = 0; round < 20; round++)
        {
            var best = current;
            List<(string Root, int From, int To)>? selected = null;
            var owner = requests.SelectMany((paths, index) => paths.Select(path => (path, index)))
                .ToDictionary(pair => pair.path, pair => pair.index, StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                var from = owner[root];
                foreach (var to in Enumerable.Range(0, requests.Length).Where(index => index != from))
                {
                    requests[from].Remove(root);
                    requests[to].Add(root);
                    var candidate = Evaluate();
                    requests[to].Remove(root);
                    requests[from].Add(root);
                    if (Better(candidate, best, epsilon))
                    {
                        best = candidate;
                        selected = [(root, from, to)];
                    }
                }
            }

            // Bounded pair swaps escape a balance barrier while bringing roots with costly shared dependencies together.
            var groupAffinity = roots.ToDictionary(root => root,
                root => requests.Select(paths => paths.Sum(path => affinity[rootIndexes[root], rootIndexes[path]])).ToArray(),
                StringComparer.OrdinalIgnoreCase);
            var pairs = roots.SelectMany((left, index) => roots.Skip(index + 1).Where(right => owner[left] != owner[right])
                .Select(right => (Left: left, Right: right,
                    Gain: groupAffinity[left][owner[right]] - groupAffinity[left][owner[left]] +
                        groupAffinity[right][owner[left]] - groupAffinity[right][owner[right]] -
                        2 * affinity[rootIndexes[left], rootIndexes[right]])))
                .OrderByDescending(pair => pair.Gain).ThenBy(pair => pair.Left, StringComparer.Ordinal)
                .ThenBy(pair => pair.Right, StringComparer.Ordinal).Take(128);
            foreach (var pair in pairs)
            {
                var from = owner[pair.Left];
                var to = owner[pair.Right];
                requests[from].Remove(pair.Left);
                requests[to].Remove(pair.Right);
                requests[from].Add(pair.Right);
                requests[to].Add(pair.Left);
                var candidate = Evaluate();
                requests[from].Remove(pair.Right);
                requests[to].Remove(pair.Left);
                requests[from].Add(pair.Left);
                requests[to].Add(pair.Right);
                if (Better(candidate, best, epsilon))
                {
                    best = candidate;
                    selected = [(pair.Left, from, to), (pair.Right, to, from)];
                }
            }

            // Shared projects can remain in the core until every owning root is colocated; single-root moves can stall.
            var bundles = graph.Keys.OrderByDescending(path => costs[path]).ThenBy(path => path, StringComparer.Ordinal)
                .Select(path => roots.Where(root => closures[root].Contains(path)).ToArray())
                .Where(bundle => bundle.Length > 1 && bundle.Select(root => owner[root]).Distinct().Count() > 1)
                .Take(32);
            foreach (var bundle in bundles)
            {
                foreach (var to in Enumerable.Range(0, requests.Length))
                {
                    var moves = bundle.Where(root => owner[root] != to).Select(root => (Root: root, From: owner[root], To: to)).ToList();
                    Apply(moves, reverse: false);
                    var candidate = Evaluate();
                    Apply(moves, reverse: true);
                    if (Better(candidate, best, epsilon))
                    {
                        best = candidate;
                        selected = moves;
                    }
                }
            }

            if (selected is null)
            {
                break;
            }

            Apply(selected, reverse: false);
            current = best;
        }

        return current with
        {
            Partition = "cluster",
            PlanningStatus = current.BalanceSatisfied ? "ok" : "infeasible-search",
            PlanningReason = current.BalanceSatisfied ? null :
                $"No feasible assignment found by deterministic local search: max/mean={current.BalanceRatio:R}, limit={1 + epsilon:R}; mean includes all {requests.Length} workers. This is not a proof of global infeasibility.",
        };

        void Apply(List<(string Root, int From, int To)> moves, bool reverse)
        {
            foreach (var move in moves)
            {
                requests[reverse ? move.To : move.From].Remove(move.Root);
            }

            foreach (var move in moves)
            {
                requests[reverse ? move.From : move.To].Add(move.Root);
            }
        }
    }

    private static bool Better(CoreFirstPlan candidate, CoreFirstPlan current, double epsilon)
    {
        var candidateExcess = Math.Max(0, candidate.BalanceRatio - 1 - epsilon);
        var currentExcess = Math.Max(0, current.BalanceRatio - 1 - epsilon);
        if (Math.Abs(candidateExcess - currentExcess) > 1e-12)
        {
            return candidateExcess < currentExcess;
        }

        if (Math.Abs(candidate.CoreCost - current.CoreCost) > 1e-9)
        {
            return candidate.CoreCost < current.CoreCost;
        }

        return candidate.GroupCosts.Max() < current.GroupCosts.Max() - 1e-9;
    }
}
