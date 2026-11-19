using Microsoft.CodeAnalysis.CSharp;
using RoslynMcpServer.Services.Testing.Baseline;
using RoslynMcpServer.Services.Testing.Baseline.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing;

public sealed class BaselineAddedRootSafetyTests
{
    [Theory]
    [InlineData("public H() { Work(); }", ".ctor")]
    [InlineData("int Value = Work();", "Value")]
    [InlineData("int Value { get; } = Work();", "Value")]
    [InlineData("[SetUp] public void Prepare() { Work(); }", "Prepare")]
    public void Existing_non_test_type_getting_its_first_test_keeps_added_roots_ordinary(string declaration, string name)
    {
        var before = Snapshot("class H { public int M() => 1; }");
        var after = Snapshot("class H { public int M() => 1; " + declaration + " [Fact] public void T() {} }");
        AssertChanges(before, after, Change(BaselineChangeKind.MemberAdded, Find(after, name)),
            Change(BaselineChangeKind.MemberAdded, Find(after, "T"), test: true));
    }

    [Theory]
    [InlineData("TestFixture")]
    [InlineData("TestClass")]
    public void Baseline_marker_without_own_methods_proves_a_known_test_type(string marker)
    {
        var before = Snapshot("[" + marker + "] class C {}");
        var after = Snapshot("[" + marker + "] class C { C() {} }");
        var type = Find(before, "C");
        Assert.True(type.IsTestType);
        Assert.False(type.IsOwnTestClassRoot);
        AssertChanges(before, after, Change(BaselineChangeKind.MemberAdded, Find(after, ".ctor"), root: true));
    }

    [Fact]
    public void Known_partial_test_type_accepts_an_added_root_without_a_type_record_root()
    {
        var before = Snapshot("partial class C { [Fact] void T() {} }");
        var after = Snapshot("partial class C { [Fact] void T() {} C() {} }");
        var type = Find(before, "C");
        Assert.True(type.IsTestType);
        Assert.False(type.IsOwnTestClassRoot);
        AssertChanges(before, after, Change(BaselineChangeKind.MemberAdded, Find(after, ".ctor"), root: true));
    }

    [Fact]
    public void New_partial_part_does_not_narrow_type_or_constructor_but_keeps_its_new_test()
    {
        var before = Snapshot("class X {}");
        var after = Snapshot("class X {} [Collection] partial class C { C() {} [Fact] void T() {} }");
        AssertChanges(before, after, Change(BaselineChangeKind.MemberAdded, Find(after, "C")),
            Change(BaselineChangeKind.MemberAdded, Find(after, ".ctor")),
            Change(BaselineChangeKind.MemberAdded, Find(after, "T"), test: true));
    }

    [Fact]
    public void Brand_new_non_partial_test_type_and_its_constructor_remain_roots()
    {
        var before = Snapshot("class X {}");
        var after = Snapshot("class X {} class C { C() {} [Fact] void T() {} }");
        AssertChanges(before, after, Change(BaselineChangeKind.MemberAdded, Find(after, "C"), root: true),
            Change(BaselineChangeKind.MemberAdded, Find(after, ".ctor"), root: true),
            Change(BaselineChangeKind.MemberAdded, Find(after, "T"), test: true));
    }

    [Theory]
    [InlineData("[TestFixture] class O { static readonly int V = Work(1); [TestFixture] class I { [Test] void U() => Use(V); } }", "V")]
    [InlineData("class O { static readonly int V = Work(1); [Fact] void T() {} class I { [Fact] void U() => Use(V); } }", "V")]
    [InlineData("class O { O() { Work(1); } [Fact] void T() {} class I { [Fact] void U() => new O(); } }", ".ctor")]
    public void Nested_type_access_keeps_outer_initializers_and_constructors_ordinary(string source, string name)
    {
        var before = Snapshot(source);
        var after = Snapshot(source.Replace("Work(1)", "Work(2)", StringComparison.Ordinal));
        Assert.False(Find(after, name).IsOwnTestClassRoot);
        AssertChanges(before, after, Change(BaselineChangeKind.MemberModified, Find(after, name)));
    }

    [Theory]
    [InlineData("class I {}")]
    [InlineData("struct I {}")]
    [InlineData("record I;")]
    [InlineData("record struct I;")]
    [InlineData("interface I {}")]
    public void Exact_nested_type_kinds_keep_outer_tests_visible_but_block_narrowing(string nested)
    {
        var source = "class O { [Fact] void T() { Work(1); } " + nested + " }";
        var before = Snapshot(source);
        var after = Snapshot(source.Replace("Work(1)", "Work(2)", StringComparison.Ordinal));
        var test = Find(after, "T");
        Assert.True(test.IsTestMethod);
        Assert.True(test.BlocksNarrowing);
        AssertChanges(before, after, Change(BaselineChangeKind.MemberModified, test, test: true, blocks: true));
    }

    [Theory]
    [InlineData("enum I { A }")]
    [InlineData("delegate void I();")]
    public void Nested_enum_and_delegate_do_not_block_outer_roots(string nested)
    {
        var source = "class O { O() { Work(1); } [Fact] void T() {} " + nested + " }";
        var before = Snapshot(source);
        var after = Snapshot(source.Replace("Work(1)", "Work(2)", StringComparison.Ordinal));
        AssertChanges(before, after, Change(BaselineChangeKind.MemberModified, Find(after, ".ctor"), root: true));
    }

    [Fact]
    public void Nested_type_on_another_same_document_partial_part_blocks_outer_roots_and_tests()
    {
        var source = "partial class O { int V = Work(1); [Fact] void T() {} } partial class O { class I {} }";
        var before = Snapshot(source);
        var after = Snapshot(source.Replace("Work(1)", "Work(2)", StringComparison.Ordinal));
        Assert.False(Find(after, "V").IsOwnTestClassRoot);
        Assert.True(Find(after, "T").BlocksNarrowing);
        AssertChanges(before, after,
            new BaselineDocumentChange(BaselineChangeKind.FileChanged, BaselineChangeReason.AmbiguousMemberKey),
            Change(BaselineChangeKind.MemberModified, Find(after, "V")));
    }

    [Fact]
    public void New_outer_type_with_nested_code_is_ordinary_and_its_test_blocks_narrowing()
    {
        var before = Snapshot("class X {}");
        var after = Snapshot("class X {} class O { [Fact] void T() {} class I {} }");
        AssertChanges(before, after, Change(BaselineChangeKind.MemberAdded, Find(after, "O")),
            Change(BaselineChangeKind.MemberAdded, Find(after, "T"), test: true, blocks: true),
            Change(BaselineChangeKind.MemberAdded, Find(after, "I")));
    }

    [Theory]
    [InlineData("I() { Work(1); }", ".ctor")]
    [InlineData("[SetUp] void Prepare() { Work(1); }", "Prepare")]
    public void Inner_test_type_keeps_its_own_modified_constructor_and_setup_roots(string declaration, string name)
    {
        var source = "class O { [Fact] void Outer() {} class I { [Fact] void Inner() {} " + declaration + " } }";
        var before = Snapshot(source);
        var after = Snapshot(source.Replace("Work(1)", "Work(2)", StringComparison.Ordinal));
        AssertChanges(before, after, Change(BaselineChangeKind.MemberModified, Find(after, name), root: true));
    }

    [Theory]
    [InlineData("I() {}", ".ctor")]
    [InlineData("[SetUp] void Prepare() {}", "Prepare")]
    public void Inner_known_test_type_keeps_its_own_added_roots(string declaration, string name)
    {
        var before = Snapshot("class O { [Fact] void Outer() {} class I { [Fact] void Inner() {} } }");
        var after = Snapshot("class O { [Fact] void Outer() {} class I { [Fact] void Inner() {} " + declaration + " } }");
        AssertChanges(before, after, Change(BaselineChangeKind.MemberAdded, Find(after, name), root: true));
    }

    [Fact]
    public void Outer_with_only_nested_tests_still_has_no_own_constructor_root()
    {
        var before = Snapshot("class O { O() { Work(1); } class I { [Fact] void Inner() {} } }");
        var after = Snapshot("class O { O() { Work(2); } class I { [Fact] void Inner() {} } }");
        AssertChanges(before, after, Change(BaselineChangeKind.MemberModified, Find(after, ".ctor")));
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
