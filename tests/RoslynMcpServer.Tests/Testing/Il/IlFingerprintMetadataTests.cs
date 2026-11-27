using RoslynMcpServer.Services.Testing.Il;
using RoslynMcpServer.Services.Testing.Il.Models;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace RoslynMcpServer.Tests.Testing.Il;

public sealed class IlFingerprintMetadataTests
{
    public static TheoryData<string> TypeFacts => new()
    {
        "nested-name", "layout-pack", "layout-size", "field-name", "field-signature", "field-flags",
        "field-constant", "field-offset", "field-rva", "field-marshal", "method-name", "method-signature",
        "method-flags", "method-impl-flags", "local-signature", "local-init", "max-stack", "pinvoke-module", "pinvoke-entry", "pinvoke-flags",
        "parameter-name", "parameter-flags", "parameter-default", "parameter-marshal",
        "return-name", "return-flags", "return-default", "return-marshal",
        "type-generic-name", "type-generic-flags", "type-generic-constraint",
        "method-generic-name", "method-generic-flags", "method-generic-constraint",
        "property-name", "property-signature", "property-flags", "property-default", "property-semantics-kind", "property-semantics-method",
        "property-setter-method", "property-other-method",
        "event-name", "event-signature", "event-flags", "event-semantics-kind", "event-semantics-method",
        "event-remover-method", "event-raiser-method", "event-other-method",
        "method-implementation-body", "method-implementation-declaration", "attribute-constructor",
        "type-attribute", "nested-attribute", "field-attribute", "method-attribute", "parameter-attribute", "return-attribute",
        "property-attribute", "event-attribute", "type-generic-attribute", "method-generic-attribute",
        "interface-attribute", "type-constraint-attribute", "method-constraint-attribute",
        "type-attribute-constructor", "nested-attribute-constructor", "field-attribute-constructor", "method-attribute-constructor",
        "parameter-attribute-constructor", "return-attribute-constructor", "property-attribute-constructor", "event-attribute-constructor",
        "type-generic-attribute-constructor", "method-generic-attribute-constructor", "interface-attribute-constructor",
        "type-constraint-attribute-constructor", "method-constraint-attribute-constructor",
        "type-security-action", "type-security-value", "method-security-action", "method-security-value",
        "type-security-attribute", "type-security-attribute-constructor", "method-security-attribute", "method-security-attribute-constructor",
        "exception-kind", "exception-try-offset", "exception-try-length", "exception-handler-offset",
        "exception-handler-length", "exception-filter-offset",
    };

    public static TheoryData<string> AssemblyFacts => new()
    {
        "assembly-attribute", "module-attribute", "assembly-attribute-constructor", "module-attribute-constructor",
        "assembly-security-action", "assembly-security-value", "forwarder-name", "forwarder-target",
        "assembly-security-attribute", "assembly-security-attribute-constructor",
        "assembly-reference-name", "assembly-reference-version",
        "assembly-reference-attribute", "module-reference-attribute", "type-reference-attribute",
        "member-reference-attribute", "standalone-signature-attribute",
        "type-specification-attribute", "method-specification-attribute", "file-attribute", "resource-attribute", "exported-attribute",
        "resource-name", "resource-content", "resource-flags", "file-name", "file-hash", "file-metadata", "forwarder-flags",
    };

    [Theory]
    [MemberData(nameof(TypeFacts))]
    public void Each_metadata_fact_changes_its_top_level_owner(string fact)
    {
        var first = Compute(fact, changed: false);
        var second = Compute(fact, changed: true);
        Assert.NotEqual(first.Types[new IlTypeName("Fixture", "A`1")], second.Types[new IlTypeName("Fixture", "A`1")]);
        Assert.Equal(first.Types[new IlTypeName("Fixture", "B")], second.Types[new IlTypeName("Fixture", "B")]);
        if (fact != "attribute-constructor")
        {
            Assert.Equal(first.AssemblyHash, second.AssemblyHash);
        }
    }

    [Theory]
    [MemberData(nameof(AssemblyFacts))]
    public void Each_assembly_fact_changes_only_assembly_fingerprint(string fact)
    {
        var first = Compute(fact, changed: false);
        var second = Compute(fact, changed: true);
        Assert.NotEqual(first.AssemblyHash, second.AssemblyHash);
        Assert.Equal(first.Types.OrderBy(pair => pair.Key.Name), second.Types.OrderBy(pair => pair.Key.Name));
    }

    [Fact]
    public void Metadata_mvid_is_excluded()
    {
        var first = Compute("none", false);
        var second = Compute("none", false);
        Assert.Equal(first.AssemblyHash, second.AssemblyHash);
        Assert.Equal(first.Types.OrderBy(pair => pair.Key.Name), second.Types.OrderBy(pair => pair.Key.Name));
    }

    [Fact]
    public void Semantics_implementation_constraint_and_attribute_row_order_is_excluded()
    {
        var first = Compute("metadata-order", false);
        var second = Compute("metadata-order", true);
        Assert.Equal(first.AssemblyHash, second.AssemblyHash);
        Assert.Equal(first.Types.OrderBy(pair => pair.Key.Name), second.Types.OrderBy(pair => pair.Key.Name));
    }

    [Fact]
    public void Rva_witness_changes_payload_without_changing_field_identity_or_il()
    {
        using var first = new PEReader(IlFingerprintMetadataFixture.Create("field-rva", false).Content);
        using var second = new PEReader(IlFingerprintMetadataFixture.Create("field-rva", true).Content);
        var firstMetadata = first.GetMetadataReader();
        var secondMetadata = second.GetMetadataReader();
        var firstField = firstMetadata.FieldDefinitions.Select(firstMetadata.GetFieldDefinition)
            .Single(field => firstMetadata.GetString(field.Name) == "Data");
        var secondField = secondMetadata.FieldDefinitions.Select(secondMetadata.GetFieldDefinition)
            .Single(field => secondMetadata.GetString(field.Name) == "Data");
        Assert.Equal(firstField.Attributes, secondField.Attributes);
        Assert.Equal(firstMetadata.GetBlobBytes(firstField.Signature), secondMetadata.GetBlobBytes(secondField.Signature));
        Assert.False(first.GetSectionData(firstField.GetRelativeVirtualAddress()).GetContent(0, 4).AsSpan().SequenceEqual(
            second.GetSectionData(secondField.GetRelativeVirtualAddress()).GetContent(0, 4).AsSpan()));
        Assert.Equal(first.GetMethodBody(firstMetadata.GetMethodDefinition(firstMetadata.MethodDefinitions.First()).RelativeVirtualAddress).GetILBytes(),
            second.GetMethodBody(secondMetadata.GetMethodDefinition(secondMetadata.MethodDefinitions.First()).RelativeVirtualAddress).GetILBytes());
    }

    private static IlFingerprintSnapshot Compute(string fact, bool changed) =>
        IlAssemblyFingerprints.Compute(IlFingerprintMetadataFixture.Create(fact, changed), new HashSet<string> {"Fixture"});
}
