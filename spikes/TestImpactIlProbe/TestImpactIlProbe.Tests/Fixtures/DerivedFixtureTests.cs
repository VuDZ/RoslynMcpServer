using Xunit;

namespace TestImpactIlProbe.Tests.Fixtures;

public sealed class DerivedFixtureTests : AbstractFixtureTests
{
    [Theory]
    [InlineData(1)]
    public void Own(int value) => Assert.Equal(1, value);
}
