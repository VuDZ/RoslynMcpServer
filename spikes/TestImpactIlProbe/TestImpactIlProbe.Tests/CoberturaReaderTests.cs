using System.Text;
using Xunit;

namespace TestImpactIlProbe.Tests;

public sealed class CoberturaReaderTests
{
    [Fact]
    public void FileIsCoveredWhenAnyLineOfAnyClassWasExecuted()
    {
        var report = Read(
            """
            <coverage>
              <packages>
                <package name="Shop">
                  <classes>
                    <class name="Shop.Order" filename="src/Order.cs">
                      <lines><line number="3" hits="0" /></lines>
                    </class>
                    <class name="Shop.Order/&lt;&gt;c" filename="src/Order.cs">
                      <methods>
                        <method name="Run"><lines><line number="9" hits="12" /></lines></method>
                      </methods>
                    </class>
                    <class name="Shop.Unused" filename="src/Unused.cs">
                      <lines><line number="1" hits="0" /><line number="2" hits="0" /></lines>
                    </class>
                  </classes>
                </package>
              </packages>
            </coverage>
            """);

        Assert.Equal(2, report.Files.Count);
        Assert.True(report.Files["src/Order.cs"]);
        Assert.False(report.Files["src/Unused.cs"]);
    }

    [Fact]
    public void SourcesAreReadWithoutLosingTheElementThatFollows()
    {
        var report = Read(
            """<coverage><sources><source>C:\repo\</source><source>D:\other</source><source /></sources><packages /></coverage>""");

        Assert.Equal([@"C:\repo\", @"D:\other"], report.Sources);
    }

    [Fact]
    public void DoctypeIsIgnored()
    {
        var report = Read(
            """
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE coverage SYSTEM "http://cobertura.sourceforge.net/xml/coverage-04.dtd">
            <coverage><packages><package><classes>
              <class filename="a.cs"><lines><line number="1" hits="1" /></lines></class>
            </classes></package></packages></coverage>
            """);

        Assert.True(report.Files["a.cs"]);
    }

    private static CoberturaReport Read(string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return CoberturaReader.Read(stream);
    }
}
