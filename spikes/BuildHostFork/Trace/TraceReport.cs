using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BuildHostFork.Trace;

/// <summary>
/// trace-report: turns one trace directory (client.json and the hosts' buildhost-*.jsonl) into report.md, report.json
/// and graph.json. With redact, paths become placeholders and their map is written to names-private.json, which must
/// not leave the machine.
/// </summary>
internal static class TraceReport
{
    public static TraceSummary Run(string directory, bool redact)
    {
        var client = ClientTraceFile.Read(directory);
        var hosts = HostTraceReader.ReadAll(directory);
        if (hosts.Any(host => host.Frequency != client.Frequency))
        {
            throw new InvalidDataException("A host trace uses another Stopwatch frequency than the client.");
        }

        // Every host of this load starts inside OpenSolutionAsync. A file that started elsewhere is left from another
        // load in a reused directory; mixing it in would add its spans and match its requests to this load.
        var foreign = hosts.Where(host => host.TraceStart < client.LoadStart || host.TraceStart > client.LoadEnd).ToArray();
        if (foreign.Length > 0)
        {
            throw new InvalidDataException(
                $"buildhost-{string.Join(", buildhost-", foreign.Select(host => host.ProcessId))}.jsonl in {directory} started outside the load of {ClientTraceFile.FileName}.");
        }

        var names = new PathNames(Path.GetDirectoryName(client.Solution)!, redact);
        var breakdown = WallBreakdown.Compute(client, hosts);
        var costs = ProjectCosts.Compute(client, hosts);
        var graph = LoadGraphBuilder.Build(client, hosts);
        var summary = new TraceSummary(
            (client.LoadEnd - client.LoadStart) * 1000.0 / client.Frequency,
            client.Projects,
            client.Documents,
            client.WorkspaceFailures,
            hosts.Count,
            hosts.Sum(host => host.UnreadableLines),
            hosts.Sum(SpansWithoutParent),
            breakdown,
            ClockCheck.Run(client, hosts),
            ProjectCosts.Repeats(hosts, client.Frequency),
            graph.Nodes.Count,
            graph.Nodes.Count(node => node.Built),
            graph.Edges.GroupBy(edge => edge.Provenance).ToDictionary(group => group.Key, group => group.Count()),
            graph.Edges.Count(edge => edge.ReferenceOutputAssembly == false),
            client.SolutionEntries.Count(entry => !entry.Loaded));

        // The report names paths first, so placeholders follow the projects table; report.json gets the same names.
        File.WriteAllText(Path.Combine(directory, "report.md"), Markdown(summary, costs, names, redact ? "(not shown)" : Path.GetFileName(client.Solution)));
        var published = summary with
        {
            Clock = summary.Clock with { Untraced = summary.Clock.Untraced.Select(operation => operation with { Path = names.Name(operation.Path) }).ToArray() }
        };
        WriteJson(Path.Combine(directory, "report.json"), published);
        WriteJson(Path.Combine(directory, "graph.json"), Named(graph, names));
        if (names.Placeholders.Count > 0)
        {
            WriteJson(Path.Combine(directory, "names-private.json"), names.Placeholders);
        }

        return published;
    }

    public static void WriteJson<T>(string path, T value)
        => File.WriteAllText(path, JsonSerializer.Serialize(value, _options));

    public static string Format(double value)
        => value.ToString("0", CultureInfo.InvariantCulture);

    public static string Precise(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

    public static string Percent(double share)
        => share.ToString("P1", CultureInfo.InvariantCulture);

    // A span whose parent line is missing comes from a host that stopped before closing the parent; the parent's own
    // time is then in no category, although the breakdown still adds up to the window.
    private static int SpansWithoutParent(HostTrace host)
    {
        var ids = host.Spans.Select(span => span.Id).ToHashSet();
        return host.Spans.Count(span => span.Parent != 0 && !ids.Contains(span.Parent));
    }

    private static LoadGraph Named(LoadGraph graph, PathNames names)
        => new(
            graph.Nodes.Select(node => node with { Path = names.Name(node.Path) }).ToArray(),
            graph.Edges.Select(edge => edge with { From = names.Name(edge.From), To = names.Name(edge.To) }).ToArray());

    private static string Markdown(TraceSummary summary, IReadOnlyList<ProjectCost> costs, PathNames names, string solution)
    {
        // Projects are named before anything else, so placeholder numbers follow the order of the projects table.
        var projects = new StringBuilder();
        AppendProjects(projects, costs, names);
        var clock = summary.Clock;
        var untraced = clock.Untraced.Count == 0
            ? "0"
            : $"{clock.Untraced.Count} ({string.Join(", ", clock.Untraced.Select(operation => $"{operation.Operation} {names.Name(operation.Path)}"))})";

        var text = new StringBuilder();
        text.AppendLine("# Traced load (BuildHost/task-01)").AppendLine();
        text.AppendLine($"- solution: {solution}; wall {Format(summary.WallMs)} ms; {summary.Projects} projects, {summary.Documents} documents, {summary.WorkspaceFailures} workspace failures; {summary.Hosts} traced BuildHost process(es)");
        text.AppendLine(summary.Complete
            ? "- trace: complete"
            : $"- trace: **incomplete**, {summary.UnreadableLines} unreadable line(s), {summary.SpansWithoutParent} span(s) whose parent is missing; the breakdown misses the time of the lost spans");
        text.AppendLine($"- clock check: {clock.Matched} requests inside their client operation, {clock.OutsideOperation} operation(s) whose requests lie outside them, " +
            $"{clock.UnclaimedRequests} request(s) outside every operation, {clock.Violations} beyond {clock.ToleranceMs} ms; min lead {Precise(clock.MinLeadMs)} ms, min tail {Precise(clock.MinTailMs)} ms");
        text.AppendLine($"- operations served by no traced host: {untraced}").AppendLine();
        AppendBreakdown(text, summary);
        text.Append(projects);
        AppendRepeats(text, summary);
        AppendGraph(text, summary);
        text.AppendLine("Host categories are self time: a span minus its child spans. Writing a child span's trace line falls into its parent's self time.")
            .AppendLine("RPC queue runs from reading the request line to the start of its handling: parsing the request and waiting for a thread pool thread.")
            .AppendLine("Process startup is placed from the wall-clock start time and is an estimate; every other host time is a Stopwatch span.")
            .AppendLine("Client categories are loader operations with no host span under them: request transport, response parsing and the loader's own work.")
            .AppendLine("Placeholder names (project-NNN) are numbered in the order this report names paths, projects table first, and hold for this report only.");
        return text.ToString();
    }

    private static void AppendBreakdown(StringBuilder text, TraceSummary summary)
    {
        text.AppendLine("## Wall time by what was running").AppendLine();
        text.AppendLine("| category | ms | share |").AppendLine("|---|---|---|");
        foreach (var (category, ms) in summary.Breakdown.OrderByDescending(pair => pair.Value))
        {
            text.AppendLine($"| {category} | {Format(ms)} | {Percent(ms / summary.WallMs)} |");
        }

        text.AppendLine();
    }

    private static void AppendProjects(StringBuilder text, IReadOnlyList<ProjectCost> costs, PathNames names)
    {
        text.AppendLine("## Projects by host time of the columns below (top 15, inclusive ms)").AppendLine();
        text.AppendLine("| project | root evaluation | TFM evaluations | design-time builds | instance | extraction | response (chars) | client Evaluate / Build / Resolve |")
            .AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var cost in costs.OrderByDescending(cost => cost.HostMs).Take(15))
        {
            text.AppendLine(
                $"| {names.Name(cost.Path)} | {Format(cost.RootEvaluationMs)} ({cost.RootEvaluations}) | {Format(cost.TfmEvaluationMs)} ({cost.TfmEvaluations}) | " +
                $"{Format(cost.DesignTimeBuildMs)} ({cost.DesignTimeBuilds}) | {Format(cost.ProjectInstanceMs)} | {Format(cost.ExtractionMs)} | " +
                $"{Format(cost.ResponseMs)} ({cost.ResponseChars}) | {Format(cost.ClientEvaluateMs)} / {Format(cost.ClientBuildMs)} / {Format(cost.ClientResolveMs)} |");
        }

        text.AppendLine().AppendLine($"Totals over {costs.Count} paths: root evaluation {Format(costs.Sum(cost => cost.RootEvaluationMs))} ms, " +
            $"TFM evaluations {Format(costs.Sum(cost => cost.TfmEvaluationMs))} ms, design-time builds {Format(costs.Sum(cost => cost.DesignTimeBuildMs))} ms, " +
            $"extraction {Format(costs.Sum(cost => cost.ExtractionMs))} ms, response {Format(costs.Sum(cost => cost.ResponseMs))} ms.").AppendLine();
    }

    private static void AppendRepeats(StringBuilder text, TraceSummary summary)
    {
        var repeats = summary.Repeats;
        text.AppendLine("## Repeats and reuse").AppendLine();
        text.AppendLine($"- paths with more than one root evaluation: {repeats.PathsEvaluatedMoreThanOnce}, {Format(repeats.RepeatedEvaluationMs)} ms after the first");
        text.AppendLine($"- project loads served from the host's ProjectCollection: {repeats.CachedProjectLoads}");
        text.AppendLine($"- repeated design-time builds of one path and target framework: {repeats.RepeatedBuildsOfSamePathAndTfm}, {Format(repeats.RepeatedBuildMs)} ms");
        text.AppendLine($"- output path queries: {repeats.OutputPathQueries}, of them on an already loaded project: {repeats.OutputPathQueriesWithCachedProject}").AppendLine();
    }

    private static void AppendGraph(StringBuilder text, TraceSummary summary)
    {
        text.AppendLine("## Observed load graph (graph.json)").AppendLine();
        text.AppendLine($"- nodes: {summary.GraphNodes}, of them built: {summary.GraphNodesBuilt}; solution entries not loaded: {summary.SolutionEntriesNotLoaded}");
        foreach (var (provenance, count) in summary.GraphEdges.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            text.AppendLine($"- edges, {provenance}: {count}");
        }

        text.AppendLine($"- ProjectReference items with ReferenceOutputAssembly=false: {summary.ReferencesWithoutOutputAssembly}").AppendLine();
    }

    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };
}
