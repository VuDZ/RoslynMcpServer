using Xunit;

namespace TestImpactIlProbe.Tests;

public sealed class ClassCoverageTests
{
    [Fact]
    public void FilterNamesTheClassWithASeparatorSoThatALongerNameDoesNotMatch()
    {
        Assert.Equal(
            "FullyQualifiedName~Shop.OrderTests.|FullyQualifiedName~Shop.OrderTests+",
            TestRunTemplate.VsTestFilter("Shop.OrderTests"));
    }

    [Fact]
    public void TemplateTakesAssemblyClassFilterAndReport()
    {
        var line = new TestRunTemplate("run {assembly} {class} [{filter}] > {report}")
            .Expand("/bin/Shop.Tests.dll", "Shop.OrderTests", "/out/report.xml");

        Assert.Equal(
            "run /bin/Shop.Tests.dll Shop.OrderTests [FullyQualifiedName~Shop.OrderTests.|FullyQualifiedName~Shop.OrderTests+] > /out/report.xml",
            line);
    }

    [Fact]
    public void CustomCommandLineWinsOverRunner()
    {
        Assert.Equal("mine", TestRunTemplate.Choose("mtp", "mine").Text);
        Assert.Same(TestRunTemplate.Mtp, TestRunTemplate.Choose("mtp", null));
        Assert.Same(TestRunTemplate.XunitV3, TestRunTemplate.Choose("xunit3", null));
        Assert.Same(TestRunTemplate.VsTest, TestRunTemplate.Choose(null, null));
        Assert.Throws<InvalidOperationException>(() => TestRunTemplate.Choose("nunit", null));
    }

    [Fact]
    public void CommandLineCanComeFromAFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            File.WriteAllText(path, "run \"{assembly}\" --filter \"{filter}\"\n");

            Assert.Equal("run \"{assembly}\" --filter \"{filter}\"", TestRunTemplate.Choose(null, "@" + path).Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OwnOutputOfTestAssemblyIsTheSmallestRunnableDirectory()
    {
        var directories = new Dictionary<string, HashSet<string>>
        {
            ["/other/bin"] = ["Shop.Tests", "Shop", "Other.Tests", "Other"],
            ["/shop/bin/net10.0"] = ["Shop.Tests", "Shop"],
            ["/shop/bin/ref"] = ["Shop.Tests"],
            ["/unrelated"] = ["Shop"],
        };
        bool Exists(string path) => path.Replace('\\', '/') is "/other/bin/Shop.Tests.runtimeconfig.json" or "/shop/bin/net10.0/Shop.Tests.runtimeconfig.json";

        Assert.Equal("/shop/bin/net10.0", TestAssemblyLocator.Find(directories, "Shop.Tests", pathFilter: null, Exists));
        Assert.Equal("/other/bin", TestAssemblyLocator.Find(directories, "Shop.Tests", pathFilter: "other", Exists));
        Assert.Equal("/shop/bin/ref", TestAssemblyLocator.Find(directories, "Shop.Tests", pathFilter: null, _ => false));
        Assert.Null(TestAssemblyLocator.Find(directories, "Missing.Tests", pathFilter: null, Exists));
    }

    [Fact]
    public void ExecutedFilesSurviveTheShortReport()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var path = ClassReports.PathFor(directory, "Shop.Tests", "Shop.OrderTests");
            CoberturaWriter.WriteExecutedFiles(
                path,
                [
                    new CoberturaReport([@"C:\repo\"], new Dictionary<string, bool> { ["src/Order.cs"] = true, ["src/Unused.cs"] = false }),
                    new CoberturaReport([], new Dictionary<string, bool> { [@"C:\repo\src\Cart.cs"] = true }),
                ]);

            var read = CoberturaReader.Read(path);

            Assert.Equal([@"C:\repo\"], read.Sources);
            Assert.Equal([@"C:\repo\src\Cart.cs", "src/Order.cs"], read.Files.Where(file => file.Value).Select(file => file.Key).Order(StringComparer.Ordinal));
            Assert.DoesNotContain("src/Unused.cs", read.Files.Keys);
            Assert.Equal([("Shop.Tests", "Shop.OrderTests", path)], ClassReports.Find(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void IdleRunOfAnAssemblyIsNotTakenForAClass()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var path = ClassReports.IdlePathFor(directory, "Shop.Tests");
            CoberturaWriter.WriteExecutedFiles(path, [new CoberturaReport([], new Dictionary<string, bool> { ["src/Order.cs"] = true })]);
            Directory.CreateDirectory(Path.Combine(directory, "Cart.Tests"));

            Assert.Empty(ClassReports.Find(directory));
            Assert.Equal([("Shop.Tests", path)], ClassReports.FindIdle(directory));
            Assert.Equal(["src/Order.cs"], CoberturaReader.Read(path).Files.Keys);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void IdleRunAsksForAClassNoTestHas()
    {
        var line = TestRunTemplate.VsTest.Expand("/bin/Shop.Tests.dll", ClassReports.IdleClass, "/out/report.xml");

        Assert.Contains("--filter \"FullyQualifiedName~TestImpactIlProbe.NoSuchTestClass.|FullyQualifiedName~TestImpactIlProbe.NoSuchTestClass+\"", line);
    }
}
