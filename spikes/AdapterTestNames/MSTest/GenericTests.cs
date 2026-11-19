using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AdapterNames.MSTest;

[TestClass]
public abstract class GenericTests<T>
{
    [TestMethod]
    public void Generic() => Assert.AreEqual(typeof(int), typeof(T));
}
