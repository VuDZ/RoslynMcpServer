using Xunit;

namespace AffectedTestsProbe.Tests;

public sealed class DiscoveryLogTests
{
    [Fact]
    public void ExecutionBatches_AreAllRead_AndDiscoveryCompletionIsIgnored()
    {
        const string log =
            """
            {"Version":7,"MessageType":"TestExecution.StatsChange","Payload":{"NewTestResults":[{"TestCase":{"Id":"id-1","FullyQualifiedName":"Tests.CalcTests.Adds","DisplayName":"Adds"}}]}}
            {"Version":7,"MessageType":"TestDiscovery.Completed","Payload":{"LastDiscoveredTests":[{"Id":"ignored","FullyQualifiedName":"Tests.CalcTests.Unrelated"}]}}
            {"Version":7,"MessageType":"TestExecution.Completed","Payload":{"LastRunTests":{"NewTestResults":[{"TestCase":{"Id":"id-1","FullyQualifiedName":"Tests.CalcTests.Adds","DisplayName":"Adds"}},{"TestCase":{"Id":"id-2","FullyQualifiedName":"Tests.CalcTests.Rows","DisplayName":"Rows {1}"}}]}}}
            """;

        var cases = DiscoveryLog.Read(log);

        Assert.Equal(["id-1", "id-2"], cases.Select(item => item.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray());
        Assert.Contains(cases, item => item.FullyQualifiedName == "Tests.CalcTests.Rows");
        Assert.DoesNotContain(cases, item => item.Id == "ignored");
    }
}
