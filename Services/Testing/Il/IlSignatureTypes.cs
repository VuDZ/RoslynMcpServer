using System.Collections.Immutable;
using System.Reflection.Metadata;
using RoslynMcpServer.Services.Testing.Il.Models;

namespace RoslynMcpServer.Services.Testing.Il;

/// <summary>
/// Signature decoder that collects every named type a signature mentions. The decoded value is the head type:
/// for <c>List&lt;Order&gt;[]</c> it is <c>List`1</c>, while <c>Order</c> is only collected.
/// </summary>
internal sealed class IlSignatureTypes : ISignatureTypeProvider<IlTypeName, object?>
{
    public HashSet<IlTypeName> Types { get; } = [];

    public HashSet<string> Scans { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IlSignatureTypes(Func<TypeDefinitionHandle, IlTypeName> definition, Func<TypeReferenceHandle, IlTypeName> reference)
    {
        _definition = definition;
        _reference = reference;
    }

    public void Reset()
    {
        Types.Clear();
        Scans.Clear();
    }

    public IlTypeName GetArrayType(IlTypeName elementType, ArrayShape shape) => elementType;

    public IlTypeName GetByReferenceType(IlTypeName elementType) => elementType;

    public IlTypeName GetFunctionPointerType(MethodSignature<IlTypeName> signature) => default;

    public IlTypeName GetGenericInstantiation(IlTypeName genericType, ImmutableArray<IlTypeName> typeArguments)
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

    public IlTypeName GetGenericMethodParameter(object? genericContext, int index) => default;

    public IlTypeName GetGenericTypeParameter(object? genericContext, int index) => default;

    public IlTypeName GetModifiedType(IlTypeName modifier, IlTypeName unmodifiedType, bool isRequired) => unmodifiedType;

    public IlTypeName GetPinnedType(IlTypeName elementType) => elementType;

    public IlTypeName GetPointerType(IlTypeName elementType) => elementType;

    public IlTypeName GetPrimitiveType(PrimitiveTypeCode typeCode) => default;

    public IlTypeName GetSZArrayType(IlTypeName elementType) => elementType;

    public IlTypeName GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
        Collect(_definition(handle));

    public IlTypeName GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
        Collect(_reference(handle));

    public IlTypeName GetTypeFromSpecification(
        MetadataReader reader,
        object? genericContext,
        TypeSpecificationHandle handle,
        byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    private IlTypeName Collect(IlTypeName type)
    {
        Types.Add(type);
        return type;
    }

    private const string WebApplicationFactory = "Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory`1";

    private readonly Func<TypeDefinitionHandle, IlTypeName> _definition;
    private readonly Func<TypeReferenceHandle, IlTypeName> _reference;
}
