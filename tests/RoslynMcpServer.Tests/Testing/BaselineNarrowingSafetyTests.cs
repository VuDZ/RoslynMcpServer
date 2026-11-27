using Microsoft.CodeAnalysis.CSharp;
using RoslynMcpServer.Services.Testing.Baseline;
using RoslynMcpServer.Services.Testing.Baseline.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

public sealed class BaselineNarrowingSafetyTests
{
    [Theory]
    [InlineData("[TestFixture] abstract class Base { [SetUp] void Prepare() { Work(1); } }", "Prepare")]
    [InlineData("[TestClass] abstract class Base { [TestInitialize] void Prepare() { Work(1); } }", "Prepare")]
    [InlineData("abstract class Base { protected Base() { Work(1); } [Fact] void Test() {} }", ".ctor")]
    [InlineData("class Base { Base() { Work(1); } [Fact] void Test() {} } class D : Base {}", ".ctor")]
    [InlineData("namespace N { class Base<T> { Base() { Work(1); } [Fact] void Test() {} } } class D : N.Base<int> {}", ".ctor")]
    public void Abstract_and_locally_inherited_type_roots_remain_ordinary(string source, string name)
    {
        var before = Snapshot(source);
        var after = Snapshot(source.Replace("Work(1)", "Work(2)", StringComparison.Ordinal));
        var member = Find(after, name);
        Assert.False(member.IsOwnTestClassRoot);
        AssertChanges(before, after, Change(BaselineChangeKind.MemberModified, member));
    }

    [Theory]
    [InlineData("abstract class Base { [Fact] void Shared() { Work(1); } }")]
    [InlineData("class Base { [Fact] void Shared() { Work(1); } } class D : Base {}")]
    public void Abstract_and_locally_inherited_tests_remain_visible_but_block_narrowing(string source)
    {
        var before = Snapshot(source);
        var after = Snapshot(source.Replace("Work(1)", "Work(2)", StringComparison.Ordinal));
        var test = Find(after, "Shared");
        Assert.True(test.IsTestMethod);
        Assert.True(test.BlocksNarrowing);
        AssertChanges(before, after, Change(BaselineChangeKind.MemberModified, test, test: true, blocks: true));
    }

    [Theory]
    [InlineData("Base() { Work(1); }", ".ctor", false)]
    [InlineData("[Fact] void Shared() { Work(1); }", "Shared", true)]
    public void Abstract_on_any_partial_part_blocks_the_whole_type(string memberSource, string name, bool test)
    {
        var source = "partial class Base { [Fact] void Other() {} " + memberSource + " } abstract partial class Base {}";
        var before = Snapshot(source);
        var after = Snapshot(source.Replace("Work(1)", "Work(2)", StringComparison.Ordinal));
        var member = Find(after, name);
        Assert.False(member.IsOwnTestClassRoot);
        Assert.Equal(test, member.BlocksNarrowing);
        AssertChanges(before, after,
            new BaselineDocumentChange(BaselineChangeKind.FileChanged, BaselineChangeReason.AmbiguousMemberKey),
            Change(BaselineChangeKind.MemberModified, member, test: test, blocks: test));
    }

    [Fact]
    public void All_base_lists_contribute_immutable_unqualified_names_without_generic_arity()
    {
        var snapshot = Snapshot("""
            class D : N.Base<int>, global::Contracts.IThing<string> {}
            interface IDerived : N.IBase<int>, IThing<long> {}
            struct S : N.IStruct {}
            record R : N.RecordBase, N.IRecord<int>;
            enum E : byte { A }
            """);
        Assert.Equal(new[] { "Base", "IBase", "IRecord", "IStruct", "IThing", "RecordBase", "byte" },
            snapshot.BaseTypeNames.Order(StringComparer.Ordinal));
        var extended = snapshot.BaseTypeNames.Add("Other");
        Assert.DoesNotContain("Other", snapshot.BaseTypeNames);
        Assert.Contains("Other", extended);
    }

    [Theory]
    [InlineData("public", "field", false)]
    [InlineData("protected", "field", false)]
    [InlineData("internal", "field", false)]
    [InlineData("protected internal", "field", false)]
    [InlineData("private protected", "field", false)]
    [InlineData("private", "field", true)]
    [InlineData("", "field", true)]
    [InlineData("public", "property", false)]
    [InlineData("protected", "property", false)]
    [InlineData("internal", "property", false)]
    [InlineData("protected internal", "property", false)]
    [InlineData("private protected", "property", false)]
    [InlineData("private", "property", true)]
    [InlineData("", "property", true)]
    [InlineData("public", "event", false)]
    [InlineData("protected", "event", false)]
    [InlineData("internal", "event", false)]
    [InlineData("protected internal", "event", false)]
    [InlineData("private protected", "event", false)]
    [InlineData("private", "event", true)]
    [InlineData("", "event", true)]
    public void Only_private_static_initializers_allow_root_narrowing(string access, string kind, bool root)
    {
        var declaration = kind switch
        {
            "field" => access + " static readonly int Value = Get(1);",
            "property" => access + " static int Value { get; } = Get(1);",
            "event" => access + " static event System.Action Value = Get(1);",
            _ => throw new InvalidOperationException(kind),
        };
        var source = "class C { [Fact] void Test() {} " + declaration + " }";
        var before = Snapshot(source);
        var after = Snapshot(source.Replace("Get(1)", "Get(2)", StringComparison.Ordinal));
        var member = Find(after, "Value");
        Assert.Equal(root, member.IsOwnTestClassRoot);
        AssertChanges(before, after, Change(BaselineChangeKind.MemberModified, member, root: root));
    }

    [Fact]
    public void Static_constructor_is_an_ordinary_member()
    {
        var before = Snapshot("class C { [Fact] void Test() {} static C() { Work(1); } }");
        var after = Snapshot("class C { [Fact] void Test() {} static C() { Work(2); } }");
        AssertChanges(before, after, Change(BaselineChangeKind.MemberModified, Find(after, ".cctor")));
    }

    [Theory]
    [InlineData("class", "C() { Work(1); }", ".ctor")]
    [InlineData("sealed class", "C() { Work(1); }", ".ctor")]
    [InlineData("class", "public int Value = Work(1);", "Value")]
    [InlineData("sealed class", "public int Value { get; } = Work(1);", "Value")]
    [InlineData("class", "[SetUp] void Prepare() { Work(1); }", "Prepare")]
    public void Ordinary_and_sealed_types_without_inheritors_keep_instance_roots(string type, string declaration, string name)
    {
        var source = type + " C { [Fact] void Test() {} " + declaration + " }";
        var before = Snapshot(source);
        var after = Snapshot(source.Replace("Work(1)", "Work(2)", StringComparison.Ordinal));
        AssertChanges(before, after, Change(BaselineChangeKind.MemberModified, Find(after, name), root: true));
    }

    [Fact]
    public void Constructor_in_a_newly_test_marked_existing_type_keeps_ordinary_role()
    {
        var before = Snapshot("class H { public H() { Work(1); } }");
        var after = Snapshot("class H { public H() { Work(2); } [Fact] void New() {} }");
        AssertChanges(before, after, Change(BaselineChangeKind.MemberModified, Find(after, ".ctor")),
            Change(BaselineChangeKind.MemberAdded, Find(after, "New"), test: true));
    }

    [Theory]
    [InlineData("int Run() => 1;", "[Fact] int Run() => 2;")]
    [InlineData("int Run() => 1;", "[Fact] int Run() => 1;")]
    public void Newly_test_marked_methods_are_visible_but_block_narrowing(string beforeMethod, string afterMethod)
    {
        var before = Snapshot("class C { " + beforeMethod + " }");
        var after = Snapshot("class C { " + afterMethod + " }");
        AssertChanges(before, after, Change(BaselineChangeKind.MemberModified, Find(after, "Run"), test: true, blocks: true));
    }

    [Fact]
    public void Newly_setup_marked_existing_method_cannot_narrow()
    {
        var before = Snapshot("class C { [Fact] void Test() {} void Prepare() { Work(1); } }");
        var after = Snapshot("class C { [Fact] void Test() {} [SetUp] void Prepare() { Work(2); } }");
        AssertChanges(before, after, Change(BaselineChangeKind.MemberModified, Find(after, "Prepare")));
    }

    [Theory]
    [InlineData("int Value = 1;", "Value")]
    [InlineData("int Value { get; } = 1;", "Value")]
    [InlineData("C() {}", ".ctor")]
    public void Added_instance_roots_in_an_existing_eligible_type_can_narrow(string declaration, string name)
    {
        var before = Snapshot("class C { [Fact] void Test() {} }");
        var after = Snapshot("class C { [Fact] void Test() {} " + declaration + " }");
        AssertChanges(before, after, Change(BaselineChangeKind.MemberAdded, Find(after, name), root: true));
    }

    [Theory]
    [InlineData("class", true, false)]
    [InlineData("sealed class", true, false)]
    [InlineData("abstract class", false, true)]
    public void Added_test_type_is_a_root_only_when_eligible(string type, bool root, bool blocks)
    {
        var before = Snapshot("class Existing { [Fact] void Old() {} }");
        var after = Snapshot("class Existing { [Fact] void Old() {} } " + type + " New { [Fact] void Added() {} }");
        AssertChanges(before, after, Change(BaselineChangeKind.MemberAdded, Find(after, "New"), root: root),
            Change(BaselineChangeKind.MemberAdded, Find(after, "Added"), test: true, blocks: blocks));
    }

    [Fact]
    public void Added_test_type_with_a_local_inheritor_does_not_narrow()
    {
        var before = Snapshot("class Existing {}");
        var after = Snapshot("class Existing {} class New { [Fact] void Added() {} } class Derived : New {}");
        AssertChanges(before, after, Change(BaselineChangeKind.MemberAdded, Find(after, "New")),
            Change(BaselineChangeKind.MemberAdded, Find(after, "Added"), test: true, blocks: true),
            Change(BaselineChangeKind.MemberAdded, Find(after, "Derived")));
    }

    [Fact]
    public void Modified_test_type_keeps_type_expansion_without_root_flags()
    {
        var before = Snapshot("class C { [Fact] void Test() {} }");
        var after = Snapshot("[A] class C { [Fact] void Test() {} }");
        AssertChanges(before, after, new BaselineDocumentChange(BaselineChangeKind.TypeChanged,
            BaselineChangeReason.TypeSurface, Find(after, "C").Key));
    }

    private static BaselineDocumentSnapshot Snapshot(string source) =>
        BaselineDocumentSnapshotBuilder.Build(CSharpSyntaxTree.ParseText(source));

    private static BaselineMemberSnapshot Find(BaselineDocumentSnapshot snapshot, string name) =>
        Assert.Single(snapshot.Members.Where(member => member.Key.Name == name));

    private static BaselineDocumentChange Change(
        BaselineChangeKind kind, BaselineMemberSnapshot member, bool test = false, bool root = false, bool blocks = false) =>
        new(kind, BaselineChangeReason.MemberSyntax, member.Key, test, root, blocks);

    private static void AssertChanges(
        BaselineDocumentSnapshot before, BaselineDocumentSnapshot after, params BaselineDocumentChange[] expected)
    {
        var actual = BaselineDocumentChangeDetector.Compare(before, after);
        Assert.Equal(expected.OrderBy(change => change.Kind).ThenBy(change => change.Key?.ToString(), StringComparer.Ordinal),
            actual.OrderBy(change => change.Kind).ThenBy(change => change.Key?.ToString(), StringComparer.Ordinal));
    }
}
