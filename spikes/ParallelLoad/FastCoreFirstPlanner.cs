using ParallelLoad.Models;

namespace ParallelLoad;

internal sealed class FastCoreFirstPlanner
{
    private const double Epsilon = 0.15;
    private const int CandidateLimit = 40000;

    private FastCoreFirstPlanner(Dictionary<string, HashSet<string>> graph, Dictionary<string, double> costs,
        int workers, TimeProvider clock, double budgetMs, Action<string>? checkpoint)
    {
        _graph = graph;
        _costs = costs;
        _workers = workers;
        _clock = clock;
        _start = clock.GetTimestamp();
        _budgetMs = budgetMs;
        _checkpoint = checkpoint;
    }

    public static CoreFirstPlan Create(Dictionary<string, HashSet<string>> graph, Dictionary<string, double> costs,
        int workers, string graphSource, string costSource, string partition,
        TimeProvider? timeProvider = null, double budgetMs = 500, Action<string>? checkpoint = null)
    {
        if (workers is < 1 or > 63 || graph.Count == 0)
        {
            throw new InvalidDataException("Fast planning requires a nonempty closed graph and 1..63 workers.");
        }
        return new FastCoreFirstPlanner(graph, costs, workers, timeProvider ?? TimeProvider.System, budgetMs, checkpoint)
            .Search(graphSource, costSource, partition);
    }

    private CoreFirstPlan Search(string graphSource, string costSource, string partition)
    {
        var exhausted = false;
        try
        {
            Prepare();
            Seed();
            var current = Evaluate();
            _lastPlan = Materialize(graphSource, costSource);
            Remember(current, graphSource, costSource);
            if (partition == "cluster" && _roots.Length <= 10)
            {
                Array.Clear(_owners);
                Enumerate(1, 0);
                Check("proof-completion");
                exhausted = true;
            }
            else if (partition == "cluster")
            {
                for (var round = 0; round < 20; round++)
                {
                    Check("round");
                    var selected = Improve(current, graphSource, costSource);
                    if (selected is null) { break; }
                    _owners = selected.Value.Owners;
                    current = selected.Value.Score;
                }
            }
        }
        catch (BudgetExpiredException)
        {
            // Only fully evaluated and materialized candidates completed before the deadline are retained.
        }
        var plan = _bestPlan ?? _lastPlan ?? new CoreFirstPlan(graphSource, costSource, [],
            Enumerable.Range(0, _workers).Select(_ => Array.Empty<string>()).ToArray(),
            Enumerable.Range(0, _workers).Select(_ => Array.Empty<string>()).ToArray(),
            0, 0, new double[_workers], false, false) { BalanceSatisfied = false };
        var proven = exhausted && _bestPlan is null;
        return plan with
        {
            Partition = partition,
            PlanningStatus = _bestPlan is not null ? "ok" : proven ? "balance-infeasible" : "planning-budget",
            PlanningReason = _bestPlan is not null ? null : proven
                ? "Complete enumeration modulo worker symmetry found no admissible assignment."
                : $"No admissible candidate completed within bounded search. Interrupted stage: {_stoppedStage ?? "search-completed"}; global infeasibility is unproven.",
            InfeasibilityProven = proven, SearchBudgetExpired = _stoppedStage is not null,
            InterruptedStage = _stoppedStage, EvaluatedCandidates = _evaluated,
            SearchMs = _clock.GetElapsedTime(_start).TotalMilliseconds,
        };

        void Enumerate(int root, int maximumOwner)
        {
            Check("enumeration");
            if (root == _roots.Length)
            {
                Remember(Evaluate(), graphSource, costSource);
                return;
            }
            for (var worker = 0; worker <= Math.Min(maximumOwner + 1, _workers - 1); worker++)
            {
                _owners[root] = worker;
                Enumerate(root + 1, Math.Max(worker, maximumOwner));
            }
        }
    }

    private void Prepare()
    {
        var sorted = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var path in _graph.Keys)
        {
            Check("preparation-paths");
            if (!_costs.TryGetValue(path, out var cost) || !double.IsFinite(cost) || cost < 0)
            {
                throw new InvalidDataException("Fast planning requires finite nonnegative costs.");
            }
            sorted.Add(path);
        }
        _paths = sorted.ToArray();
        var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < _paths.Length; index++)
        {
            Check("preparation-indexes");
            indexes.Add(_paths[index], index);
        }
        _indexes = indexes;
        _weights = new double[_paths.Length];
        _edges = new int[_paths.Length][];
        var directories = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < _paths.Length; index++)
        {
            Check("preparation-edges");
            var path = _paths[index];
            _weights[index] = _costs[path];
            var edges = new List<int>();
            foreach (var edge in _graph[path])
            {
                Check("preparation-edges");
                if (!indexes.TryGetValue(edge, out var target)) { throw new InvalidDataException("The graph is not closed."); }
                edges.Add(target);
                referenced.Add(edge);
            }
            _edges[index] = edges.ToArray();
            var directory = Path.GetDirectoryName(path)!;
            if (!directories.TryGetValue(directory, out var siblings)) { directories.Add(directory, siblings = []); }
            siblings.Add(index);
        }
        var directoryArrays = new List<int[]>();
        foreach (var siblings in directories.Values)
        {
            Check("preparation-directories");
            directoryArrays.Add(siblings.ToArray());
        }
        _directories = directoryArrays.ToArray();
        var roots = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var path in _paths)
        {
            Check("preparation-roots");
            if (!referenced.Contains(path)) { roots.Add(path); }
        }
        var covered = Closure(roots);
        foreach (var path in _paths)
        {
            Check("preparation-roots");
            if (!covered.Contains(path)) { roots.Add(path); covered.UnionWith(Closure([path])); }
        }
        _roots = roots.ToArray();
        _closures = new int[_roots.Length][];
        for (var root = 0; root < _roots.Length; root++)
        {
            Check("preparation-closures");
            _closures[root] = Closure([_roots[root]]).Select(path => indexes[path]).ToArray();
        }
        _affinity = new double[_roots.Length, _roots.Length];
        for (var index = 0; index < _paths.Length; index++)
        {
            var owners = new List<int>();
            for (var root = 0; root < _roots.Length; root++)
            {
                Check("preparation-affinity");
                if (_closures[root].Contains(index)) { owners.Add(root); }
            }
            foreach (var left in owners)
            {
                Check("preparation-affinity");
                foreach (var right in owners) { _affinity[left, right] += _weights[index]; }
            }
        }
        _owners = new int[_roots.Length];
        _membership = new ulong[_paths.Length];
        _core = new bool[_paths.Length];
        _groupCosts = new double[_workers];
        Check("preparation-completion");
    }

    private HashSet<string> Closure(IEnumerable<string> roots)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        foreach (var root in roots) { Check("preparation-closures"); pending.Push(root); }
        while (pending.TryPop(out var path))
        {
            Check("preparation-closures");
            if (!visited.Add(path)) { continue; }
            foreach (var target in _graph[path]) { Check("preparation-closures"); pending.Push(target); }
        }
        return visited;
    }

    private (Score Score, int[] Owners)? Improve(Score current, string graphSource, string costSource)
    {
        int[]? selected = null;
        var roundBest = current;
        var original = (int[])_owners.Clone();
        for (var root = 0; root < _roots.Length; root++)
        {
            for (var to = 0; to < _workers; to++)
            {
                Check("moves");
                if (to == original[root]) { continue; }
                _owners[root] = to;
                Consider();
                _owners[root] = original[root];
            }
        }
        foreach (var pair in RankPairs(original))
        {
            Check("swaps");
            (_owners[pair.Left], _owners[pair.Right]) = (_owners[pair.Right], _owners[pair.Left]);
            Consider();
            (_owners[pair.Left], _owners[pair.Right]) = (_owners[pair.Right], _owners[pair.Left]);
        }
        foreach (var bundle in RankBundles(original))
        {
            for (var to = 0; to < _workers; to++)
            {
                Check("bundles");
                foreach (var root in bundle) { _owners[root] = to; }
                Consider();
                foreach (var root in bundle) { _owners[root] = original[root]; }
            }
        }
        return selected is null ? null : (roundBest, selected);

        void Consider()
        {
            var candidate = Evaluate();
            Remember(candidate, graphSource, costSource);
            if (Better(candidate, roundBest)) { roundBest = candidate; selected = (int[])_owners.Clone(); }
        }
    }

    private (int Left, int Right)[] RankPairs(int[] owners)
    {
        var affinity = new double[_roots.Length, _workers];
        for (var left = 0; left < _roots.Length; left++)
        {
            Check("swap-ranking");
            for (var root = 0; root < _roots.Length; root++) { affinity[left, owners[root]] += _affinity[left, root]; }
        }
        var pairs = new List<(int Left, int Right, double Gain)>();
        for (var left = 0; left < _roots.Length; left++)
        {
            for (var right = left + 1; right < _roots.Length; right++)
            {
                Check("swap-ranking");
                if (owners[left] == owners[right]) { continue; }
                var gain = affinity[left, owners[right]] - affinity[left, owners[left]] +
                    affinity[right, owners[left]] - affinity[right, owners[right]] - 2 * _affinity[left, right];
                var insertion = pairs.FindIndex(previous => gain > previous.Gain || (gain == previous.Gain &&
                    (left < previous.Left || (left == previous.Left && right < previous.Right))));
                if (insertion < 0) { insertion = pairs.Count; }
                if (insertion < 128)
                {
                    pairs.Insert(insertion, (left, right, gain));
                    if (pairs.Count > 128) { pairs.RemoveAt(128); }
                }
            }
        }
        Check("swap-ranking-completion");
        return pairs.Select(pair => (pair.Left, pair.Right)).ToArray();
    }

    private int[][] RankBundles(int[] owners)
    {
        var sorted = new SortedSet<(double Cost, int Index)>(DescendingCostComparer());
        for (var index = 0; index < _paths.Length; index++) { Check("bundle-ranking"); sorted.Add((_weights[index], index)); }
        var bundles = new List<int[]>();
        foreach (var item in sorted)
        {
            var bundle = new List<int>();
            for (var root = 0; root < _roots.Length; root++)
            {
                Check("bundle-ranking");
                if (_closures[root].Contains(item.Index)) { bundle.Add(root); }
            }
            if (bundle.Count > 1 && bundle.Select(root => owners[root]).Distinct().Count() > 1) { bundles.Add(bundle.ToArray()); }
            if (bundles.Count == 32) { break; }
        }
        Check("bundle-ranking-completion");
        return bundles.ToArray();
    }

    private void Seed()
    {
        var membership = new bool[_workers, _paths.Length];
        var totals = new double[_workers];
        var sorted = new SortedSet<(double Cost, int Index)>(DescendingCostComparer());
        for (var root = 0; root < _roots.Length; root++)
        {
            Check("seed-ranking");
            sorted.Add((_closures[root].Sum(index => _weights[index]), root));
        }
        foreach (var item in sorted)
        {
            var worker = 0;
            var minimum = double.PositiveInfinity;
            for (var candidate = 0; candidate < _workers; candidate++)
            {
                Check("seed");
                var total = totals[candidate] + _closures[item.Index].Where(index => !membership[candidate, index]).Sum(index => _weights[index]);
                if (total < minimum) { minimum = total; worker = candidate; }
            }
            _owners[item.Index] = worker;
            foreach (var index in _closures[item.Index])
            {
                if (!membership[worker, index]) { totals[worker] += _weights[index]; }
                membership[worker, index] = true;
            }
        }
        Check("seed-completion");
    }

    private Score Evaluate()
    {
        Check("evaluation");
        if (_evaluated >= CandidateLimit) { _stoppedStage = "candidate-limit"; throw new BudgetExpiredException(); }
        Array.Clear(_membership);
        Array.Clear(_core);
        Array.Clear(_groupCosts);
        for (var root = 0; root < _roots.Length; root++)
        {
            if ((root & 15) == 0) { Check("evaluation"); }
            foreach (var index in _closures[root]) { _membership[index] |= 1UL << _owners[root]; }
        }
        for (var index = 0; index < _paths.Length; index++)
        {
            if ((index & 31) == 0) { Check("evaluation"); }
            var owners = _membership[index];
            _core[index] = (owners & (owners - 1)) != 0;
        }
        bool changed;
        do
        {
            changed = false;
            for (var directory = 0; directory < _directories.Length; directory++)
            {
                if ((directory & 15) == 0) { Check("evaluation"); }
                var siblings = _directories[directory];
                ulong owners = 0;
                var hasCore = false;
                foreach (var index in siblings) { owners |= _membership[index]; hasCore |= _core[index]; }
                if (hasCore || (owners & (owners - 1)) != 0)
                {
                    foreach (var index in siblings)
                    {
                        if (!_core[index]) { _core[index] = true; changed = true; }
                    }
                }
            }
            for (var index = 0; index < _paths.Length; index++)
            {
                if ((index & 31) == 0) { Check("evaluation"); }
                if (!_core[index]) { continue; }
                foreach (var edge in _edges[index])
                {
                    if (!_core[edge]) { _core[edge] = true; changed = true; }
                }
            }
        } while (changed);
        double coreCost = 0;
        for (var index = 0; index < _paths.Length; index++)
        {
            if ((index & 31) == 0) { Check("evaluation"); }
            if (_core[index]) { coreCost += _weights[index]; }
            else { _groupCosts[System.Numerics.BitOperations.TrailingZeroCount(_membership[index])] += _weights[index]; }
        }
        var mean = _groupCosts.Sum() / _workers;
        var score = new Score(coreCost, _groupCosts.Max(), mean > 0 ? _groupCosts.Max() / mean : 1);
        Check("evaluation-completion");
        _evaluated++;
        return score;
    }

    private CoreFirstPlan Materialize(string graphSource, string costSource)
    {
        // The evaluated masks already include dependency/directory promotion; no second unbounded BuildPlan walk is needed.
        var core = new List<string>();
        var groups = Enumerable.Range(0, _workers).Select(_ => new List<string>()).ToArray();
        var requests = Enumerable.Range(0, _workers).Select(_ => new List<string>()).ToArray();
        for (var index = 0; index < _paths.Length; index++)
        {
            Check("materialization");
            if (_core[index]) { core.Add(_paths[index]); }
            else { groups[System.Numerics.BitOperations.TrailingZeroCount(_membership[index])].Add(_paths[index]); }
        }
        for (var root = 0; root < _roots.Length; root++)
        {
            Check("materialization");
            if (!_core[_indexes[_roots[root]]]) { requests[_owners[root]].Add(_roots[root]); }
        }
        var average = _groupCosts.Sum() / _workers;
        var ratio = average > 0 ? _groupCosts.Max() / average : 1;
        var plan = new CoreFirstPlan(graphSource, costSource, core.ToArray(), requests.Select(group => group.ToArray()).ToArray(),
            groups.Select(group => group.ToArray()).ToArray(), core.Sum(path => _costs[path]), _weights.Sum(),
            (double[])_groupCosts.Clone(), true, true)
        {
            BalanceEpsilon = Epsilon, BalanceRatio = ratio, BalanceSatisfied = ratio <= 1 + Epsilon + 1e-12,
        };
        Check("materialization-completion");
        return plan;
    }

    private void Remember(Score candidate, string graphSource, string costSource)
    {
        if (Admissible(candidate) && (_bestPlan is null || Better(candidate, _bestScore)))
        {
            var plan = Materialize(graphSource, costSource);
            _bestPlan = plan;
            _bestScore = candidate;
        }
    }

    private void Check(string stage)
    {
        _checkpoint?.Invoke(stage);
        // Reserve a small part of the 500ms total for unwinding and reporting the cached result.
        if (_clock.GetElapsedTime(_start).TotalMilliseconds >= _budgetMs - Math.Min(5, _budgetMs * 0.01))
        {
            _stoppedStage = stage;
            throw new BudgetExpiredException();
        }
    }

    private static IComparer<(double Cost, int Index)> DescendingCostComparer() =>
        Comparer<(double Cost, int Index)>.Create((left, right) =>
        {
            var cost = right.Cost.CompareTo(left.Cost);
            return cost != 0 ? cost : left.Index.CompareTo(right.Index);
        });

    private static bool Admissible(Score score) => score.Ratio <= 1 + Epsilon + 1e-12;
    private static bool Better(Score candidate, Score current)
    {
        var excess = Math.Max(0, candidate.Ratio - 1 - Epsilon);
        var previousExcess = Math.Max(0, current.Ratio - 1 - Epsilon);
        if (Math.Abs(excess - previousExcess) > 1e-12) { return excess < previousExcess; }
        if (Math.Abs(candidate.Core - current.Core) > 1e-9) { return candidate.Core < current.Core; }
        return candidate.Maximum < current.Maximum - 1e-9;
    }

    private readonly record struct Score(double Core, double Maximum, double Ratio);
    private sealed class BudgetExpiredException : Exception;

    private readonly Dictionary<string, HashSet<string>> _graph;
    private readonly Dictionary<string, double> _costs;
    private readonly int _workers;
    private readonly TimeProvider _clock;
    private readonly long _start;
    private readonly double _budgetMs;
    private readonly Action<string>? _checkpoint;
    private Dictionary<string, int> _indexes = [];
    private string[] _paths = [];
    private string[] _roots = [];
    private double[] _weights = [];
    private int[][] _edges = [];
    private int[][] _directories = [];
    private int[][] _closures = [];
    private double[,] _affinity = new double[0, 0];
    private ulong[] _membership = [];
    private bool[] _core = [];
    private double[] _groupCosts = [];
    private int[] _owners = [];
    private int _evaluated;
    private CoreFirstPlan? _bestPlan;
    private CoreFirstPlan? _lastPlan;
    private Score _bestScore;
    private string? _stoppedStage;
}
