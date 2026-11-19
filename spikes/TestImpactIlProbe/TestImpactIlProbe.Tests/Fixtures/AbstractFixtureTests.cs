using Xunit;

namespace TestImpactIlProbe.Tests.Fixtures;

public abstract class AbstractFixtureTests
{
    [Fact]
    public void Inherited() => Assert.True(true);
}
