namespace TestImpactIlProbe;

/// <summary>How much a test is assumed to reach beyond the types its code names.</summary>
internal enum Layer
{
    /// <summary>Only types reachable through references in metadata and IL.</summary>
    References,

    /// <summary>
    /// Also, in every assembly the reached code hands to reflection: subtypes of the reached types and types
    /// a framework finds by itself (an external non-core base type, interface or attribute).
    /// </summary>
    Discovery,

    /// <summary>Also every type of every assembly the reached code hands to reflection.</summary>
    WholeAssembly,

    /// <summary>
    /// <see cref="References"/>, and a test class that reaches code loading assemblies by name depends on
    /// every type of every assembly lying next to its test assembly.
    /// </summary>
    ReferencesWithLoads,

    /// <summary><see cref="Discovery"/> with the same rule for code that loads assemblies by name.</summary>
    DiscoveryWithLoads,

    /// <summary>
    /// <see cref="DiscoveryWithLoads"/> with a narrower rule for <c>AppDomain.GetAssemblies</c>. That call
    /// names nothing and loads nothing: it returns what the process has loaded, so it counts as reflection
    /// over the assemblies the test class already reaches, not over everything next to the test assembly.
    /// </summary>
    DiscoveryWithNamedLoads,
}
