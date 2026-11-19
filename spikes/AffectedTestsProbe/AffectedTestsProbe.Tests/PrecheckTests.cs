using Xunit;

namespace AffectedTestsProbe.Tests;

public sealed class PrecheckTests
{
    [Fact]
    public async Task SharedEdgeRules_MatchInBothDirections()
    {
        var report = await Precheck.AnalyzeAsync();

        Assert.True(report.Passed, string.Join(Environment.NewLine, report.Failures));
    }

    [Fact]
    public async Task ReverseFixturePath_DoesNotNeedTheForwardGraph()
    {
        using var session = ProbeSession.Open(PrecheckSource.Text);
        var map = new ProjectMap(session.Solution, "unspecified", "unspecified");
        var roots = await RootIndexBuilder.BuildAsync(session.Solution, map, [], CancellationToken.None);
        var node = await MemberLocator.FindAsync(session.Solution, map, "FromFixtureCtor", CancellationToken.None);

        Assert.NotNull(node);
        var outcome = await ReverseSearch.SearchAsync(
            session.Solution,
            map,
            roots,
            node.Value,
            ProbeLimits.Default,
            CancellationToken.None);

        Assert.Null(outcome.Limit);
        var names = outcome.Units.Select(unit => unit.ToString()).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert.Equal(
            ["Probe.SampleTests.Direct", "Probe.SampleTests.Explicit", "Probe.SampleTests.FromRows"],
            names);
    }
}
