using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using RoslynMcpServer.Services.Testing.Il;
using RoslynMcpServer.Services.Testing.Il.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing.Il;

public sealed class IlFingerprintSecurityTests
{
    [Theory]
    [InlineData("type", "attribute")]
    [InlineData("method", "attribute")]
    [InlineData("assembly", "attribute")]
    [InlineData("type", "type")]
    [InlineData("method", "type")]
    [InlineData("assembly", "type")]
    [InlineData("type", "enum")]
    [InlineData("method", "enum")]
    [InlineData("assembly", "enum")]
    public void Solution_version_in_binary_permission_typed_names_is_excluded(string owner, string typedValue)
    {
        var first = Image(owner, typedValue, "1.0.0.0");
        var second = Image(owner, typedValue, "2.0.0.0");
        Assert.False(Permission(first).AsSpan().SequenceEqual(Permission(second)));
        var before = Compute(first, solutionLibrary: true);
        var after = Compute(second, solutionLibrary: true);
        Assert.Equal(before.Types.OrderBy(pair => pair.Key.Name), after.Types.OrderBy(pair => pair.Key.Name));
        Assert.Equal(before.AssemblyHash, after.AssemblyHash);
    }

    [Theory]
    [InlineData("type", "attribute")]
    [InlineData("method", "attribute")]
    [InlineData("assembly", "attribute")]
    [InlineData("type", "type")]
    [InlineData("method", "type")]
    [InlineData("assembly", "type")]
    [InlineData("type", "enum")]
    [InlineData("method", "enum")]
    [InlineData("assembly", "enum")]
    public void External_permission_typed_versions_without_assembly_references_are_detected(string owner, string typedValue)
    {
        var first = Image(owner, typedValue, "1.0.0.0");
        var second = Image(owner, typedValue, "2.0.0.0");
        foreach (var image in new[] {first, second})
        {
            using var pe = new PEReader(image.Content);
            var metadata = pe.GetMetadataReader();
            Assert.DoesNotContain(metadata.AssemblyReferences,
                handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name) == "Library");
        }

        AssertOwnerChanges(owner, Compute(first, solutionLibrary: false), Compute(second, solutionLibrary: false));
    }

    [Theory]
    [InlineData("type", "identity")]
    [InlineData("method", "identity")]
    [InlineData("assembly", "identity")]
    [InlineData("type", "action")]
    [InlineData("method", "action")]
    [InlineData("assembly", "action")]
    [InlineData("type", "string")]
    [InlineData("method", "string")]
    [InlineData("assembly", "string")]
    [InlineData("type", "number")]
    [InlineData("method", "number")]
    [InlineData("assembly", "number")]
    public void Permission_identity_action_and_ordinary_values_keep_their_owner(string owner, string fact) =>
        AssertOwnerChanges(owner, Compute(Image(owner), solutionLibrary: true),
            Compute(Image(owner, changedFact: fact), solutionLibrary: true));

    [Theory]
    [InlineData("missing-entry")]
    [InlineData("trailing")]
    [InlineData("short-block")]
    [InlineData("missing-named-value")]
    public void Incomplete_binary_permission_sets_never_produce_snapshots(string malformed) =>
        Assert.Throws<BadImageFormatException>(() => Compute(Image("type", malformed: malformed), solutionLibrary: true));

    private static void AssertOwnerChanges(string owner, IlFingerprintSnapshot before, IlFingerprintSnapshot after)
    {
        Assert.Equal(before.Types[new IlTypeName("Consumer", "B")], after.Types[new IlTypeName("Consumer", "B")]);
        var typeChanged = before.Types[new IlTypeName("Consumer", "C")] != after.Types[new IlTypeName("Consumer", "C")];
        Assert.Equal(owner != "assembly", typeChanged);
        Assert.Equal(owner == "assembly", before.AssemblyHash != after.AssemblyHash);
    }

    private static IlAssemblyImage Image(string owner, string typedValue = "attribute", string version = "1.0.0.0",
        string? changedFact = null, string? malformed = null)
    {
        var metadata = new MetadataBuilder();
        StringHandle Text(string value) => metadata.GetOrAddString(value);
        metadata.AddModule(0, Text("Consumer"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        var assembly = metadata.AddAssembly(Text("Consumer"), new Version(1, 0), default, default, 0, AssemblyHashAlgorithm.None);
        var core = metadata.AddAssemblyReference(Text("System.Runtime"), new Version(10, 0), default, default, 0, default);
        var objectType = metadata.AddTypeReference(core, Text("System"), Text("Object"));
        var fieldStart = MetadataTokens.FieldDefinitionHandle(1);
        var methodStart = MetadataTokens.MethodDefinitionHandle(1);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, Text("<Module>"), default, fieldStart, methodStart);
        var type = metadata.AddTypeDefinition(TypeAttributes.Public | (owner == "type" ? TypeAttributes.HasSecurity : 0),
            default, Text("C"), objectType, fieldStart, methodStart);
        metadata.AddTypeDefinition(TypeAttributes.Public, default, Text("B"), objectType, fieldStart, MetadataTokens.MethodDefinitionHandle(2));
        var il = new BlobBuilder();
        var code = new BlobBuilder();
        var encoder = new InstructionEncoder(code);
        encoder.OpCode(ILOpCode.Ret);
        var body = new MethodBodyStreamEncoder(il).AddMethodBody(encoder);
        var method = metadata.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static
            | (owner == "method" ? MethodAttributes.HasSecurity : 0), MethodImplAttributes.IL, Text("M"),
            metadata.GetOrAddBlob(new byte[] {0, 0, 1}), body, MetadataTokens.ParameterHandle(1));
        EntityHandle parent = owner switch
        {
            "type" => type,
            "method" => method,
            "assembly" => assembly,
            _ => throw new ArgumentException("Unknown security owner.", nameof(owner)),
        };
        metadata.AddDeclarativeSecurityAttribute(parent,
            changedFact == "action" ? DeclarativeSecurityAction.Assert : DeclarativeSecurityAction.Demand,
            metadata.GetOrAddBlob(PermissionSet(typedValue, version, changedFact, malformed)));
        var output = new BlobBuilder();
        new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll),
            new MetadataRootBuilder(metadata), il).Serialize(output);
        return new IlAssemblyImage("memory/Consumer.dll", [.. output.ToArray()]);
    }

    private static byte[] PermissionSet(string typedValue, string version, string? changedFact, string? malformed)
    {
        if (malformed == "missing-entry")
        {
            return [0x2E, 1];
        }

        if (malformed == "trailing")
        {
            return [0x2E, 0, 1];
        }

        var arguments = new BlobBuilder();
        arguments.WriteCompressedInteger(typedValue == "attribute" ? 2 : 3);
        arguments.WriteBytes(new byte[] {0x54, 0x0E});
        arguments.WriteSerializedString("Text");
        arguments.WriteSerializedString("Record, Library, Version=" + (changedFact == "string" ? "2.0.0.0" : "1.0.0.0"));
        arguments.WriteBytes(new byte[] {0x54, 0x08});
        arguments.WriteSerializedString("Value");
        arguments.WriteInt32(changedFact == "number" ? 2 : 1);
        if (typedValue != "attribute")
        {
            arguments.WriteByte(0x54);
            arguments.WriteByte(typedValue == "type" ? (byte)0x50 : (byte)0x55);
            if (typedValue == "enum")
            {
                arguments.WriteSerializedString(Qualified("Level", version));
            }

            arguments.WriteSerializedString("Typed");
            if (typedValue == "type")
            {
                arguments.WriteSerializedString(Qualified("Record", version));
            }
            else
            {
                arguments.WriteInt32(1);
            }
        }

        var permission = new BlobBuilder();
        permission.WriteByte(0x2E);
        permission.WriteCompressedInteger(1);
        permission.WriteSerializedString(Qualified(changedFact == "identity" ? "OtherPermissionAttribute" : "PermissionAttribute",
            typedValue == "attribute" ? version : "1.0.0.0"));
        if (malformed is "short-block" or "missing-named-value")
        {
            permission.WriteCompressedInteger(malformed == "short-block" ? 5 : 1);
            permission.WriteCompressedInteger(1);
        }
        else
        {
            permission.WriteCompressedInteger(arguments.Count);
            permission.WriteBytes(arguments.ToArray());
        }

        return permission.ToArray();
    }

    private static string Qualified(string name, string version) =>
        name + ", Library, Version=" + version + ", Culture=neutral, PublicKeyToken=null";

    private static byte[] Permission(IlAssemblyImage image)
    {
        using var pe = new PEReader(image.Content);
        var metadata = pe.GetMetadataReader();
        return metadata.GetBlobBytes(metadata.GetDeclarativeSecurityAttribute(metadata.DeclarativeSecurityAttributes.Single()).PermissionSet);
    }

    private static IlFingerprintSnapshot Compute(IlAssemblyImage image, bool solutionLibrary) =>
        IlAssemblyFingerprints.Compute(image, solutionLibrary
            ? new HashSet<string> {"Consumer", "Library"} : new HashSet<string> {"Consumer"});
}
