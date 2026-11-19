namespace TestImpactIlProbe;

internal enum AssemblyOrigin
{
    /// <summary>A portable PDB names at least one source file that exists on disk.</summary>
    Sources,

    /// <summary>No PDB, or a PDB whose sources are not on this machine: a package or a framework assembly.</summary>
    External,

    /// <summary>
    /// A PDB file lies next to the assembly but is not a portable PDB. The assembly may belong to the solution,
    /// and the probe cannot tell: its types are missing from the graph.
    /// </summary>
    UnreadablePdb,

    /// <summary>A reference assembly: metadata without method bodies.</summary>
    ReferenceAssembly,
}
