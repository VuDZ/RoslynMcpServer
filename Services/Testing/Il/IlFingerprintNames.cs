using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using static RoslynMcpServer.Services.Testing.Il.IlFingerprintEncoding;

namespace RoslynMcpServer.Services.Testing.Il;

/// <summary>Resolves metadata tokens to complete identities, preserving nested types and signature shape.</summary>
internal sealed class IlFingerprintNames : ISignatureTypeProvider<string, object?>
{
    public string Assembly { get; }

    public IlFingerprintNames(MetadataReader metadata)
    {
        _metadata = metadata;
        Assembly = metadata.GetString(metadata.GetAssemblyDefinition().Name);
    }

    public string Resolve(EntityHandle handle)
    {
        if (handle.IsNil)
        {
            return "nil";
        }

        if (_resolved.TryGetValue(handle, out var cached))
        {
            return cached;
        }

        if (!_active.Add(handle))
        {
            throw new BadImageFormatException("Cyclic metadata identity.");
        }

        var value = Decode(handle);
        _active.Remove(handle);
        _resolved.Add(handle, value);
        return value;
    }

    public string DefinitionName(TypeDefinitionHandle handle) => DefinitionName(handle, []);

    public string StorageType(string type)
    {
        while (_storageTypes.TryGetValue(type, out var storage))
        {
            type = storage;
        }

        return type;
    }

    public bool IsPointer(string type) => _pointers.Contains(type);

    public string Method(MethodSignature<string> signature) => Value("signature",
        signature.Header.RawValue, signature.GenericParameterCount, signature.RequiredParameterCount,
        signature.ReturnType, Sequence("parameters", signature.ParameterTypes));

    public MethodSignature<string> MethodSignature(EntityHandle handle) => handle.Kind switch
    {
        HandleKind.MethodDefinition => _metadata.GetMethodDefinition((MethodDefinitionHandle)handle).DecodeSignature(this, null),
        HandleKind.MemberReference => _metadata.GetMemberReference((MemberReferenceHandle)handle).DecodeMethodSignature(this, null),
        _ => throw new BadImageFormatException("An attribute constructor is not a method."),
    };

    public string SerializedType(string name) => Serialized(TypeName.Parse(name, new TypeNameParseOptions {MaxNodes = name.Length}));

    public string SerializedType(string name, IReadOnlySet<string> solutionAssemblies) =>
        Serialized(TypeName.Parse(name, new TypeNameParseOptions {MaxNodes = name.Length}), solutionAssemblies);

    public bool IsSystemType(string type) => _systemTypes.Contains(type);

    public string AttributeIdentity(string type) => _attributeIdentities.GetValueOrDefault(type, type);

    public bool IsBuildVersionConstructor(EntityHandle constructor)
    {
        var owner = constructor.Kind == HandleKind.MethodDefinition
            ? _metadata.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType()
            : _metadata.GetMemberReference((MemberReferenceHandle)constructor).Parent;
        if (owner.Kind != HandleKind.TypeReference)
        {
            return false;
        }

        var (assembly, name) = ReferenceName(_metadata.GetTypeReference((TypeReferenceHandle)owner), []);
        return assembly is "System.Private.CoreLib" or "System.Runtime" or "mscorlib"
            && name is "System.Reflection.AssemblyFileVersionAttribute" or "System.Reflection.AssemblyInformationalVersionAttribute";
    }

    public bool TryArrayElement(string type, out string element) => _arrays.TryGetValue(type, out element!);

    public bool TryPrimitive(string type, out PrimitiveTypeCode code)
    {
        foreach (var candidate in Enum.GetValues<PrimitiveTypeCode>())
        {
            if (type == GetPrimitiveType(candidate))
            {
                code = candidate;
                return true;
            }
        }

        code = default;
        return false;
    }

    public int? EnumWidth(string type)
    {
        if (!_definitions.TryGetValue(type, out var handle))
        {
            return null;
        }

        foreach (var fieldHandle in _metadata.GetTypeDefinition(handle).GetFields())
        {
            var field = _metadata.GetFieldDefinition(fieldHandle);
            if (Text(field.Name) == "value__" && TryPrimitive(field.DecodeSignature(this, null), out var code))
            {
                return PrimitiveWidth(code, pointerSize: 0);
            }
        }

        throw new BadImageFormatException("An attribute enum has no integral underlying field.");
    }

    public TypeDefinitionHandle Definition(string type) => _definitions.TryGetValue(type, out var handle)
        ? handle : throw new BadImageFormatException("The initial-data field type is not defined in this image.");

    public static int PrimitiveWidth(PrimitiveTypeCode code, int pointerSize) => code switch
    {
        PrimitiveTypeCode.Boolean or PrimitiveTypeCode.Byte or PrimitiveTypeCode.SByte => 1,
        PrimitiveTypeCode.Char or PrimitiveTypeCode.Int16 or PrimitiveTypeCode.UInt16 => 2,
        PrimitiveTypeCode.Int32 or PrimitiveTypeCode.UInt32 or PrimitiveTypeCode.Single => 4,
        PrimitiveTypeCode.Int64 or PrimitiveTypeCode.UInt64 or PrimitiveTypeCode.Double => 8,
        PrimitiveTypeCode.IntPtr or PrimitiveTypeCode.UIntPtr when pointerSize != 0 => pointerSize,
        _ => throw new BadImageFormatException("A non-blittable type cannot describe field initial data."),
    };

    public string GetArrayType(string elementType, ArrayShape shape) => Value("array", elementType, shape.Rank,
        Sequence("sizes", shape.Sizes.Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture))),
        Sequence("bounds", shape.LowerBounds.Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture))));

    public string GetByReferenceType(string elementType) => Value("byref", elementType);

    public string GetFunctionPointerType(MethodSignature<string> signature)
    {
        var value = Value("function", Method(signature));
        _pointers.Add(value);
        return value;
    }

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
        Value("generic", genericType, Sequence("arguments", typeArguments));

    public string GetGenericMethodParameter(object? genericContext, int index) => Value("method-parameter", index);

    public string GetGenericTypeParameter(object? genericContext, int index) => Value("type-parameter", index);

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
    {
        var value = Value("modified", isRequired, modifier, unmodifiedType);
        _storageTypes[value] = unmodifiedType;
        return value;
    }

    public string GetPinnedType(string elementType) => Value("pinned", elementType);

    public string GetPointerType(string elementType)
    {
        var value = Value("pointer", elementType);
        _pointers.Add(value);
        return value;
    }

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => Value("primitive", (int)typeCode);

    public string GetSZArrayType(string elementType)
    {
        var value = Value("vector", elementType);
        _arrays[value] = elementType;
        return value;
    }

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var value = Value("type", rawTypeKind, Resolve(handle));
        _definitions[value] = handle;
        _attributeIdentities[value] = Resolve(handle);
        return value;
    }

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var value = Value("type", rawTypeKind, Resolve(handle));
        _attributeIdentities[value] = Resolve(handle);
        var reference = reader.GetTypeReference(handle);
        if (Text(reference.Namespace) == "System" && Text(reference.Name) == "Type")
        {
            _systemTypes.Add(value);
        }

        return value;
    }

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle,
        byte rawTypeKind) => Resolve(handle);

    private string DefinitionName(TypeDefinitionHandle handle, HashSet<TypeDefinitionHandle> visited)
    {
        if (!visited.Add(handle))
        {
            throw new BadImageFormatException("Cyclic nested type definition.");
        }

        var definition = _metadata.GetTypeDefinition(handle);
        var parent = definition.GetDeclaringType();
        return parent.IsNil
            ? FullName(Text(definition.Namespace), Text(definition.Name))
            : DefinitionName(parent, visited) + "+" + Text(definition.Name);
    }

    private string Decode(EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                return Value("named", Assembly, DefinitionName((TypeDefinitionHandle)handle));
            case HandleKind.TypeReference:
                var reference = _metadata.GetTypeReference((TypeReferenceHandle)handle);
                var (assembly, name) = ReferenceName(reference, []);
                return Value("named", assembly, name);
            case HandleKind.TypeSpecification:
                return _metadata.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(this, null);
            case HandleKind.FieldDefinition:
                var field = _metadata.GetFieldDefinition((FieldDefinitionHandle)handle);
                return Value("field", Resolve(field.GetDeclaringType()), Text(field.Name), field.DecodeSignature(this, null));
            case HandleKind.MethodDefinition:
                var method = _metadata.GetMethodDefinition((MethodDefinitionHandle)handle);
                return Value("method", Resolve(method.GetDeclaringType()), Text(method.Name), Method(method.DecodeSignature(this, null)));
            case HandleKind.MemberReference:
                var member = _metadata.GetMemberReference((MemberReferenceHandle)handle);
                return member.GetKind() == MemberReferenceKind.Field
                    ? Value("field", Resolve(member.Parent), Text(member.Name), member.DecodeFieldSignature(this, null))
                    : Value("method", Resolve(member.Parent), Text(member.Name), Method(member.DecodeMethodSignature(this, null)));
            case HandleKind.MethodSpecification:
                var specification = _metadata.GetMethodSpecification((MethodSpecificationHandle)handle);
                return Value("method-instance", Resolve(specification.Method), Sequence("arguments", specification.DecodeSignature(this, null)));
            case HandleKind.StandaloneSignature:
                var signature = _metadata.GetStandaloneSignature((StandaloneSignatureHandle)handle);
                return signature.GetKind() == StandaloneSignatureKind.LocalVariables
                    ? Sequence("locals", signature.DecodeLocalSignature(this, null))
                    : Method(signature.DecodeMethodSignature(this, null));
            case HandleKind.ModuleReference:
                return Value("module", Text(_metadata.GetModuleReference((ModuleReferenceHandle)handle).Name));
            case HandleKind.ModuleDefinition:
                return Value("module", Assembly);
            case HandleKind.AssemblyReference:
                return Value("assembly", Text(_metadata.GetAssemblyReference((AssemblyReferenceHandle)handle).Name));
            case HandleKind.AssemblyDefinition:
                return Value("assembly", Assembly);
            case HandleKind.AssemblyFile:
                var file = _metadata.GetAssemblyFile((AssemblyFileHandle)handle);
                return Value("file", Text(file.Name), file.ContainsMetadata, Convert.ToHexString(_metadata.GetBlobBytes(file.HashValue)));
            case HandleKind.ExportedType:
                var exported = _metadata.GetExportedType((ExportedTypeHandle)handle);
                return Value("exported", Text(exported.Namespace), Text(exported.Name), exported.Attributes, Resolve(exported.Implementation));
            case HandleKind.ManifestResource:
                return Value("resource", Text(_metadata.GetManifestResource((ManifestResourceHandle)handle).Name));
            case HandleKind.DeclarativeSecurityAttribute:
                var security = _metadata.GetDeclarativeSecurityAttribute((DeclarativeSecurityAttributeHandle)handle);
                return Value("security", Resolve(security.Parent), security.Action, Convert.ToHexString(_metadata.GetBlobBytes(security.PermissionSet)));
            default:
                throw new BadImageFormatException($"Unsupported metadata token kind: {handle.Kind}.");
        }
    }

    private (string Assembly, string Name) ReferenceName(TypeReference reference, HashSet<TypeReferenceHandle> visited)
    {
        if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            var parentHandle = (TypeReferenceHandle)reference.ResolutionScope;
            if (!visited.Add(parentHandle))
            {
                throw new BadImageFormatException("Cyclic nested type reference.");
            }

            var parent = ReferenceName(_metadata.GetTypeReference(parentHandle), visited);
            return (parent.Assembly, parent.Name + "+" + Text(reference.Name));
        }

        var assembly = reference.ResolutionScope.Kind == HandleKind.AssemblyReference
            ? Text(_metadata.GetAssemblyReference((AssemblyReferenceHandle)reference.ResolutionScope).Name)
            : Assembly;
        return (assembly, FullName(Text(reference.Namespace), Text(reference.Name)));
    }

    private string Serialized(TypeName type, IReadOnlySet<string>? solutionAssemblies = null)
    {
        if (type.IsArray)
        {
            return Value(type.IsSZArray ? "vector-name" : "array-name", type.GetArrayRank(), Serialized(type.GetElementType(), solutionAssemblies));
        }

        if (type.IsPointer || type.IsByRef)
        {
            return Value(type.IsPointer ? "pointer-name" : "byref-name", Serialized(type.GetElementType(), solutionAssemblies));
        }

        if (type.IsConstructedGenericType)
        {
            return Value("generic-name", Serialized(type.GetGenericTypeDefinition(), solutionAssemblies),
                Sequence("arguments", type.GetGenericArguments().Select(argument => Serialized(argument, solutionAssemblies))));
        }

        var assembly = type.AssemblyName?.Name ?? Assembly;
        var identity = Value("named", assembly, TypeName.Unescape(type.FullName));
        return solutionAssemblies is null ? identity : Value("versioned-name", identity,
            assembly == Assembly || solutionAssemblies.Contains(assembly) ? "" : type.AssemblyName?.Version?.ToString());
    }

    private string Text(StringHandle handle) => _metadata.GetString(handle);

    private static string FullName(string space, string name) => space.Length == 0 ? name : space + "." + name;

    private readonly MetadataReader _metadata;
    private readonly Dictionary<EntityHandle, string> _resolved = [];
    private readonly HashSet<EntityHandle> _active = [];
    private readonly Dictionary<string, TypeDefinitionHandle> _definitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _arrays = new(StringComparer.Ordinal);
    private readonly HashSet<string> _systemTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _attributeIdentities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _storageTypes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pointers = new(StringComparer.Ordinal);
}
