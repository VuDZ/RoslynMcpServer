using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace TestImpactIlProbe;

/// <summary>
/// Signature decoder that collects every named type a signature mentions. The decoded value is the head type:
/// for <c>List&lt;Order&gt;[]</c> it is <c>List`1</c>, while <c>Order</c> is only collected.
/// </summary>
internal sealed class SignatureTypes : ISignatureTypeProvider<TypeName, object?>
{
    public SignatureTypes(Func<TypeDefinitionHandle, TypeName> definition, Func<TypeReferenceHandle, TypeName> reference)
    {
        _definition = definition;
        _reference = reference;
    }

    public HashSet<TypeName> Types { get; } = [];

    public HashSet<string> Scans { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Reset()
    {
        Types.Clear();
        Scans.Clear();
    }

    public TypeName GetArrayType(TypeName elementType, ArrayShape shape) => elementType;

    public TypeName GetByReferenceType(TypeName elementType) => elementType;

    public TypeName GetFunctionPointerType(MethodSignature<TypeName> signature) => default;

    public TypeName GetGenericInstantiation(TypeName genericType, ImmutableArray<TypeName> typeArguments)
    {
        // ASP.NET Core finds controllers and other application parts by the assembly of the entry point type,
        // so a test host for T depends on types that no code names.
        if (genericType.Name == WebApplicationFactory)
        {
            foreach (var argument in typeArguments.Where(argument => !argument.IsNone))
            {
                Scans.Add(argument.Assembly);
            }
        }

        return genericType;
    }

    public TypeName GetGenericMethodParameter(object? genericContext, int index) => default;

    public TypeName GetGenericTypeParameter(object? genericContext, int index) => default;

    public TypeName GetModifiedType(TypeName modifier, TypeName unmodifiedType, bool isRequired) => unmodifiedType;

    public TypeName GetPinnedType(TypeName elementType) => elementType;

    public TypeName GetPointerType(TypeName elementType) => elementType;

    public TypeName GetPrimitiveType(PrimitiveTypeCode typeCode) => default;

    public TypeName GetSZArrayType(TypeName elementType) => elementType;

    public TypeName GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
        Collect(_definition(handle));

    public TypeName GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
        Collect(_reference(handle));

    public TypeName GetTypeFromSpecification(
        MetadataReader reader,
        object? genericContext,
        TypeSpecificationHandle handle,
        byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    private TypeName Collect(TypeName type)
    {
        Types.Add(type);
        return type;
    }

    private const string WebApplicationFactory = "Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory`1";

    private readonly Func<TypeDefinitionHandle, TypeName> _definition;
    private readonly Func<TypeReferenceHandle, TypeName> _reference;
}
