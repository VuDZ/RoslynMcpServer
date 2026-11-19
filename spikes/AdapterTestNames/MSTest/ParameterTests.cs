using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AdapterNames.MSTest;

[TestClass]
public class ParameterTests
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void Rows(int value) => Assert.IsTrue(value is 1 or 2);
}
