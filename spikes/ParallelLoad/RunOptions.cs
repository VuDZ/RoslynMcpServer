namespace ParallelLoad;

internal sealed record RunOptions(string Mode, string SolutionPath, string OutputPath, int Workers, string Strategy)
{
    public string? BinlogDirectory { get; init; }
    public string Graph { get; init; } = "xml";
    public string? SnapshotPath { get; init; }
    public string? ProfilePath { get; init; }
    public string Aggregator { get; init; } = "no-restore";
    public string Partition { get; init; } = "greedy";
    public double BalanceEpsilon { get; init; } = 0.15;
    public int CoreDepth { get; init; } = 1;
    public string Phase2 { get; init; } = "parallel";
    public bool RefusalPolicy { get; init; }
    public bool FastPlanner { get; init; }
    public bool ForceParallel { get; init; }
    public string? BaselineEstimatePath { get; init; }
    public string? LocalSdkPath { get; init; }
    public string GlobalProperties { get; init; } = "debug-x64";
    public int TimeoutMinutes { get; init; } = 10;

    public static RunOptions Parse(string[] args)
    {
        if (args.Length < 1 || args[0] is not ("baseline" or "baseline-binlog" or "parallel" or "profile" or "replay-profile" or "core-first"))
        {
            throw new ArgumentException("Usage: baseline|baseline-binlog|parallel|profile --solution PATH --output PATH [--workers N --strategy round-robin|cluster]");
        }

        var values = new Dictionary<string, string>();
        for (var index = 1; index < args.Length; index += 2)
        {
            if (args[index] == "--force-parallel")
            {
                if (!values.TryAdd(args[index], "true")) { throw new ArgumentException("Duplicate force flag."); }
                index--;
                continue;
            }
            if (index + 1 >= args.Length || !values.TryAdd(args[index], args[index + 1]))
            {
                throw new ArgumentException("Each option must have one value and occur only once.");
            }
        }

        var allowed = new[] { "--solution", "--output", "--workers", "--strategy", "--graph", "--snapshot", "--profile", "--aggregator",
            "--partition", "--epsilon", "--core-depth", "--phase2", "--refusal-policy", "--force-parallel",
            "--baseline-estimate", "--local-sdk", "--global-properties", "--binlog-dir", "--timeout-minutes", "--fast-planner" };
        if (values.Keys.Except(allowed).Any())
        {
            throw new ArgumentException("Unknown option.");
        }

        var workers = values.TryGetValue("--workers", out var count) ? int.Parse(count) : 1;
        var strategy = values.GetValueOrDefault("--strategy", "round-robin");
        if (workers < 1 || strategy is not ("round-robin" or "cluster"))
        {
            throw new ArgumentException("Workers must be positive; strategy must be round-robin or cluster.");
        }

        var graph = values.GetValueOrDefault("--graph", "xml");
        var aggregator = values.GetValueOrDefault("--aggregator", "no-restore");
        var partition = values.GetValueOrDefault("--partition", "greedy");
        var epsilon = values.TryGetValue("--epsilon", out var epsilonValue)
            ? double.Parse(epsilonValue, System.Globalization.CultureInfo.InvariantCulture) : 0.15;
        var coreDepth = values.TryGetValue("--core-depth", out var depth) ? int.Parse(depth) : 1;
        var phase2 = values.GetValueOrDefault("--phase2", "parallel");
        var refusalPolicy = bool.Parse(values.GetValueOrDefault("--refusal-policy", "false"));
        var globalProperties = values.GetValueOrDefault("--global-properties", "debug-x64");
        var timeoutMinutes = int.Parse(values.GetValueOrDefault("--timeout-minutes", "10"));
        if (globalProperties is not ("debug-x64" or "server-default") || timeoutMinutes < 1 ||
            (refusalPolicy && (epsilon != 0.15 || coreDepth != 1 || phase2 != "parallel" ||
                args[0] != "core-first" || !values.ContainsKey("--baseline-estimate") || !values.ContainsKey("--profile"))))
        {
            throw new ArgumentException("Refusal policy requires core-first, profile, baseline estimate, epsilon .15, depth 1 and parallel phase 2.");
        }
        if (graph is not ("xml" or "snapshot" or "snapshot-load") || aggregator is not ("no-restore" or "restore" or "sequential") ||
            (args[0] == "core-first" && graph is "snapshot" or "snapshot-load" && !values.ContainsKey("--snapshot")) ||
            partition is not ("greedy" or "cluster") || !double.IsFinite(epsilon) || epsilon < 0 ||
            coreDepth is not (1 or 2) || phase2 is not ("parallel" or "isolated"))
        {
            throw new ArgumentException("Core-first requires graph xml|snapshot|snapshot-load, a snapshot for snapshot modes, partition greedy|cluster, finite nonnegative epsilon, core-depth 1|2, phase2 parallel|isolated, and aggregator no-restore|restore|sequential.");
        }

        return new RunOptions(
            args[0],
            Path.GetFullPath(values["--solution"]),
            Path.GetFullPath(values["--output"]),
            args[0] is "parallel" or "core-first" ? workers : 1,
            strategy)
        {
            Graph = graph,
            SnapshotPath = values.TryGetValue("--snapshot", out var snapshot) ? Path.GetFullPath(snapshot) : null,
            ProfilePath = values.TryGetValue("--profile", out var profile) ? Path.GetFullPath(profile) : null,
            Aggregator = aggregator,
            Partition = partition,
            BalanceEpsilon = epsilon,
            CoreDepth = coreDepth,
            Phase2 = phase2,
            RefusalPolicy = refusalPolicy,
            FastPlanner = refusalPolicy || bool.Parse(values.GetValueOrDefault("--fast-planner", "false")),
            ForceParallel = values.ContainsKey("--force-parallel"),
            BaselineEstimatePath = values.TryGetValue("--baseline-estimate", out var baseline) ? Path.GetFullPath(baseline) : null,
            LocalSdkPath = values.TryGetValue("--local-sdk", out var sdk) ? Path.GetFullPath(sdk) : null,
            GlobalProperties = globalProperties,
            BinlogDirectory = values.TryGetValue("--binlog-dir", out var binlog) ? Path.GetFullPath(binlog) : null,
            TimeoutMinutes = timeoutMinutes,
        };
    }
}
