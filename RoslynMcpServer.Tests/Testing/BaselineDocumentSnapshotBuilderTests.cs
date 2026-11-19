using Microsoft.CodeAnalysis.CSharp;
using RoslynMcpServer.Services.Testing.Baseline;
using RoslynMcpServer.Services.Testing.Baseline.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

public sealed class BaselineDocumentSnapshotBuilderTests
{
    [Fact]
    public void Generic_overloads_have_distinct_keys()
    {
        var methods = Snapshot("class C { void Run<T>() {} void Run<T, U>() {} }").Members
            .Where(member => member.Key.Kind == SyntaxKind.MethodDeclaration).ToArray();
        Assert.Equal(new[] { 1, 2 }, methods.Select(member => member.Key.Arity));
        Assert.NotEqual(methods[0].Key, methods[1].Key);
    }

    [Fact]
    public void Explicit_interface_and_ordinary_method_have_distinct_keys()
    {
        var methods = Snapshot("class C { void I.Run() {} void Run() {} }").Members
            .Where(member => member.Key.Kind == SyntaxKind.MethodDeclaration).ToArray();
        Assert.All(methods, member => Assert.Equal("Run", member.Key.Name));
        Assert.NotEmpty(methods[0].Key.ExplicitInterface);
        Assert.Empty(methods[1].Key.ExplicitInterface);
        Assert.NotEqual(methods[0].Key, methods[1].Key);
    }

    [Fact]
    public void Partial_declarations_are_retained_in_each_document()
    {
        var definition = Method(Snapshot("partial class C { partial void Run(); }"));
        var implementation = Method(Snapshot("partial class C { partial void Run() {} }"));
        Assert.Equal(definition.Key, implementation.Key);
        Assert.True(definition.IsPartial);
        Assert.True(implementation.IsPartial);
        Assert.NotEqual(definition.SyntaxHash, implementation.SyntaxHash);
        Assert.Equal(2, Snapshot("partial class C { partial void Run(); partial void Run() {} }").Members
            .Count(member => member.Key.Kind == SyntaxKind.MethodDeclaration));
    }

    [Fact]
    public void Comments_and_whitespace_change_only_exact_text_checksum()
    {
        var before = Snapshot("using System; namespace N { class C { int Run(int x) => x + 1; } }");
        var after = Snapshot("/* header */ using  System; namespace N { class C {\n int Run ( int x ) => x /* note */ + 1 ; } }");
        Assert.NotEqual(before.TextChecksum, after.TextChecksum);
        Assert.Equal(before.Members.ToArray(), after.Members.ToArray());
        Assert.Equal(before.ImportsAndNamespacesHash, after.ImportsAndNamespacesHash);
        Assert.Equal(before.GlobalUsingsHash, after.GlobalUsingsHash);
        Assert.Equal(before.AssemblyAndModuleAttributesHash, after.AssemblyAndModuleAttributesHash);
        Assert.Equal(before.DirectivesHash, after.DirectivesHash);
        Assert.Equal(before.TextChecksum, Snapshot("using System; namespace N { class C { int Run(int x) => x + 1; } }").TextChecksum);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void Editing_active_or_inactive_conditional_code_changes_member_hash(string condition)
    {
        var source = "class C { int Run() {\n#if " + condition + "\nreturn 1;\n#endif\nreturn 0; } }";
        Assert.NotEqual(Method(Snapshot(source)).SyntaxHash,
            Method(Snapshot(source.Replace("return 1", "return 2", StringComparison.Ordinal))).SyntaxHash);
    }

    [Fact]
    public void Directive_condition_changes_hash_even_when_branch_stays_inactive()
    {
        var before = Snapshot("class C { void Run() {\n#if A\nreturn;\n#endif\n} }");
        var after = Snapshot("class C { void Run() {\n#if B\nreturn;\n#endif\n} }");
        Assert.NotEqual(Method(before).SyntaxHash, Method(after).SyntaxHash);
    }

    [Fact]
    public void Directive_and_disabled_code_comments_and_spacing_do_not_change_hashes()
    {
        var before = Snapshot("class C { void Run() {\n#if false\nint x = 1;\n#endif\n} }");
        var after = Snapshot("class C { void Run() {\n#if  false // comment\nint  x=1; /* comment */\n#endif\n} }");
        Assert.Equal(before.Members.ToArray(), after.Members.ToArray());
        Assert.Equal(before.DirectivesHash, after.DirectivesHash);
    }

    [Theory]
    [InlineData("using A;", "using B;", 0)]
    [InlineData("using static A;", "using static B;", 0)]
    [InlineData("using X = A;", "using X = B;", 0)]
    [InlineData("namespace A;", "namespace B;", 0)]
    [InlineData("global using A;", "global using B;", 1)]
    [InlineData("[assembly: A]", "[assembly: B]", 2)]
    [InlineData("[module: A]", "[module: B]", 2)]
    public void Outside_changes_affect_only_their_bucket_and_leave_member_hashes(string beforeText, string afterText, int bucket)
    {
        var before = Snapshot(beforeText + " class C { void Run() {} }");
        var after = Snapshot(afterText + " class C { void Run() {} }");
        var beforeHashes = new[] { before.ImportsAndNamespacesHash, before.GlobalUsingsHash, before.AssemblyAndModuleAttributesHash };
        var afterHashes = new[] { after.ImportsAndNamespacesHash, after.GlobalUsingsHash, after.AssemblyAndModuleAttributesHash };
        for (var index = 0; index < beforeHashes.Length; index++)
        {
            Assert.Equal(index == bucket, beforeHashes[index] != afterHashes[index]);
        }
        Assert.Equal(before.Members.Select(member => member.SyntaxHash), after.Members.Select(member => member.SyntaxHash));
    }

    [Fact]
    public void Nested_type_paths_include_namespaces_and_every_generic_arity()
    {
        var snapshot = Snapshot("namespace N { class Outer<T> { class Inner<U, V> { void Run() {} } } }");
        Assert.Equal("N.Outer`1.Inner`2", Method(snapshot).Key.ContainingTypePath);
        Assert.NotEqual(Method(snapshot).Key,
            Method(Snapshot("namespace N { class Outer<T, U> { class Inner<V> { void Run() {} } } }")).Key);
    }

    [Fact]
    public void Parameter_syntax_distinguishes_overloads()
    {
        var methods = Snapshot("class C { void Run(int x) {} void Run(ref int x) {} void Run(string x) {} }").Members
            .Where(member => member.Key.Kind == SyntaxKind.MethodDeclaration).ToArray();
        Assert.Equal(3, methods.Select(member => member.Key).Distinct().Count());
    }

    [Fact]
    public void Type_surface_hash_excludes_child_bodies()
    {
        var before = Snapshot("[A] partial class C : B { int Run() => 1; }");
        var bodyChange = Snapshot("[A] partial class C : B { int Run() => 2; }");
        Assert.Equal(before.Members[0], bodyChange.Members[0]);
        Assert.True(before.Members[0].IsPartial);
        foreach (var source in new[] { "[Z] partial class C : B {}", "[A] class C : B {}", "[A] partial class C : D {}" })
        {
            Assert.NotEqual(before.Members[0].SyntaxHash, Snapshot(source).Members[0].SyntaxHash);
        }
    }

    [Fact]
    public void Every_type_member_form_is_retained_without_key_collisions()
    {
        var snapshot = Snapshot("""
            class C
            {
                int a, b;
                event System.Action E, F;
                C() {} static C() {} ~C() {}
                int P { get; set; }
                event System.Action Changed { add {} remove {} }
                int this[int x] => x;
                void Run() {}
                public static C operator +(C x, C y) => x;
                public static implicit operator int(C x) => 0;
                public static implicit operator long(C x) => 0;
                delegate void D<T>(T x);
                enum State { A, B }
                record Nested(int Value);
            }
            """);
        Assert.False(snapshot.HasUnparseableContent);
        Assert.Equal(20, snapshot.Members.Length);
        Assert.Equal(snapshot.Members.Length, snapshot.Members.Select(member => member.Key).Distinct().Count());
        Assert.Equal(new[] { "a", "b" }, snapshot.Members.Where(member => member.Key.Kind == SyntaxKind.FieldDeclaration)
            .Select(member => member.Key.Name));
    }

    [Fact]
    public void Top_level_statements_have_entries()
    {
        var before = Snapshot("System.Console.WriteLine(1);");
        var after = Snapshot("System.Console.WriteLine(2);");
        Assert.Single(before.Members);
        Assert.Equal(SyntaxKind.GlobalStatement, before.Members[0].Key.Kind);
        Assert.NotEqual(before.Members[0].SyntaxHash, after.Members[0].SyntaxHash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("// empty")]
    [InlineData("global using A;")]
    [InlineData("[assembly: A]")]
    public void Valid_documents_without_members_are_not_marked_unparseable(string source)
    {
        var snapshot = Snapshot(source);
        Assert.False(snapshot.HasUnparseableContent);
        Assert.Empty(snapshot.Members);
    }

    [Theory]
    [InlineData("class {")]
    [InlineData("@#$")]
    [InlineData("class C { void Run(")]
    public void Broken_syntax_is_marked_instead_of_silently_becoming_empty(string source)
    {
        Assert.True(Snapshot(source).HasUnparseableContent);
    }

    [Fact]
    public void Standalone_directives_and_disabled_declarations_are_covered()
    {
        var before = Snapshot("#if false\nclass C { int X = 1; }\n#endif");
        var after = Snapshot("#if false\nclass C { int X = 2; }\n#endif");
        Assert.Empty(before.Members);
        Assert.NotEqual(before.DirectivesHash, after.DirectivesHash);
        Assert.NotEqual(Snapshot("#nullable enable").DirectivesHash, Snapshot("#nullable disable").DirectivesHash);
    }

    [Theory]
    [InlineData("namespace N { using A; class C {} } namespace N { class D {} }",
        "namespace N { class C {} } namespace N { using A; class D {} }")]
    [InlineData("extern alias A; class C {}", "extern alias B; class C {}")]
    public void Import_scope_and_extern_alias_changes_are_covered(string before, string after)
    {
        Assert.NotEqual(Snapshot(before).ImportsAndNamespacesHash, Snapshot(after).ImportsAndNamespacesHash);
    }

    [Fact]
    public void Preprocessor_diagnostic_messages_are_covered()
    {
        Assert.NotEqual(Snapshot("#warning first").DirectivesHash, Snapshot("#warning second").DirectivesHash);
    }

    [Theory]
    [InlineData("namespace N { using X = System.Int32; class C { X Value; } } namespace N { using X = System.Int64; }",
        "namespace N { using X = System.Int32; } namespace N { using X = System.Int64; class C { X Value; } }")]
    [InlineData("namespace N { using X = System.Int32; partial class C { X Value; } } namespace N { using X = System.Int64; partial class C {} }",
        "namespace N { using X = System.Int32; partial class C {} } namespace N { using X = System.Int64; partial class C { X Value; } }")]
    public void Moving_declarations_between_import_scopes_changes_member_hash(string beforeSource, string afterSource)
    {
        var before = Snapshot(beforeSource);
        var after = Snapshot(afterSource);
        var beforeField = Assert.Single(before.Members.Where(member => member.Key.Kind == SyntaxKind.FieldDeclaration));
        var afterField = Assert.Single(after.Members.Where(member => member.Key.Kind == SyntaxKind.FieldDeclaration));
        Assert.False(before.HasUnparseableContent);
        Assert.False(after.HasUnparseableContent);
        Assert.Equal(beforeField.Key, afterField.Key);
        Assert.NotEqual(beforeField.SyntaxHash, afterField.SyntaxHash);
    }

    [Fact]
    public void Editing_scoped_imports_leaves_member_hashes_unchanged()
    {
        var before = Snapshot("namespace N { using X = System.Int32; partial class C { X Value; } }");
        var after = Snapshot("namespace N { using X = System.Int64; partial class C { X Value; } }");
        Assert.Equal(before.Members.ToArray(), after.Members.ToArray());
        Assert.NotEqual(before.ImportsAndNamespacesHash, after.ImportsAndNamespacesHash);
    }

    [Theory]
    [InlineData("// comment\n")]
    [InlineData("   \n")]
    public void Trivia_inserted_into_empty_inactive_branch_does_not_change_hashes(string insertedTrivia)
    {
        var before = Snapshot("class C { void Run() {\n#if false\n#endif\n} }");
        var after = Snapshot("class C { void Run() {\n#if false\n" + insertedTrivia + "#endif\n} }");
        Assert.Equal(before.Members.ToArray(), after.Members.ToArray());
        Assert.Equal(before.ImportsAndNamespacesHash, after.ImportsAndNamespacesHash);
        Assert.Equal(before.GlobalUsingsHash, after.GlobalUsingsHash);
        Assert.Equal(before.AssemblyAndModuleAttributesHash, after.AssemblyAndModuleAttributesHash);
        Assert.Equal(before.DirectivesHash, after.DirectivesHash);
        Assert.NotEqual(before.TextChecksum, after.TextChecksum);
    }

    [Theory]
    [InlineData("enum E { A, B }", "enum E { B, A }", "A")]
    [InlineData("class C { static int A = 1; static int B = A + 1; }",
        "class C { static int B = A + 1; static int A = 1; }", "A")]
    [InlineData("struct S { int a; long b; }", "struct S { long b; int a; }", "a")]
    [InlineData("struct S { event System.Action A; event System.Action B; }",
        "struct S { event System.Action B; event System.Action A; }", "A")]
    [InlineData("class C { static event System.Action A = Create(1); static event System.Action B = Create(2); }",
        "class C { static event System.Action B = Create(2); static event System.Action A = Create(1); }", "A")]
    [InlineData("class C { static int A { get; } = 1; static int B { get; } = A + 1; }",
        "class C { static int B { get; } = A + 1; static int A { get; } = 1; }", "A")]
    [InlineData("class C { static int A = 1; static int B { get; } = A + 1; }",
        "class C { static int B { get; } = A + 1; static int A = 1; }", "A")]
    [InlineData("partial class C { static int A = 1; } partial class C { static int B = A + 1; }",
        "partial class C { static int B = A + 1; } partial class C { static int A = 1; }", "A")]
    [InlineData("record R { public int A { get; init; } public int B { get; init; } }",
        "record R { public int B { get; init; } public int A { get; init; } }", "A")]
    [InlineData("struct S { int A { get; set; } long B { get; set; } }",
        "struct S { long B { get; set; } int A { get; set; } }", "A")]
    [InlineData("class D { public int A { get; set; } public int B { get; set; } }",
        "class D { public int B { get; set; } public int A { get; set; } }", "A")]
    public void Meaningful_declaration_order_changes_member_hash(string beforeSource, string afterSource, string memberName)
    {
        var before = Snapshot(beforeSource);
        var after = Snapshot(afterSource);
        Assert.False(before.HasUnparseableContent);
        Assert.False(after.HasUnparseableContent);
        var beforeByKey = before.Members.GroupBy(member => member.Key).ToDictionary(group => group.Key, group => group.Count());
        var afterByKey = after.Members.GroupBy(member => member.Key).ToDictionary(group => group.Key, group => group.Count());
        Assert.Equal(beforeByKey.Count, afterByKey.Count);
        Assert.All(beforeByKey, entry => Assert.Equal(entry.Value, afterByKey[entry.Key]));
        var movedMember = Assert.Single(before.Members.Where(member => member.Key.Name == memberName));
        var afterMember = Assert.Single(after.Members.Where(member => member.Key == movedMember.Key));
        Assert.NotEqual(movedMember.SyntaxHash, afterMember.SyntaxHash);
    }

    [Theory]
    [InlineData("class C { void M() {} int x = 1; void N() {} }",
        "class C { void N() {} int x = 1; void M() {} }")]
    [InlineData("class C { class A { int a; } int x = 1; class B { int b; } }",
        "class C { class B { int b; } int x = 1; class A { int a; } }")]
    [InlineData("class C { int this[int i] => i; int x = 1; int this[string s] => s.Length; }",
        "class C { int this[string s] => s.Length; int x = 1; int this[int i] => i; }")]
    [InlineData("class C { event System.Action A { add {} remove {} } int x = 1; event System.Action B { add {} remove {} } }",
        "class C { event System.Action B { add {} remove {} } int x = 1; event System.Action A { add {} remove {} } }")]
    public void Reordering_members_without_meaningful_order_keeps_hashes_by_key(string beforeSource, string afterSource)
    {
        var before = Snapshot(beforeSource);
        var after = Snapshot(afterSource);
        var afterByKey = after.Members.ToDictionary(member => member.Key);
        Assert.Equal(before.Members.Length, afterByKey.Count);
        Assert.All(before.Members, member => Assert.Equal(member, afterByKey[member.Key]));
        Assert.Equal(before.ImportsAndNamespacesHash, after.ImportsAndNamespacesHash);
        Assert.Equal(before.GlobalUsingsHash, after.GlobalUsingsHash);
        Assert.Equal(before.AssemblyAndModuleAttributesHash, after.AssemblyAndModuleAttributesHash);
        Assert.Equal(before.DirectivesHash, after.DirectivesHash);
    }

    private static BaselineDocumentSnapshot Snapshot(string source) =>
        BaselineDocumentSnapshotBuilder.Build(CSharpSyntaxTree.ParseText(source));

    private static BaselineMemberSnapshot Method(BaselineDocumentSnapshot snapshot) =>
        Assert.Single(snapshot.Members.Where(member => member.Key.Kind == SyntaxKind.MethodDeclaration));
}
