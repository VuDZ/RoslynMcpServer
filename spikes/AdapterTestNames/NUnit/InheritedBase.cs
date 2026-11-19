using NUnit.Framework;

namespace AdapterNames.NUnit;

public abstract class InheritedBase
{
    [Test]
    public void Inherited() => Assert.That(GetType(), Is.EqualTo(typeof(InheritedTests)));
}
