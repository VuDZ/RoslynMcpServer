using Xunit;

namespace TestImpactIlProbe.Tests.Fixtures;

/// <summary>A test that reaches code loading an assembly by name and names nothing of that assembly.</summary>
public sealed class ModuleLoaderTests
{
    [Fact]
    public void NamesLoader() => Assert.NotNull(typeof(ModuleLoader));
}
