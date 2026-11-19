using Xunit;

namespace TestImpactIlProbe.Tests.Fixtures;

/// <summary>A unit test: it names the consumer and never hands an assembly to reflection.</summary>
public sealed class ReferenceOnlyTests
{
    [Fact]
    public void NamesConsumer() => Assert.NotNull(typeof(OrderConsumer));
}
