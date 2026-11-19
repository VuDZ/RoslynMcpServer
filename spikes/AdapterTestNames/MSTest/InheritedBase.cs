using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AdapterNames.MSTest;

[TestClass]
public abstract class InheritedBase
{
    [TestMethod]
    public void Inherited() => Assert.AreEqual(typeof(InheritedTests), GetType());
}
