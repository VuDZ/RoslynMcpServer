namespace TestImpactIlProbe;

/// <summary>Why a walk from a test class added a type.</summary>
internal enum ReachReason
{
    Root,

    /// <summary>The previous type names this type in metadata or IL.</summary>
    Reference,

    /// <summary>The previous type hands this type's assembly to reflection, and a framework finds the type.</summary>
    FoundByFramework,

    /// <summary>The type's assembly is handed to reflection, and the type implements a reached type.</summary>
    ImplementsReached,

    /// <summary>The previous type hands this type's assembly to reflection.</summary>
    WholeAssembly,

    /// <summary>The previous type loads assemblies by name, and this type's assembly lies next to the test assembly.</summary>
    LoadedByName,
}
