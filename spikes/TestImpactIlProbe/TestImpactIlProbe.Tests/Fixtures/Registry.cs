using System.Reflection;

namespace TestImpactIlProbe.Tests.Fixtures;

/// <summary>Stands in for a container: explicit registration and registration by scanning an assembly.</summary>
internal static class Registry
{
    public static void Add<TService, TImplementation>()
        where TImplementation : TService
    {
    }

    public static void Scan(Assembly assembly)
    {
    }
}
