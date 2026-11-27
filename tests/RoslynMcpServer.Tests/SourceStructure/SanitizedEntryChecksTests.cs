using Xunit;

namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// Counterexample corpus of the cached-snapshot contract checks, independent of their implementation:
/// every example states an expected verdict, and examples declared as executable mutations must
/// compile, because a check failure on a non-compiling source proves nothing.
/// <para>
/// Positive and negative pairs: a safe change (reordering, formatting, <c>this.</c> qualification, an
/// expression body, a partial declaration, a same-named method of another receiver, a user-defined
/// <c>Result</c>, a safe overload) keeps the checks green, while a violation turns them red and names
/// the offending method. Inputs are built from normalized text, so a CRLF checkout and an LF checkout
/// give the same verdict.
/// </para>
/// </summary>
public sealed class SanitizedEntryChecksTests
{
    private const string DemoPath = SanitizedEntryDemoSource.Path;
    private const string DemoNamespace = SanitizedEntryDemoSource.NamespaceName;
    private const string DemoType = SanitizedEntryDemoSource.TypeName;

    private const string AsyncSignature = "    public async Task<Snapshot?> GetSanitizedPublishedSolutionAsync";
    private const string SyncSignature = "    public Snapshot? GetSanitizedPublishedSolution()";
    private const string RawSignature = "    public async Task<Snapshot?> GetPublishedSolutionAfterDiskSyncAsync";
    private const string HelperSignature = "    private Snapshot? GetOrCreateSanitizedPublishedSolution()";
    private const string LockField = "    private readonly SemaphoreSlim _workspaceLock = new(1, 1);";
    private const string ClassHeader = "internal sealed class " + DemoType;
    private const string ClassEnd = "\n}";
    private const string HelperExpression = "GetOrCreateSanitizedPublishedSolution()";
    private const string SyncExpression = "GetSanitizedPublishedSolution()";
    private const string HelperInvocation = HelperExpression + ";";
    private const string SyncInvocation = SyncExpression + ";";
    private const string HelperCall = "return " + HelperInvocation;
    private const string SyncCall = "return " + SyncInvocation;
    private const string AsyncHelperCall = "            " + HelperCall;
    private const string AsyncWait = "        await _workspaceLock.WaitAsync(cancellationToken).ConfigureAwait(false);";

    /// <summary>Reachability violation of the seam facts: the path the walk has to report.</summary>
    private const string SeamPath = "path: " + DemoType + "." + SanitizedEntryChecks.AsyncEntry
        + " -> " + DemoType + "." + SanitizedEntryChecks.SyncEntry;

    [Fact]
    public void Demo_baseline_compiles()
    {
        Assert.Empty(InputErrors(SanitizedEntryDemoSource.Baseline));
    }

    [Fact]
    public void Demo_baseline_passes_sanitized_and_raw_checks()
    {
        Assert.Empty(VerifySanitized(SanitizedEntryDemoSource.Baseline));
        Assert.Empty(VerifyRaw(SanitizedEntryDemoSource.Baseline));
    }

    [Fact]
    public void Method_reordering_keeps_success()
    {
        var baseline = SanitizedEntryDemoSource.Baseline;
        var rawMember = CutMember(baseline, RawSignature, HelperSignature);
        var helperMember = CutMember(baseline, HelperSignature, ClassEnd);
        var reordered = ReplaceFirst(baseline, rawMember, string.Empty)
            .Replace(helperMember, helperMember + "\n" + rawMember, StringComparison.Ordinal);

        Assert.Empty(InputErrors(reordered));
        Assert.Empty(VerifySanitized(reordered));
        Assert.Empty(VerifyRaw(reordered));
    }

    [Fact]
    public void Formatting_change_keeps_success()
    {
        var baseline = SanitizedEntryDemoSource.Baseline;
        var reformatted = ReplaceFirst(baseline, SyncSignature, "\n" + SyncSignature)
            .Replace(Indent(2) + "try\n", Indent(2) + "try // the snapshot read stays protected\n", StringComparison.Ordinal);

        Assert.Empty(InputErrors(reformatted));
        Assert.Empty(VerifySanitized(reformatted));
    }

    [Fact]
    public void Foreign_member_with_Results_and_forbidden_names_in_comments_keep_success()
    {
        var foreignMember = Member(
            "private int CompletePrepare(PrepareOutcome prepared)",
            Line(2, "return prepared.Results.Count;"));
        var withForeignMember = InsertMember(SanitizedEntryDemoSource.Baseline, foreignMember);
        var withCommentsAndStrings = withForeignMember.Replace(
            Line(2, "_workspaceLock.Wait();"),
            Line(2, "_workspaceLock.Wait();")
            + Line(2, "// GetSanitizedPublishedSolution(); .Result GetAwaiter()")
            + Line(2, "var note = \"_workspaceLock.Release(); .Result GetSanitizedPublishedSolution();\";"),
            StringComparison.Ordinal);

        Assert.Empty(InputErrors(withCommentsAndStrings));
        Assert.Empty(VerifySanitized(withCommentsAndStrings));
        Assert.Empty(VerifyRaw(withCommentsAndStrings));
    }

    [Fact]
    public void This_qualified_self_call_and_lock_keep_success()
    {
        var mutated = SanitizedEntryDemoSource.Baseline
            .Replace(HelperInvocation, "this." + HelperInvocation, StringComparison.Ordinal)
            .Replace("_workspaceLock.", "this._workspaceLock.", StringComparison.Ordinal);

        Assert.Empty(InputErrors(mutated));
        Assert.Empty(VerifySanitized(mutated));
        Assert.Empty(VerifyRaw(mutated));
    }

    [Fact]
    public void This_qualified_published_solution_return_keeps_success()
    {
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            RawSignature,
            HelperSignature,
            Line(3, "return _solution;"),
            Line(3, "return this._solution;"));

        Assert.Empty(InputErrors(mutated));
        Assert.Empty(VerifyRaw(mutated));
    }

    [Fact]
    public void Expression_body_helper_keeps_success()
    {
        var helperMember = HelperSignature + "\n" + Line(1, "{") + Line(2, "return _solution;") + Line(1, "}");
        var mutated = SanitizedEntryDemoSource.Baseline.Replace(helperMember, HelperSignature + " => _solution;", StringComparison.Ordinal);

        Assert.Empty(InputErrors(mutated));
        Assert.Empty(VerifySanitized(mutated));
        Assert.Empty(VerifyRaw(mutated));
    }

    [Fact]
    public void Same_named_method_on_another_receiver_keeps_success()
    {
        var foreignType = "internal sealed class OtherManager\n{\n"
            + Line(1, "public Snapshot? " + SanitizedEntryChecks.SyncEntry + "() => null;")
            + "\n}\n";
        var source = SanitizedEntryDemoSource.Baseline + "\n" + foreignType;
        var mutated = ReplaceFirst(
            source,
            AsyncHelperCall,
            AsyncHelperCall + "\n" + Line(3, "var ignored = new OtherManager()." + SanitizedEntryChecks.SyncEntry + "();"));

        Assert.Empty(InputErrors(mutated));
        Assert.Empty(VerifySanitized(mutated));
    }

    [Fact]
    public void Safe_overload_of_the_helper_keeps_success()
    {
        var overload = Member(
            "private Snapshot? GetOrCreateSanitizedPublishedSolution(int unused)",
            Line(2, "return null;"));
        var mutated = InsertMember(SanitizedEntryDemoSource.Baseline, overload);

        Assert.Empty(InputErrors(mutated));
        Assert.Empty(VerifySanitized(mutated));
    }

    [Fact]
    public void User_defined_Result_property_is_not_a_task_result()
    {
        var outcomeType = "internal sealed class LocalOutcome\n{\n"
            + Line(1, "public int Result => 7;")
            + "\n}\n";
        var source = InsertMember(
            SanitizedEntryDemoSource.Baseline,
            Line(1, "private readonly LocalOutcome _prepared = new();")) + "\n" + outcomeType;
        var mutated = MutateMember(
            source,
            SyncSignature,
            RawSignature,
            Line(2, "_workspaceLock.Wait();"),
            Line(2, "_workspaceLock.Wait();") + Line(2, "var prepared = _prepared.Result;"));

        Assert.Empty(InputErrors(mutated));
        Assert.Empty(VerifySanitized(mutated));
    }

    [Fact]
    public void Direct_public_sync_call_from_async_entry_fails()
    {
        var mutated = SanitizedEntryDemoSource.AsyncCallsPublicSync;

        Assert.Empty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), "reaches the public synchronous entry " + SanitizedEntryChecks.SyncEntry);
    }

    [Fact]
    public void This_qualified_public_sync_call_from_async_entry_fails()
    {
        // A `this.`-qualified call is a call, not a mention. The whole violation set is asserted here:
        // an additional "method group" violation would mean the reference was classified by the parent
        // node of the name instead of by the invocation the name belongs to.
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            AsyncSignature,
            SyncSignature,
            AsyncHelperCall,
            Line(3, "var snapshot = " + HelperInvocation)
            + Line(3, "_ = this." + SyncInvocation)
            + Indent(3) + "return snapshot;");

        Assert.Empty(InputErrors(mutated));
        var violations = VerifySanitized(mutated);
        Assert.Single(violations);
        AssertViolation(violations, "reaches the public synchronous entry " + SanitizedEntryChecks.SyncEntry);
    }

    [Fact]
    public void Transitive_async_wrapper_sync_path_fails()
    {
        var wrapper = Member("private Snapshot? ReadThroughWrapper()", Line(2, SyncCall));
        var source = InsertMember(SanitizedEntryDemoSource.Baseline, wrapper);
        var mutated = ReplaceFirst(
            source,
            AsyncHelperCall,
            AsyncHelperCall + "\n" + Line(3, "var wrapped = ReadThroughWrapper();"));

        Assert.Empty(InputErrors(mutated));
        var violations = VerifySanitized(mutated);
        AssertViolation(violations, "reaches the public synchronous entry " + SanitizedEntryChecks.SyncEntry);
        AssertViolation(violations, "ReadThroughWrapper -> " + DemoType + "." + SanitizedEntryChecks.SyncEntry);
    }

    [Fact]
    public void Recursive_chain_terminates_without_target()
    {
        var recursion = Member("private Snapshot? Recurse(int depth)", Line(2, "return depth <= 0 ? _solution : Recurse(depth - 1);"));
        var source = InsertMember(SanitizedEntryDemoSource.Baseline, recursion);
        var mutated = ReplaceFirst(source, AsyncHelperCall, AsyncHelperCall + "\n" + Line(3, "var recursed = Recurse(2);"));

        Assert.Empty(InputErrors(mutated));
        Assert.Empty(VerifySanitized(mutated));
    }

    [Fact]
    public void Target_reached_through_recursive_chain_fails()
    {
        var recursion = Member("private Snapshot? Recurse(int depth)", Line(2, "return depth <= 0 ? " + SyncExpression + " : Recurse(depth - 1);"));
        var source = InsertMember(SanitizedEntryDemoSource.Baseline, recursion);
        var mutated = ReplaceFirst(source, AsyncHelperCall, AsyncHelperCall + "\n" + Line(3, "var recursed = Recurse(2);"));

        Assert.Empty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), "Recurse -> " + DemoType + "." + SanitizedEntryChecks.SyncEntry);
    }

    [Fact]
    public void Method_group_of_the_sync_entry_in_async_entry_fails()
    {
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            AsyncSignature,
            SyncSignature,
            AsyncHelperCall,
            Line(3, "Func<Snapshot?> read = " + SanitizedEntryChecks.SyncEntry + ";")
            + Line(3, "return read();"));

        Assert.Empty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), "method group of the public synchronous entry " + SanitizedEntryChecks.SyncEntry);
    }

    [Fact]
    public void Delegate_assigned_inside_the_scope_to_the_sync_entry_fails()
    {
        // The target is registered by an assignment statement inside the declared scope, not by the
        // declaration of the delegate. Claiming "no assignment inside the declared scope" for this source
        // would be false, so the walk has to follow `_seam = GetSanitizedPublishedSolution;` and report
        // the path; the method group at the assignment site stays reported as the weaker form it is.
        var withSeam = InsertMember(SanitizedEntryDemoSource.Baseline, Line(1, "private Func<Snapshot?>? _seam;"));
        var mutated = ReplaceFirst(
            withSeam,
            AsyncHelperCall,
            AsyncHelperCall
            + "\n" + Line(3, "_seam = " + SanitizedEntryChecks.SyncEntry + ";")
            + "\n" + Line(3, "_ = _seam();"));

        Assert.Empty(InputErrors(mutated));
        var violations = VerifySanitized(mutated);
        Assert.Equal(2, violations.Count);
        AssertViolation(violations, "method group of the public synchronous entry " + SanitizedEntryChecks.SyncEntry);
        AssertViolation(violations, "reaches the public synchronous entry " + SanitizedEntryChecks.SyncEntry);
    }

    [Fact]
    public void Delegate_registered_in_another_method_is_followed()
    {
        // Control for the explicit-invocation facts below: the registration sits in a method of the same
        // declared scope that the walk never executes, which is the shape of the production test seam.
        // Delegate targets are looked up by assignment in the whole scope, so the bare invocation reaches
        // the sync entry and the walk reports the path instead of a decided "not reachable".
        var mutated = SeamSource("_ = _seam();");

        Assert.Empty(InputErrors(mutated));
        var violations = VerifySanitized(mutated);
        Assert.Single(violations);
        AssertViolation(violations, SeamPath);
    }

    [Fact]
    public void Explicit_delegate_invoke_registered_in_another_method_fails()
    {
        // `_seam.Invoke()` binds to Invoke of the delegate type instead of the field. Reading it as an
        // ordinary call to a metadata member left the walk with no edge, no limit and no diagnostic, so
        // the check stayed green for a source whose bare `_seam()` form reports the path.
        var mutated = SeamSource("_ = _seam.Invoke();");

        Assert.Empty(InputErrors(mutated));
        var violations = VerifySanitized(mutated);
        Assert.Single(violations);
        AssertViolation(violations, SeamPath);
    }

    [Fact]
    public void Conditional_explicit_delegate_invoke_registered_in_another_method_fails()
    {
        // The conditional-access form writes the member binding inside the conditional access, so the
        // receiver the binding is resolved against is the receiver of that conditional access.
        var mutated = SeamSource("_ = _seam?.Invoke();");

        Assert.Empty(InputErrors(mutated));
        var violations = VerifySanitized(mutated);
        Assert.Single(violations);
        AssertViolation(violations, SeamPath);
    }

    [Fact]
    public void Null_forgiving_this_qualified_explicit_delegate_invoke_fails()
    {
        // `this.` and the null-forgiving `!` are wrappers that carry no member of their own: the
        // delegate-typed member behind them is what the walk has to follow.
        var mutated = SeamSource("_ = this._seam!.Invoke();");

        Assert.Empty(InputErrors(mutated));
        var violations = VerifySanitized(mutated);
        Assert.Single(violations);
        AssertViolation(violations, SeamPath);
    }

    [Fact]
    public void Cleared_delegate_keeps_success_for_both_invocation_forms()
    {
        // Negative side of the facts above: a seam the scope clears carries no target, so neither form
        // may report a path, and the walk may not record a limit either. This is the shape of the
        // production publisher hooks, which the analyzed scope assigns `null` and then invokes with the
        // explicit conditional form; the walk-side half of the claim is asserted by the production fact
        // `Production_reachability_records_the_test_seam_as_the_single_limit`.
        var mutated = SeamSource(
            "_ = _seam();\n" + Line(3, "_ = _seam.Invoke();"),
            "null");

        Assert.Empty(InputErrors(mutated));
        Assert.Empty(VerifySanitized(mutated));
    }

    [Fact]
    public void Legacy_begin_and_end_invoke_forms_are_followed()
    {
        // A delegate type declares three invocation members, and a walk that accepted only `Invoke`
        // would silently swallow the other two instead of recording a limit. Each member is checked in
        // its own source, because either one alone is enough to reach the sync entry.
        var begin = SeamSource("_ = _seam.BeginInvoke(null, null);");
        var end = SeamSource("_ = _seam.EndInvoke(null!);");

        Assert.Empty(InputErrors(begin));
        var beginViolations = VerifySanitized(begin);
        Assert.Single(beginViolations);
        AssertViolation(beginViolations, SeamPath);

        Assert.Empty(InputErrors(end));
        var endViolations = VerifySanitized(end);
        Assert.Single(endViolations);
        AssertViolation(endViolations, SeamPath);
    }

    [Fact]
    public void Delegate_invocation_through_a_computed_receiver_is_an_undecidable_refusal()
    {
        // The async entry reaches the seam through `GetSeam()` instead of naming the delegate member. The
        // call binds to `Invoke` of the delegate type, so before the fix the walk emitted an edge to a
        // metadata member, recorded nothing and the check returned green for a source whose registered
        // target is the public synchronous entry. The walk now records a limit naming the construct, and
        // the check reports that limit as a refusal: a call site no other rule of this check can see must
        // not end in a green verdict.
        var mutated = ComputedReceiverSeamSource("_ = GetSeam()!.Invoke();");

        Assert.Empty(InputErrors(mutated));
        var violations = Assert.Single(VerifySanitized(mutated));
        Assert.Contains("reachability is undecided", violations, StringComparison.Ordinal);
        Assert.Contains("the invoked delegate is not a delegate member of the declared scope", violations, StringComparison.Ordinal);
        Assert.Contains("`GetSeam()!.Invoke()`", violations, StringComparison.Ordinal);
    }

    [Fact]
    public void Bare_delegate_invocation_of_a_computed_receiver_is_an_undecidable_refusal()
    {
        // The bare form of the same receiver: `GetSeam()!()` binds the outer invocation to a delegate
        // value, which the walk cannot attribute to a member of the scope either. Before the fix the call
        // was read as an ordinary call of `GetSeam` and the check stayed green.
        var mutated = ComputedReceiverSeamSource("_ = GetSeam()!();");

        Assert.Empty(InputErrors(mutated));
        var violations = Assert.Single(VerifySanitized(mutated));
        Assert.Contains("reachability is undecided", violations, StringComparison.Ordinal);
        Assert.Contains("the invoked delegate is not a delegate member of the declared scope", violations, StringComparison.Ordinal);
        Assert.Contains("`GetSeam()!()`", violations, StringComparison.Ordinal);
    }

    [Fact]
    public void Delegate_value_produced_by_an_accessor_is_a_reachability_violation()
    {
        // The async entry takes the delegate from the accessor `GetSeam()` and invokes the local copy.
        // Before the fix the walk recorded an edge to `GetSeam` itself, so the target the accessor
        // returned stayed invisible and the check reported 0 violations for a source whose byte-identical
        // control with `_ = _seam();` reports the deadlock path. At run time `local()` calls the public
        // synchronous entry while the non-recursive lock is held.
        var mutated = ComputedReceiverSeamSource(
            Line(3, "System.Func<Snapshot?> local = GetSeam();")
            + Line(3, "_ = local();"));

        Assert.Empty(InputErrors(mutated));
        var violations = VerifySanitized(mutated);
        Assert.Single(violations);
        AssertViolation(violations, SeamPath);
    }

    [Fact]
    public void Delegate_value_produced_by_an_accessor_is_a_violation_in_the_explicit_invoke_form()
    {
        // The same source through the explicit invocation member: the local copy holds the delegate the
        // accessor returned, so the explicit form reaches the same target and the check refuses it too.
        var mutated = ComputedReceiverSeamSource(
            Line(3, "System.Func<Snapshot?> local = GetSeam();")
            + Line(3, "_ = local.Invoke();"));

        Assert.Empty(InputErrors(mutated));
        var violations = VerifySanitized(mutated);
        Assert.Single(violations);
        AssertViolation(violations, SeamPath);
    }

    [Fact]
    public void Delegate_value_computed_by_an_accessor_is_an_undecidable_refusal()
    {
        // The accessor hands out the result of another call, which this expansion does not follow: the
        // target it can carry is unknown, so the check refuses instead of reporting 0 violations. Without
        // the limit this source is green for exactly the same reason a delegate with no target in the
        // scope used to be.
        var source = InsertMember(
            SeamSource(
                Line(3, "System.Func<Snapshot?> local = GetSeam();") + Line(3, "_ = local();"),
                "null"),
            Member("private System.Func<Snapshot?> GetSeam()", Line(2, "return MakeSeam();")));
        source = InsertMember(source, Member("private System.Func<Snapshot?> MakeSeam()", Line(2, "return _seam;")));

        Assert.Empty(InputErrors(source));
        var violations = Assert.Single(VerifySanitized(source));
        Assert.Contains("reachability is undecided", violations, StringComparison.Ordinal);
        Assert.Contains("the delegate value it hands out is the result of another call", violations, StringComparison.Ordinal);
        Assert.Contains("`MakeSeam()`", violations, StringComparison.Ordinal);
    }

    [Fact]
    public void Nameof_mention_of_the_sync_entry_is_reported_as_a_mention()
    {
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            AsyncSignature,
            SyncSignature,
            AsyncHelperCall,
            Line(3, "var ignored = nameof(" + SanitizedEntryChecks.SyncEntry + ");")
            + Line(3, HelperCall));

        Assert.Empty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), "nameof mention of the public synchronous entry " + SanitizedEntryChecks.SyncEntry);
    }

    [Fact]
    public void Generic_call_with_a_method_group_argument_fails()
    {
        var generic = Member("private T Call<T>(Func<T> factory)", Line(2, "return factory();"));
        var source = InsertMember(SanitizedEntryDemoSource.Baseline, generic);
        var mutated = ReplaceFirst(
            source,
            AsyncHelperCall,
            AsyncHelperCall + "\n" + Line(3, "var generic = Call<Snapshot?>(" + SanitizedEntryChecks.SyncEntry + ");"));

        Assert.Empty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), "method group of the public synchronous entry " + SanitizedEntryChecks.SyncEntry);
    }

    [Fact]
    public void Conditional_access_call_from_async_entry_fails()
    {
        // The same distinction for the conditional-access form: `_neighbor?.M()` is the expression of
        // its invocation, so it is reported once, as the call it is.
        var source = InsertMember(SanitizedEntryDemoSource.Baseline, Line(1, "private DemoManager? _neighbor;"));
        var mutated = ReplaceFirst(
            source,
            AsyncHelperCall,
            AsyncHelperCall + "\n" + Line(3, "var throughConditionalAccess = _neighbor?." + SanitizedEntryChecks.SyncEntry + "();"));

        Assert.Empty(InputErrors(mutated));
        var violations = VerifySanitized(mutated);
        Assert.Single(violations);
        AssertViolation(violations, "reaches the public synchronous entry " + SanitizedEntryChecks.SyncEntry);
    }

    [Fact]
    public void Extension_method_path_to_the_sync_entry_fails()
    {
        var extensions = "internal static class DemoExtensions\n{\n"
            + Line(1, "public static Snapshot? Sanitized(this " + DemoType + " manager) => manager." + SanitizedEntryChecks.SyncEntry + "();")
            + "\n}\n";
        var source = SanitizedEntryDemoSource.Baseline + "\n" + extensions;
        var mutated = ReplaceFirst(
            source,
            AsyncHelperCall,
            AsyncHelperCall + "\n" + Line(3, "var throughExtension = this.Sanitized();"));

        Assert.Empty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), "Sanitized -> " + DemoType + "." + SanitizedEntryChecks.SyncEntry);
    }

    [Fact]
    public void Called_local_function_body_is_analyzed()
    {
        // The function is declared inside the async entry body and then invoked, so its statements are
        // executed calls of the entry and the sync entry reached from them is a violation. Removing the
        // local-function support of the walk turns this fact red: the body is then never traversed.
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            AsyncSignature,
            SyncSignature,
            AsyncHelperCall,
            Line(3, "var snapshot = " + HelperInvocation)
            + Line(3, "Snapshot? Read()")
            + Line(3, "{")
            + Line(4, SyncCall)
            + Line(3, "}")
            + "\n"
            + Line(3, "_ = Read();")
            + Indent(3) + "return snapshot;");

        Assert.Empty(InputErrors(mutated));
        var violations = VerifySanitized(mutated);
        Assert.Single(violations);
        AssertViolation(violations, DemoType + ".Read -> " + DemoType + "." + SanitizedEntryChecks.SyncEntry);
    }

    [Fact]
    public void Uncalled_local_function_body_is_not_executed()
    {
        // The function is declared but never invoked, so the call written inside it does not run and is
        // not a path to the sync entry: refusing here would be a false alarm.
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            AsyncSignature,
            SyncSignature,
            AsyncHelperCall,
            Line(3, "Snapshot? Unused()")
            + Line(3, "{")
            + Line(4, SyncCall)
            + Line(3, "}")
            + "\n"
            + AsyncHelperCall);

        Assert.Empty(InputErrors(mutated));
        Assert.Empty(VerifySanitized(mutated));
    }

    [Fact]
    public void Uncalled_private_method_body_is_not_executed()
    {
        // The same rule for the other declaration form: a member method of the scope is not executed
        // because it is declared, so a forbidden call inside a method nobody invokes is not a violation.
        var mutated = InsertMember(
            SanitizedEntryDemoSource.Baseline,
            Member("private Snapshot? Unused()", Line(2, SyncCall)));

        Assert.Empty(InputErrors(mutated));
        Assert.Empty(VerifySanitized(mutated));
    }

    [Fact]
    public void Called_lambda_body_is_analyzed()
    {
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            AsyncSignature,
            SyncSignature,
            AsyncHelperCall,
            Line(3, "Func<Snapshot?> read = () => " + SyncInvocation)
            + Line(3, "return read();"));

        Assert.Empty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), DemoType + "." + SanitizedEntryChecks.SyncEntry);
    }

    [Fact]
    public void Discarded_lambda_body_is_traversed_fail_closed()
    {
        // The lambda is never invoked. Traversing it is the fail-closed direction: a passed lambda is
        // normally invoked by its receiver, and a missed violation would be a silently green check.
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            AsyncSignature,
            SyncSignature,
            AsyncHelperCall,
            Line(3, "_ = (Func<Snapshot?>)(() => " + SyncExpression + ");")
            + Line(3, HelperCall));

        Assert.Empty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), DemoType + "." + SanitizedEntryChecks.SyncEntry);
    }

    [Fact]
    public void Helper_call_only_inside_local_function_fails()
    {
        // The snapshot read sits in a local function declared inside the protected try. Where the
        // function is written is not where it runs, so the direct call required in the method body is
        // missing; the whole violation set is asserted to keep that the only reason.
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            SyncSignature,
            RawSignature,
            Line(3, HelperCall),
            Line(3, "Snapshot? Read()")
            + Line(3, "{")
            + Line(4, HelperCall)
            + Line(3, "}")
            + "\n"
            + Line(3, "return _solution;"));

        Assert.Empty(InputErrors(mutated));
        var violations = VerifySanitized(mutated);
        Assert.Single(violations);
        AssertViolation(violations, "must happen inside the protected try");
    }

    [Fact]
    public void Helper_call_only_inside_lambda_fails()
    {
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            SyncSignature,
            RawSignature,
            Line(3, HelperCall),
            Line(3, "Func<Snapshot?> read = () => " + HelperInvocation)
            + Line(3, "return read();"));

        Assert.Empty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), "must happen inside the protected try");
    }

    [Fact]
    public void Helper_call_only_inside_a_deferred_body_of_the_async_entry_fails()
    {
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            AsyncSignature,
            SyncSignature,
            AsyncHelperCall,
            Line(3, "Func<Snapshot?> read = () => " + HelperInvocation)
            + Line(3, "return _solution;"));

        Assert.Empty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), "no direct call to " + SanitizedEntryChecks.Helper + "() in the method body");
    }

    [Fact]
    public void Missing_lock_wait_fails()
    {
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            SyncSignature,
            RawSignature,
            Line(2, "_workspaceLock.Wait();"),
            string.Empty);

        Assert.Empty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), "no call to _workspaceLock.Wait() in the method body");
    }

    [Fact]
    public void Missing_lock_release_fails()
    {
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            SyncSignature,
            RawSignature,
            Line(3, "_workspaceLock.Release();"),
            string.Empty);

        Assert.Empty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), "no call to _workspaceLock.Release() in the finally of the protected try");
    }

    [Fact]
    public void Foreign_lock_receiver_fails()
    {
        var mutated = MutateMember(SanitizedEntryDemoSource.Baseline, SyncSignature, RawSignature, "_workspaceLock", "_otherLock");

        Assert.Empty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), "Wait() is called on _otherLock, not on _workspaceLock");
    }

    [Fact]
    public void Lock_field_of_another_type_is_diagnosed()
    {
        var customLock = "internal sealed class CustomLock\n{\n"
            + Line(1, "public Task WaitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;")
            + "\n"
            + Line(1, "public void Wait()")
            + Line(1, "{")
            + Line(1, "}")
            + "\n"
            + Line(1, "public void Release()")
            + Line(1, "{")
            + Line(1, "}")
            + "\n}\n";
        var mutated = SanitizedEntryDemoSource.Baseline
            .Replace(LockField, Line(1, "private readonly CustomLock _workspaceLock = new();"), StringComparison.Ordinal)
            + "\n" + customLock;

        Assert.Empty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), "not SemaphoreSlim");
    }

    [Fact]
    public void Release_outside_finally_fails()
    {
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            SyncSignature,
            RawSignature,
            SyncLockBlock(),
            Line(2, "_workspaceLock.Wait();")
            + Line(2, "Snapshot? snapshot = null;")
            + Line(2, "try")
            + Line(2, "{")
            + Line(3, "snapshot = " + HelperInvocation)
            + Line(2, "}")
            + Line(2, "finally")
            + Line(2, "{")
            + Line(2, "}")
            + "\n"
            + Line(2, "_workspaceLock.Release();")
            + Line(2, "return snapshot;"));

        Assert.Empty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), "no call to _workspaceLock.Release() in the finally of the protected try");
    }

    [Fact]
    public void Release_in_the_finally_of_another_try_fails()
    {
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            SyncSignature,
            RawSignature,
            SyncLockBlock(),
            Line(2, "_workspaceLock.Wait();")
            + Line(2, "try")
            + Line(2, "{")
            + Line(3, HelperCall)
            + Line(2, "}")
            + Line(2, "finally")
            + Line(2, "{")
            + Line(3, "_otherLock.Release();")
            + Line(2, "}"));

        Assert.Empty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), "no call to _workspaceLock.Release() in the finally of the protected try");
    }

    [Fact]
    public void Snapshot_read_before_lock_fails()
    {
        // Only the opening block of the sync entry is replaced, so the original protected finally stays
        // in place: the lock is taken, but the snapshot is read before it and outside the protected try.
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            SyncSignature,
            RawSignature,
            SyncLockOpening(),
            Line(2, "var snapshot = " + HelperInvocation)
            + Line(2, "_workspaceLock.Wait();")
            + Line(2, "try")
            + Line(2, "{")
            + Line(3, "return snapshot;")
            + Line(2, "}"));

        Assert.Empty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), "must happen inside the protected try");
    }

    [Fact]
    public void Lock_wait_inside_the_protected_try_fails()
    {
        // The snapshot read and the lock acquisition both stay inside the protected try, so the read
        // satisfies its own rule and the acquisition order is the only defect left. Without this
        // example the order rule is reachable only through a mutation that fails for a second reason.
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            SyncSignature,
            RawSignature,
            SyncLockOpening(),
            Line(2, "try")
            + Line(2, "{")
            + Line(3, "var snapshot = " + HelperInvocation)
            + Line(3, "_workspaceLock.Wait();")
            + Line(3, "return snapshot;")
            + Line(2, "}"));

        Assert.Empty(InputErrors(mutated));
        var violations = VerifySanitized(mutated);
        Assert.Single(violations);
        AssertViolation(violations, SanitizedEntryChecks.WorkspaceLock + ".Wait() must be taken before the protected try");
    }

    [Fact]
    public void Expression_body_target_reports_missing_lock()
    {
        var syncMember = SyncSignature + "\n" + Line(1, "{") + SyncLockBlock() + Line(1, "}");
        var mutated = SanitizedEntryDemoSource.Baseline.Replace(syncMember, SyncSignature + " => " + HelperInvocation, StringComparison.Ordinal);

        Assert.Empty(InputErrors(mutated));
        var violations = VerifySanitized(mutated);
        AssertViolation(violations, "no call to _workspaceLock.Wait() in the method body");
        AssertViolation(violations, "no call to _workspaceLock.Release() in the finally of the protected try");
    }

    [Fact]
    public void Task_result_in_sync_entry_fails()
    {
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            SyncSignature,
            RawSignature,
            Line(2, "_workspaceLock.Wait();"),
            Line(2, "_workspaceLock.Wait();") + Line(2, "var ignored = Task.FromResult(0).Result;"));

        Assert.Empty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), "sync-over-async — task result read (`Result`)");
    }

    [Fact]
    public void ValueTask_result_in_sync_entry_fails()
    {
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            SyncSignature,
            RawSignature,
            Line(2, "_workspaceLock.Wait();"),
            Line(2, "_workspaceLock.Wait();")
            + Line(2, "var valueTask = new ValueTask<int>(1);")
            + Line(2, "var ignored = valueTask.Result;"));

        Assert.Empty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), "sync-over-async — task result read (`Result`)");
    }

    [Fact]
    public void Task_wait_in_sync_entry_fails()
    {
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            SyncSignature,
            RawSignature,
            Line(2, "_workspaceLock.Wait();"),
            Line(2, "_workspaceLock.Wait();") + Line(2, "Task.CompletedTask.Wait();"));

        Assert.Empty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), "sync-over-async — blocking wait on a task (`Wait`/`WaitAll`/`WaitAny`)");
    }

    [Fact]
    public void Task_wait_all_in_sync_entry_fails()
    {
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            SyncSignature,
            RawSignature,
            Line(2, "_workspaceLock.Wait();"),
            Line(2, "_workspaceLock.Wait();") + Line(2, "Task.WaitAll(Task.CompletedTask);"));

        Assert.Empty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), "sync-over-async — blocking wait on a task (`Wait`/`WaitAll`/`WaitAny`)");
    }

    [Fact]
    public void Task_wait_any_in_sync_entry_fails()
    {
        // WaitAny is claimed as a symbol-matched rule next to Wait and WaitAll, and this example is what
        // makes the claim observable: dropping it from the member set leaves the other facts green.
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            SyncSignature,
            RawSignature,
            Line(2, "_workspaceLock.Wait();"),
            Line(2, "_workspaceLock.Wait();") + Line(2, "Task.WaitAny(Task.CompletedTask);"));

        Assert.Empty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), "sync-over-async — blocking wait on a task (`Wait`/`WaitAll`/`WaitAny`)");
    }

    [Fact]
    public void GetAwaiter_get_result_in_sync_entry_fails()
    {
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            SyncSignature,
            RawSignature,
            Line(2, "_workspaceLock.Wait();"),
            Line(2, "_workspaceLock.Wait();") + Line(2, "Task.CompletedTask.GetAwaiter().GetResult();"));

        Assert.Empty(InputErrors(mutated));
        var violations = VerifySanitized(mutated);
        AssertViolation(violations, "sync-over-async — awaiter acquisition (`GetAwaiter`)");
        AssertViolation(violations, "sync-over-async — blocking awaiter call (`GetResult`)");
    }

    [Fact]
    public void Safe_await_in_async_entry_keeps_success()
    {
        // Positive side of the async-entry blocking-member pair: an ordinary await in the async entry is
        // not a blocking member, and the rest of the contract still holds.
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            AsyncSignature,
            SyncSignature,
            AsyncWait,
            AsyncWait + Line(3, "await Task.CompletedTask.ConfigureAwait(false);"));

        Assert.Empty(InputErrors(mutated));
        Assert.Empty(VerifySanitized(mutated));
    }

    [Fact]
    public void Task_result_in_async_entry_fails()
    {
        // The sync-over-async rules are checked for the async entry as well; without this example the
        // async-entry call to the rules could be deleted and the whole corpus would stay green.
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            AsyncSignature,
            SyncSignature,
            AsyncWait,
            AsyncWait + Line(3, "var ignored = Task.FromResult(0).Result;"));

        Assert.Empty(InputErrors(mutated));
        var violations = VerifySanitized(mutated);
        var violation = Assert.Single(violations);
        Assert.Contains("sync-over-async — task result read (`Result`)", violation, StringComparison.Ordinal);
        Assert.Contains(DemoType + "." + SanitizedEntryChecks.AsyncEntry, violation, StringComparison.Ordinal);
    }

    [Fact]
    public void GetAwaiter_get_result_in_async_entry_fails()
    {
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            AsyncSignature,
            SyncSignature,
            AsyncWait,
            AsyncWait + Line(3, "Task.CompletedTask.GetAwaiter().GetResult();"));

        Assert.Empty(InputErrors(mutated));
        var violations = VerifySanitized(mutated);
        Assert.Equal(2, violations.Count);
        AssertViolation(violations, "sync-over-async — awaiter acquisition (`GetAwaiter`)");
        AssertViolation(violations, "sync-over-async — blocking awaiter call (`GetResult`)");
    }

    [Fact]
    public void Configured_awaiter_get_result_fails()
    {
        // Every `Configured*` awaiter type is claimed as a blocking `GetResult` target next to the plain
        // awaiter types, and each of the four forms below reaches a different one. Each statement
        // contributes exactly the same two violations — the awaiter acquisition and the blocking call —
        // so the total is what guards every entry: dropping one entry leaves one statement with a single
        // violation and this fact red, while the whole-violation-set assertion keeps the count honest.
        // The plain `await ... ConfigureAwait(false)` next to them acquires no awaiter and must stay green.
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            AsyncSignature,
            SyncSignature,
            AsyncWait,
            AsyncWait
            + Line(3, "await Task.CompletedTask.ConfigureAwait(false);")
            + Line(3, "Task.CompletedTask.ConfigureAwait(false).GetAwaiter().GetResult();")
            + Line(3, "Task.FromResult(0).ConfigureAwait(false).GetAwaiter().GetResult();")
            + Line(3, "new ValueTask().ConfigureAwait(false).GetAwaiter().GetResult();")
            + Line(3, "new ValueTask<int>(1).ConfigureAwait(false).GetAwaiter().GetResult();"));

        Assert.Empty(InputErrors(mutated));
        var violations = VerifySanitized(mutated);
        Assert.Equal(8, violations.Count);
        AssertViolation(violations, "sync-over-async — awaiter acquisition (`GetAwaiter`)");
        AssertViolation(violations, "sync-over-async — blocking awaiter call (`GetResult`)");
    }

    [Fact]
    public void WaitAsync_result_is_a_non_compiling_example()
    {
        // SemaphoreSlim.WaitAsync() returns Task without a Result property: the mutated source does not
        // compile, so the input error is reported as a violation and the unresolved blocking member name
        // is reported instead of being silently dropped.
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            SyncSignature,
            RawSignature,
            Line(2, "_workspaceLock.Wait();"),
            Line(2, "_workspaceLock.WaitAsync().Result;"));

        var violations = VerifySanitized(mutated);
        Assert.NotEmpty(InputErrors(mutated));
        AssertViolation(violations, "unresolved blocking member name `Result`");
    }

    [Fact]
    public void Raw_entry_returning_sanitized_snapshot_fails()
    {
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            RawSignature,
            HelperSignature,
            Line(3, "return _solution;"),
            Line(3, HelperCall));

        Assert.Empty(InputErrors(mutated));
        var violations = VerifyRaw(mutated);
        AssertViolation(violations, "body does not return _solution");
        AssertViolation(violations, "forbidden reference to " + SanitizedEntryChecks.Helper);
    }

    [Fact]
    public void Missing_target_method_is_a_diagnostic_error()
    {
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            SyncSignature,
            RawSignature,
            SyncSignature,
            "    public Snapshot? RenamedEntry()");

        Assert.Empty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), SanitizedEntryChecks.SyncEntry + ": method not found");
    }

    [Fact]
    public void Ambiguous_target_method_is_a_diagnostic_error()
    {
        var overload = Member("public Snapshot? " + SanitizedEntryChecks.SyncEntry + "(int unused)", Line(2, "return _solution;"));
        var mutated = InsertMember(SanitizedEntryDemoSource.Baseline, overload);

        Assert.Empty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), "an unambiguous signature is required");
    }

    [Fact]
    public void Partial_type_parts_are_merged_into_one_target()
    {
        // The helper moves into a second declaration of the same partial type: a check that looked at
        // one declaration only would lose it silently.
        var mutated = SplitPartial(SanitizedEntryDemoSource.Baseline, SyncSignature, HelperSignature);

        Assert.Empty(InputErrors(mutated));
        Assert.Empty(VerifySanitized(mutated));
        Assert.Empty(VerifyRaw(mutated));
    }

    [Fact]
    public void Violation_in_the_second_partial_declaration_is_detected()
    {
        var source = SplitPartial(SanitizedEntryDemoSource.Baseline, AsyncSignature, SyncSignature);
        var mutated = MutateMember(source, AsyncSignature, ClassEnd, AsyncHelperCall, Indent(3) + SyncCall);

        Assert.Empty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), "reaches the public synchronous entry " + SanitizedEntryChecks.SyncEntry);
    }

    [Fact]
    public void Method_without_body_is_a_diagnostic_error()
    {
        const string source = """
            #nullable enable
            namespace Demo;

            internal abstract class DemoManager
            {
                public abstract Snapshot? GetSanitizedPublishedSolution();
            }

            internal sealed class Snapshot
            {
            }
            """;

        Assert.Empty(InputErrors(source));
        AssertViolation(VerifySanitized(source), "method has no body to check");
    }

    [Fact]
    public void Truncated_source_is_a_diagnostic_error()
    {
        var baseline = SanitizedEntryDemoSource.Baseline;
        var truncated = baseline[..baseline.IndexOf(HelperSignature, StringComparison.Ordinal)];

        Assert.NotEmpty(InputErrors(truncated));
        AssertViolation(VerifySanitized(truncated), "compilation error");
    }

    [Fact]
    public void Type_from_another_namespace_is_a_diagnostic_error()
    {
        var analysis = AnalyzeDemo(SanitizedEntryDemoSource.Baseline);
        var target = new SourceTarget(analysis.Scope.Files[0], "Other.Namespace", DemoType);

        AssertViolation(SanitizedEntryChecks.VerifySanitizedEntries(analysis, target), "type not found in the declared scope");
        AssertViolation(SanitizedEntryChecks.VerifyRawPublishedEntry(analysis, target), "type not found in the declared scope");
    }

    [Fact]
    public void Unresolved_call_is_a_diagnostic_instead_of_a_green_result()
    {
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            AsyncSignature,
            SyncSignature,
            AsyncHelperCall,
            Indent(3) + "return MissingHelper();");

        Assert.NotEmpty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), "does not resolve");
    }

    [Fact]
    public void Unsupported_dynamic_call_is_a_diagnostic()
    {
        var mutated = MutateMember(
            SanitizedEntryDemoSource.Baseline,
            AsyncSignature,
            SyncSignature,
            AsyncHelperCall,
            Line(3, "dynamic manager = this;")
            + Line(3, "return (Snapshot?)manager." + SanitizedEntryChecks.SyncEntry + "();"));

        Assert.Empty(InputErrors(mutated));
        AssertViolation(VerifySanitized(mutated), "does not resolve");
    }

    [Fact]
    public void Conditional_directive_is_outside_the_declared_scope()
    {
        var mutated = ReplaceFirst(
            SanitizedEntryDemoSource.Baseline,
            HelperSignature,
            "#if DEBUG\n" + HelperSignature + "\n#endif");

        AssertViolation(VerifySanitized(mutated), "conditional directive");
    }

    [Fact]
    public void Crlf_and_lf_inputs_give_the_same_verdict()
    {
        // Both sides are built explicitly: the CRLF text from the normalized mutation, the LF text back
        // out through SourceFile.Create. Neither side depends on the line endings of this file, and a
        // no-op normalization would leave the LF side with CRLF and fail here.
        var crlf = SanitizedEntryDemoSource.AsyncCallsPublicSync.Replace("\n", "\r\n", StringComparison.Ordinal);
        Assert.Contains("\r\n", crlf, StringComparison.Ordinal);
        var lf = SourceFile.Create(DemoPath, crlf).Text;
        Assert.DoesNotContain("\r\n", lf, StringComparison.Ordinal);
        Assert.NotEqual(lf, crlf);

        // The reachability violation names the call site as `path:line`, so the two verdicts are
        // compared on a located list instead of on two empty lists that are equal either way.
        var lfViolations = VerifySanitized(lf);
        var crlfViolations = VerifySanitized(crlf);
        Assert.NotEmpty(lfViolations);
        Assert.Equal(lfViolations, crlfViolations);
        AssertViolation(lfViolations, "reaches the public synchronous entry " + SanitizedEntryChecks.SyncEntry);
    }

    [Fact]
    public void Crlf_input_is_normalized_before_the_anchors_are_applied()
    {
        // The text of a CRLF checkout arrives with CRLF while the mutation anchors are written with LF,
        // so an unnormalized input would silently drop the mutation and leave the violating example
        // green.
        var crlfBaseline = SanitizedEntryDemoSource.Baseline.Replace("\n", "\r\n", StringComparison.Ordinal);
        Assert.Contains("\r\n", crlfBaseline, StringComparison.Ordinal);

        var normalized = SourceFile.Create(DemoPath, crlfBaseline);
        Assert.DoesNotContain("\r\n", normalized.Text, StringComparison.Ordinal);
        Assert.Equal(SanitizedEntryDemoSource.Baseline, normalized.Text);

        var mutated = SanitizedEntryDemoSource.AsyncCallsPublicSync;
        var crlfMutated = SourceFile.Create(DemoPath, mutated.Replace("\n", "\r\n", StringComparison.Ordinal));

        Assert.Equal(mutated, crlfMutated.Text);
        Assert.Equal(VerifySanitized(mutated), VerifySanitized(crlfMutated.Text));
        AssertViolation(VerifySanitized(crlfMutated.Text), "reaches the public synchronous entry " + SanitizedEntryChecks.SyncEntry);
    }

    private static IReadOnlyList<string> VerifySanitized(string sourceText) =>
        SanitizedEntryChecks.VerifySanitizedEntries(AnalyzeDemo(sourceText), TargetOf(sourceText));

    private static IReadOnlyList<string> VerifyRaw(string sourceText) =>
        SanitizedEntryChecks.VerifyRawPublishedEntry(AnalyzeDemo(sourceText), TargetOf(sourceText));

    private static SourceTarget TargetOf(string sourceText) =>
        SourceTarget.FromText(DemoPath, sourceText, DemoNamespace, DemoType);

    private static SourceSetAnalysis AnalyzeDemo(string sourceText) =>
        SourceSetAnalysis.Create(SourceSetScope.Demo($"demo source {DemoPath}", new[] { SourceFile.Create(DemoPath, sourceText) }));

    private static IReadOnlyList<string> InputErrors(string sourceText) => AnalyzeDemo(sourceText).InputDiagnostics;

    private static void AssertViolation(IReadOnlyList<string> violations, string expectedFragment) =>
        Assert.Contains(violations, violation => violation.Contains(expectedFragment, StringComparison.Ordinal));

    /// <summary>Member text of the source between two anchors, indentation and trailing newlines included.</summary>
    private static string CutMember(string source, string signature, string terminator)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"member marker not found: {signature}");
        var end = source.IndexOf(terminator, start, StringComparison.Ordinal);
        Assert.True(end > start, $"member terminator not found: {terminator}");
        return source[start..end];
    }

    /// <summary>Replaces the first occurrence of an anchor; a missing anchor fails the example loudly.</summary>
    private static string ReplaceFirst(string source, string anchor, string replacement)
    {
        var index = source.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(index >= 0, $"anchor not found: `{anchor}`");
        return string.Concat(source.AsSpan(0, index), replacement, source.AsSpan(index + anchor.Length));
    }

    /// <summary>Replaces a fragment inside one member, so a shared statement is mutated in that member only.</summary>
    private static string MutateMember(string source, string signature, string terminator, string oldValue, string newValue)
    {
        var member = CutMember(source, signature, terminator);
        Assert.Contains(oldValue, member, StringComparison.Ordinal);
        return source.Replace(member, member.Replace(oldValue, newValue, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    /// <summary>Adds a member at the end of the demo type.</summary>
    private static string InsertMember(string source, string memberText) =>
        ReplaceFirst(source, ClassEnd, "\n\n" + memberText.TrimEnd('\n') + "\n}");

    /// <summary>
    /// Splits the demo type into two partial declarations: everything from <paramref name="movedSignature"/>
    /// up to <paramref name="terminator"/> moves into the second declaration.
    /// </summary>
    private static string SplitPartial(string source, string movedSignature, string terminator)
    {
        var moved = CutMember(source, movedSignature, terminator);
        var withoutMoved = ReplaceFirst(source, moved, string.Empty);
        return ReplaceFirst(withoutMoved, ClassHeader, "internal sealed partial class " + DemoType)
            + "\ninternal sealed partial class " + DemoType + "\n{\n"
            + moved.TrimEnd('\n')
            + "\n}\n";
    }

    private static string SyncLockOpening() =>
        Line(2, "_workspaceLock.Wait();")
        + Line(2, "try")
        + Line(2, "{")
        + Line(3, HelperCall)
        + Line(2, "}");

    private static string SyncLockBlock() =>
        SyncLockOpening()
        + Line(2, "finally")
        + Line(2, "{")
        + Line(3, "_workspaceLock.Release();")
        + Line(2, "}");

    /// <summary>
    /// Demo source with a delegate seam whose registration lives in a separate method of the same
    /// declared scope — the shape of the production test seam, whose target is registered outside the
    /// method that invokes it. The async entry executes <paramref name="asyncEntryStatement"/>, and the
    /// seam is registered with the synchronous entry.
    /// </summary>
    private static string SeamSource(string asyncEntryStatement) =>
        SeamSource(asyncEntryStatement, SanitizedEntryChecks.SyncEntry);

    /// <summary>
    /// The same source with an arbitrary registration value; <c>null</c> gives the negative side, where
    /// the seam carries no target at all.
    /// </summary>
    private static string SeamSource(string asyncEntryStatement, string registration)
    {
        var withSeam = InsertMember(SanitizedEntryDemoSource.Baseline, Line(1, "private Func<Snapshot?>? _seam;"));
        withSeam = InsertMember(
            withSeam,
            Member("private void RegisterSeam()", Line(2, "_seam = " + registration + ";")));
        return ReplaceFirst(withSeam, AsyncHelperCall, AsyncHelperCall + "\n" + Line(3, asyncEntryStatement));
    }

    /// <summary>
    /// The seam source above with a second accessor of the same field: the async entry reaches the seam
    /// through the result of <c>GetSeam()</c> instead of naming the delegate member, which is a call shape
    /// the walk cannot attribute to a member of the scope.
    /// </summary>
    private static string ComputedReceiverSeamSource(string asyncEntryStatement)
    {
        var source = SeamSource(asyncEntryStatement);
        return InsertMember(source, Member("private Func<Snapshot?>? GetSeam()", Line(2, "return _seam;")));
    }

    private static string Member(string signature, params string[] bodyLines)
    {
        var text = signature + "\n" + Line(1, "{") + "\n";
        foreach (var line in bodyLines)
        {
            text += line + "\n";
        }

        return text + Line(1, "}");
    }

    private static string Line(int level, string text) => Indent(level) + text + "\n";

    private static string Indent(int level) => new(' ', level * 4);
}
