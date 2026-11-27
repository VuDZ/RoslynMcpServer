namespace RoslynMcpServer.Services.Testing.Il.Models;

/// <summary>
/// How much a test class is assumed to reach beyond the types its code names. Every stage includes the
/// previous one.
/// </summary>
internal enum IlDependencyStage
{
    /// <summary>Only types reachable through references in metadata and IL.</summary>
    References,

    /// <summary>
    /// Also, in every assembly the reached code hands to reflection: subtypes of the reached types and types
    /// a framework finds by itself (an external non-core base type, interface or attribute).
    /// </summary>
    Discovery,

    /// <summary>
    /// <see cref="Discovery"/>, and a test class that reaches code loading assemblies by name depends on every
    /// type of every solution assembly lying next to its test assembly.
    /// </summary>
    DiscoveryWithLoads,
}
