using System.Reflection.Metadata;
using RoslynMcpServer.Services.Testing.Il.Models;

namespace RoslynMcpServer.Services.Testing.Il;

/// <summary>
/// Attribute value decoder that keeps only what the graph needs: which arguments are <c>typeof(...)</c>.
/// A type is described by a string; for a <c>typeof</c> argument it is the serialized type name.
/// </summary>
internal sealed class IlAttributeTypeNames : ICustomAttributeTypeProvider<string>
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

    /// <summary>Collects top-level types, including generic arguments and element types, without loading assemblies.</summary>
    public static IReadOnlyList<IlTypeName> Parse(string serialized, string ownAssembly)
    {
        var types = new List<IlTypeName>();
        var pending = new Stack<TypeName>();
        pending.Push(TypeName.Parse(serialized, new TypeNameParseOptions { MaxNodes = serialized.Length }));
        while (pending.TryPop(out var type))
        {
            while (type.IsArray || type.IsPointer || type.IsByRef)
            {
                type = type.GetElementType();
            }

            // typeof(Data<T>) stores T only in this string; keeping just Data loses theory data dependencies.
            foreach (var argument in type.GetGenericArguments())
            {
                pending.Push(argument);
            }

            if (type.IsConstructedGenericType)
            {
                type = type.GetGenericTypeDefinition();
            }

            while (type.IsNested)
            {
                type = type.DeclaringType;
            }

            types.Add(new IlTypeName(type.AssemblyName?.Name ?? ownAssembly, TypeName.Unescape(type.FullName)));
        }

        return types;
    }
}
