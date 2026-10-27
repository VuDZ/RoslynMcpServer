namespace BuildHostFork.Trace;

/// <summary>
/// Checks that the host and client timestamps share one clock: each LoadProjectFileAsync and GetProjectFileInfosAsync
/// request must lie inside the client's Evaluate or Build operation of the same project.
/// </summary>
/// <remarks>
/// The tail is measured from where the host began sending the response, the last host moment the client's read must
/// follow. It was measured from the end of the send span at first, but the client can finish reading before the
/// host's flush returns: on a 2 vCPU machine the end came 2.6 ms after the client's operation had ended. From the
/// start of sending, the tail cannot be negative on one clock; the 1 ms tolerance stays as a margin. The check is
/// weaker by the time the host spends serializing the response. Breakdowns that place host spans
/// between client timestamps are only
/// valid when <see cref="ClockCheckResult.Agrees"/>. The check runs both ways: from the operations, so a request
/// outside its operation is not mistaken for a project of an untraced host, and from the requests, so a request no
/// operation covers is not left out.
/// </remarks>
internal static class ClockCheck
{
    public const double ToleranceMs = 1.0;

    public static ClockCheckResult Run(ClientTrace client, IReadOnlyList<HostTrace> hosts)
    {
        var requests = hosts.SelectMany(HostRequests.Read)
            .Where(request => request.Method is "LoadProjectFileAsync" or "GetProjectFileInfosAsync")
            .ToArray();
        var claimed = new HashSet<HostRequest>(ReferenceEqualityComparer.Instance);
        var untraced = new List<UntracedOperation>();
        int matched = 0, outside = 0, violations = 0;
        double minLead = double.MaxValue, minTail = double.MaxValue;
        foreach (var operation in client.Operations.Where(operation => operation.Operation is "Evaluate" or "Build"))
        {
            var method = operation.Operation == "Evaluate" ? "LoadProjectFileAsync" : "GetProjectFileInfosAsync";
            var candidates = requests.Where(request => request.Method == method && PathsEqual(request.Path, operation.Path)).ToArray();
            if (candidates.Length == 0)
            {
                untraced.Add(new UntracedOperation(operation.Operation, operation.Path));
                continue;
            }

            var request = candidates
                .Where(request => !claimed.Contains(request) && request.Sent >= operation.Start && request.Received <= operation.End)
                .MinBy(request => Math.Abs(request.Received - operation.Start));
            if (request is null)
            {
                outside++;
                continue;
            }

            claimed.Add(request);
            matched++;
            var lead = (request.Received - operation.Start) * 1000.0 / client.Frequency;
            var tail = (operation.End - request.SendStart) * 1000.0 / client.Frequency;
            minLead = Math.Min(minLead, lead);
            minTail = Math.Min(minTail, tail);
            if (lead < -ToleranceMs || tail < -ToleranceMs)
            {
                violations++;
            }
        }

        return new ClockCheckResult(
            matched,
            outside,
            requests.Count(request => !claimed.Contains(request)),
            violations,
            matched == 0 ? 0 : minLead,
            matched == 0 ? 0 : minTail,
            ToleranceMs,
            untraced);
    }

    public static bool PathsEqual(string? left, string? right)
        => left is not null && right is not null && string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
