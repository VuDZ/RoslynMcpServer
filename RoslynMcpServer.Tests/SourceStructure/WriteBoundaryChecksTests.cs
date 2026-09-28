using Microsoft.CodeAnalysis;
using Xunit;

namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// Counterexample corpus of the workspace write-boundary check: the single call site of
/// <c>Workspace.TryApplyChanges</c> is found by symbol, so a same-named method of another type, a
/// comment and a string literal are not call sites, while a second call site, a call from another
/// method, a missing wrapper and an unresolved reference are violations.
/// </summary>
public sealed class WriteBoundaryChecksTests
{
    private const string DemoPath = "Demo/DemoWriteBoundary.cs";
    private const string DemoNamespace = "Demo";
    private const string DemoType = "DemoWriteBoundary";
    private const string WrapperSignature = "    private bool TryApplyWorkspaceChanges(Workspace workspace, Solution cleaned)";
    private const string ClassEnd = "\n}";

    [Fact]
    public void Baseline_passes_the_write_boundary_check()
    {
        var violations = Verify(Baseline);

        Assert.Empty(InputErrors(Baseline));
        Assert.Empty(violations);
    }

    [Fact]
    public void Second_call_site_fails()
    {
        var mutated = ReplaceFirst(
            Baseline,
            WrapperSignature,
            Member(
                "private bool ApplyAgain(Workspace workspace, Solution cleaned)",
                "        return workspace.TryApplyChanges(cleaned);")
            + "\n"
            + WrapperSignature);

        Assert.Empty(InputErrors(mutated));
        var violations = Verify(mutated);
        AssertViolation(violations, "2 reference sites");
        AssertViolation(violations, "not from " + DemoType + ".TryApplyWorkspaceChanges");
    }

    [Fact]
    public void Call_from_another_method_fails()
    {
        // The write moves out of the wrapper into the public entry: the reference stays single, but the
        // workspace is no longer written by the method that turns a cleaned candidate into a write.
        var mutated = ReplaceFirst(Baseline, "        return workspace.TryApplyChanges(cleaned);", "        return cleaned is not null;");
        mutated = ReplaceFirst(
            mutated,
            "        return TryApplyWorkspaceChanges(workspace, solution);",
            "        var applied = workspace.TryApplyChanges(solution);\n"
            + "        return applied && TryApplyWorkspaceChanges(workspace, solution);");

        Assert.Empty(InputErrors(mutated));
        var violations = Verify(mutated);
        AssertViolation(violations, "is called from " + DemoType + ".Apply");
        AssertViolation(violations, "not from " + DemoType + ".TryApplyWorkspaceChanges");
    }

    [Fact]
    public void Missing_wrapper_method_is_a_diagnostic()
    {
        var mutated = ReplaceFirst(Baseline, "TryApplyWorkspaceChanges(workspace, solution)", "ApplyDirectly(workspace, solution)");
        mutated = ReplaceFirst(mutated, WrapperSignature, "    private bool ApplyDirectly(Workspace workspace, Solution cleaned)");

        Assert.Empty(InputErrors(mutated));
        AssertViolation(Verify(mutated), "TryApplyWorkspaceChanges: method not found");
    }

    [Fact]
    public void Same_named_method_of_another_type_is_not_a_call_site()
    {
        var foreignType = """
            internal sealed class FakeWorkspace
            {
                public bool TryApplyChanges(Solution solution)
                {
                    return true;
                }
            }
            """;
        var mutated = Baseline + "\n" + foreignType;
        mutated = ReplaceFirst(
            mutated,
            "        return workspace.TryApplyChanges(cleaned);",
            "            return new FakeWorkspace().TryApplyChanges(cleaned) && workspace.TryApplyChanges(cleaned);");

        Assert.Empty(InputErrors(mutated));
        var references = Analyze(mutated).FindReferences(WriteBoundaryChecks.ApplyChangesMember);
        Assert.Equal(2, references.Count);
        Assert.Empty(Verify(mutated));
    }

    [Fact]
    public void Comment_and_string_mention_are_not_call_sites()
    {
        var mutated = ReplaceFirst(
            Baseline,
            "        return workspace.TryApplyChanges(cleaned);",
            "            // workspace.TryApplyChanges(cleaned);\n"
            + "            var note = \"workspace.TryApplyChanges(cleaned)\";\n"
            + "        return workspace.TryApplyChanges(cleaned);");

        Assert.Empty(InputErrors(mutated));
        Assert.Empty(Verify(mutated));
    }

    [Fact]
    public void Reverted_overlay_helper_is_a_violation()
    {
        var helper = Member(
            "private void " + WriteBoundaryChecks.RevertedOverlayHelper + "(Workspace workspace)",
            "        _ = workspace;");
        var mutated = ReplaceFirst(Baseline, ClassEnd, "\n\n" + helper.TrimEnd('\n') + "\n}");

        Assert.Empty(InputErrors(mutated));
        AssertViolation(Verify(mutated), WriteBoundaryChecks.RevertedOverlayHelper + " exists in the declared scope");
    }

    [Fact]
    public void Unresolved_reference_makes_the_count_undecidable()
    {
        var mutated = ReplaceFirst(
            Baseline,
            "        return workspace.TryApplyChanges(cleaned);",
            "        return missing.TryApplyChanges(cleaned);");

        Assert.NotEmpty(InputErrors(mutated));
        AssertViolation(Verify(mutated), "does not bind");
    }

    [Fact]
    public void Nameof_and_method_group_mentions_stay_fail_closed()
    {
        // Neither mention is a call, and neither may turn the check green. GetSymbolInfo reports the
        // member group of `nameof(...)` as a candidate with Symbol == null, so the count is reported as
        // undecidable — a fail-closed false alarm, because a nameof mention cannot be invoked. A
        // method-group reference binds and is counted as a reference site, because a delegate can be
        // invoked later. Accepting a mention would require proving the remaining count complete.
        var mutated = ReplaceFirst(
            Baseline,
            "        return workspace.TryApplyChanges(cleaned);",
            "            _ = nameof(workspace.TryApplyChanges);\n"
            + "            System.Func<Solution, bool> apply = workspace.TryApplyChanges;\n"
            + "            _ = apply;\n"
            + "        return workspace.TryApplyChanges(cleaned);");

        Assert.Empty(InputErrors(mutated));
        var violations = Verify(mutated);
        Assert.Equal(2, violations.Count);
        AssertViolation(violations, "reference to " + WriteBoundaryChecks.ApplyChangesMember + " does not bind");
        AssertViolation(violations, "2 reference sites");
    }

    [Fact]
    public void Crlf_and_lf_inputs_give_the_same_verdict()
    {
        // The raw literal takes the line endings of the working copy, so both sides are built explicitly:
        // the CRLF text from the normalized literal, the LF text back out through SourceFile.Create. A
        // no-op normalization would leave the LF side with CRLF and fail here, whatever the markup.
        var crlf = SourceFile.Normalize(Baseline).Replace("\n", "\r\n", StringComparison.Ordinal);
        Assert.Contains("\r\n", crlf, StringComparison.Ordinal);
        var lf = SourceFile.Create(DemoPath, crlf).Text;
        Assert.DoesNotContain("\r\n", lf, StringComparison.Ordinal);
        Assert.NotEqual(lf, crlf);

        // The mutation moves the single write out of the wrapper, so the verdict names the call site as
        // `path:line`: two empty verdict lists would compare equal without proving anything.
        var withoutWrapperCall = ReplaceFirst(lf, "        return workspace.TryApplyChanges(cleaned);", "        return false;");
        var movedOut = ReplaceFirst(
            withoutWrapperCall,
            "        return TryApplyWorkspaceChanges(workspace, solution);",
            "        var applied = workspace.TryApplyChanges(solution);\n"
            + "        return applied && TryApplyWorkspaceChanges(workspace, solution);");

        var lfViolations = Verify(movedOut);
        var crlfViolations = Verify(movedOut.Replace("\n", "\r\n", StringComparison.Ordinal));
        Assert.NotEmpty(lfViolations);
        Assert.Equal(lfViolations, crlfViolations);
        AssertViolation(lfViolations, "is called from " + DemoType + ".Apply");
    }

    private static IReadOnlyList<string> Verify(string sourceText) =>
        WriteBoundaryChecks.Verify(Analyze(sourceText), DemoNamespace, DemoType);

    private static SourceSetAnalysis Analyze(string sourceText) =>
        SourceSetAnalysis.Create(SourceSetScope.Demo($"demo source {DemoPath}", new[] { SourceFile.Create(DemoPath, sourceText) }));

    private static IReadOnlyList<string> InputErrors(string sourceText) => Analyze(sourceText).InputDiagnostics;

    private static void AssertViolation(IReadOnlyList<string> violations, string expectedFragment) =>
        Assert.Contains(violations, violation => violation.Contains(expectedFragment, StringComparison.Ordinal));

    private static string ReplaceFirst(string source, string anchor, string replacement)
    {
        var index = source.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(index >= 0, $"anchor not found: `{anchor}`");
        return string.Concat(source.AsSpan(0, index), replacement, source.AsSpan(index + anchor.Length));
    }

    private static string Member(string signature, params string[] bodyLines) =>
        signature + "\n    {\n" + string.Join("\n", bodyLines) + "\n    }";

    private const string Baseline = """
        #nullable enable
        using Microsoft.CodeAnalysis;

        namespace Demo;

        internal sealed class DemoWriteBoundary
        {
            public bool Apply(Workspace workspace, Solution solution)
            {
                return TryApplyWorkspaceChanges(workspace, solution);
            }

            private bool TryApplyWorkspaceChanges(Workspace workspace, Solution cleaned)
            {
                return workspace.TryApplyChanges(cleaned);
            }
        }
        """;
}
