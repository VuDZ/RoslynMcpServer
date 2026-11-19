using System.Reflection;

namespace TestImpactIlProbe.Tests.Fixtures;

/// <summary>Asks for the entry assembly, which in a test run is the test host and loads nothing by name.</summary>
internal static class EntryAssemblyReader
{
    public static string? Name() => Assembly.GetEntryAssembly()?.GetName().Name;
}
