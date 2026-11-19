using Xunit;

namespace TestImpactIlProbe.Tests.Fixtures;

public sealed class RegistrationTests
{
    [Fact]
    public void NamesRegistration() => Assert.NotNull(typeof(OrderRegistration));
}
