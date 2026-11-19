using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AdapterNames.MSTest;

public class Outer
{
    [TestClass]
    public class Inner
    {
        [TestMethod]
        public void Nested() => Assert.IsTrue(true);
    }
}
