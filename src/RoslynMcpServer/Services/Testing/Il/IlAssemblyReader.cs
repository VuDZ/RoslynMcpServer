using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using RoslynMcpServer.Services.Testing.Il.Models;

namespace RoslynMcpServer.Services.Testing.Il;

/// <summary>
/// Reads the dependencies of every top-level type of one assembly from metadata and IL. Nothing is loaded
/// for execution and no compiler is involved.
/// </summary>
internal sealed class IlAssemblyReader
{
    private IlAssemblyReader(PEReader image, MetadataReader metadata)
    {
        _image = image;
        _metadata = metadata;
        _assembly = metadata.GetString(metadata.GetAssemblyDefinition().Name);
        _signatures = new IlSignatureTypes(Definition, Reference);
        _owners = new TypeDraft[metadata.TypeDefinitions.Count + 1];
    }

    /// <summary>Parses an image that is already in memory. A damaged image gives a failure, not an exception.</summary>
    public static IlAssemblyReadResult Read(IlAssemblyImage image)
    {
        try
        {
            using var pe = new PEReader(image.Content);
            if (!pe.HasMetadata)
            {
                return Failed(image.Path, "The file is not a managed assembly: it has no CLI metadata.");
            }

            var metadata = pe.GetMetadataReader();
            if (!metadata.IsAssembly)
            {
                return Failed(image.Path, "The file is a module without an assembly manifest.");
            }

            var reader = new IlAssemblyReader(pe, metadata);
            return IlAssemblyReadResult.Read(image.Path, reader.ReadAll());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A file caught in the middle of a write, or damaged in any other way, fails in the metadata
            // reader in more ways than BadImageFormatException: a handle of the wrong kind, an offset past
            // the end of a heap. For the caller all of them mean the same: this file gives no facts now.
            return Failed(image.Path, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Loads the file into memory, parses it and drops the image.</summary>
    public static IlAssemblyReadResult ReadFile(string path)
    {
        var load = IlAssemblyImageLoader.Load(path);
        return load.Image is null ? IlAssemblyReadResult.Unread(load.Failure!) : Read(load.Image);
    }

    private static IlAssemblyReadResult Failed(string path, string message) =>
        IlAssemblyReadResult.Unread(new IlUnreadAssembly(path, IlAssemblyFailureKind.NotAnAssembly, message));

    private static string FullName(string @namespace, string name) =>
        @namespace.Length == 0 ? name : @namespace + "." + name;

    private IlAssemblyFacts ReadAll()
    {
        var forwarders = ReadForwarders();
        var types = new List<TypeDraft>();
        foreach (var handle in _metadata.TypeDefinitions)
        {
            var top = TopLevel(handle);
            var row = MetadataTokens.GetRowNumber(top);
            if (_owners[row] is null)
            {
                _owners[row] = NewType(top);
                types.Add(_owners[row]);
            }

            _owners[MetadataTokens.GetRowNumber(handle)] = _owners[row];
        }

        foreach (var handle in _metadata.TypeDefinitions)
        {
            ReadType(handle);
        }

        foreach (var type in ArgumentTypes(_metadata.GetAssemblyDefinition().GetCustomAttributes()))
        {
            _preamble.Add(type);
        }

        return new IlAssemblyFacts
        {
            Name = _assembly,
            UnreadableBodies = _unreadableBodies,
            Types = [.. types.Select(type => type.ToFacts())],
            Preamble = Sorted(_preamble),
            Forwarders = forwarders,
        };
    }

    private Dictionary<string, string> ReadForwarders()
    {
        var forwarders = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var handle in _metadata.ExportedTypes)
        {
            var exported = _metadata.GetExportedType(handle);
            if (exported.IsForwarder && exported.Implementation.Kind == HandleKind.AssemblyReference)
            {
                var target = _metadata.GetAssemblyReference((AssemblyReferenceHandle)exported.Implementation);
                forwarders[FullName(_metadata.GetString(exported.Namespace), _metadata.GetString(exported.Name))] =
                    _metadata.GetString(target.Name);
            }
        }

        return forwarders;
    }

    private TypeDraft NewType(TypeDefinitionHandle top)
    {
        var definition = _metadata.GetTypeDefinition(top);
        var name = FullName(_metadata.GetString(definition.Namespace), _metadata.GetString(definition.Name));
        return new TypeDraft(new IlTypeName(_assembly, name))
        {
            IsAbstract = (definition.Attributes & (TypeAttributes.Abstract | TypeAttributes.Sealed)) == TypeAttributes.Abstract,
        };
    }

    private TypeDefinitionHandle TopLevel(TypeDefinitionHandle handle)
    {
        while (true)
        {
            var declaring = _metadata.GetTypeDefinition(handle).GetDeclaringType();
            if (declaring.IsNil)
            {
                return handle;
            }

            handle = declaring;
        }
    }

    private void ReadType(TypeDefinitionHandle handle)
    {
        var definition = _metadata.GetTypeDefinition(handle);
        var owner = _owners[MetadataTokens.GetRowNumber(handle)];
        AddSupertype(owner, definition.BaseType);
        foreach (var implementation in definition.GetInterfaceImplementations())
        {
            AddSupertype(owner, _metadata.GetInterfaceImplementation(implementation).Interface);
        }

        AddConstraints(owner, definition.GetGenericParameters());
        AddAttributes(owner, definition.GetCustomAttributes(), method: null, typeLevel: true);
        foreach (var field in definition.GetFields())
        {
            var fieldDefinition = _metadata.GetFieldDefinition(field);
            _signatures.Reset();
            fieldDefinition.DecodeSignature(_signatures, null);
            AddCollected(owner);
            AddAttributes(owner, fieldDefinition.GetCustomAttributes(), method: null, typeLevel: false);
        }

        foreach (var property in definition.GetProperties())
        {
            AddAttributes(owner, _metadata.GetPropertyDefinition(property).GetCustomAttributes(), method: null, typeLevel: false);
        }

        var prefix = NestedName(handle) + ".";
        foreach (var method in definition.GetMethods())
        {
            ReadMethod(owner, method, prefix);
        }
    }

    private string NestedName(TypeDefinitionHandle handle)
    {
        var definition = _metadata.GetTypeDefinition(handle);
        var declaring = definition.GetDeclaringType();
        return declaring.IsNil
            ? FullName(_metadata.GetString(definition.Namespace), _metadata.GetString(definition.Name))
            : NestedName(declaring) + "+" + _metadata.GetString(definition.Name);
    }

    private void ReadMethod(TypeDraft owner, MethodDefinitionHandle handle, string prefix)
    {
        var method = _metadata.GetMethodDefinition(handle);
        try
        {
            _signatures.Reset();
            method.DecodeSignature(_signatures, null);
            AddCollected(owner);
            AddConstraints(owner, method.GetGenericParameters());
            AddAttributes(owner, method.GetCustomAttributes(), prefix + _metadata.GetString(method.Name), typeLevel: false);
            if (method.RelativeVirtualAddress == 0)
            {
                return;
            }

            var body = _image.GetMethodBody(method.RelativeVirtualAddress);
            if (!body.LocalSignature.IsNil)
            {
                Add(owner, Resolve(body.LocalSignature));
            }

            foreach (var region in body.ExceptionRegions)
            {
                if (region.Kind == ExceptionRegionKind.Catch && !region.CatchType.IsNil)
                {
                    Add(owner, Resolve(region.CatchType));
                }
            }

            ScanBody(owner, body.GetILReader());
        }
        catch (BadImageFormatException)
        {
            _unreadableBodies++;
        }
    }

    private void ScanBody(TypeDraft owner, BlobReader code)
    {
        var position = 0;
        var tokenType = default(IlTypeName);
        var tokenPosition = int.MinValue;
        while (code.RemainingBytes > 0)
        {
            var first = code.ReadByte();
            var twoByte = first == 0xFE;
            var opcode = twoByte ? code.ReadByte() : first;
            if (!IlOperandSizes.TryGet(twoByte, opcode, out var operand))
            {
                _unreadableBodies++;
                return;
            }

            position++;
            switch (operand)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
                    code.Offset += 1;
                    break;
                case OperandType.InlineVar:
                    code.Offset += 2;
                    break;
                case OperandType.InlineI8 or OperandType.InlineR:
                    code.Offset += 8;
                    break;
                case OperandType.InlineSwitch:
                    // Read the count first: "Offset += 4 * Read()" would take the offset before the read.
                    var targets = (int)code.ReadUInt32();
                    code.Offset += 4 * targets;
                    break;
                case OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineTok
                    or OperandType.InlineType or OperandType.InlineSig:
                    var facts = Resolve(MetadataTokens.EntityHandle(code.ReadInt32()));
                    Add(owner, facts);
                    if (!twoByte && opcode == LoadToken)
                    {
                        tokenType = facts.Member is null ? facts.Head : default;
                        tokenPosition = position;
                    }
                    else if (facts.Member is not null)
                    {
                        // typeof(X).Assembly and typeof(X).GetTypeInfo().Assembly put the property call two or
                        // three instructions after the token.
                        var named = position - tokenPosition <= TokenReach ? tokenType : default;
                        AddAssemblyUse(owner, facts, named);
                    }

                    break;
                default:
                    code.Offset += 4;
                    break;
            }
        }
    }

    /// <summary>
    /// Records code that hands a whole assembly to reflection. A framework that enumerates an assembly
    /// (a DI scan, MEF, MVC application parts) reaches types that no code names.
    /// </summary>
    private void AddAssemblyUse(TypeDraft owner, IlTokenFacts call, IlTypeName named)
    {
        var type = call.Head.Name;
        var member = call.Member!;
        if (member == "get_Assembly" && type is "System.Type" or "System.Reflection.TypeInfo")
        {
            // x.GetType().Assembly names no type. It is most often the assembly of the code itself.
            owner.Scans.Add(named.IsNone ? _assembly : named.Assembly);
        }
        else if (type == "System.Reflection.Assembly" && member is "GetExecutingAssembly" or "GetCallingAssembly")
        {
            owner.Scans.Add(_assembly);
        }
        else if ((type == "System.Reflection.Assembly" && (member == "GetEntryAssembly" || member.StartsWith("Load", StringComparison.Ordinal)))
            || (type == "System.AppDomain" && member == "GetAssemblies")
            || (type == "System.Runtime.Loader.AssemblyLoadContext" && member.StartsWith("Load", StringComparison.Ordinal)))
        {
            owner.Loads |= LoadKind(type, member);
        }
        else if (call.TypeArguments.Length > 0
            && (member.Contains("Assembly", StringComparison.Ordinal) || member == "UseStartup"))
        {
            // FromAssemblyOf<T>(), AddValidatorsFromAssemblyContaining<T>(), UseStartup<T>().
            foreach (var argument in call.TypeArguments)
            {
                owner.Scans.Add(argument.Assembly);
            }
        }
    }

    // Assembly.GetEntryAssembly is left out: in a test run it returns the test host or the test assembly,
    // not whatever a string names.
    private static IlAssemblyLoadKinds LoadKind(string type, string member) => type switch
    {
        "System.Reflection.Assembly" when member.StartsWith("Load", StringComparison.Ordinal) => IlAssemblyLoadKinds.AssemblyLoad,
        "System.Runtime.Loader.AssemblyLoadContext" => IlAssemblyLoadKinds.LoadContext,
        "System.AppDomain" => IlAssemblyLoadKinds.LoadedAssemblies,
        _ => IlAssemblyLoadKinds.None,
    };

    private void AddSupertype(TypeDraft owner, EntityHandle handle)
    {
        if (handle.IsNil)
        {
            return;
        }

        var facts = Resolve(handle);
        Add(owner, facts);
        if (!facts.Head.IsNone && facts.Head != owner.Name)
        {
            owner.Supertypes.Add(facts.Head);
        }
    }

    private void AddConstraints(TypeDraft owner, GenericParameterHandleCollection parameters)
    {
        foreach (var parameter in parameters)
        {
            foreach (var constraint in _metadata.GetGenericParameter(parameter).GetConstraints())
            {
                Add(owner, Resolve(_metadata.GetGenericParameterConstraint(constraint).Type));
            }
        }
    }

    private void AddAttributes(TypeDraft owner, CustomAttributeHandleCollection attributes, string? method, bool typeLevel)
    {
        foreach (var handle in attributes)
        {
            var constructor = Resolve(_metadata.GetCustomAttribute(handle).Constructor);
            var type = constructor.Head;
            if (type.IsNone)
            {
                continue;
            }

            Add(owner, constructor);
            if (typeLevel)
            {
                owner.TypeAttributes.Add(type);
            }

            if (method is not null && type.Name == ModuleInitializer)
            {
                _preamble.Add(owner.Name);
            }
            else if (method is not null && !type.Name.StartsWith("System.", StringComparison.Ordinal))
            {
                owner.MethodAttributes.Add(new IlMethodAttribute(method, type));
            }
        }

        foreach (var type in ArgumentTypes(attributes))
        {
            if (type != owner.Name)
            {
                owner.Dependencies.Add(type);
            }
        }
    }

    /// <summary>Types passed as <c>typeof(...)</c> arguments: they are stored as strings, not as tokens.</summary>
    private List<IlTypeName> ArgumentTypes(CustomAttributeHandleCollection attributes)
    {
        var types = new List<IlTypeName>();
        foreach (var handle in attributes)
        {
            var attribute = _metadata.GetCustomAttribute(handle);
            if (Resolve(attribute.Constructor).Head.Name?.StartsWith(CompilerAttributes, StringComparison.Ordinal) != false)
            {
                continue;
            }

            try
            {
                var value = attribute.DecodeValue(_attributeTypes);
                foreach (var argument in value.FixedArguments)
                {
                    AddArgumentTypes(types, argument.Type, argument.Value);
                }

                foreach (var argument in value.NamedArguments)
                {
                    AddArgumentTypes(types, argument.Type, argument.Value);
                }
            }
            catch (Exception ex) when (ex is BadImageFormatException or ArgumentException or InvalidCastException)
            {
                // An enum argument that is not Int32-sized; see IlAttributeTypeNames.GetUnderlyingEnumType.
            }
        }

        return types;
    }

    private void AddArgumentTypes(List<IlTypeName> types, string type, object? value)
    {
        if (value is string serialized && type == IlAttributeTypeNames.SystemType)
        {
            types.AddRange(IlAttributeTypeNames.Parse(serialized, _assembly));
        }
        else if (value is ImmutableArray<CustomAttributeTypedArgument<string>> elements)
        {
            foreach (var element in elements)
            {
                AddArgumentTypes(types, element.Type, element.Value);
            }
        }
    }

    private void AddCollected(TypeDraft owner)
    {
        foreach (var type in _signatures.Types)
        {
            if (type != owner.Name)
            {
                owner.Dependencies.Add(type);
            }
        }

        owner.Scans.UnionWith(_signatures.Scans);
    }

    private static void Add(TypeDraft owner, IlTokenFacts facts)
    {
        foreach (var type in facts.Types)
        {
            if (type != owner.Name)
            {
                owner.Dependencies.Add(type);
            }
        }

        foreach (var scan in facts.Scans)
        {
            owner.Scans.Add(scan);
        }
    }

    private IlTokenFacts Resolve(EntityHandle handle)
    {
        if (!_tokens.TryGetValue(handle, out var facts))
        {
            facts = Decode(handle);
            _tokens[handle] = facts;
        }

        return facts;
    }

    private IlTokenFacts Decode(EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                return Named(Definition((TypeDefinitionHandle)handle), member: null);
            case HandleKind.TypeReference:
                return Named(Reference((TypeReferenceHandle)handle), member: null);
            case HandleKind.TypeSpecification:
                _signatures.Reset();
                var head = _metadata.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(_signatures, null);
                return Collected(head, member: null, [], IlTokenFacts.Empty);
            case HandleKind.MethodDefinition:
                var method = _metadata.GetMethodDefinition((MethodDefinitionHandle)handle);
                return Named(Definition(method.GetDeclaringType()), _metadata.GetString(method.Name));
            case HandleKind.FieldDefinition:
                var field = _metadata.GetFieldDefinition((FieldDefinitionHandle)handle);
                return Named(Definition(field.GetDeclaringType()), _metadata.GetString(field.Name));
            case HandleKind.MemberReference:
                return DecodeMember(_metadata.GetMemberReference((MemberReferenceHandle)handle));
            case HandleKind.MethodSpecification:
                var specification = _metadata.GetMethodSpecification((MethodSpecificationHandle)handle);
                var generic = Resolve(specification.Method);
                _signatures.Reset();
                var arguments = specification.DecodeSignature(_signatures, null);
                return Collected(generic.Head, generic.Member, [.. arguments.Where(argument => !argument.IsNone)], generic);
            case HandleKind.StandaloneSignature:
                return DecodeStandalone(_metadata.GetStandaloneSignature((StandaloneSignatureHandle)handle));
            default:
                return IlTokenFacts.Empty;
        }
    }

    private IlTokenFacts DecodeMember(MemberReference member)
    {
        var parent = member.Parent.Kind is HandleKind.TypeDefinition or HandleKind.TypeReference
            or HandleKind.TypeSpecification or HandleKind.MethodDefinition
            ? Resolve(member.Parent)
            : IlTokenFacts.Empty;
        _signatures.Reset();
        if (member.GetKind() == MemberReferenceKind.Method)
        {
            member.DecodeMethodSignature(_signatures, null);
        }
        else
        {
            member.DecodeFieldSignature(_signatures, null);
        }

        return Collected(parent.Head, _metadata.GetString(member.Name), [], parent);
    }

    private IlTokenFacts DecodeStandalone(StandaloneSignature signature)
    {
        _signatures.Reset();
        if (signature.GetKind() == StandaloneSignatureKind.Method)
        {
            signature.DecodeMethodSignature(_signatures, null);
        }
        else
        {
            signature.DecodeLocalSignature(_signatures, null);
        }

        return Collected(default, member: null, [], IlTokenFacts.Empty);
    }

    private static IlTokenFacts Named(IlTypeName type, string? member) =>
        new() { Head = type, Member = member, Types = [type] };

    private IlTokenFacts Collected(IlTypeName head, string? member, IlTypeName[] typeArguments, IlTokenFacts inner)
    {
        var types = new HashSet<IlTypeName>(_signatures.Types);
        types.UnionWith(inner.Types);
        if (!head.IsNone)
        {
            types.Add(head);
        }

        return new IlTokenFacts
        {
            Head = head,
            Member = member,
            Types = [.. types],
            TypeArguments = typeArguments,
            Scans = [.. _signatures.Scans.Union(inner.Scans, StringComparer.OrdinalIgnoreCase)],
        };
    }

    private IlTypeName Definition(TypeDefinitionHandle handle) => _owners[MetadataTokens.GetRowNumber(handle)].Name;

    private IlTypeName Reference(TypeReferenceHandle handle)
    {
        if (_references.TryGetValue(handle, out var known))
        {
            return known;
        }

        var reference = _metadata.GetTypeReference(handle);
        var scope = reference.ResolutionScope;
        IlTypeName name;
        if (scope.Kind == HandleKind.TypeReference)
        {
            name = Reference((TypeReferenceHandle)scope);
        }
        else
        {
            var assembly = scope.Kind == HandleKind.AssemblyReference
                ? _metadata.GetString(_metadata.GetAssemblyReference((AssemblyReferenceHandle)scope).Name)
                : _assembly;
            name = new IlTypeName(assembly, FullName(_metadata.GetString(reference.Namespace), _metadata.GetString(reference.Name)));
        }

        _references[handle] = name;
        return name;
    }

    private static IlTypeName[] Sorted(IEnumerable<IlTypeName> types) =>
        [.. types.OrderBy(type => type.Assembly, StringComparer.Ordinal).ThenBy(type => type.Name, StringComparer.Ordinal)];

    private const int TokenReach = 3;
    private const byte LoadToken = 0xD0;
    private const string ModuleInitializer = "System.Runtime.CompilerServices.ModuleInitializerAttribute";
    private const string CompilerAttributes = "System.Runtime.CompilerServices.";

    /// <summary>The facts of one top-level type while its members and nested types are being read.</summary>
    private sealed class TypeDraft
    {
        public IlTypeName Name { get; }

        public bool IsAbstract { get; init; }

        public IlAssemblyLoadKinds Loads { get; set; }

        public HashSet<IlTypeName> Dependencies { get; } = [];

        public HashSet<IlTypeName> Supertypes { get; } = [];

        public HashSet<IlTypeName> TypeAttributes { get; } = [];

        public HashSet<IlMethodAttribute> MethodAttributes { get; } = [];

        public HashSet<string> Scans { get; } = new(StringComparer.OrdinalIgnoreCase);

        public TypeDraft(IlTypeName name)
        {
            Name = name;
        }

        public IlTypeFacts ToFacts() => new()
        {
            Name = Name,
            IsAbstract = IsAbstract,
            Loads = Loads,
            Dependencies = Sorted(Dependencies),
            Supertypes = Sorted(Supertypes),
            TypeAttributes = Sorted(TypeAttributes),
            MethodAttributes =
            [
                .. MethodAttributes
                    .OrderBy(attribute => attribute.Method, StringComparer.Ordinal)
                    .ThenBy(attribute => attribute.Attribute.Assembly, StringComparer.Ordinal)
                    .ThenBy(attribute => attribute.Attribute.Name, StringComparer.Ordinal),
            ],
            Scans = [.. Scans.Order(StringComparer.Ordinal)],
        };
    }

    private static readonly IlAttributeTypeNames _attributeTypes = new();

    private readonly PEReader _image;
    private readonly MetadataReader _metadata;
    private readonly string _assembly;
    private readonly IlSignatureTypes _signatures;
    private readonly TypeDraft[] _owners;
    private readonly Dictionary<EntityHandle, IlTokenFacts> _tokens = [];
    private readonly Dictionary<TypeReferenceHandle, IlTypeName> _references = [];
    private readonly HashSet<IlTypeName> _preamble = [];
    private int _unreadableBodies;
}
