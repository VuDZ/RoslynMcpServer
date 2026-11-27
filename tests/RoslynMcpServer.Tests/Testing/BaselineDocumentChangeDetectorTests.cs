using Microsoft.CodeAnalysis.CSharp;
using RoslynMcpServer.Services.Testing.Baseline;
using RoslynMcpServer.Services.Testing.Baseline.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

public sealed class BaselineDocumentChangeDetectorTests
{
    [Fact]
    public void Method_body_change_is_one_modified_member()
    {
        var before = Snapshot("class C { int Run() => 1; }");
        var after = Snapshot("class C { int Run() => 2; }");
        AssertChanges(before, after, Member(BaselineChangeKind.MemberModified, Find(after, "Run")));
    }

    [Theory]
    [InlineData("class C {}", "class C { void Run() {} }", true)]
    [InlineData("class C { void Run() {} }", "class C {}", false)]
    public void Added_and_removed_members_retain_the_correct_key(string beforeSource, string afterSource, bool added)
    {
        var before = Snapshot(beforeSource);
        var after = Snapshot(afterSource);
        AssertChanges(before, after, Member(added ? BaselineChangeKind.MemberAdded : BaselineChangeKind.MemberRemoved,
            Find(added ? after : before, "Run")));
    }

    [Theory]
    [InlineData("void Run(int x) {}", "void Run(string x) {}")]
    [InlineData("void Run(int x) {}", "void Run(ref int x) {}")]
    [InlineData("void Run() {}", "void Renamed() {}")]
    [InlineData("void Run<T>() {}", "void Run<T, U>() {}")]
    [InlineData("partial void Run();", "partial void Run(int x);")]
    public void Signature_changes_are_old_key_removal_and_new_key_addition(string beforeMember, string afterMember)
    {
        var before = Snapshot("partial class C { " + beforeMember + " }");
        var after = Snapshot("partial class C { " + afterMember + " }");
        AssertChanges(before, after,
            Member(BaselineChangeKind.MemberRemoved, before.Members[1]),
            Member(BaselineChangeKind.MemberAdded, after.Members[1]));
    }

    [Theory]
    [InlineData("class C { int Run<T>() => 1; int Run<T, U>() => 0; }",
        "class C { int Run<T>() => 2; int Run<T, U>() => 0; }", 0)]
    [InlineData("class C { int I.Run() => 1; int Run() => 0; }",
        "class C { int I.Run() => 2; int Run() => 0; }", 0)]
    [InlineData("partial class C { partial int Run() => 1; }",
        "partial class C { partial int Run() => 2; }", 0)]
    public void Overloads_explicit_implementations_and_partial_members_match_exactly(
        string beforeSource, string afterSource, int methodIndex)
    {
        var before = Snapshot(beforeSource);
        var after = Snapshot(afterSource);
        var method = after.Members.Where(member => member.Key.Kind == SyntaxKind.MethodDeclaration).ElementAt(methodIndex);
        AssertChanges(before, after, Member(BaselineChangeKind.MemberModified, method));
    }

    [Theory]
    [InlineData("class C { int Run() => 1; }", "// header\n class C { int Run ( ) => 1 /* note */ ; }")]
    [InlineData("class C { int Run() => 1; }", "class C { int Run() => 1; }")]
    [InlineData("class C { void A() {} void B() {} }", "class C { void B() {} void A() {} }")]
    [InlineData("partial class C {} partial class C {}", "partial class C { /* trivia */ } partial class C {}")]
    public void Trivia_reverted_text_and_unordered_method_moves_have_no_changes(string beforeSource, string afterSource)
    {
        AssertChanges(Snapshot(beforeSource), Snapshot(afterSource));
    }

    [Theory]
    [InlineData("using A; class C { void Run() {} }", "using B; class C { void Run() {} }")]
    [InlineData("using X = A; class C { void Run() {} }", "using X = B; class C { void Run() {} }")]
    [InlineData("using static A; class C { void Run() {} }", "using static B; class C { void Run() {} }")]
    [InlineData("namespace A {}", "namespace B {}")]
    public void Imports_aliases_and_namespaces_expand_the_file(string beforeSource, string afterSource)
    {
        AssertChanges(Snapshot(beforeSource), Snapshot(afterSource),
            Scope(BaselineChangeKind.FileChanged, BaselineChangeReason.ImportsAndNamespaces));
    }

    [Theory]
    [InlineData("global using A;", "global using B;", (int)BaselineChangeReason.GlobalUsings)]
    [InlineData("", "global using A;", (int)BaselineChangeReason.GlobalUsings)]
    [InlineData("[assembly: InternalsVisibleTo(\"A\")]", "[assembly: InternalsVisibleTo(\"B\")]",
        (int)BaselineChangeReason.AssemblyAndModuleAttributes)]
    [InlineData("", "[assembly: InternalsVisibleTo(\"Tests\")]", (int)BaselineChangeReason.AssemblyAndModuleAttributes)]
    [InlineData("[assembly: InternalsVisibleTo(\"Tests\")]", "", (int)BaselineChangeReason.AssemblyAndModuleAttributes)]
    [InlineData("[module: A]", "[module: B]", (int)BaselineChangeReason.AssemblyAndModuleAttributes)]
    public void Global_imports_and_assembly_module_attributes_expand_the_project(
        string beforeSource, string afterSource, int reason)
    {
        AssertChanges(Snapshot(beforeSource), Snapshot(afterSource),
            Scope(BaselineChangeKind.ProjectChanged, (BaselineChangeReason)reason));
    }

    [Fact]
    public void Inactive_only_document_changes_expand_the_file()
    {
        AssertChanges(Snapshot("#if false\nclass C { int X = 1; }\n#endif"),
            Snapshot("#if false\nclass C { int X = 2; }\n#endif"),
            Scope(BaselineChangeKind.FileChanged, BaselineChangeReason.ImportsAndNamespaces));
    }

    [Fact]
    public void Isolated_directives_hash_change_expands_the_file_even_with_the_same_checksum()
    {
        var before = Snapshot("");
        var after = before with { DirectivesHash = "changed-directives" };
        AssertChanges(before, after, Scope(BaselineChangeKind.FileChanged, BaselineChangeReason.Directives));
    }

    [Theory]
    [InlineData("class C {}", "[A] class C {}")]
    [InlineData("class C {}", "partial class C {}")]
    [InlineData("class C : A {}", "class C : B {}")]
    [InlineData("class C : I {}", "class C : I, J {}")]
    [InlineData("record C(int X);", "record C(int X, int Y);")]
    [InlineData("delegate void D();", "[A] delegate void D();")]
    public void Type_surface_changes_expand_only_the_type(string beforeSource, string afterSource)
    {
        var before = Snapshot(beforeSource);
        var after = Snapshot(afterSource);
        var beforeType = Assert.Single(before.Members);
        var afterType = Assert.Single(after.Members);
        if (beforeType.Key == afterType.Key)
        {
            AssertChanges(before, after,
                new BaselineDocumentChange(BaselineChangeKind.TypeChanged, BaselineChangeReason.TypeSurface, afterType.Key));
        }
        else
        {
            AssertChanges(before, after, Member(BaselineChangeKind.MemberRemoved, beforeType),
                Member(BaselineChangeKind.MemberAdded, afterType));
        }
    }

    [Theory]
    [InlineData("class C { void Run() {} }", "class C { void Run(")]
    [InlineData("class C { void Run(", "class C { void Run() {} }")]
    public void Broken_syntax_in_either_snapshot_expands_the_file(string beforeSource, string afterSource)
    {
        AssertChanges(Snapshot(beforeSource), Snapshot(afterSource),
            Scope(BaselineChangeKind.FileChanged, BaselineChangeReason.UnparseableContent));
    }

    [Fact]
    public void Ambiguous_duplicate_keys_expand_the_file_without_collapsing_partial_declarations()
    {
        var before = Snapshot("partial class C { partial void Run(); partial void Run() {} }");
        var after = Snapshot("partial class C { partial void Run(); partial void Run() { Work(); } }");
        AssertChanges(before, after, Scope(BaselineChangeKind.FileChanged, BaselineChangeReason.AmbiguousMemberKey));
    }

    [Fact]
    public void Project_expansion_keeps_precise_member_changes_available()
    {
        var before = Snapshot("global using A; class C { int Run() => 1; }");
        var after = Snapshot("global using B; class C { int Run() => 2; }");
        AssertChanges(before, after, Scope(BaselineChangeKind.ProjectChanged, BaselineChangeReason.GlobalUsings),
            Member(BaselineChangeKind.MemberModified, Find(after, "Run")));
    }

    [Theory]
    [InlineData("public static C operator +(C x, C y) => x;", "public static C operator +(C x, C y) => y;")]
    [InlineData("public static implicit operator int(C x) => 1;", "public static implicit operator int(C x) => 2;")]
    public void Modified_operators_and_conversions_expand_the_project(string beforeMember, string afterMember)
    {
        var before = Snapshot("class C { " + beforeMember + " }");
        var after = Snapshot("class C { " + afterMember + " }");
        AssertChanges(before, after, Member(BaselineChangeKind.MemberModified, after.Members[1]),
            new BaselineDocumentChange(BaselineChangeKind.ProjectChanged,
                BaselineChangeReason.OperatorOrConversion, after.Members[1].Key));
    }

    [Theory]
    [InlineData("public static C operator +(C x, C y) => x;")]
    [InlineData("public static implicit operator int(C x) => 1;")]
    public void New_operators_and_conversions_expand_the_project(string member)
    {
        var before = Snapshot("class C {}");
        var after = Snapshot("class C { " + member + " }");
        AssertChanges(before, after, Member(BaselineChangeKind.MemberAdded, after.Members[1]),
            new BaselineDocumentChange(BaselineChangeKind.ProjectChanged,
                BaselineChangeReason.OperatorOrConversion, after.Members[1].Key));
    }

    [Fact]
    public void New_extension_method_expands_the_project()
    {
        var before = Snapshot("static class C {}");
        var after = Snapshot("static class C { static void Run(this string value) {} }");
        AssertChanges(before, after, Member(BaselineChangeKind.MemberAdded, Find(after, "Run")),
            new BaselineDocumentChange(BaselineChangeKind.ProjectChanged,
                BaselineChangeReason.NewExtensionMethod, Find(after, "Run").Key));
    }

    [Theory]
    [InlineData("Fact")]
    [InlineData("Xunit.FactAttribute")]
    [InlineData("Theory")]
    [InlineData("NUnit.Framework.Test")]
    [InlineData("TestCase")]
    [InlineData("TestCaseSource")]
    [InlineData("Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute")]
    public void Changed_test_method_is_flagged_from_the_current_attribute_name(string attribute)
    {
        var before = Snapshot("class C { [" + attribute + "] int Run() => 1; }");
        var after = Snapshot("class C { [" + attribute + "] int Run() => 2; }");
        AssertChanges(before, after, Member(BaselineChangeKind.MemberModified, Find(after, "Run"), test: true));
    }

    [Theory]
    [InlineData("void Run() {}", "[Fact] void Run() {}", true, true)]
    [InlineData("[Fact] void Run() {}", "void Run() {}", false, false)]
    [InlineData("[CustomFact] int Run() => 1;", "[CustomFact] int Run() => 2;", false, false)]
    public void Test_role_uses_current_syntax_and_custom_attribute_hierarchies_are_not_guessed(
        string beforeMember, string afterMember, bool isTest, bool blocks)
    {
        var before = Snapshot("class C { " + beforeMember + " }");
        var after = Snapshot("class C { " + afterMember + " }");
        AssertChanges(before, after, Member(BaselineChangeKind.MemberModified, Find(after, "Run"), test: isTest, blocks: blocks));
    }

    [Fact]
    public void New_test_is_flagged_but_removed_test_has_no_current_role()
    {
        var before = Snapshot("class C { [Fact] void Old() {} }");
        var after = Snapshot("class C { [Fact] void New() {} }");
        AssertChanges(before, after, Member(BaselineChangeKind.MemberRemoved, Find(before, "Old")),
            Member(BaselineChangeKind.MemberAdded, Find(after, "New"), test: true));
    }

    [Theory]
    [InlineData("C() { Work(1); }", "C() { Work(2); }", ".ctor")]
    [InlineData("int value = 1;", "int value = 2;", "value")]
    [InlineData("int Value { get; } = 1;", "int Value { get; } = 2;", "Value")]
    public void Own_constructor_and_initializer_changes_are_test_class_roots(
        string beforeMember, string afterMember, string name)
    {
        var before = Snapshot("class C { [Fact] void Test() {} " + beforeMember + " }");
        var after = Snapshot("class C { [Fact] void Test() {} " + afterMember + " }");
        AssertChanges(before, after, Member(BaselineChangeKind.MemberModified, Find(after, name), root: true));
    }

    [Theory]
    [InlineData("SetUp")]
    [InlineData("NUnit.Framework.TearDownAttribute")]
    [InlineData("OneTimeSetUp")]
    [InlineData("OneTimeTearDown")]
    [InlineData("TestInitialize")]
    [InlineData("TestCleanup")]
    [InlineData("ClassInitialize")]
    [InlineData("ClassCleanup")]
    public void Own_setup_and_teardown_methods_are_test_class_roots(string attribute)
    {
        var before = Snapshot("class C { [Fact] void Test() {} [" + attribute + "] void Prepare() { Work(1); } }");
        var after = Snapshot("class C { [Fact] void Test() {} [" + attribute + "] void Prepare() { Work(2); } }");
        AssertChanges(before, after, Member(BaselineChangeKind.MemberModified, Find(after, "Prepare"), root: true));
    }

    [Theory]
    [InlineData("Xunit.IAsyncLifetime", "System.Threading.Tasks.Task InitializeAsync()", "InitializeAsync")]
    [InlineData("Xunit.IAsyncLifetime", "System.Threading.Tasks.Task DisposeAsync()", "DisposeAsync")]
    [InlineData("System.IDisposable", "void Dispose()", "Dispose")]
    [InlineData("System.IAsyncDisposable", "System.Threading.Tasks.ValueTask DisposeAsync()", "DisposeAsync")]
    public void Own_explicit_lifecycle_contracts_are_roots(string contract, string signature, string name)
    {
        var before = Snapshot("class C : " + contract + " { [Fact] void Test() {} " + signature + " { Work(1); } }");
        var after = Snapshot("class C : " + contract + " { [Fact] void Test() {} " + signature + " { Work(2); } }");
        AssertChanges(before, after, Member(BaselineChangeKind.MemberModified, Find(after, name), root: true));
    }

    [Theory]
    [InlineData("class C { [Fact] void Test() {} void Helper() { Work(1); } }", "Helper")]
    [InlineData("class C { [Fact] void Test() {} void Dispose() { Work(1); } }", "Dispose")]
    [InlineData("class C { [Fact] void Test() {} } class Fixture { Fixture() { Work(1); } }", ".ctor")]
    [InlineData("class C { [Fact] void Test() {} } class Fixture { [SetUp] void Prepare() { Work(1); } }", "Prepare")]
    [InlineData("class Outer { Outer() { Work(1); } class Nested { [Fact] void Test() {} } }", ".ctor")]
    public void Helper_fixture_and_outer_class_members_are_not_own_roots(string source, string name)
    {
        var before = Snapshot(source);
        var after = Snapshot(source.Replace("Work(1)", "Work(2)", StringComparison.Ordinal));
        AssertChanges(before, after, Member(BaselineChangeKind.MemberModified, Find(after, name)));
    }

    [Fact]
    public void Property_accessor_change_is_not_misclassified_as_an_initializer_change()
    {
        var before = Snapshot("class C { [Fact] void Test() {} int Value { get { return 1; } } = 0; }");
        var after = Snapshot("class C { [Fact] void Test() {} int Value { get { return 2; } } = 0; }");
        AssertChanges(before, after, Member(BaselineChangeKind.MemberModified, Find(after, "Value")));
    }

    [Fact]
    public void In_member_preprocessor_edit_remains_a_member_change()
    {
        var before = Snapshot("class C { int Run() {\n#if false\nreturn 1;\n#endif\nreturn 0; } }");
        var after = Snapshot("class C { int Run() {\n#if false\nreturn 2;\n#endif\nreturn 0; } }");
        AssertChanges(before, after, Member(BaselineChangeKind.MemberModified, Find(after, "Run")));
    }

    [Theory]
    [InlineData("class C { [Fact] void Test() {} int value; }",
        "class C { [Fact] void Test() {} int value = 1; }", "value")]
    [InlineData("class C { [Fact] void Test() {} int Value { get; } }",
        "class C { [Fact] void Test() {} int Value { get; } = 1; }", "Value")]
    public void Introducing_an_initializer_on_an_existing_member_does_not_allow_narrowing(
        string beforeSource, string afterSource, string name)
    {
        var before = Snapshot(beforeSource);
        var after = Snapshot(afterSource);
        AssertChanges(before, after, Member(BaselineChangeKind.MemberModified, Find(after, name)));
    }

    [Theory]
    [InlineData("using A; class C {}", "using B; class C { [Fact] void New() {} }",
        (int)BaselineChangeReason.ImportsAndNamespaces)]
    [InlineData("partial class C {} partial class C {}", "partial class C {} partial class C { [Fact] void New() {} }",
        (int)BaselineChangeReason.AmbiguousMemberKey)]
    [InlineData("class C {}", "class C { [Fact] void New() {} void Broken(",
        (int)BaselineChangeReason.UnparseableContent)]
    public void File_fallback_preserves_new_test_entries(string beforeSource, string afterSource, int reason)
    {
        var before = Snapshot(beforeSource);
        var after = Snapshot(afterSource);
        AssertChanges(before, after, Scope(BaselineChangeKind.FileChanged, (BaselineChangeReason)reason),
            Member(BaselineChangeKind.MemberAdded, Find(after, "New"), test: true));
    }

    [Fact]
    public void Const_value_changes_are_ordinary_members_even_inside_a_test_class()
    {
        var before = Snapshot("class C { [Fact] void Test() {} const int Value = 1; }");
        var after = Snapshot("class C { [Fact] void Test() {} const int Value = 2; }");
        AssertChanges(before, after, Member(BaselineChangeKind.MemberModified, Find(after, "Value")));
    }

    [Theory]
    [InlineData("void Dispose<T>() { Work(1); }")]
    [InlineData("static void Dispose() { Work(1); }")]
    [InlineData("void Dispose() { Work(1); }")]
    public void Disposal_helpers_do_not_replace_an_explicit_lifecycle_implementation(string helper)
    {
        var source = "class C : System.IDisposable { [Fact] void Test() {} void System.IDisposable.Dispose() {} " + helper + " }";
        var before = Snapshot(source);
        var after = Snapshot(source.Replace("Work(1)", "Work(2)", StringComparison.Ordinal));
        var changed = Assert.Single(after.Members.Where(member => member.Key.Name == "Dispose"
            && member.Key.ExplicitInterface.Length == 0));
        AssertChanges(before, after, Member(BaselineChangeKind.MemberModified, changed));
    }

    private static BaselineDocumentSnapshot Snapshot(string source) =>
        BaselineDocumentSnapshotBuilder.Build(CSharpSyntaxTree.ParseText(source));

    private static BaselineMemberSnapshot Find(BaselineDocumentSnapshot snapshot, string name) =>
        Assert.Single(snapshot.Members.Where(member => member.Key.Name == name));

    private static BaselineDocumentChange Member(
        BaselineChangeKind kind, BaselineMemberSnapshot member, bool test = false, bool root = false, bool blocks = false) =>
        new(kind, BaselineChangeReason.MemberSyntax, member.Key, test, root, blocks);

    private static BaselineDocumentChange Scope(BaselineChangeKind kind, BaselineChangeReason reason) => new(kind, reason);

    private static void AssertChanges(
        BaselineDocumentSnapshot before, BaselineDocumentSnapshot after, params BaselineDocumentChange[] expected)
    {
        var actual = BaselineDocumentChangeDetector.Compare(before, after);
        Assert.Equal(expected.OrderBy(change => change.Kind).ThenBy(change => change.Key?.ToString(), StringComparer.Ordinal)
                .ThenBy(change => change.Reason),
            actual.OrderBy(change => change.Kind).ThenBy(change => change.Key?.ToString(), StringComparer.Ordinal)
                .ThenBy(change => change.Reason));
    }
}
