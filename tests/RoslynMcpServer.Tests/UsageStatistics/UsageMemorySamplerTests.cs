using RoslynMcpServer.Diagnostics.UsageStatistics;
using Xunit;

namespace RoslynMcpServer.Tests.UsageStatistics;

public sealed class UsageMemorySamplerTests
{
    [Fact]
    public void Unreadable_child_command_line_leaves_buildhost_memory_and_count_unknown()
    {
        var started = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc);
        var plan = UnregisteredBuildHostReaper.Select(
            new Dictionary<int, BuildHostProcessObservation>
            {
                [20] = new(20, 10, started.AddSeconds(1), "dotnet", null, HasExited: false),
            },
            new HashSet<int>(),
            ownProcessId: 10,
            ownStartTime: started,
            startTimeTolerance: TimeSpan.Zero);

        Assert.Empty(plan.ProcessIds);
        Assert.Equal(1, plan.UnidentifiedChildren);
        var totals = UsageMemorySampler.BuildHostTotals(plan, []);
        Assert.Null(totals.Bytes);
        Assert.Null(totals.Count);
    }

    [Fact]
    public void No_child_is_zero_but_a_missing_known_child_counter_is_unknown()
    {
        var empty = UsageMemorySampler.BuildHostTotals(new UnregisteredBuildHostReapPlan([], 0), []);
        Assert.Equal(0, empty.Bytes);
        Assert.Equal(0, empty.Count);
        var partial = UsageMemorySampler.BuildHostTotals(new UnregisteredBuildHostReapPlan([20, 21], 0), [100L, null]);
        Assert.Null(partial.Bytes);
        Assert.Equal(2, partial.Count);
    }
}
