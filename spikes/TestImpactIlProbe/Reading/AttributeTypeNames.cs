using System.Reflection.Metadata;

namespace TestImpactIlProbe;

/// <summary>
/// Attribute value decoder that keeps only what the probe needs: which arguments are <c>typeof(...)</c>.
/// A type is described by a string; for a <c>typeof</c> argument it is the serialized type name.
/// </summary>
internal sealed class AttributeTypeNames : ICustomAttributeTypeProvider<string>
{
    public const string SystemType = "System.Type";

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

    public string GetSystemType() => SystemType;

    public string GetSZArrayType(string elementType) => elementType + "[]";

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => "enum";

    // A constructor parameter of type System.Type arrives here as a type reference. Any other referenced
    // type in an attribute signature is an enum.
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var reference = reader.GetTypeReference(handle);
        return reader.GetString(reference.Name) == "Type" && reader.GetString(reference.Namespace) == "System"
            ? SystemType
            : "enum";
    }

    public string GetTypeFromSerializedName(string name) => name;

    // The underlying type of an enum from another assembly is not in this metadata. Int32 is right for almost
    // every enum used in attributes; for another size the blob fails to decode and the caller skips it.
    public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;

    public bool IsSystemType(string type) => type == SystemType;

    /// <summary>Turns <c>Ns.Outer+Inner`1[[...]], Assembly, Version=...</c> into the top-level type name.</summary>
    public static TypeName Parse(string serialized, string ownAssembly)
    {
        var depth = 0;
        var comma = -1;
        for (var i = 0; i < serialized.Length && comma < 0; i++)
        {
            depth += serialized[i] switch { '[' => 1, ']' => -1, _ => 0 };
            if (serialized[i] == ',' && depth == 0)
            {
                comma = i;
            }
        }

        var name = comma < 0 ? serialized : serialized[..comma];
        var rest = comma < 0 ? string.Empty : serialized[(comma + 1)..];
        var nextComma = rest.IndexOf(',');
        var assembly = (nextComma < 0 ? rest : rest[..nextComma]).Trim();
        var cut = name.IndexOfAny(['[', '+', '*', '&']);
        return new TypeName(assembly.Length == 0 ? ownAssembly : assembly, (cut < 0 ? name : name[..cut]).Trim());
    }
}
