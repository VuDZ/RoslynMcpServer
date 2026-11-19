using NUnit.Framework;

namespace AdapterNames.NUnit;

[TestFixture(typeof(int))]
public class GenericTests<T>
{
    [Test]
    public void Generic() => Assert.That(typeof(T), Is.EqualTo(typeof(int)));
}
