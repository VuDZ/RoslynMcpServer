namespace TestImpactIlProbe;

/// <summary>One assembly file that was built from sources found on disk.</summary>
internal sealed class AssemblyFacts
{
    public AssemblyFacts(string name, Guid mvid, string path)
    {
        Name = name;
        Mvid = mvid;
        Path = path;
    }

    public string Name { get; }

    public Guid Mvid { get; }

    public string Path { get; }

    /// <summary>Method bodies the probe could not read. Their dependencies are missing from the graph.</summary>
    public int UnreadableBodies { get; set; }

    public List<string> References { get; } = [];

    public List<TypeFacts> Types { get; } = [];

    /// <summary>
    /// Types every test of the assembly depends on: types with a module initializer and types named in
    /// assembly-level attributes, such as an assembly fixture.
    /// </summary>
    public HashSet<TypeName> Preamble { get; } = [];

    /// <summary>Type full name to the simple name of the assembly the type was forwarded to.</summary>
    public Dictionary<string, string> Forwarders { get; } = new(StringComparer.Ordinal);
}
