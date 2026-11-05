using Xunit;

namespace AdapterNames.Xunit;

public abstract class GenericTests<T>
{
    [Fact]
    public void Generic() => Assert.Equal(typeof(int), typeof(T));
}
