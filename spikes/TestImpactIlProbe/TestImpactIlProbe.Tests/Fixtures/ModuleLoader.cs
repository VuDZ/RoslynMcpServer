using System.Reflection;

namespace TestImpactIlProbe.Tests.Fixtures;

/// <summary>Loads an assembly by a name that only a string holds, the way a plugin host does.</summary>
internal static class ModuleLoader
{
    public static Assembly Load(string name) => Assembly.Load(new AssemblyName(name));
}
