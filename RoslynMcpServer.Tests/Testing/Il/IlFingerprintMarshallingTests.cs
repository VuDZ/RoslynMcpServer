using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcpServer.Services.Testing.Il;
using RoslynMcpServer.Services.Testing.Il.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing.Il;

public sealed class IlFingerprintMarshallingTests
{
    [Theory]
    [InlineData("field")]
    [InlineData("parameter")]
    [InlineData("return")]
    public void Custom_marshaler_solution_version_preserves_consumer_fingerprints(string owner)
    {
        var first = Consumer(owner, Library("1.0.0.0"));
        var second = Consumer(owner, Library("2.0.0.0"));
        Assert.False(Descriptor(first, owner).AsSpan().SequenceEqual(Descriptor(second, owner)));

        var before = Compute(first, solutionLibrary: true);
        var after = Compute(second, solutionLibrary: true);
        Assert.Equal(before.Types.OrderBy(pair => pair.Key.Name), after.Types.OrderBy(pair => pair.Key.Name));
        Assert.Equal(before.AssemblyHash, after.AssemblyHash);
    }

    [Theory]
    [InlineData("field")]
    [InlineData("parameter")]
    [InlineData("return")]
    public void External_marshaler_version_without_an_assembly_reference_is_detected(string owner)
    {
        var first = Consumer(owner, Library("1.0.0.0"));
        var second = Consumer(owner, Library("2.0.0.0"));
        foreach (var image in new[] {first, second})
        {
            using var pe = new PEReader(image.Content);
            var metadata = pe.GetMetadataReader();
            Assert.DoesNotContain(metadata.AssemblyReferences,
                handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name) == "Library");
        }

        var before = Compute(first, solutionLibrary: false);
        var after = Compute(second, solutionLibrary: false);
        var changed = before.Types[new IlTypeName("Consumer", "C")] != after.Types[new IlTypeName("Consumer", "C")]
            || before.AssemblyHash != after.AssemblyHash;
        Assert.True(changed, "The external marshaler version must remain observable without an AssemblyRef row.");
    }

    [Theory]
    [InlineData("field")]
    [InlineData("parameter")]
    [InlineData("return")]
    public void Custom_marshaler_type_identity_is_preserved(string owner)
    {
        var library = Library("1.0.0.0");
        AssertOnlyOwnerChanges(Consumer(owner, library), Consumer(owner, library, marshaler: "OtherMarshaller"));
    }

    [Theory]
    [InlineData("field")]
    [InlineData("parameter")]
    [InlineData("return")]
    public void Custom_marshaler_cookie_preserves_ordinary_assembly_version_text(string owner)
    {
        var library = Library("1.0.0.0");
        AssertOnlyOwnerChanges(
            Consumer(owner, library, cookie: "Marshaller, Library, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"),
            Consumer(owner, library, cookie: "Marshaller, Library, Version=2.0.0.0, Culture=neutral, PublicKeyToken=null"));
    }

    [Theory]
    [InlineData("field")]
    [InlineData("parameter")]
    [InlineData("return")]
    public void Safe_array_solution_version_preserves_consumer_fingerprints(string owner)
    {
        var first = SafeArrayConsumer(owner, RecordLibrary("1.0.0.0"));
        var second = SafeArrayConsumer(owner, RecordLibrary("2.0.0.0"));
        Assert.False(Descriptor(first, owner).AsSpan().SequenceEqual(Descriptor(second, owner)));
        var before = Compute(first, solutionLibrary: true);
        var after = Compute(second, solutionLibrary: true);
        Assert.Equal(before.Types.OrderBy(pair => pair.Key.Name), after.Types.OrderBy(pair => pair.Key.Name));
        Assert.Equal(before.AssemblyHash, after.AssemblyHash);
    }

    [Theory]
    [InlineData("field")]
    [InlineData("parameter")]
    [InlineData("return")]
    public void Safe_array_external_version_without_an_assembly_reference_is_detected(string owner)
    {
        var first = SafeArrayConsumer(owner, RecordLibrary("1.0.0.0"));
        var second = SafeArrayConsumer(owner, RecordLibrary("2.0.0.0"));
        foreach (var image in new[] {first, second})
        {
            using var pe = new PEReader(image.Content);
            var metadata = pe.GetMetadataReader();
            Assert.DoesNotContain(metadata.AssemblyReferences,
                handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name) == "Library");
        }

        var before = Compute(first, solutionLibrary: false);
        var after = Compute(second, solutionLibrary: false);
        Assert.True(before.Types[new IlTypeName("Consumer", "C")] != after.Types[new IlTypeName("Consumer", "C")]
            || before.AssemblyHash != after.AssemblyHash);
    }

    [Theory]
    [InlineData("field")]
    [InlineData("parameter")]
    [InlineData("return")]
    public void Safe_array_user_defined_type_identity_is_preserved(string owner)
    {
        var library = RecordLibrary("1.0.0.0");
        AssertOnlyOwnerChanges(SafeArrayConsumer(owner, library), SafeArrayConsumer(owner, library, record: "OtherRecord"));
    }

    [Theory]
    [InlineData("field")]
    [InlineData("parameter")]
    [InlineData("return")]
    public void Safe_array_subtype_is_preserved(string owner)
    {
        var library = RecordLibrary("1.0.0.0");
        AssertOnlyOwnerChanges(SafeArrayConsumer(owner, library), SafeArrayConsumer(owner, library, subtype: "VT_UNKNOWN"));
    }

    [Theory]
    [InlineData("field")]
    [InlineData("parameter")]
    [InlineData("return")]
    public void Safe_array_without_a_user_type_preserves_remaining_descriptor(string owner)
    {
        var library = RecordLibrary("1.0.0.0");
        AssertOnlyOwnerChanges(SafeArrayConsumer(owner, library, record: null, subtype: "VT_I4"),
            SafeArrayConsumer(owner, library, record: null, subtype: "VT_I8"));
    }

    private static void AssertOnlyOwnerChanges(IlAssemblyImage first, IlAssemblyImage second)
    {
        var before = Compute(first, solutionLibrary: true);
        var after = Compute(second, solutionLibrary: true);
        Assert.NotEqual(before.Types[new IlTypeName("Consumer", "C")], after.Types[new IlTypeName("Consumer", "C")]);
        Assert.Equal(before.Types[new IlTypeName("Consumer", "B")], after.Types[new IlTypeName("Consumer", "B")]);
        Assert.Equal(before.AssemblyHash, after.AssemblyHash);
    }

    private static IlAssemblyImage Library(string version)
    {
        const string marshaler = """
            public sealed class Marshaller : System.Runtime.InteropServices.ICustomMarshaler
            {
                public static System.Runtime.InteropServices.ICustomMarshaler GetInstance(string cookie) => new Marshaller();
                public void CleanUpManagedData(object value) {}
                public void CleanUpNativeData(System.IntPtr value) {}
                public int GetNativeDataSize() => System.IntPtr.Size;
                public System.IntPtr MarshalManagedToNative(object value) => System.IntPtr.Zero;
                public object MarshalNativeToManaged(System.IntPtr value) => new object();
            }
            """;
        return IlFingerprintCompiler.Compile("[assembly:System.Reflection.AssemblyVersion(\"" + version + "\")]"
            + marshaler + marshaler.Replace("Marshaller", "OtherMarshaller", StringComparison.Ordinal), "Library");
    }

    private static IlAssemblyImage RecordLibrary(string version) => IlFingerprintCompiler.Compile(
        "[assembly:System.Reflection.AssemblyVersion(\"" + version + "\")] public struct Record {public int X;} public struct OtherRecord {public int X;}",
        "Library");

    private static IlAssemblyImage SafeArrayConsumer(string owner, IlAssemblyImage library, string? record = "Record", string subtype = "VT_RECORD")
    {
        var marshal = "[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.SafeArray, "
            + "SafeArraySubType=System.Runtime.InteropServices.VarEnum." + subtype
            + (record is null ? "" : ", SafeArrayUserDefinedSubType=typeof(" + record + ")") + ")]";
        const string import = "[System.Runtime.InteropServices.DllImport(\"native\")]";
        var member = owner switch
        {
            "field" => marshal + " public object[] Value;",
            "parameter" => import + " public static extern void M(" + marshal + " object[] value);",
            "return" => import + marshal.Replace("[System", "[return: System", StringComparison.Ordinal) + " public static extern object[] M();",
            _ => throw new ArgumentException("Unknown marshaling owner.", nameof(owner)),
        };
        return IlFingerprintCompiler.Compile("public class C {" + member + "} public class B {}", "Consumer",
            [IlFingerprintCompiler.Reference(library)]);
    }

    private static IlAssemblyImage Consumer(string owner, IlAssemblyImage library, string marshaler = "Marshaller", string cookie = "cookie")
    {
        var marshal = "[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.CustomMarshaler, "
            + "MarshalTypeRef=typeof(" + marshaler + "), MarshalCookie=" + SymbolDisplay.FormatLiteral(cookie, quote: true) + ")]";
        const string import = "[System.Runtime.InteropServices.DllImport(\"native\")]";
        var member = owner switch
        {
            "field" => marshal + " public object Value;",
            "parameter" => import + " public static extern void M(" + marshal + " object value);",
            "return" => import + marshal.Replace("[System", "[return: System", StringComparison.Ordinal) + " public static extern object M();",
            _ => throw new ArgumentException("Unknown marshaling owner.", nameof(owner)),
        };
        return IlFingerprintCompiler.Compile("public class C {" + member + "} public class B {}", "Consumer",
            [IlFingerprintCompiler.Reference(library)]);
    }

    private static byte[] Descriptor(IlAssemblyImage image, string owner)
    {
        using var pe = new PEReader(image.Content);
        var metadata = pe.GetMetadataReader();
        var type = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition)
            .Single(definition => metadata.GetString(definition.Name) == "C");
        var descriptor = owner == "field"
            ? metadata.GetFieldDefinition(type.GetFields().Single()).GetMarshallingDescriptor()
            : type.GetMethods().Select(metadata.GetMethodDefinition)
                .Single(method => metadata.GetString(method.Name) == "M").GetParameters().Select(metadata.GetParameter)
                .Single(parameter => parameter.SequenceNumber == (owner == "return" ? 0 : 1)).GetMarshallingDescriptor();
        return metadata.GetBlobBytes(descriptor);
    }

    private static IlFingerprintSnapshot Compute(IlAssemblyImage image, bool solutionLibrary) =>
        IlAssemblyFingerprints.Compute(image, solutionLibrary
            ? new HashSet<string> {"Consumer", "Library"} : new HashSet<string> {"Consumer"});
}
