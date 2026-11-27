using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using RoslynMcpServer.Services.Testing.Il.Models;

namespace RoslynMcpServer.Tests.Testing.Il;

/// <summary>Emits independent metadata facts which the C# compiler couples to other facts or cannot express.</summary>
internal static class IlFingerprintMetadataFixture
{
    public static IlAssemblyImage Create(string fact, bool changed)
    {
        var metadata = new MetadataBuilder();
        var il = new BlobBuilder();
        var bodies = new MethodBodyStreamEncoder(il);
        var data = new BlobBuilder();
        data.WriteBytes(new byte[] {1, 2, 3, (byte)(Is("field-rva") ? 5 : 4)});
        StringHandle Text(string text) => metadata.GetOrAddString(text);
        BlobHandle Blob(params byte[] bytes) => metadata.GetOrAddBlob(bytes);
        bool Is(string name) => changed && fact == name;
        var module = metadata.AddModule(0, Text("Fixture"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        var assembly = metadata.AddAssembly(Text("Fixture"), new Version(1, 0), default, default, 0, AssemblyHashAlgorithm.None);
        var core = metadata.AddAssemblyReference(Text("Core"), new Version(1, 0), default, default, 0, default);
        var other = metadata.AddAssemblyReference(Text("Other"), new Version(1, 0), default, default, 0, default);
        var exported = metadata.AddExportedType(TypeAttributes.Public | (TypeAttributes)0x00200000
            | (Is("forwarder-flags") ? TypeAttributes.Sealed : 0), Text("Shared"),
            Text(Is("forwarder-name") ? "Renamed" : "Forwarded"), Is("forwarder-target") ? other : core, 0);
        var file = metadata.AddAssemblyFile(Text(Is("file-name") ? "renamed.bin" : "linked.bin"),
            Blob(1, 2, (byte)(Is("file-hash") ? 4 : 3)), Is("file-metadata"));
        var resource = metadata.AddManifestResource(Is("resource-flags") ? ManifestResourceAttributes.Private : ManifestResourceAttributes.Public,
            Text(Is("resource-name") ? "renamed" : "data"), default, 0);
        metadata.AddAssemblyReference(Text(Is("assembly-reference-name") ? "Renamed" : "Unused"),
            new Version(Is("assembly-reference-version") ? 2 : 1, 0), default, default, 0, default);
        var objectType = metadata.AddTypeReference(core, Text("System"), Text("Object"));
        var exception = metadata.AddTypeReference(core, Text("System"), Text("Exception"));
        var interfaceType = metadata.AddTypeReference(core, Text("System"), Text("IDisposable"));
        var alternateInterface = metadata.AddTypeReference(core, Text("System"), Text("ICloneable"));
        var attribute = metadata.AddTypeReference(core, Text("System"), Text(Is("attribute-constructor") ? "OtherAttribute" : "MarkAttribute"));
        var constructor = metadata.AddMemberReference(attribute, Text(".ctor"), Blob(0x20, 1, 1, 8));
        var alternateAttribute = metadata.AddTypeReference(core, Text("System"), Text("AlternateAttribute"));
        var alternateConstructor = metadata.AddMemberReference(alternateAttribute, Text(".ctor"), Blob(0x20, 1, 1, 8));
        var moduleReference = metadata.AddModuleReference(Text(Is("pinvoke-module") ? "two" : "one"));
        var firstField = MetadataTokens.FieldDefinitionHandle(1);
        var firstMethod = MetadataTokens.MethodDefinitionHandle(1);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, Text("<Module>"), default, firstField, firstMethod);
        var a = metadata.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.SequentialLayout,
            default, Text("A`1"), Is("base-type") ? exception : objectType, firstField, firstMethod);
        var b = metadata.AddTypeDefinition(TypeAttributes.Public, default, Text("B"), objectType,
            MetadataTokens.FieldDefinitionHandle(3), MetadataTokens.MethodDefinitionHandle(7));
        var nested = metadata.AddTypeDefinition(TypeAttributes.NestedPublic, default, Text(Is("nested-name") ? "Renamed" : "Nested"),
            objectType, MetadataTokens.FieldDefinitionHandle(3), MetadataTokens.MethodDefinitionHandle(7));
        metadata.AddNestedType(nested, a);
        metadata.AddTypeLayout(a, (ushort)(Is("layout-pack") ? 2 : 1), (uint)(Is("layout-size") ? 8 : 4));
        var implementation = metadata.AddInterfaceImplementation(a, Is("interface-type") ? alternateInterface : interfaceType);
        var first = metadata.AddFieldDefinition(FieldAttributes.Public | (Is("field-flags") ? FieldAttributes.InitOnly : 0),
            Text(Is("field-name") ? "Renamed" : "First"), Blob(6, (byte)(Is("field-signature") ? 0x0A : 8)));
        var second = metadata.AddFieldDefinition(FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.HasFieldRVA,
            Text("Data"), Blob(6, 8));
        metadata.AddFieldLayout(first, Is("field-offset") ? 4 : 0);
        metadata.AddFieldRelativeVirtualAddress(second, 0);
        metadata.AddConstant(first, Is("field-constant") ? 2 : 1);
        metadata.AddMarshallingDescriptor(first, Blob((byte)(Is("field-marshal") ? 8 : 7)));

        var returnParameter = metadata.AddParameter(Is("return-flags") ? ParameterAttributes.Out : ParameterAttributes.None,
            Text(Is("return-name") ? "renamed" : "result"), 0);
        var parameter = metadata.AddParameter(Is("parameter-flags") ? ParameterAttributes.In : ParameterAttributes.None,
            Text(Is("parameter-name") ? "renamed" : "value"), 1);
        metadata.AddConstant(returnParameter, Is("return-default") ? 2 : 1);
        metadata.AddConstant(parameter, Is("parameter-default") ? 2 : 1);
        metadata.AddMarshallingDescriptor(returnParameter, Blob((byte)(Is("return-marshal") ? 8 : 7)));
        metadata.AddMarshallingDescriptor(parameter, Blob((byte)(Is("parameter-marshal") ? 8 : 7)));
        var code = new BlobBuilder();
        var instructions = new InstructionEncoder(code);
        instructions.OpCode(ILOpCode.Ldc_i4_0);
        instructions.OpCode(ILOpCode.Ret);
        var localSignature = metadata.AddStandaloneSignature(Blob(7, 1, (byte)(Is("local-signature") ? 9 : 8)));
        var typeSpecification = metadata.AddTypeSpecification(Blob(0x1D, 8));
        var offset = bodies.AddMethodBody(instructions, Is("max-stack") ? 2 : 1, localSignature,
            Is("local-init") ? MethodBodyAttributes.None : MethodBodyAttributes.InitLocals);
        if (fact.StartsWith("exception-", StringComparison.Ordinal))
        {
            var body = bodies.AddMethodBody(32, 1, 1, hasSmallExceptionRegions: false, localSignature);
            var writer = new BlobWriter(body.Instructions);
            writer.WriteBytes(new byte[31]);
            writer.WriteByte(0x2A);
            var kind = fact == "exception-filter-offset" ? ExceptionRegionKind.Filter : ExceptionRegionKind.Finally;
            if (Is("exception-kind"))
            {
                kind = ExceptionRegionKind.Fault;
            }

            body.ExceptionRegions.Add(kind,
                Is("exception-try-offset") ? 1 : 0,
                Is("exception-try-length") ? 3 : 4,
                Is("exception-handler-offset") ? 9 : 8,
                Is("exception-handler-length") ? 3 : 4,
                default,
                kind == ExceptionRegionKind.Filter ? (Is("exception-filter-offset") ? 17 : 16) : 0);
            offset = body.Offset;
        }
        var m = metadata.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static | (Is("method-flags") ? MethodAttributes.HideBySig : 0),
            Is("method-impl-flags") ? MethodImplAttributes.NoInlining : MethodImplAttributes.IL,
            Text(Is("method-name") ? "Renamed" : "M"), Blob(0x10, 1, 1, 8, (byte)(Is("method-signature") ? 9 : 8)),
            offset, returnParameter);
        var n = metadata.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static, MethodImplAttributes.IL,
            Text("N"), Blob(0, 1, 8, 8), offset, MetadataTokens.ParameterHandle(3));
        var methodSpecification = metadata.AddMethodSpecification(m, Blob(0x0A, 1, 8));
        var getter = metadata.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
            MethodImplAttributes.IL, Text("get_P"), Blob(0, 0, 8), offset, MetadataTokens.ParameterHandle(3));
        var setter = metadata.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
            MethodImplAttributes.IL, Text("set_P"), Blob(0, 1, 1, 8), 0, MetadataTokens.ParameterHandle(3));
        var adder = metadata.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
            MethodImplAttributes.IL, Text("add_E"), Blob(0, 1, 1, 0x12, (byte)(MetadataTokens.GetRowNumber(objectType) << 2 | 1)), 0,
            MetadataTokens.ParameterHandle(3));
        var remover = metadata.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
            MethodImplAttributes.IL, Text("remove_E"), Blob(0, 1, 1, 0x12, (byte)(MetadataTokens.GetRowNumber(objectType) << 2 | 1)), 0,
            MetadataTokens.ParameterHandle(3));
        metadata.AddMethodImport(n, Is("pinvoke-flags") ? MethodImportAttributes.ExactSpelling : MethodImportAttributes.None,
            Text(Is("pinvoke-entry") ? "y" : "x"), moduleReference);
        var declaration = metadata.AddMemberReference(interfaceType,
            Text(Is("method-implementation-declaration") ? "Other" : "Dispose"), Blob(0x20, 0, 1));
        if (fact == "metadata-order" && changed)
        {
            metadata.AddMethodImplementation(a, n, alternateConstructor);
        }

        metadata.AddMethodImplementation(a, Is("method-implementation-body") ? n : m, declaration);
        if (fact == "metadata-order" && !changed)
        {
            metadata.AddMethodImplementation(a, n, alternateConstructor);
        }

        var property = metadata.AddProperty(Is("property-flags") ? PropertyAttributes.SpecialName : PropertyAttributes.None,
            Text(Is("property-name") ? "Renamed" : "P"), Blob(8, 0, (byte)(Is("property-signature") ? 9 : 8)));
        metadata.AddPropertyMap(a, property);
        metadata.AddConstant(property, Is("property-default") ? 2 : 1);
        var propertySemantics = new[]
        {
            (Is("property-semantics-kind") ? MethodSemanticsAttributes.Other : MethodSemanticsAttributes.Getter,
                Is("property-semantics-method") ? n : getter),
            (MethodSemanticsAttributes.Setter, Is("property-setter-method") ? n : setter),
            (MethodSemanticsAttributes.Other, Is("property-other-method") ? m : n),
        };
        foreach (var (kind, method) in Is("metadata-order") ? propertySemantics.Reverse() : propertySemantics)
        {
            metadata.AddMethodSemantics(property, kind, method);
        }
        var @event = metadata.AddEvent(Is("event-flags") ? EventAttributes.SpecialName : EventAttributes.None,
            Text(Is("event-name") ? "Renamed" : "E"), Is("event-signature") ? exception : objectType);
        metadata.AddEventMap(a, @event);
        var eventSemantics = new[]
        {
            (Is("event-semantics-kind") ? MethodSemanticsAttributes.Other : MethodSemanticsAttributes.Adder,
                Is("event-semantics-method") ? n : adder),
            (MethodSemanticsAttributes.Remover, Is("event-remover-method") ? n : remover),
            (MethodSemanticsAttributes.Raiser, Is("event-raiser-method") ? n : getter),
            (MethodSemanticsAttributes.Other, Is("event-other-method") ? n : m),
        };
        foreach (var (kind, method) in Is("metadata-order") ? eventSemantics.Reverse() : eventSemantics)
        {
            metadata.AddMethodSemantics(@event, kind, method);
        }

        var methodParameter = metadata.AddGenericParameter(m,
            Is("method-generic-flags") ? GenericParameterAttributes.ReferenceTypeConstraint : GenericParameterAttributes.None,
            Text(Is("method-generic-name") ? "Renamed" : "T"), 0);
        var typeParameter = metadata.AddGenericParameter(a,
            Is("type-generic-flags") ? GenericParameterAttributes.ReferenceTypeConstraint : GenericParameterAttributes.None,
            Text(Is("type-generic-name") ? "Renamed" : "T"), 0);
        GenericParameterConstraintHandle Constraint(GenericParameterHandle parameterHandle, string constraintFact)
        {
            if (Is("metadata-order"))
            {
                metadata.AddGenericParameterConstraint(parameterHandle, interfaceType);
            }

            var constraint = metadata.AddGenericParameterConstraint(parameterHandle, Is(constraintFact) ? exception : objectType);
            if (fact == "metadata-order" && !changed)
            {
                metadata.AddGenericParameterConstraint(parameterHandle, interfaceType);
            }

            return constraint;
        }

        var methodConstraint = Constraint(methodParameter, "method-generic-constraint");
        var typeConstraint = Constraint(typeParameter, "type-generic-constraint");
        var owners = new Dictionary<string, EntityHandle>
        {
            ["type"] = a, ["nested"] = nested, ["field"] = first, ["method"] = m,
            ["parameter"] = parameter, ["return"] = returnParameter, ["property"] = property,
            ["event"] = @event, ["type-generic"] = typeParameter, ["method-generic"] = methodParameter,
            ["interface"] = implementation, ["type-constraint"] = typeConstraint, ["method-constraint"] = methodConstraint,
            ["module"] = module, ["assembly"] = assembly,
        };
        if (fact is "assembly-reference-attribute" or "module-reference-attribute" or "type-reference-attribute"
            or "member-reference-attribute" or "standalone-signature-attribute" or "type-specification-attribute"
            or "method-specification-attribute" or "file-attribute" or "resource-attribute" or "exported-attribute")
        {
            owners.Add("assembly-reference", other);
            owners.Add("module-reference", moduleReference);
            owners.Add("type-reference", objectType);
            owners.Add("member-reference", constructor);
            owners.Add("standalone-signature", localSignature);
            owners.Add("type-specification", typeSpecification);
            owners.Add("method-specification", methodSpecification);
            owners.Add("file", file);
            owners.Add("resource", resource);
            owners.Add("exported", exported);
        }

        foreach (var owner in owners)
        {
            if (Is("metadata-order"))
            {
                metadata.AddCustomAttribute(owner.Value, constructor, Blob(1, 0, 99, 0, 0, 0, 0, 0));
            }

            metadata.AddCustomAttribute(owner.Value, Is(owner.Key + "-attribute-constructor") ? alternateConstructor : constructor,
                Blob(1, 0, (byte)(Is(owner.Key + "-attribute") ? 2 : 1), 0, 0, 0, 0, 0));
            if (fact == "metadata-order" && !changed)
            {
                metadata.AddCustomAttribute(owner.Value, constructor, Blob(1, 0, 99, 0, 0, 0, 0, 0));
            }
        }

        // DeclSecurity handles are used by CustomAttribute rows, so emit in HasDeclSecurity coded-owner order.
        foreach (var owner in new[] {("method", (EntityHandle)m), ("assembly", (EntityHandle)assembly), ("type", (EntityHandle)a)})
        {
            var security = metadata.AddDeclarativeSecurityAttribute(owner.Item2,
                Is(owner.Item1 + "-security-action") ? DeclarativeSecurityAction.Assert : DeclarativeSecurityAction.Demand,
                metadata.GetOrAddBlob(SecurityValue(Is(owner.Item1 + "-security-value"))));
            metadata.AddCustomAttribute(security, Is(owner.Item1 + "-security-attribute-constructor") ? alternateConstructor : constructor,
                Blob(1, 0, (byte)(Is(owner.Item1 + "-security-attribute") ? 2 : 1), 0, 0, 0, 0, 0));
        }

        var resources = new BlobBuilder();
        resources.WriteInt32(2);
        resources.WriteBytes(new byte[] {1, (byte)(Is("resource-content") ? 3 : 2)});
        var pe = new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll),
            new MetadataRootBuilder(metadata), il, mappedFieldData: data, managedResources: resources);
        var output = new BlobBuilder();
        pe.Serialize(output);
        return new IlAssemblyImage("does-not-exist/Fixture.dll", [.. output.ToArray()]);
    }

    private static byte[] SecurityValue(bool changed)
    {
        var arguments = new BlobBuilder();
        arguments.WriteCompressedInteger(1);
        arguments.WriteBytes(new byte[] {0x54, 0x08});
        arguments.WriteSerializedString("Value");
        arguments.WriteInt32(changed ? 2 : 1);
        var permission = new BlobBuilder();
        permission.WriteByte(0x2E);
        permission.WriteCompressedInteger(1);
        permission.WriteSerializedString("PermissionAttribute, Core");
        permission.WriteCompressedInteger(arguments.Count);
        permission.WriteBytes(arguments.ToArray());
        return permission.ToArray();
    }
}
