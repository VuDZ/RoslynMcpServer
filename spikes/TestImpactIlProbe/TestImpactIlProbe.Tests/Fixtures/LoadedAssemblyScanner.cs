namespace TestImpactIlProbe.Tests.Fixtures;

/// <summary>Enumerates the assemblies the process has loaded, the way a type finder does.</summary>
internal static class LoadedAssemblyScanner
{
    public static int Count() => AppDomain.CurrentDomain.GetAssemblies().Length;
}
