namespace RoslynMcpServer.Services.Testing.Il.Models;

/// <summary>
/// What one top-level type, together with its nested types, says in metadata and IL. The value is immutable
/// and its lists are sorted, so two reads of the same file give equal facts.
/// </summary>
internal sealed class IlTypeFacts
{
    public required IlTypeName Name { get; init; }

    /// <summary>Abstract and not sealed: a static class is abstract and sealed in metadata and is not counted.</summary>
    public required bool IsAbstract { get; init; }

    /// <summary>How the type's code loads assemblies that its metadata does not name.</summary>
    public required IlAssemblyLoadKinds Loads { get; init; }

    /// <summary>Top-level types the type names in metadata or IL, itself excluded.</summary>
    public required IReadOnlyList<IlTypeName> Dependencies { get; init; }

    /// <summary>Direct base types and interfaces of the type and of its nested types.</summary>
    public required IReadOnlyList<IlTypeName> Supertypes { get; init; }

    /// <summary>Attributes on the type and on its nested types, without attributes on members.</summary>
    public required IReadOnlyList<IlTypeName> TypeAttributes { get; init; }

    public required IReadOnlyList<IlMethodAttribute> MethodAttributes { get; init; }

    /// <summary>Simple names of assemblies whose types this type's code enumerates by reflection.</summary>
    public required IReadOnlyList<string> Scans { get; init; }
}
