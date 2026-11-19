using RoslynMcpServer.Services.Testing.Il;
using RoslynMcpServer.Services.Testing.Il.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing.Il;

public sealed class IlAttributeTypeNamesTests : IDisposable
{
    public IlAttributeTypeNamesTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "RoslynMcpIlAttributes-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData("Targets.Target", 0)]
    [InlineData("System.Collections.Generic.List<Targets.Target>", 0)]
    [InlineData("System.Collections.Generic.Dictionary<AttributeCases.Target, System.Collections.Generic.List<Targets.Target[]>>", 0)]
    [InlineData("Targets.Target[,][]", 0)]
    [InlineData("AttributeCases.Outer<Targets.Target>.Inner<AttributeCases.Target>", 0)]
    [InlineData("System.Collections.Generic.List<Targets.Target>[,][]", 0)]
    [InlineData("Targets.Target", 24)]
    public void MemberData_typeof_reaches_generic_arguments_and_array_elements_from_their_own_assemblies(string argumentType, int wrapperDepth)
    {
        for (var depth = 0; depth < wrapperDepth; depth++)
        {
            argumentType = $"System.Collections.Generic.List<{argumentType}>";
        }

        var library = IlFixtureCompiler.Compile("Targets", "namespace Targets; public sealed class Target { }", Path.Combine(_directory, "lib"));
        var source = $$"""
            using Xunit;

            namespace AttributeCases;

            public sealed class Target { }
            public sealed class Outer<T> { public sealed class Inner<TInner> { } }
            public static class Data<T>
            {
                public static System.Collections.Generic.IEnumerable<object[]> Values =>
                    new[] { new object[] { System.Activator.CreateInstance<T>() } };
            }

            public sealed class DataTests
            {
                [Theory]
                [MemberData("Values", MemberType = typeof(Data<{{argumentType}}>))]
                public void Run(object value) { }
            }
            """;
        var tests = IlFixtureCompiler.Compile("AttributeCases", source, Path.Combine(_directory, "tests"), library);
        var result = IlDependencyGraphBuilder.BuildFromFiles(
            [new(library, IsTestProject: false), new(tests, IsTestProject: true)], IlNeighborMap.Empty);
        var test = new IlTypeName("AttributeCases", "AttributeCases.DataTests");
        var target = new IlTypeName("Targets", "Targets.Target");

        Assert.Empty(result.Unread);
        Assert.Equal([test], result.Closures.TestClasses);
        Assert.Contains(new IlTypeName("AttributeCases", "AttributeCases.Data`1"), result.DependenciesOf(test));
        Assert.Contains(target, result.DependenciesOf(test));
        Assert.All(Enum.GetValues<IlDependencyStage>(), stage =>
        {
            Assert.True(result.Closures.Reaches(test, target, stage));
            Assert.Equal([test], result.Closures.AffectedTestClasses([target], stage));
        });
        result.AssertEveryStageIncludesThePrevious();
    }

    private readonly string _directory;
}
