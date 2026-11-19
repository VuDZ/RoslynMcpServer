namespace TestImpactIlProbe;

/// <summary>
/// Ways code brings in assemblies that nothing in its metadata names. What gets loaded is decided at run
/// time, by a string or by what the process has loaded so far, so the probe cannot tell which assemblies.
/// </summary>
[Flags]
internal enum AssemblyLoadKinds
{
    None = 0,

    /// <summary><c>Assembly.Load</c>, <c>Assembly.LoadFrom</c> and the other <c>Assembly.Load*</c> methods.</summary>
    AssemblyLoad = 1,

    /// <summary><c>AssemblyLoadContext.Load*</c>.</summary>
    LoadContext = 2,

    /// <summary><c>AppDomain.GetAssemblies</c>: everything the process has loaded.</summary>
    LoadedAssemblies = 4,
}
