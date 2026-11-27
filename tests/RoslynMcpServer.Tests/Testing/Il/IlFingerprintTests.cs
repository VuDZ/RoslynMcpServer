using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using RoslynMcpServer.Services.Testing.Il;
using RoslynMcpServer.Services.Testing.Il.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing.Il;

public sealed class IlFingerprintTests
{
    public static TheoryData<string, string, string> SourceChanges => new()
    {
        { "body", "public int M() => 1;", "public int M() => 2;" },
        { "default", "public int M(int x = 1) => x;", "public int M(int x = 2) => x;" },
        { "rva", "public static readonly byte[] Data = new byte[] {1,2,3,4,5,6,7,8};", "public static readonly byte[] Data = new byte[] {1,2,3,4,5,6,7,9};" },
        { "string", "public string M() => \"one\";", "public string M() => \"two\";" },
        { "method-signature", "public void M(int x) {}", "public void M(long x) {}" },
        { "method-access", "public void M() {}", "private void M() {}" },
        { "type-attribute", "[Mark(1)] public class Inner {}", "[Mark(2)] public class Inner {}" },
        { "member-attribute", "[Mark(1)] public void M() {}", "[Mark(2)] public void M() {}" },
        { "parameter-attribute", "public void M([Mark(1)] int x) {}", "public void M([Mark(2)] int x) {}" },
        { "return-attribute", "[return: Mark(1)] public int M() => 0;", "[return: Mark(2)] public int M() => 0;" },
        { "generic-attribute", "public void M<[Mark(1)] T>() {}", "public void M<[Mark(2)] T>() {}" },
        { "type-constraint", "public class Inner<T> where T : class {}", "public class Inner<T> where T : struct {}" },
        { "method-constraint", "public void M<T>() where T : class {}", "public void M<T>() where T : struct {}" },
        { "variance", "public interface I<in T> {}", "public interface I<out T> {}" },
        { "layout-kind", "[StructLayout(LayoutKind.Sequential)] public struct Inner {public int X;}", "[StructLayout(LayoutKind.Auto)] public struct Inner {public int X;}" },
        { "layout-pack", "[StructLayout(LayoutKind.Sequential, Pack=1)] public struct Inner {public int X;}", "[StructLayout(LayoutKind.Sequential, Pack=2)] public struct Inner {public int X;}" },
        { "layout-size", "[StructLayout(LayoutKind.Sequential, Size=8)] public struct Inner {public int X;}", "[StructLayout(LayoutKind.Sequential, Size=16)] public struct Inner {public int X;}" },
        { "field-offset", "[StructLayout(LayoutKind.Explicit)] public struct Inner {[FieldOffset(0)] public int X;}", "[StructLayout(LayoutKind.Explicit)] public struct Inner {[FieldOffset(4)] public int X;}" },
        { "field-order", "public struct Inner {public int X; public int Y;}", "public struct Inner {public int Y; public int X;}" },
        { "field-marshal", "[MarshalAs(UnmanagedType.I4)] public int X;", "[MarshalAs(UnmanagedType.U4)] public int X;" },
        { "parameter-marshal", "public void M([MarshalAs(UnmanagedType.I4)] int x) {}", "public void M([MarshalAs(UnmanagedType.U4)] int x) {}" },
        { "return-marshal", "[return: MarshalAs(UnmanagedType.I4)] public int M() => 0;", "[return: MarshalAs(UnmanagedType.U4)] public int M() => 0;" },
        { "method-impl-flags", "public void M() {}", "[MethodImpl(MethodImplOptions.NoInlining)] public void M() {}" },
        { "pinvoke-module", "[DllImport(\"one\")] public static extern void M();", "[DllImport(\"two\")] public static extern void M();" },
        { "pinvoke-entry", "[DllImport(\"one\", EntryPoint=\"x\")] public static extern void M();", "[DllImport(\"one\", EntryPoint=\"y\")] public static extern void M();" },
        { "pinvoke-flags", "[DllImport(\"one\", ExactSpelling=false)] public static extern void M();", "[DllImport(\"one\", ExactSpelling=true)] public static extern void M();" },
        { "lambda", "public Func<int> M() => () => 1;", "public Func<int> M() => () => 2;" },
        { "async", "public async Task<int> M() {await Task.Yield(); return 1;}", "public async Task<int> M() {await Task.Yield(); return 2;}" },
        { "nested", "public class Inner {public int M() => 1;}", "public class Inner {public int M() => 2;}" },
        { "filter", "public void M() {try {Throw();} catch(Exception) when(Check(1)) {}}", "public void M() {try {Throw();} catch(Exception) when(Check(2)) {}}" },
        { "finally", "public void M() {try {Throw();} finally {Check(1);}}", "public void M() {try {Throw();} finally {Check(2);}}" },
        { "property", "public int P {get; set;}", "public long P {get; set;}" },
        { "event", "public event Action E;", "public event Action<int> E;" },
        { "explicit-interface", "public class Inner : IDisposable {void IDisposable.Dispose() {Check(1);}}", "public class Inner : IDisposable {void IDisposable.Dispose() {Check(2);}}" },
        { "local-signature", "public void M() {int x = 0; GC.KeepAlive(x);}", "public void M() {uint x = 0; GC.KeepAlive(x);}" },
        { "nested-token-identity", "public class X {public class Y {}} public class Z {public class Y {}} public Type M() => typeof(X.Y);", "public class X {public class Y {}} public class Z {public class Y {}} public Type M() => typeof(Z.Y);" },
    };

    public static TheoryData<string, string> TypeChanges => new()
    {
        { "public class A {}", "internal class A {}" },
        { "public class A {}", "public sealed class A {}" },
        { "public class A {}", "public abstract class A {}" },
        { "public class A {}", "public struct A {}" },
        { "public class A : Exception {}", "public class A : ApplicationException {}" },
        { "public class A {}", "public class A : IDisposable {public void Dispose() {}}" },
    };

    [Theory]
    [MemberData(nameof(SourceChanges))]
    public void Each_source_fact_changes_its_owner_only(string fact, string before, string after)
    {
        var first = Fingerprint(Source(before));
        var second = Fingerprint(Source(after));

        Assert.True(first.Types[Name("A")] != second.Types[Name("A")], $"The {fact} fact was not fingerprinted.");
        Assert.Equal(first.Types[Name("B")], second.Types[Name("B")]);
        Assert.DoesNotContain(second.Types.Keys, type => type.Name.Contains('+'));
    }

    [Theory]
    [MemberData(nameof(TypeChanges))]
    public void Each_type_fact_changes_its_owner_only(string before, string after)
    {
        var first = Fingerprint("using System; " + before + " public class B {}");
        var second = Fingerprint("using System; " + after + " public class B {}");
        Assert.NotEqual(first.Types[Name("A")], second.Types[Name("A")]);
        Assert.Equal(first.Types[Name("B")], second.Types[Name("B")]);
    }

    [Theory]
    [InlineData("public int M() => 1;", "// comment\n public int M() => 1;")]
    [InlineData("public int M() => 1;", "public   int M()  =>  1;")]
    [InlineData("public int M() => 1; public int N() => 2;", "public int N() => 2; public int M() => 1;")]
    [InlineData("public int M() {int x = 1; return x;}", "public int M() {int renamed = 1; return renamed;}")]
    public void Source_only_edits_preserve_fingerprints(string before, string after) =>
        Equal(Fingerprint(Source(before)), Fingerprint(Source(after)));

    [Fact]
    public void Recompilation_path_pdb_and_mvid_are_excluded()
    {
        var first = IlFingerprintCompiler.Compile(Source("public int M() => 1;"), path: "one/first.dll");
        var second = IlFingerprintCompiler.Compile(Source("public int M() => 1;"), path: "two/second.dll", pdb: true);
        using var firstPe = new PEReader(first.Content);
        using var secondPe = new PEReader(second.Content);
        Assert.NotEqual(firstPe.GetMetadataReader().GetGuid(firstPe.GetMetadataReader().GetModuleDefinition().Mvid),
            secondPe.GetMetadataReader().GetGuid(secondPe.GetMetadataReader().GetModuleDefinition().Mvid));
        Equal(Compute(first), Compute(second));
        Equal(Compute(first), Compute(first));
        Equal(Compute(first), Compute(IlFingerprintCompiler.Compile(Source("public int M() => 1;"), path: "one/first.dll")));
    }

    [Fact]
    public void New_member_shifts_raw_tokens_but_preserves_unrelated_type()
    {
        const string before = "public class A {} public class B {public string M() => string.Concat(\"b\", 1.ToString());}";
        const string after = "public class A {public string N() => System.Guid.NewGuid().ToString() + \"new\";} public class B {public string M() => string.Concat(\"b\", 1.ToString());}";
        var first = IlFingerprintCompiler.Compile(before);
        var second = IlFingerprintCompiler.Compile(after);
        Assert.NotEqual(Body(first, "B", "M"), Body(second, "B", "M"));
        Assert.Equal(Compute(first).Types[Name("B")], Compute(second).Types[Name("B")]);
    }

    [Fact]
    public void Catch_type_changes_with_identical_il_are_detected()
    {
        var first = IlFingerprintCompiler.Compile(Source("public void M() {try {Throw();} catch(ArgumentException) {}}"));
        var second = IlFingerprintCompiler.Compile(Source("public void M() {try {Throw();} catch(InvalidOperationException) {}}"));
        Assert.Equal(Body(first, "A", "M"), Body(second, "A", "M"));
        Assert.NotEqual(Compute(first).Types[Name("A")], Compute(second).Types[Name("A")]);
    }

    [Theory]
    [InlineData("const")]
    [InlineData("enum")]
    [InlineData("overload")]
    [InlineData("unused")]
    [InlineData("version")]
    public void Recompiled_dependent_tracks_only_compiled_behavior(string fact)
    {
        var sources = fact switch
        {
            "const" => ("public class A {public const int X=1;}", "public class A {public const int X=2;}", "public class C {public int M() => A.X;}"),
            "enum" => ("public enum A {X=1}", "public enum A {X=2}", "public class C {public int M() => (int)A.X;}"),
            "overload" => ("public class A {public static int M(object x) => 1;}", "public class A {public static int M(object x) => 1; public static int M(string x) => 2;}", "public class C {public int M() => A.M(\"s\");}"),
            "version" => ("[assembly:System.Reflection.AssemblyVersion(\"1.0.0.0\")] public class A {public static int X;}", "[assembly:System.Reflection.AssemblyVersion(\"2.0.0.0\")] public class A {public static int X;}", "public class C {public int M() => A.X;}"),
            _ => ("public class A {public static int X;}", "public class A {public static int X; public void N() {}}", "public class C {public int M() => A.X;}"),
        };
        var first = IlFingerprintCompiler.Compile(sources.Item1, "Library");
        var second = IlFingerprintCompiler.Compile(sources.Item2, "Library");
        var firstDependent = IlFingerprintCompiler.Compile(sources.Item3, "Dependent", [IlFingerprintCompiler.Reference(first)]);
        var secondDependent = IlFingerprintCompiler.Compile(sources.Item3, "Dependent", [IlFingerprintCompiler.Reference(second)]);
        var changed = fact is "const" or "enum" or "overload";
        var before = Compute(firstDependent);
        var after = Compute(secondDependent);
        Assert.Equal(changed, before.Types[new IlTypeName("Dependent", "C")] != after.Types[new IlTypeName("Dependent", "C")]);
        Assert.Equal(before.AssemblyHash, after.AssemblyHash);
        if (fact == "version")
        {
            Equal(Compute(first), Compute(second));
            Assert.NotEqual(IlAssemblyFingerprints.Compute(firstDependent, new HashSet<string>()).AssemblyHash,
                IlAssemblyFingerprints.Compute(secondDependent, new HashSet<string>()).AssemblyHash);
        }
        else
        {
            Assert.NotEqual(Compute(first).Types[new IlTypeName("Library", "A")], Compute(second).Types[new IlTypeName("Library", "A")]);
        }
    }

    [Theory]
    [InlineData("AssemblyVersion")]
    [InlineData("AssemblyFileVersion")]
    [InlineData("AssemblyInformationalVersion")]
    public void Build_versions_are_excluded(string attribute)
    {
        var first = Fingerprint($"[assembly:System.Reflection.{attribute}(\"1.0.0.0\")] public class A {{}}");
        var second = Fingerprint($"[assembly:System.Reflection.{attribute}(\"2.0.0.0\")] public class A {{}}");
        Equal(first, second);
    }

    [Fact]
    public void A_lookalike_version_attribute_is_retained()
    {
        const string source = "[assembly: User.System.Reflection.AssemblyFileVersionAttributeShadow(VALUE)] namespace User.System.Reflection { public class AssemblyFileVersionAttributeShadow : global::System.Attribute {public AssemblyFileVersionAttributeShadow(int value) {}}} public class A {}";
        var first = Fingerprint(source.Replace("VALUE", "1"));
        var second = Fingerprint(source.Replace("VALUE", "2"));
        Assert.NotEqual(first.AssemblyHash, second.AssemblyHash);
        Assert.Equal(first.Types.OrderBy(pair => pair.Key.Name), second.Types.OrderBy(pair => pair.Key.Name));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("new object[] {null, 1, \"value\", typeof(string)}")]
    [InlineData("new string[] {null, \"value\"}")]
    public void Boxed_null_and_attribute_arrays_are_read_completely(string value)
    {
        var source = "using System; public class Mark:Attribute {public Mark(object value) {}} [Mark(" + value + ")] public class A {}";
        var first = Fingerprint(source);
        Equal(first, Fingerprint(source));
        Assert.NotEqual(first.Types[Name("A")], Fingerprint("using System; public class Mark:Attribute {public Mark(object value) {}} [Mark(1)] public class A {}").Types[Name("A")]);
    }

    [Fact]
    public void Ordinary_attribute_strings_keep_assembly_version_text()
    {
        const string source = "class Mark:System.Attribute {public Mark(string value) {}} [Mark(\"A, Library, Version=VERSION, Culture=neutral, PublicKeyToken=null\")] class A {}";
        Assert.NotEqual(Fingerprint(source.Replace("VERSION", "1.0.0.0")).Types[Name("A")],
            Fingerprint(source.Replace("VERSION", "2.0.0.0")).Types[Name("A")]);
    }

    [Fact]
    public void Serialized_type_values_preserve_nested_generic_arguments()
    {
        const string source = "using System; class Mark:Attribute {public Mark(Type value) {}} [Mark(typeof(A.CHOICE<int>))] class A {public class X<T> {} public class Y<T> {}}";
        Assert.NotEqual(Fingerprint(source.Replace("CHOICE", "X")).Types[Name("A")],
            Fingerprint(source.Replace("CHOICE", "Y")).Types[Name("A")]);
    }

    [Fact]
    public void Signature_and_local_tokens_are_canonical_after_new_type_references()
    {
        const string members = "class B {public System.Collections.Generic.List<System.Text.StringBuilder[]> M(System.Tuple<System.Exception> x) {System.Text.StringBuilder local = null; GC.KeepAlive(local); return null;}}";
        var first = IlFingerprintCompiler.Compile("using System; class A {} " + members);
        var second = IlFingerprintCompiler.Compile("using System; class A {public System.Uri N()=>null;} " + members);
        using var firstPe = new PEReader(first.Content);
        using var secondPe = new PEReader(second.Content);
        var firstMetadata = firstPe.GetMetadataReader();
        var secondMetadata = secondPe.GetMetadataReader();
        var firstMethod = firstMetadata.GetMethodDefinition(firstMetadata.GetTypeDefinition(firstMetadata.TypeDefinitions
            .Single(handle => firstMetadata.GetString(firstMetadata.GetTypeDefinition(handle).Name) == "B")).GetMethods()
            .Single(handle => firstMetadata.GetString(firstMetadata.GetMethodDefinition(handle).Name) == "M"));
        var secondMethod = secondMetadata.GetMethodDefinition(secondMetadata.GetTypeDefinition(secondMetadata.TypeDefinitions
            .Single(handle => secondMetadata.GetString(secondMetadata.GetTypeDefinition(handle).Name) == "B")).GetMethods()
            .Single(handle => secondMetadata.GetString(secondMetadata.GetMethodDefinition(handle).Name) == "M"));
        Assert.NotEqual(firstMetadata.GetBlobBytes(firstMethod.Signature), secondMetadata.GetBlobBytes(secondMethod.Signature));
        Assert.Equal(Compute(first).Types[Name("B")], Compute(second).Types[Name("B")]);
    }

    [Fact]
    public void Typeof_attribute_values_normalize_solution_versions_and_keep_nested_generic_identity()
    {
        const string library = "[assembly:System.Reflection.AssemblyVersion(\"VERSION\")] public class A {public class Nested<T> {}}";
        var first = IlFingerprintCompiler.Compile(library.Replace("VERSION", "1.0.0.0"), "Library");
        var second = IlFingerprintCompiler.Compile(library.Replace("VERSION", "2.0.0.0"), "Library");
        const string source = "using System; [AttributeUsage(AttributeTargets.All)] public class Mark : Attribute {public Mark(Type type) {}} [Mark(typeof(A.Nested<int>))] public class C {}";
        Equal(Compute(IlFingerprintCompiler.Compile(source, "Dependent", [IlFingerprintCompiler.Reference(first)])),
            Compute(IlFingerprintCompiler.Compile(source, "Dependent", [IlFingerprintCompiler.Reference(second)])));
    }

    [Theory]
    [InlineData("byte")]
    [InlineData("short")]
    [InlineData("int")]
    [InlineData("long")]
    public void External_enum_before_typeof_does_not_break_solution_version_normalization(string underlying)
    {
        var library = "[assembly:System.Reflection.AssemblyVersion(\"VERSION\")] public enum Small : " + underlying + " {X=1} public class A {}";
        var first = IlFingerprintCompiler.Compile(library.Replace("VERSION", "1.0.0.0"), "Library");
        var second = IlFingerprintCompiler.Compile(library.Replace("VERSION", "2.0.0.0"), "Library");
        const string source = "using System; class Mark:Attribute {public Mark(Small value,Type type) {}} [Mark(Small.X,typeof(A))] class C {}";
        Equal(Compute(IlFingerprintCompiler.Compile(source, "Dependent", [IlFingerprintCompiler.Reference(first)])),
            Compute(IlFingerprintCompiler.Compile(source, "Dependent", [IlFingerprintCompiler.Reference(second)])));
    }

    [Fact]
    public void Interface_constraint_attribute_and_type_row_orders_are_excluded()
    {
        const string before = "using System; interface I1 {} interface I2 {} [AttributeUsage(AttributeTargets.All,AllowMultiple=true)] class Mark:Attribute {public Mark(int x) {}} [Mark(1), Mark(2)] class A<T>: I1,I2 where T:I1,I2 {} class B {}";
        const string after = "using System; class B {} interface I2 {} interface I1 {} [AttributeUsage(AttributeTargets.All,AllowMultiple=true)] class Mark:Attribute {public Mark(int x) {}} [Mark(2), Mark(1)] class A<T>: I2,I1 where T:I2,I1 {}";
        Equal(Fingerprint(before), Fingerprint(after));
    }

    [Fact]
    public void Broken_images_do_not_produce_partial_snapshots() =>
        Assert.Throws<BadImageFormatException>(() => Compute(new IlAssemblyImage("missing.dll", [1, 2, 3])));

    [Fact]
    public void String_tokens_preserve_unpaired_utf16_code_units()
    {
        var first = Fingerprint(Source("public string M() => \"\\uD800\";"));
        var second = Fingerprint(Source("public string M() => \"\\uD801\";"));
        Assert.NotEqual(first.Types[Name("A")], second.Types[Name("A")]);
    }

    [Fact]
    public void Assembly_and_module_attributes_and_resources_are_separate()
    {
        const string source = "public class A {}";
        var baseline = Compute(IlFingerprintCompiler.Compile(source, resource: [1, 2]));
        foreach (var image in new[]
        {
            IlFingerprintCompiler.Compile(source, resource: [1, 3]),
            IlFingerprintCompiler.Compile(source, resource: [1, 2], resourceName: "renamed"),
            IlFingerprintCompiler.Compile("[assembly:System.CLSCompliant(true)] " + source, resource: [1, 2]),
            IlFingerprintCompiler.Compile("[module:System.CLSCompliant(true)] " + source, resource: [1, 2]),
        })
        {
            var current = Compute(image);
            Assert.NotEqual(baseline.AssemblyHash, current.AssemblyHash);
            Assert.Equal(baseline.Types.OrderBy(p => p.Key.Name), current.Types.OrderBy(p => p.Key.Name));
        }
    }

    [Fact]
    public void Same_full_name_in_different_assemblies_is_a_different_token()
    {
        const string library = "namespace Shared {public class T {public static int M() => 1;}}";
        var left = IlFingerprintCompiler.Compile(library, "Left");
        var right = IlFingerprintCompiler.Compile(library, "Right");
        var references = new[] {IlFingerprintCompiler.Reference(left, "left"), IlFingerprintCompiler.Reference(right, "right")};
        const string source = "extern alias left; extern alias right; public class A {public int M() => ALIAS::Shared.T.M(); public System.Type Both() => typeof(left::Shared.T) == typeof(right::Shared.T) ? typeof(int) : typeof(long);} public class B {}";
        var first = Compute(IlFingerprintCompiler.Compile(source.Replace("ALIAS", "left"), references: references));
        var second = Compute(IlFingerprintCompiler.Compile(source.Replace("ALIAS", "right"), references: references));
        Assert.NotEqual(first.Types[Name("A")], second.Types[Name("A")]);
        Assert.Equal(first.Types[Name("B")], second.Types[Name("B")]);
        Assert.Equal(first.AssemblyHash, second.AssemblyHash);
    }

    [Fact]
    public void Comparison_separates_changed_added_removed_and_assembly_change()
    {
        var before = Fingerprint("public class A {public int X;} public class Removed {} public class B {}");
        var after = Fingerprint("[assembly:System.CLSCompliant(true)] public class A {public long X;} public class Added {} public class B {}");
        var changes = before.Compare(after);
        Assert.Equal(new[] {Name("A")}, changes.Changed);
        Assert.Equal(new[] {Name("Added")}, changes.Added);
        Assert.Equal(new[] {Name("Removed")}, changes.Removed);
        Assert.True(changes.AssemblyChanged);
        var same = before.Compare(before);
        Assert.Empty(same.Changed);
        Assert.Empty(same.Added);
        Assert.Empty(same.Removed);
        Assert.False(same.AssemblyChanged);
        Assert.Throws<ArgumentException>(() => before.Compare(Compute(IlFingerprintCompiler.Compile("class A {}", "Other"))));
    }

    [Fact]
    public void Anonymous_type_renumbering_selects_every_changed_user()
    {
        var first = Fingerprint("class A {} class B {public object M() => new {Value=1};}");
        var second = Fingerprint("class A {public object M() => new {Other=1};} class B {public object M() => new {Value=1};}");
        var changes = first.Compare(second);
        Assert.Contains(Name("A"), changes.Changed);
        Assert.Contains(Name("B"), changes.Changed);
        foreach (var type in first.Types.Keys.Intersect(second.Types.Keys).Where(type => first.Types[type] != second.Types[type]))
        {
            Assert.Contains(type, changes.Changed);
        }
    }

    [Fact]
    public void Generated_member_renumbering_after_lambda_method_reorder_conservatively_changes_owner()
    {
        const string prefix = "using System; public class A {";
        const string firstMethod = "public Func<int> M() => () => 1;";
        const string secondMethod = "public Func<int> N() => () => 2;";
        const string suffix = "} public class B {public int Value() => 3;}";
        var first = IlFingerprintCompiler.Compile(prefix + firstMethod + secondMethod + suffix);
        var repeated = IlFingerprintCompiler.Compile(prefix + firstMethod + secondMethod + suffix);
        var second = IlFingerprintCompiler.Compile(prefix + secondMethod + firstMethod + suffix);

        Assert.True(first.Content.AsSpan().SequenceEqual(repeated.Content.AsSpan()));
        Assert.NotEqual(GeneratedLambdaName(first, "M"), GeneratedLambdaName(second, "M"));
        Assert.NotEqual(GeneratedLambdaName(first, "N"), GeneratedLambdaName(second, "N"));

        var before = Compute(first);
        var after = Compute(second);
        var changes = before.Compare(after);
        Assert.Equal(new[] {Name("A")}, changes.Changed);
        Assert.Empty(changes.Added);
        Assert.Empty(changes.Removed);
        Assert.Equal(before.Types[Name("B")], after.Types[Name("B")]);
        Assert.Equal(before.AssemblyHash, after.AssemblyHash);
        Assert.False(changes.AssemblyChanged);
    }

    private static string Source(string members) => """
        using System;
        using System.Runtime.InteropServices;
        using System.Runtime.CompilerServices;
        using System.Threading.Tasks;
        [AttributeUsage(AttributeTargets.All)] public class MarkAttribute : Attribute {public MarkAttribute(int value) {}}
        public class A {
            public static void Throw() => throw new Exception();
            public static bool Check(int x) => x != 0;
        """ + members + "} public class B {public string M() => \"unchanged\";}";

    private static IlTypeName Name(string name) => new("Fixture", name);

    private static IlFingerprintSnapshot Fingerprint(string source) => Compute(IlFingerprintCompiler.Compile(source));

    private static IlFingerprintSnapshot Compute(IlAssemblyImage image) =>
        IlAssemblyFingerprints.Compute(image, new HashSet<string>(StringComparer.Ordinal) {"Fixture", "Library", "Dependent"});

    private static void Equal(IlFingerprintSnapshot first, IlFingerprintSnapshot second)
    {
        Assert.Equal(first.AssemblyHash, second.AssemblyHash);
        Assert.Equal(first.Types.OrderBy(pair => pair.Key.Name), second.Types.OrderBy(pair => pair.Key.Name));
    }

    private static byte[] Body(IlAssemblyImage image, string type, string method)
    {
        using var pe = new PEReader(image.Content);
        var metadata = pe.GetMetadataReader();
        var definition = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition)
            .Single(definition => metadata.GetString(definition.Name) == type);
        var target = definition.GetMethods().Select(metadata.GetMethodDefinition)
            .Single(definition => metadata.GetString(definition.Name) == method);
        return pe.GetMethodBody(target.RelativeVirtualAddress).GetILBytes()!;
    }

    private static string GeneratedLambdaName(IlAssemblyImage image, string method)
    {
        using var pe = new PEReader(image.Content);
        var metadata = pe.GetMetadataReader();
        var owner = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition)
            .Single(type => metadata.GetString(type.Name) == "A");
        return owner.GetNestedTypes().Select(metadata.GetTypeDefinition).SelectMany(type => type.GetMethods())
            .Select(metadata.GetMethodDefinition).Select(definition => metadata.GetString(definition.Name))
            .Single(name => name.StartsWith("<" + method + ">b__", StringComparison.Ordinal));
    }
}
