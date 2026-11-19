using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using RoslynMcpServer.Services.Testing.Il.Models;
using static RoslynMcpServer.Services.Testing.Il.IlFingerprintEncoding;

namespace RoslynMcpServer.Services.Testing.Il;

/// <summary>Reads every behavior-bearing fact before publishing any hashes; a damaged section fails the whole image.</summary>
internal sealed class IlFingerprintReader
{
    public IlFingerprintReader(PEReader pe, MetadataReader metadata, IReadOnlySet<string> solutionAssemblies)
    {
        _pe = pe;
        _metadata = metadata;
        _names = new IlFingerprintNames(metadata);
        _attributes = new IlFingerprintAttributes(metadata, _names);
        _fieldData = new IlFingerprintFieldData(pe, metadata, _names);
        _solutionAssemblies = solutionAssemblies;
    }

    public IlFingerprintSnapshot Read()
    {
        var groups = new Dictionary<TypeDefinitionHandle, List<string>>();
        foreach (var handle in _metadata.TypeDefinitions)
        {
            var top = TopLevel(handle);
            if (!groups.TryGetValue(top, out var values))
            {
                values = [];
                groups.Add(top, values);
            }

            values.Add(Type(handle));
        }

        var types = groups.ToImmutableDictionary(
            pair => new IlTypeName(_names.Assembly, _names.DefinitionName(pair.Key)),
            pair => Hash(Set("type-tree", pair.Value)));
        return new IlFingerprintSnapshot(_names.Assembly, Hash(Assembly(_solutionAssemblies)), types);
    }

    private TypeDefinitionHandle TopLevel(TypeDefinitionHandle handle)
    {
        var visited = new HashSet<TypeDefinitionHandle>();
        while (visited.Add(handle))
        {
            var parent = _metadata.GetTypeDefinition(handle).GetDeclaringType();
            if (parent.IsNil)
            {
                return handle;
            }

            handle = parent;
        }

        throw new BadImageFormatException("Cyclic nested type ownership.");
    }

    private string Type(TypeDefinitionHandle handle)
    {
        var type = _metadata.GetTypeDefinition(handle);
        var layout = type.GetLayout();
        return Value("type-definition", _names.Resolve(handle), type.Attributes, _names.Resolve(type.BaseType),
            layout.IsDefault, layout.PackingSize, layout.Size,
            _attributes.Read(type.GetCustomAttributes()), Security(type.GetDeclarativeSecurityAttributes()),
            Generics(type.GetGenericParameters()),
            Set("interfaces", type.GetInterfaceImplementations().Select(Interface)),
            Sequence("fields", type.GetFields().Select(Field)),
            Set("methods", type.GetMethods().Select(Method)),
            Set("properties", type.GetProperties().Select(Property)),
            Set("events", type.GetEvents().Select(Event)),
            Set("implementations", type.GetMethodImplementations().Select(Implementation)));
    }

    private string Interface(InterfaceImplementationHandle handle)
    {
        var implementation = _metadata.GetInterfaceImplementation(handle);
        return Value("interface", _names.Resolve(implementation.Interface), _attributes.Read(implementation.GetCustomAttributes()));
    }

    private string Implementation(MethodImplementationHandle handle)
    {
        var implementation = _metadata.GetMethodImplementation(handle);
        return Value("implementation", _names.Resolve(implementation.MethodBody), _names.Resolve(implementation.MethodDeclaration),
            _attributes.Read(implementation.GetCustomAttributes()));
    }

    private string Field(FieldDefinitionHandle handle)
    {
        var field = _metadata.GetFieldDefinition(handle);
        var signature = field.DecodeSignature(_names, null);
        return Value("field", Text(field.Name), signature, field.Attributes,
            Constant(field.GetDefaultValue()), field.GetOffset(), Marshal(field.GetMarshallingDescriptor()),
            _fieldData.Read(field, signature), _attributes.Read(field.GetCustomAttributes()));
    }

    private string Method(MethodDefinitionHandle handle)
    {
        var method = _metadata.GetMethodDefinition(handle);
        var import = method.GetImport();
        return Value("method", _names.Resolve(handle), method.Attributes, method.ImplAttributes,
            Generics(method.GetGenericParameters()),
            Set("parameters", method.GetParameters().Select(Parameter)),
            _attributes.Read(method.GetCustomAttributes()), Security(method.GetDeclarativeSecurityAttributes()),
            import.Module.IsNil ? "no-import" : Value("import", _names.Resolve(import.Module), Text(import.Name), import.Attributes),
            method.RelativeVirtualAddress == 0 ? "no-body" : Body(_pe.GetMethodBody(method.RelativeVirtualAddress)));
    }

    private string Parameter(ParameterHandle handle)
    {
        var parameter = _metadata.GetParameter(handle);
        return Value("parameter", parameter.SequenceNumber, Text(parameter.Name), parameter.Attributes,
            Constant(parameter.GetDefaultValue()), Marshal(parameter.GetMarshallingDescriptor()),
            _attributes.Read(parameter.GetCustomAttributes()));
    }

    private string Property(PropertyDefinitionHandle handle)
    {
        var property = _metadata.GetPropertyDefinition(handle);
        var accessors = property.GetAccessors();
        return Value("property", Text(property.Name), property.Attributes, _names.Method(property.DecodeSignature(_names, null)),
            Constant(property.GetDefaultValue()), _attributes.Read(property.GetCustomAttributes()),
            Value("semantics", _names.Resolve(accessors.Getter), _names.Resolve(accessors.Setter),
                Set("other", accessors.Others.Select(method => _names.Resolve(method)))));
    }

    private string Event(EventDefinitionHandle handle)
    {
        var definition = _metadata.GetEventDefinition(handle);
        var accessors = definition.GetAccessors();
        return Value("event", Text(definition.Name), definition.Attributes, _names.Resolve(definition.Type),
            _attributes.Read(definition.GetCustomAttributes()),
            Value("semantics", _names.Resolve(accessors.Adder), _names.Resolve(accessors.Remover), _names.Resolve(accessors.Raiser),
                Set("other", accessors.Others.Select(method => _names.Resolve(method)))));
    }

    private string Generics(GenericParameterHandleCollection handles) => Set("generics", handles.Select(handle =>
    {
        var parameter = _metadata.GetGenericParameter(handle);
        return Value("generic-parameter", parameter.Index, Text(parameter.Name), parameter.Attributes,
            _attributes.Read(parameter.GetCustomAttributes()), Set("constraints", parameter.GetConstraints().Select(constraintHandle =>
            {
                var constraint = _metadata.GetGenericParameterConstraint(constraintHandle);
                return Value("constraint", _names.Resolve(constraint.Type), _attributes.Read(constraint.GetCustomAttributes()));
            })));
    }));

    private string Constant(ConstantHandle handle)
    {
        if (handle.IsNil)
        {
            return "no-constant";
        }

        var constant = _metadata.GetConstant(handle);
        return Value("constant", constant.TypeCode, Blob(constant.Value));
    }

    private string Marshal(BlobHandle handle)
    {
        if (handle.IsNil)
        {
            return "nil";
        }

        var reader = _metadata.GetBlobReader(handle);
        if (reader.RemainingBytes == 0)
        {
            return Blob(handle);
        }

        const byte CustomMarshaler = 0x2C;
        const byte SafeArray = 0x1D;
        var nativeType = reader.ReadByte();
        if (nativeType == CustomMarshaler)
        {
            reader.ReadSerializedString(); // GUID.
            reader.ReadSerializedString(); // Native type name.
        }
        else if (nativeType == SafeArray)
        {
            if (reader.RemainingBytes == 0)
            {
                return Blob(handle);
            }

            reader.ReadCompressedInteger(); // Variant subtype.
            if (reader.RemainingBytes == 0)
            {
                return Blob(handle);
            }
        }
        else
        {
            return Blob(handle);
        }

        var start = reader.Offset;
        var managedType = reader.ReadSerializedString();
        var end = reader.Offset;
        if (nativeType == CustomMarshaler)
        {
            reader.ReadSerializedString(); // Cookie.
        }

        if (reader.RemainingBytes != 0)
        {
            throw new BadImageFormatException("Unexpected data after the typed marshaling descriptor.");
        }

        // MarshalTypeRef and SafeArrayUserDefinedSubType embed type names; subtype/cookie bytes are ordinary data.
        // See docs/rapid-tia-2/mvp/evidence/task-02-review-round-3.md, R3-F-01.
        var bytes = _metadata.GetBlobBytes(handle);
        return Value("typed-marshal", Convert.ToHexString(bytes.AsSpan(0, start)),
            managedType is null ? "null" : _names.SerializedType(managedType, _solutionAssemblies),
            Convert.ToHexString(bytes.AsSpan(end)));
    }

    private string Security(DeclarativeSecurityAttributeHandleCollection handles) => Set("security", handles.Select(handle =>
    {
        var security = _metadata.GetDeclarativeSecurityAttribute(handle);
        return Value("security", security.Action, _attributes.ReadSecurity(security.PermissionSet, _solutionAssemblies),
            _attributes.Read(_metadata.GetCustomAttributes(handle)));
    }));

    private string Body(MethodBodyBlock body)
    {
        return Value("body", body.MaxStack, body.LocalVariablesInitialized, _names.Resolve(body.LocalSignature),
            Code(body.GetILReader()), Sequence("exceptions", body.ExceptionRegions.Select(region =>
                Value("exception", region.Kind, region.TryOffset, region.TryLength, region.HandlerOffset, region.HandlerLength,
                    region.Kind == ExceptionRegionKind.Catch ? _names.Resolve(region.CatchType) : "nil",
                    region.Kind == ExceptionRegionKind.Filter ? region.FilterOffset : 0))));
    }

    private string Code(BlobReader reader)
    {
        var instructions = new List<string>();
        while (reader.RemainingBytes > 0)
        {
            var first = reader.ReadByte();
            var twoByte = first == 0xFE;
            var opcode = twoByte ? reader.ReadByte() : first;
            if (!IlOperandSizes.TryGet(twoByte, opcode, out var operand))
            {
                throw new BadImageFormatException("Unknown IL opcode.");
            }

            string value;
            switch (operand)
            {
                case OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineTok
                    or OperandType.InlineType or OperandType.InlineSig:
                    value = _names.Resolve(MetadataTokens.EntityHandle(reader.ReadInt32()));
                    break;
                case OperandType.InlineString:
                    value = Value("string", _metadata.GetUserString(MetadataTokens.UserStringHandle(reader.ReadInt32())));
                    break;
                case OperandType.InlineSwitch:
                    var count = reader.ReadInt32();
                    if (count < 0 || count > reader.RemainingBytes / 4)
                    {
                        throw new BadImageFormatException("Invalid IL switch length.");
                    }

                    value = Value("switch", count, Convert.ToHexString(reader.ReadBytes(count * 4)));
                    break;
                default:
                    var size = operand switch
                    {
                        OperandType.InlineNone => 0,
                        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                        OperandType.InlineVar => 2,
                        OperandType.InlineI8 or OperandType.InlineR => 8,
                        OperandType.InlineI or OperandType.InlineBrTarget or OperandType.ShortInlineR => 4,
                        _ => throw new BadImageFormatException("Unknown IL operand kind."),
                    };
                    value = Convert.ToHexString(reader.ReadBytes(size));
                    break;
            }

            instructions.Add(Value("instruction", twoByte, opcode, value));
        }

        return Sequence("code", instructions);
    }

    private string Assembly(IReadOnlySet<string> solutionAssemblies)
    {
        var assembly = _metadata.GetAssemblyDefinition();
        var module = _metadata.GetModuleDefinition();
        return Value("assembly", Text(assembly.Name), assembly.Flags, Text(assembly.Culture), Blob(assembly.PublicKey), assembly.HashAlgorithm,
            _attributes.Read(assembly.GetCustomAttributes(), excludeBuildVersions: true), _attributes.Read(module.GetCustomAttributes()),
            OtherAttributes(),
            Security(assembly.GetDeclarativeSecurityAttributes()),
            Set("references", _metadata.AssemblyReferences.Select(handle =>
            {
                var reference = _metadata.GetAssemblyReference(handle);
                var name = Text(reference.Name);
                return Value("reference", name, solutionAssemblies.Contains(name) ? "" : reference.Version.ToString());
            })),
            Set("resources", _metadata.ManifestResources.Select(Resource)),
            Set("forwarders", _metadata.ExportedTypes.Select(handle => _names.Resolve(handle))),
            Set("files", _metadata.AssemblyFiles.Select(handle => _names.Resolve(handle))));
    }

    private string OtherAttributes() => Set("other-attributes", _metadata.CustomAttributes
        .Select(handle => (Handle: handle, Attribute: _metadata.GetCustomAttribute(handle)))
        .Where(item => item.Attribute.Parent.Kind is not (HandleKind.TypeDefinition or HandleKind.FieldDefinition
            or HandleKind.MethodDefinition or HandleKind.Parameter or HandleKind.PropertyDefinition or HandleKind.EventDefinition
            or HandleKind.GenericParameter or HandleKind.GenericParameterConstraint or HandleKind.InterfaceImplementation
            or HandleKind.MethodImplementation or HandleKind.DeclarativeSecurityAttribute
            or HandleKind.AssemblyDefinition or HandleKind.ModuleDefinition))
        .Select(item => Value("owner", _names.Resolve(item.Attribute.Parent), _attributes.Read([item.Handle]))));

    private string Resource(ManifestResourceHandle handle)
    {
        var resource = _metadata.GetManifestResource(handle);
        if (!resource.Implementation.IsNil)
        {
            return Value("linked-resource", Text(resource.Name), resource.Attributes, resource.Offset, _names.Resolve(resource.Implementation));
        }

        var directory = _pe.PEHeaders.CorHeader!.ResourcesDirectory;
        if (resource.Offset > int.MaxValue || directory.Size < 4 || resource.Offset > directory.Size - 4)
        {
            throw new BadImageFormatException("An embedded resource is outside the managed resources directory.");
        }

        var reader = _pe.GetSectionData(directory.RelativeVirtualAddress).GetReader((int)resource.Offset, directory.Size - (int)resource.Offset);
        var length = reader.ReadInt32();
        return Value("resource", Text(resource.Name), resource.Attributes, Convert.ToHexString(reader.ReadBytes(length)));
    }

    private string Text(StringHandle handle) => _metadata.GetString(handle);

    private string Blob(BlobHandle handle) => handle.IsNil ? "nil" : Convert.ToHexString(_metadata.GetBlobBytes(handle));

    private readonly PEReader _pe;
    private readonly MetadataReader _metadata;
    private readonly IlFingerprintNames _names;
    private readonly IlFingerprintAttributes _attributes;
    private readonly IlFingerprintFieldData _fieldData;
    private readonly IReadOnlySet<string> _solutionAssemblies;
}
