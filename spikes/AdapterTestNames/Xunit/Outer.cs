using Xunit;

namespace AdapterNames.Xunit;

public class Outer
{
    // VSTest discovery requires a public nested fixture for this probe.
    public class Inner
    {
        [Fact]
        public void Nested() => Assert.True(true);
    }
}
