using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace TestImpactIlProbe;

/// <summary>
/// Reads the dependencies of every top-level type of one assembly from metadata, IL and the portable PDB.
/// Nothing is loaded for execution and no compiler is involved.
/// </summary>
internal sealed class AssemblyReader
{
    private AssemblyReader(PEReader image, string path, DocumentPaths paths)
    {
        _image = image;
        _paths = paths;
        _metadata = image.GetMetadataReader();
        _facts = new AssemblyFacts(
            _metadata.GetString(_metadata.GetAssemblyDefinition().Name),
            _metadata.GetGuid(_metadata.GetModuleDefinition().Mvid),
            path);
        _signatures = new SignatureTypes(Definition, Reference);
        _owners = new TypeFacts[_metadata.TypeDefinitions.Count + 1];
    }

    /// <summary>Returns null for a file that is not a managed assembly.</summary>
    public static AssemblyIdentity? Identify(string path, DocumentPaths paths)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var image = new PEReader(stream);
            if (!image.HasMetadata)
            {
                return null;
            }

            var metadata = image.GetMetadataReader();
            if (!metadata.IsAssembly)
            {
                return null;
            }

            var name = metadata.GetString(metadata.GetAssemblyDefinition().Name);
            var mvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
            var origin = IsReferenceAssembly(metadata) ? AssemblyOrigin.ReferenceAssembly : Origin(image, path, paths);
            return new AssemblyIdentity(path, name, mvid, origin);
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static AssemblyFacts Read(string path, DocumentPaths paths)
    {
        using var stream = File.OpenRead(path);
        using var image = new PEReader(stream, PEStreamOptions.PrefetchEntireImage);
        var reader = new AssemblyReader(image, path, paths);
        reader.ReadAll(path);
        return reader._facts;
    }

    private static AssemblyOrigin Origin(PEReader image, string path, DocumentPaths paths)
    {
        MetadataReaderProvider? provider = null;
        try
        {
            if (!image.TryOpenAssociatedPortablePdb(path, OpenPdb, out provider, out _) || provider is null)
            {
                return File.Exists(Path.ChangeExtension(path, ".pdb")) ? AssemblyOrigin.UnreadablePdb : AssemblyOrigin.External;
            }

            var pdb = provider.GetMetadataReader();
            var exists = pdb.Documents
                .Take(DocumentsToCheck)
                .Any(handle => File.Exists(paths.Map(pdb.GetString(pdb.GetDocument(handle).Name))));
            return exists ? AssemblyOrigin.Sources : AssemblyOrigin.External;
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException)
        {
            return AssemblyOrigin.UnreadablePdb;
        }
        finally
        {
            provider?.Dispose();
        }
    }

    private static Stream? OpenPdb(string path) => File.Exists(path) ? File.OpenRead(path) : null;

    private static bool IsReferenceAssembly(MetadataReader metadata)
    {
        foreach (var handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
        {
            var constructor = metadata.GetCustomAttribute(handle).Constructor;
            if (constructor.Kind == HandleKind.MemberReference
                && metadata.GetMemberReference((MemberReferenceHandle)constructor).Parent is { Kind: HandleKind.TypeReference } parent
                && metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)parent).Name) == "ReferenceAssemblyAttribute")
            {
                return true;
            }
        }

        return false;
    }

    private static string FullName(string @namespace, string name) =>
        @namespace.Length == 0 ? name : @namespace + "." + name;

    private void ReadAll(string path)
    {
        foreach (var handle in _metadata.AssemblyReferences)
        {
            _facts.References.Add(_metadata.GetString(_metadata.GetAssemblyReference(handle).Name));
        }

        ReadForwarders();
        foreach (var handle in _metadata.TypeDefinitions)
        {
            var top = TopLevel(handle);
            var row = MetadataTokens.GetRowNumber(top);
            _owners[MetadataTokens.GetRowNumber(handle)] = _owners[row] ??= NewType(top);
        }

        foreach (var handle in _metadata.TypeDefinitions)
        {
            ReadType(handle);
        }

        foreach (var type in ArgumentTypes(_metadata.GetAssemblyDefinition().GetCustomAttributes()))
        {
            _facts.Preamble.Add(type);
        }

        ReadDocuments(path);
    }

    private void ReadForwarders()
    {
        foreach (var handle in _metadata.ExportedTypes)
        {
            var exported = _metadata.GetExportedType(handle);
            if (exported.IsForwarder && exported.Implementation.Kind == HandleKind.AssemblyReference)
            {
                var target = _metadata.GetAssemblyReference((AssemblyReferenceHandle)exported.Implementation);
                _facts.Forwarders[FullName(_metadata.GetString(exported.Namespace), _metadata.GetString(exported.Name))] =
                    _metadata.GetString(target.Name);
            }
        }
    }

    private TypeFacts NewType(TypeDefinitionHandle top)
    {
        var definition = _metadata.GetTypeDefinition(top);
        var name = FullName(_metadata.GetString(definition.Namespace), _metadata.GetString(definition.Name));
        var type = new TypeFacts(new TypeName(_facts.Name, name))
        {
            IsAbstract = (definition.Attributes & (TypeAttributes.Abstract | TypeAttributes.Sealed)) == TypeAttributes.Abstract,
        };
        _facts.Types.Add(type);
        return type;
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

    private void ReadMethod(TypeFacts owner, MethodDefinitionHandle handle, string prefix)
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
            _facts.UnreadableBodies++;
        }
    }

    private void ScanBody(TypeFacts owner, BlobReader code)
    {
        var position = 0;
        var tokenType = default(TypeName);
        var tokenPosition = int.MinValue;
        while (code.RemainingBytes > 0)
        {
            var first = code.ReadByte();
            var twoByte = first == 0xFE;
            var opcode = twoByte ? code.ReadByte() : first;
            if (!OperandSizes.TryGet(twoByte, opcode, out var operand))
            {
                _facts.UnreadableBodies++;
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
    private void AddAssemblyUse(TypeFacts owner, TokenFacts call, TypeName named)
    {
        var type = call.Head.Name;
        var member = call.Member!;
        if (member == "get_Assembly" && type is "System.Type" or "System.Reflection.TypeInfo")
        {
            // x.GetType().Assembly names no type. It is most often the assembly of the code itself.
            owner.Scans.Add(named.IsNone ? _facts.Name : named.Assembly);
            owner.OpaqueLoads += named.IsNone ? 1 : 0;
        }
        else if (type == "System.Reflection.Assembly" && member is "GetExecutingAssembly" or "GetCallingAssembly")
        {
            owner.Scans.Add(_facts.Name);
        }
        else if ((type == "System.Reflection.Assembly" && (member == "GetEntryAssembly" || member.StartsWith("Load", StringComparison.Ordinal)))
            || (type == "System.AppDomain" && member == "GetAssemblies")
            || (type == "System.Runtime.Loader.AssemblyLoadContext" && member.StartsWith("Load", StringComparison.Ordinal)))
        {
            owner.OpaqueLoads++;
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
    private static AssemblyLoadKinds LoadKind(string type, string member) => type switch
    {
        "System.Reflection.Assembly" when member.StartsWith("Load", StringComparison.Ordinal) => AssemblyLoadKinds.AssemblyLoad,
        "System.Runtime.Loader.AssemblyLoadContext" => AssemblyLoadKinds.LoadContext,
        "System.AppDomain" => AssemblyLoadKinds.LoadedAssemblies,
        _ => AssemblyLoadKinds.None,
    };

    private void AddSupertype(TypeFacts owner, EntityHandle handle)
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

    private void AddConstraints(TypeFacts owner, GenericParameterHandleCollection parameters)
    {
        foreach (var parameter in parameters)
        {
            foreach (var constraint in _metadata.GetGenericParameter(parameter).GetConstraints())
            {
                Add(owner, Resolve(_metadata.GetGenericParameterConstraint(constraint).Type));
            }
        }
    }

    private void AddAttributes(TypeFacts owner, CustomAttributeHandleCollection attributes, string? method, bool typeLevel)
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
                _facts.Preamble.Add(owner.Name);
            }
            else if (method is not null && !type.Name.StartsWith("System.", StringComparison.Ordinal))
            {
                owner.MethodAttributes.Add(new MethodAttribute(method, type));
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
    private List<TypeName> ArgumentTypes(CustomAttributeHandleCollection attributes)
    {
        var types = new List<TypeName>();
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
                // An enum argument that is not Int32-sized; see AttributeTypeNames.GetUnderlyingEnumType.
            }
        }

        return types;
    }

    private void AddArgumentTypes(List<TypeName> types, string type, object? value)
    {
        if (value is string serialized && type == AttributeTypeNames.SystemType)
        {
            types.Add(AttributeTypeNames.Parse(serialized, _facts.Name));
        }
        else if (value is ImmutableArray<CustomAttributeTypedArgument<string>> elements)
        {
            foreach (var element in elements)
            {
                AddArgumentTypes(types, element.Type, element.Value);
            }
        }
    }

    private void AddCollected(TypeFacts owner)
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

    private static void Add(TypeFacts owner, TokenFacts facts)
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

    private TokenFacts Resolve(EntityHandle handle)
    {
        if (!_tokens.TryGetValue(handle, out var facts))
        {
            facts = Decode(handle);
            _tokens[handle] = facts;
        }

        return facts;
    }

    private TokenFacts Decode(EntityHandle handle)
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
                return Collected(head, member: null, [], TokenFacts.Empty);
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
                return TokenFacts.Empty;
        }
    }

    private TokenFacts DecodeMember(MemberReference member)
    {
        var parent = member.Parent.Kind is HandleKind.TypeDefinition or HandleKind.TypeReference
            or HandleKind.TypeSpecification or HandleKind.MethodDefinition
            ? Resolve(member.Parent)
            : TokenFacts.Empty;
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

    private TokenFacts DecodeStandalone(StandaloneSignature signature)
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

        return Collected(default, member: null, [], TokenFacts.Empty);
    }

    private static TokenFacts Named(TypeName type, string? member) =>
        new() { Head = type, Member = member, Types = [type] };

    private TokenFacts Collected(TypeName head, string? member, TypeName[] typeArguments, TokenFacts inner)
    {
        var types = new HashSet<TypeName>(_signatures.Types);
        types.UnionWith(inner.Types);
        if (!head.IsNone)
        {
            types.Add(head);
        }

        return new TokenFacts
        {
            Head = head,
            Member = member,
            Types = [.. types],
            TypeArguments = typeArguments,
            Scans = [.. _signatures.Scans.Union(inner.Scans, StringComparer.OrdinalIgnoreCase)],
        };
    }

    private TypeName Definition(TypeDefinitionHandle handle) => _owners[MetadataTokens.GetRowNumber(handle)].Name;

    private TypeName Reference(TypeReferenceHandle handle)
    {
        if (_references.TryGetValue(handle, out var known))
        {
            return known;
        }

        var reference = _metadata.GetTypeReference(handle);
        var scope = reference.ResolutionScope;
        TypeName name;
        if (scope.Kind == HandleKind.TypeReference)
        {
            name = Reference((TypeReferenceHandle)scope);
        }
        else
        {
            var assembly = scope.Kind == HandleKind.AssemblyReference
                ? _metadata.GetString(_metadata.GetAssemblyReference((AssemblyReferenceHandle)scope).Name)
                : _facts.Name;
            name = new TypeName(assembly, FullName(_metadata.GetString(reference.Namespace), _metadata.GetString(reference.Name)));
        }

        _references[handle] = name;
        return name;
    }

    private void ReadDocuments(string path)
    {
        if (!_image.TryOpenAssociatedPortablePdb(path, OpenPdb, out var provider, out _) || provider is null)
        {
            return;
        }

        using (provider)
        {
            var pdb = provider.GetMetadataReader();
            var names = new Dictionary<DocumentHandle, string>();
            string Name(DocumentHandle handle)
            {
                if (!names.TryGetValue(handle, out var name))
                {
                    name = _paths.Map(pdb.GetString(pdb.GetDocument(handle).Name));
                    names[handle] = name;
                }

                return name;
            }

            foreach (var handle in pdb.MethodDebugInformation)
            {
                var information = pdb.GetMethodDebugInformation(handle);
                if (information.SequencePointsBlob.IsNil)
                {
                    continue;
                }

                var declaring = _metadata.GetMethodDefinition(handle.ToDefinitionHandle()).GetDeclaringType();
                var owner = _owners[MetadataTokens.GetRowNumber(declaring)];
                if (!information.Document.IsNil)
                {
                    owner.Documents.Add(Name(information.Document));
                    continue;
                }

                foreach (var point in information.GetSequencePoints())
                {
                    if (!point.Document.IsNil)
                    {
                        owner.Documents.Add(Name(point.Document));
                    }
                }
            }

            // A type without method bodies, such as an interface, has no sequence points. The compiler records
            // its files in a separate table.
            foreach (var handle in pdb.CustomDebugInformation)
            {
                var information = pdb.GetCustomDebugInformation(handle);
                if (information.Parent.Kind != HandleKind.TypeDefinition || pdb.GetGuid(information.Kind) != _typeDefinitionDocuments)
                {
                    continue;
                }

                var owner = _owners[MetadataTokens.GetRowNumber((TypeDefinitionHandle)information.Parent)];
                var rows = pdb.GetBlobReader(information.Value);
                while (rows.RemainingBytes > 0)
                {
                    owner.Documents.Add(Name(MetadataTokens.DocumentHandle(rows.ReadCompressedInteger())));
                }
            }
        }
    }

    private const int DocumentsToCheck = 50;
    private const int TokenReach = 3;
    private const byte LoadToken = 0xD0;
    private const string ModuleInitializer = "System.Runtime.CompilerServices.ModuleInitializerAttribute";
    private const string CompilerAttributes = "System.Runtime.CompilerServices.";

    private static readonly Guid _typeDefinitionDocuments = new("932E74BC-DBA9-4478-8D46-0F32A7BAB3D3");
    private static readonly AttributeTypeNames _attributeTypes = new();

    private readonly PEReader _image;
    private readonly DocumentPaths _paths;
    private readonly MetadataReader _metadata;
    private readonly AssemblyFacts _facts;
    private readonly SignatureTypes _signatures;
    private readonly TypeFacts[] _owners;
    private readonly Dictionary<EntityHandle, TokenFacts> _tokens = [];
    private readonly Dictionary<TypeReferenceHandle, TypeName> _references = [];
}
