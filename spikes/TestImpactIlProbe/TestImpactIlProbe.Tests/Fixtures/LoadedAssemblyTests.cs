using Xunit;

namespace TestImpactIlProbe.Tests.Fixtures;

/// <summary>A test that reaches code enumerating the loaded assemblies and loads nothing by name.</summary>
public sealed class LoadedAssemblyTests
{
    [Fact]
    public void NamesScanner() => Assert.NotNull(typeof(LoadedAssemblyScanner));
}
