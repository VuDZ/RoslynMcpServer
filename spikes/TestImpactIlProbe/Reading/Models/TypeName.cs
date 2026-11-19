namespace TestImpactIlProbe;

/// <summary>
/// A top-level type by the simple name of its assembly and its metadata full name. Nested types are named by
/// their outermost type: the graph works at the level of a top-level type.
/// </summary>
internal readonly record struct TypeName(string Assembly, string Name)
{
    /// <summary>True for signature parts that name no type: primitives and generic parameters.</summary>
    public bool IsNone => Name is null;
}
