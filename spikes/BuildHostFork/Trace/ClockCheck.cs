namespace BuildHostFork.Trace;

/// <summary>
/// Checks that the host and client timestamps share one clock: each LoadProjectFileAsync and GetProjectFileInfosAsync
/// request must lie inside the client's Evaluate or Build operation of the same project.
/// </summary>
/// <remarks>
/// The client may read the response a little before the host finishes its send span, so a small negative tail is
/// expected; 1 ms covers what was seen on Linux. Breakdowns that place host spans between client timestamps are only
/// valid when this check has no violations.
/// </remarks>
internal static class ClockCheck
{
    public const double ToleranceMs = 1.0;

    public static ClockCheckResult Run(ClientTrace client, IReadOnlyList<HostTrace> hosts)
    {
        var requests = hosts.SelectMany(HostRequests.Read).ToArray();
        int matched = 0, unmatched = 0, violations = 0;
        double minLead = double.MaxValue, minTail = double.MaxValue;
        foreach (var operation in client.Operations.Where(operation => operation.Operation is "Evaluate" or "Build"))
        {
            var method = operation.Operation == "Evaluate" ? "LoadProjectFileAsync" : "GetProjectFileInfosAsync";
            var request = requests
                .Where(request => request.Method == method && PathsEqual(request.Path, operation.Path))
                .Where(request => request.Sent >= operation.Start && request.Received <= operation.End)
                .MinBy(request => Math.Abs(request.Received - operation.Start));
            if (request is null)
            {
                unmatched++;
                continue;
            }

            matched++;
            var lead = (request.Received - operation.Start) * 1000.0 / client.Frequency;
            var tail = (operation.End - request.Sent) * 1000.0 / client.Frequency;
            minLead = Math.Min(minLead, lead);
            minTail = Math.Min(minTail, tail);
            if (lead < -ToleranceMs || tail < -ToleranceMs)
            {
                violations++;
            }
        }

        return new ClockCheckResult(
            matched,
            unmatched,
            violations,
            matched == 0 ? 0 : minLead,
            matched == 0 ? 0 : minTail,
            ToleranceMs);
    }

    public static bool PathsEqual(string? left, string? right)
        => left is not null && right is not null && string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
