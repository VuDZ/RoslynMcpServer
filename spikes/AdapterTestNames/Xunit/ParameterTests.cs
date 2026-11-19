using Xunit;

namespace AdapterNames.Xunit;

public class ParameterTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Rows(int value) => Assert.InRange(value, 1, 2);
}
