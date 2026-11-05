using Xunit;

namespace AdapterNames.Xunit;

public abstract class InheritedBase
{
    [Fact]
    public void Inherited() => Assert.Equal(typeof(InheritedTests), GetType());
}
