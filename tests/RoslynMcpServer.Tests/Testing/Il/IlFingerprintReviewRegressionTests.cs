using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using RoslynMcpServer.Services.Testing.Il;
using RoslynMcpServer.Services.Testing.Il.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing.Il;

public sealed class IlFingerprintReviewRegressionTests
{
    [Theory]
    [InlineData("native-int", false)]
    [InlineData("native-int", true)]
    [InlineData("pointer", false)]
    [InlineData("pointer", true)]
    [InlineData("function-pointer", false)]
    [InlineData("function-pointer", true)]
    [InlineData("nested", false)]
    [InlineData("nested", true)]
    public void Anycpu_runtime_dependent_rva_storage_is_rejected(string storage, bool changed)
    {
        var image = NativeDataImage(storage, changed, is64Bit: false, requires32Bit: false);
        using var pe = new PEReader(image.Content);
        Assert.Equal(PEMagic.PE32, pe.PEHeaders.PEHeader!.Magic);
        Assert.Equal(CorFlags.ILOnly, pe.PEHeaders.CorHeader!.Flags);

        Assert.Throws<BadImageFormatException>(() => Compute(image));
    }

    [Theory]
    [InlineData("native-int", false, false)]
    [InlineData("native-int", true, false)]
    [InlineData("pointer", false, false)]
    [InlineData("pointer", true, false)]
    [InlineData("function-pointer", false, false)]
    [InlineData("function-pointer", true, false)]
    [InlineData("nested", false, false)]
    [InlineData("nested", true, false)]
    [InlineData("native-int", false, true)]
    [InlineData("pointer", false, true)]
    [InlineData("function-pointer", false, true)]
    [InlineData("nested", false, true)]
    public void Fixed_bitness_rva_storage_uses_its_complete_extent(string storage, bool is64Bit, bool mixedMode)
    {
        var first = Compute(NativeDataImage(storage, changed: false, is64Bit, requires32Bit: !is64Bit && !mixedMode, ilOnly: !mixedMode));
        var second = Compute(NativeDataImage(storage, changed: true, is64Bit, requires32Bit: !is64Bit && !mixedMode, ilOnly: !mixedMode));
        var owner = new IlTypeName("NativeData", "A");
        Assert.Equal(is64Bit, first.Types[owner] != second.Types[owner]);
        Assert.Equal(first.Types[new IlTypeName("NativeData", "B")], second.Types[new IlTypeName("NativeData", "B")]);
        Assert.Equal(first.AssemblyHash, second.AssemblyHash);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Named_empty_and_null_arrays_keep_their_element_type(bool nullArray, bool field)
    {
        var first = NamedArrayConsumer("int", nullArray, field);
        var second = NamedArrayConsumer("long", nullArray, field);
        Assert.False(AttributeBlob(first).AsSpan().SequenceEqual(AttributeBlob(second)));

        var before = Compute(first);
        var after = Compute(second);
        Assert.NotEqual(before.Types[new IlTypeName("Consumer", "A")], after.Types[new IlTypeName("Consumer", "A")]);
        Assert.Equal(before.Types[new IlTypeName("Consumer", "B")], after.Types[new IlTypeName("Consumer", "B")]);
        Assert.Equal(before.AssemblyHash, after.AssemblyHash);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void String_attribute_arguments_preserve_unpaired_surrogates(bool boxed)
    {
        var first = Compute(StringAttributeConsumer(boxed, @"\uD800"));
        var rebuilt = Compute(StringAttributeConsumer(boxed, @"\uD800", path: "rebuilt/Consumer.dll"));
        var replacement = Compute(StringAttributeConsumer(boxed, @"\uFFFD"));
        var owner = new IlTypeName("Consumer", "A");

        Assert.Equal(first.Types[owner], rebuilt.Types[owner]);
        Assert.NotEqual(first.Types[owner], replacement.Types[owner]);
        Assert.Equal(first.Types[new IlTypeName("Consumer", "B")], replacement.Types[new IlTypeName("Consumer", "B")]);
    }

    [Fact]
    public void String_attribute_arguments_reject_other_malformed_utf8()
    {
        var image = StringAttributeConsumer(boxed: false, @"\uD800");
        var blob = AttributeBlob(image);
        var contents = image.Content.ToArray();
        var offset = contents.AsSpan().IndexOf(blob);
        Assert.InRange(offset, 0, contents.Length - blob.Length);
        Assert.Equal(offset, contents.AsSpan().LastIndexOf(blob));
        Assert.Equal(new byte[] {0xED, 0xA0, 0x80}, blob[3..6]);
        contents[offset + 5] = 0x41;

        Assert.Throws<BadImageFormatException>(() => Compute(new IlAssemblyImage(image.Path, [.. contents])));
    }

    [Theory]
    [InlineData("base-type")]
    [InlineData("interface-type")]
    public void Base_and_interface_metadata_change_without_member_or_il_changes(string fact)
    {
        var first = IlFingerprintMetadataFixture.Create(fact, changed: false);
        var second = IlFingerprintMetadataFixture.Create(fact, changed: true);
        using var firstPe = new PEReader(first.Content);
        using var secondPe = new PEReader(second.Content);
        var firstMetadata = firstPe.GetMetadataReader();
        var secondMetadata = secondPe.GetMetadataReader();
        Assert.Equal(firstMetadata.MethodDefinitions.Count, secondMetadata.MethodDefinitions.Count);
        foreach (var method in firstMetadata.MethodDefinitions)
        {
            var firstDefinition = firstMetadata.GetMethodDefinition(method);
            var secondDefinition = secondMetadata.GetMethodDefinition(method);
            Assert.Equal(firstMetadata.GetString(firstDefinition.Name), secondMetadata.GetString(secondDefinition.Name));
            Assert.True(firstMetadata.GetBlobBytes(firstDefinition.Signature).AsSpan()
                .SequenceEqual(secondMetadata.GetBlobBytes(secondDefinition.Signature)));
            if (firstDefinition.RelativeVirtualAddress != 0)
            {
                Assert.True(firstPe.GetMethodBody(firstDefinition.RelativeVirtualAddress).GetILBytes()!.AsSpan()
                    .SequenceEqual(secondPe.GetMethodBody(secondDefinition.RelativeVirtualAddress).GetILBytes()));
            }
        }

        var before = Compute(first);
        var after = Compute(second);
        Assert.NotEqual(before.Types[new IlTypeName("Fixture", "A`1")], after.Types[new IlTypeName("Fixture", "A`1")]);
        Assert.Equal(before.Types[new IlTypeName("Fixture", "B")], after.Types[new IlTypeName("Fixture", "B")]);
        Assert.Equal(before.AssemblyHash, after.AssemblyHash);
    }

    private static IlAssemblyImage NamedArrayConsumer(string elementType, bool nullArray, bool field)
    {
        var member = "public " + elementType + "[] P" + (field ? ";" : " {get;set;}");
        var attribute = IlFingerprintCompiler.Compile(
            "public class MarkAttribute : System.Attribute {public MarkAttribute(int value) {} " + member + "}", "Attributes");
        var value = nullArray ? "null" : "new " + elementType + "[0]";
        return IlFingerprintCompiler.Compile("[Mark(1, P=" + value + ")] public class A {} public class B {}",
            "Consumer", [IlFingerprintCompiler.Reference(attribute)]);
    }

    private static IlAssemblyImage StringAttributeConsumer(bool boxed, string literal, string path = "missing/Consumer.dll")
    {
        var argumentType = boxed ? "object[]" : "string";
        var quoted = "\"" + literal + "\"";
        var argument = boxed ? "new object[] { " + quoted + " }" : quoted;
        return IlFingerprintCompiler.Compile($$"""
            public class MarkAttribute : System.Attribute { public MarkAttribute({{argumentType}} value) {} }
            [Mark({{argument}})] public class A {}
            public class B {}
            """, "Consumer", path: path);
    }

    private static byte[] AttributeBlob(IlAssemblyImage image)
    {
        using var pe = new PEReader(image.Content);
        var metadata = pe.GetMetadataReader();
        var owner = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition)
            .Single(type => metadata.GetString(type.Name) == "A");
        return metadata.GetBlobBytes(metadata.GetCustomAttribute(owner.GetCustomAttributes().Single()).Value);
    }

    private static IlAssemblyImage NativeDataImage(string storage, bool changed, bool is64Bit, bool requires32Bit, bool ilOnly = true)
    {
        var metadata = new MetadataBuilder();
        StringHandle Text(string text) => metadata.GetOrAddString(text);
        metadata.AddModule(0, Text("NativeData"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(Text("NativeData"), new Version(1, 0), default, default, 0, AssemblyHashAlgorithm.None);
        var core = metadata.AddAssemblyReference(Text("System.Runtime"), new Version(10, 0), default, default, 0, default);
        var objectType = metadata.AddTypeReference(core, Text("System"), Text("Object"));
        var valueType = metadata.AddTypeReference(core, Text("System"), Text("ValueType"));
        var firstField = MetadataTokens.FieldDefinitionHandle(1);
        var firstMethod = MetadataTokens.MethodDefinitionHandle(1);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, Text("<Module>"), default, firstField, firstMethod);
        metadata.AddTypeDefinition(TypeAttributes.Public, default, Text("A"), objectType, firstField, firstMethod);
        metadata.AddTypeDefinition(TypeAttributes.Public, default, Text("B"), objectType,
            MetadataTokens.FieldDefinitionHandle(2), firstMethod);
        metadata.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout,
            default, Text("Storage"), valueType, MetadataTokens.FieldDefinitionHandle(2), firstMethod);
        byte[] signature = storage switch
        {
            "native-int" => [6, 0x18],
            "pointer" => [6, 0x0F, 8],
            "function-pointer" => [6, 0x1B, 0, 0, 1],
            "nested" => [6, 0x11, 0x10],
            _ => throw new ArgumentException("Unknown native storage fixture.", nameof(storage)),
        };
        var dataField = metadata.AddFieldDefinition(FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.HasFieldRVA,
            Text("Data"), metadata.GetOrAddBlob(signature));
        metadata.AddFieldDefinition(FieldAttributes.Public, Text("Value"), metadata.GetOrAddBlob(new byte[] {6, 0x18}));
        metadata.AddFieldRelativeVirtualAddress(dataField, 0);
        var data = new BlobBuilder();
        data.WriteBytes(new byte[] {1, 2, 3, 4, (byte)(changed ? 1 : 0), 0, 0, 0});
        var flags = (ilOnly ? CorFlags.ILOnly : 0) | (requires32Bit ? CorFlags.Requires32Bit : 0);
        var pe = new ManagedPEBuilder(new PEHeaderBuilder(machine: is64Bit ? Machine.Amd64 : Machine.I386,
                imageCharacteristics: Characteristics.Dll | Characteristics.ExecutableImage),
            new MetadataRootBuilder(metadata), new BlobBuilder(), mappedFieldData: data, flags: flags);
        var output = new BlobBuilder();
        pe.Serialize(output);
        return new IlAssemblyImage("memory/NativeData.dll", [.. output.ToArray()]);
    }

    private static IlFingerprintSnapshot Compute(IlAssemblyImage image) =>
        IlAssemblyFingerprints.Compute(image, new HashSet<string> {"NativeData", "Consumer", "Attributes", "Fixture"});
}
