namespace TestImpactIlProbe;

/// <summary>What one top-level type, together with its nested types, says in metadata and IL.</summary>
internal sealed class TypeFacts
{
    public TypeFacts(TypeName name)
    {
        Name = name;
    }

    public TypeName Name { get; }

    /// <summary>Abstract and not sealed: a static class is abstract and sealed in metadata and is not counted.</summary>
    public bool IsAbstract { get; set; }

    /// <summary>Calls that load assemblies the probe cannot name, such as <c>Assembly.Load</c>.</summary>
    public int OpaqueLoads { get; set; }

    /// <summary>How the type's code loads assemblies that its metadata does not name.</summary>
    public AssemblyLoadKinds Loads { get; set; }

    public HashSet<TypeName> Dependencies { get; } = [];

    /// <summary>Direct base types and interfaces of the type and of its nested types.</summary>
    public HashSet<TypeName> Supertypes { get; } = [];

    public HashSet<TypeName> TypeAttributes { get; } = [];

    public List<MethodAttribute> MethodAttributes { get; } = [];

    /// <summary>Simple names of assemblies whose types this type's code enumerates by reflection.</summary>
    public HashSet<string> Scans { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Documents { get; } = new(StringComparer.OrdinalIgnoreCase);
}
