using Xunit;

namespace AffectedTestsProbe.Tests;

public sealed class OracleModelTests
{
    [Fact]
    public void TwoControlRows_OnePassingMutation_IsMissingResults()
    {
        var control = new[]
        {
            new ExecutedCase("unit", "T.M", "Passed"),
            new ExecutedCase("unit", "T.M", "Passed"),
        };
        var mutation = new[] { new ExecutedCase("unit", "T.M", "Passed") };

        var judgment = PairValidity.Judge(control, mutation);

        Assert.Equal(PairStatus.MissingResults, judgment.Status);
    }

    [Fact]
    public void ControlFailure_IsNotTheOracle()
    {
        var control = new[]
        {
            new ExecutedCase("already", "T.Already", "Failed"),
            new ExecutedCase("fresh", "T.Fresh", "Passed"),
        };

        var affected = OracleSet.Affected(["already", "fresh"], control);

        Assert.Equal("fresh", Assert.Single(affected));
    }

    [Fact]
    public void TwoControlRows_OneFailingMutation_PutsTheUnitInTheOracle()
    {
        var control = new[]
        {
            new ExecutedCase("unit", "T.M", "Passed"),
            new ExecutedCase("unit", "T.M", "Passed"),
        };
        var mutation = new[] { new ExecutedCase("unit", "T.M", "Failed") };

        var judgment = PairValidity.Judge(control, mutation);

        Assert.Equal(PairStatus.Valid, judgment.Status);
        Assert.Equal("unit", Assert.Single(judgment.FailedUnits));
    }

    [Fact]
    public void FullyQualifiedNames_UseRuntimeShapeNotDisplayName()
    {
        var project = new ProjectKey("probe.csproj", "Debug", "AnyCPU", "net10.0");
        var rows = new TestUnit(project, "Ns.ParameterTests", "Rows", new MemberNode(project, "M:Ns.ParameterTests.Rows"));
        var nested = new TestUnit(project, "Ns.Outer+Inner", "Nested", new MemberNode(project, "M:Ns.Outer.Inner.Nested"));
        var heir = new TestUnit(project, "Ns.InheritedTests", "Inherited", new MemberNode(project, "M:Ns.InheritedBase.Inherited"));
        var units = new[] { rows, nested, heir };

        Assert.Same(rows, FqnMapper.Map("Ns.ParameterTests.Rows(1)", units));
        Assert.Same(rows, FqnMapper.Map("Ns.ParameterTests.Rows(2)", units));
        Assert.Same(nested, FqnMapper.Map("Ns.Outer+Inner.Nested", units));
        Assert.Null(FqnMapper.Map("Ns.Outer.Inner.Nested", units));
        Assert.Same(heir, FqnMapper.Map("Ns.InheritedTests.Inherited", units));
        Assert.Null(FqnMapper.Map("Ns.InheritedBase.Inherited", units));
    }

    [Fact]
    public void Join_IgnoresDisplayNameAndCountsUnmappedExecutedCases()
    {
        var project = new ProjectKey("probe.csproj", "Debug", "AnyCPU", "net10.0");
        var unit = new TestUnit(project, "Ns.ParameterTests", "Rows", new MemberNode(project, "M:Ns.ParameterTests.Rows"));
        var discovered = new[] { new DiscoveredCase("abc", "Ns.ParameterTests.Rows") };
        var rows = new[]
        {
            new TrxRow("abc", "Passed"),
            new TrxRow("missing", "Passed"),
        };

        var joined = RunJoin.Join(discovered, rows, [unit]);

        Assert.Equal(unit.Key, Assert.Single(joined.Cases).UnitKey);
        Assert.Equal(2, joined.Executed);
        Assert.Equal(1, joined.UnmappedExecuted);
        Assert.True(CompletenessGate.Publish(100, 5));
        Assert.False(CompletenessGate.Publish(100, 6));
        Assert.False(CompletenessGate.Publish(0, 0));
    }

    [Fact]
    public async Task Write_PreservesBomAndRestoreBytes()
    {
        var path = Path.Combine(Path.GetTempPath(), "tia-bom-" + Guid.NewGuid().ToString("N") + ".cs");
        try
        {
            var bom = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'a' };
            await File.WriteAllBytesAsync(path, bom);
            await FileBytes.WriteTextPreservingBomAsync(path, "b", bom, CancellationToken.None);
            var written = await File.ReadAllBytesAsync(path);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, written[..3]);

            await File.WriteAllBytesAsync(path, "plain"u8.ToArray());
            var plain = await File.ReadAllBytesAsync(path);
            await FileBytes.WriteTextPreservingBomAsync(path, "next", plain, CancellationToken.None);
            var withoutBom = await File.ReadAllBytesAsync(path);
            Assert.NotEqual(0xEF, withoutBom[0]);
            await File.WriteAllBytesAsync(path, plain);
            Assert.Equal(plain, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
