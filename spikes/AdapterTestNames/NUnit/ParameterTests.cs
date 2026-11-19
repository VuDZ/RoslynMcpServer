using NUnit.Framework;

namespace AdapterNames.NUnit;

public class ParameterTests
{
    [TestCase(1)]
    [TestCase(2)]
    public void Rows(int value) => Assert.That(value, Is.InRange(1, 2));
}
