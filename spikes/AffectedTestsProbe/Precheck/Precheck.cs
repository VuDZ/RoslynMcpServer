namespace AffectedTestsProbe;

internal static class Precheck
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var report = await AnalyzeAsync(cancellationToken).ConfigureAwait(false);
        foreach (var failure in report.Failures)
        {
            Console.Error.WriteLine(failure);
        }

        return report.Passed ? 0 : 1;
    }

    public static async Task<PrecheckReport> AnalyzeAsync(CancellationToken cancellationToken = default)
    {
        using var session = ProbeSession.Open(PrecheckSource.Text);
        var errors = await MemberLocator.ErrorsAsync(session.Solution, cancellationToken).ConfigureAwait(false);
        if (errors.Count > 0)
        {
            return new PrecheckReport(false, errors);
        }

        var probe = await LoadedProbe.LoadAsync(session, cancellationToken).ConfigureAwait(false);
        var failures = new List<string>();
        await ExpectBothAsync(probe, failures, "FromFixtureCtor", cancellationToken,
            "Probe.SampleTests.Direct", "Probe.SampleTests.Explicit", "Probe.SampleTests.FromRows").ConfigureAwait(false);
        await ExpectBothAsync(probe, failures, "FromFixtureInit", cancellationToken,
            "Probe.SampleTests.Direct", "Probe.SampleTests.Explicit", "Probe.SampleTests.FromRows").ConfigureAwait(false);
        await ExpectBothAsync(probe, failures, "FromStaticCtor", cancellationToken, "Probe.SampleTests.Direct").ConfigureAwait(false);
        await ExpectBothAsync(probe, failures, "FromFieldInit", cancellationToken, "Probe.SampleTests.Direct").ConfigureAwait(false);
        await ExpectBothAsync(probe, failures, "FromData", cancellationToken, "Probe.SampleTests.FromRows").ConfigureAwait(false);
        await ExpectBothAsync(probe, failures, "FromTestBody", cancellationToken, "Probe.SampleTests.Direct").ConfigureAwait(false);
        await ExpectBothAsync(probe, failures, "FromOther", cancellationToken, "Probe.OtherTests.Other").ConfigureAwait(false);
        await ExpectBothAsync(probe, failures, "FromLocal", cancellationToken, "Probe.OtherTests.LocalResult").ConfigureAwait(false);
        await ExpectBothAsync(probe, failures, "FromSetup", cancellationToken, "Probe.SetupTests.One", "Probe.SetupTests.Two").ConfigureAwait(false);
        await ExpectBothAsync(probe, failures, "N", cancellationToken, "Probe.SampleTests.Direct").ConfigureAwait(false);
        await ExpectBothAsync(probe, failures, "FromLeft", cancellationToken, "Probe.VirtualTests.Virtual", "Probe.MockTests.Mocked").ConfigureAwait(false);
        await ExpectBothAsync(probe, failures, "FromRight", cancellationToken, "Probe.VirtualTests.Virtual", "Probe.MockTests.Mocked").ConfigureAwait(false);
        await ExpectRtaAsync(probe, failures, "FromLeft", cancellationToken, "Probe.VirtualTests.Virtual").ConfigureAwait(false);
        await ExpectRtaAsync(probe, failures, "FromRight", cancellationToken).ConfigureAwait(false);

        if (failures.Count > 0)
        {
            failures.Add("Root limitations: " + string.Join("; ", probe.Roots.Limitations));
            failures.Add("Unmapped types: " + string.Join("; ", probe.Roots.Unmapped.Select(item => item.DeclaredName + " (" + item.Reason + ")")));
            failures.Add("Test units: " + string.Join(", ", probe.Roots.Units.Select(unit => unit.ToString())));
            failures.Add("Reach limitations: " + string.Join("; ", probe.Reach.Limitations));
        }

        return new PrecheckReport(failures.Count == 0, failures);
    }

    private static async Task ExpectBothAsync(
        LoadedProbe probe,
        List<string> failures,
        string member,
        CancellationToken cancellationToken,
        params string[] expected)
    {
        var node = await MemberLocator.FindAsync(probe.Solution, probe.Map, member, cancellationToken).ConfigureAwait(false);
        if (node is not { } found)
        {
            failures.Add(member + " was not found.");
            return;
        }

        var forward = Names(probe.Reach.Units(DispatchKind.Cha, found));
        var reverse = await probe.ReverseAsync(found, cancellationToken).ConfigureAwait(false);
        Compare(failures, member + " forward CHA", forward, expected);
        if (reverse.Limit is not null)
        {
            failures.Add(member + " reverse hit limit " + reverse.Limit + ".");
            return;
        }

        Compare(failures, member + " reverse", Names(reverse.Units), expected);
    }

    private static async Task ExpectRtaAsync(
        LoadedProbe probe,
        List<string> failures,
        string member,
        CancellationToken cancellationToken,
        params string[] expected)
    {
        var node = await MemberLocator.FindAsync(probe.Solution, probe.Map, member, cancellationToken).ConfigureAwait(false);
        if (node is not { } found)
        {
            failures.Add(member + " was not found for RTA.");
            return;
        }

        Compare(failures, member + " forward RTA", Names(probe.Reach.Units(DispatchKind.Rta, found)), expected);
    }

    private static void Compare(List<string> failures, string label, IReadOnlyCollection<string> actual, string[] expected)
    {
        var actualOrdered = actual.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var expectedOrdered = expected.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        if (!actualOrdered.SequenceEqual(expectedOrdered))
        {
            failures.Add(label + " expected [" + string.Join(", ", expectedOrdered) + "] actual [" + string.Join(", ", actualOrdered) + "].");
        }
    }

    private static string[] Names(IEnumerable<TestUnit> units) =>
        units.Select(unit => unit.ToString()).Distinct(StringComparer.Ordinal).ToArray();
}
