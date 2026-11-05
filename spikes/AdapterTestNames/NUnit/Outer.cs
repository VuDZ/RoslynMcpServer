using NUnit.Framework;

namespace AdapterNames.NUnit;

public class Outer
{
    public class Inner
    {
        [Test]
        public void Nested() => Assert.That(true, Is.True);
    }
}
