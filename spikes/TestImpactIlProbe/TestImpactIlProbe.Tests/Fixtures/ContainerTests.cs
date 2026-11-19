using Xunit;

namespace TestImpactIlProbe.Tests.Fixtures;

/// <summary>A test that builds its services by scanning an assembly.</summary>
public sealed class ContainerTests
{
    [Fact]
    public void NamesConsumerAndScanner()
    {
        Assert.NotNull(typeof(OrderConsumer));
        Assert.NotNull(typeof(AssemblyScanner));
    }
}
