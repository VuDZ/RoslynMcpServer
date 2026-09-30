using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// Corpus of the analysis mechanism itself: what the declared scope covers, how symbols are compared,
/// and which input conditions are reported as diagnostics instead of a green verdict. Every example is
/// a small in-memory source with a stated expectation, independent of the contract checks built on top.
/// </summary>
public sealed class SourceSetAnalysisTests
{
    private const string DemoPath = "Demo/Demo.cs";

    [Fact]
    public void Crlf_and_lf_texts_produce_the_same_compilation_and_line_numbers()
    {
        const string lf = "namespace Demo;\n\ninternal sealed class Demo\n{\n    public int Value() => 1;\n}\n";
        var crlf = lf.Replace("\n", "\r\n", StringComparison.Ordinal);
        Assert.Contains("\r\n", crlf, StringComparison.Ordinal);

        // The analysis normalizes its input at the single entry point, so without that normalization the
        // CRLF text would stay CRLF and the two analyses below would compare equal for an unrelated
        // reason. Line numbers are counted over the normalized text.
        var normalized = SourceFile.Create(DemoPath, crlf).Text;
        Assert.DoesNotContain("\r\n", normalized, StringComparison.Ordinal);
        Assert.Equal(lf, normalized);

        var lfAnalysis = Analyze(("Demo/Demo.cs", lf));
        var crlfAnalysis = Analyze(("Demo/Demo.cs", crlf));
        var lfLine = MethodLine(lfAnalysis, "Demo", "Demo", "Value");
        var crlfLine = MethodLine(crlfAnalysis, "Demo", "Demo", "Value");

        Assert.Equal(lfLine, crlfLine);
        Assert.Empty(lfAnalysis.InputDiagnostics);
        Assert.Empty(crlfAnalysis.InputDiagnostics);
    }

    [Fact]
    public void Partial_declarations_in_two_files_are_merged_into_one_symbol()
    {
        var analysis = Analyze(
            ("Demo/First.cs", "namespace Demo;\n\ninternal sealed partial class Demo\n{\n    public int First() => 1;\n}\n"),
            ("Demo/Second.cs", "namespace Demo;\n\ninternal sealed partial class Demo\n{\n    public int Second() => 2;\n}\n"));

        Assert.Empty(analysis.InputDiagnostics);
        var type = FindType(analysis, "Demo", "Demo");
        Assert.NotNull(type);
        Assert.NotNull(type!.GetMembers("First").FirstOrDefault());
        Assert.NotNull(type.GetMembers("Second").FirstOrDefault());
    }

    [Fact]
    public void Same_named_types_in_different_namespaces_stay_distinct()
    {
        var analysis = Analyze(
            ("Demo/First.cs", "namespace Demo;\n\ninternal sealed class Manager\n{\n    public int Value() => 1;\n}\n"),
            ("Other/Second.cs", "namespace Other;\n\ninternal sealed class Manager\n{\n    public int Value() => 2;\n}\n"));

        Assert.Empty(analysis.InputDiagnostics);
        var demoManager = FindType(analysis, "Demo", "Manager");
        var otherManager = FindType(analysis, "Other", "Manager");
        Assert.NotNull(demoManager);
        Assert.NotNull(otherManager);
        Assert.NotSame(demoManager, otherManager);
        Assert.Contains("Demo.Manager", demoManager!.ToDisplayString(), StringComparison.Ordinal);
        Assert.Contains("Other.Manager", otherManager!.ToDisplayString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_type_is_a_diagnostic_that_names_the_declared_candidates()
    {
        var analysis = Analyze(("Demo/First.cs", "namespace Demo;\n\ninternal sealed class Manager\n{\n}\n"));
        var diagnostics = new List<string>();

        var type = analysis.FindType("Missing", "Manager", diagnostics);

        Assert.Null(type);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Contains("type not found in the declared scope", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Contains("Demo.Manager", StringComparison.Ordinal));
    }

    [Fact]
    public void Compilation_error_is_an_input_diagnostic_and_the_analysis_is_not_usable()
    {
        var analysis = Analyze(("Demo/Broken.cs", "namespace Demo;\n\ninternal sealed class Broken\n{\n    public int Value() => Missing();\n}\n"));

        Assert.False(analysis.IsUsable);
        Assert.Contains(analysis.InputDiagnostics, diagnostic => diagnostic.Contains("compilation error", StringComparison.Ordinal));
    }

    [Fact]
    public void Conditional_directive_is_reported_with_the_declared_symbols()
    {
        var analysis = Analyze(
            ("Demo/Conditional.cs", "namespace Demo;\n\ninternal sealed class Conditional\n{\n#if NEVER_DEFINED\n    public int Hidden() => 1;\n#endif\n    public int Visible() => 2;\n}\n"));

        Assert.False(analysis.IsUsable);
        Assert.Contains(
            analysis.InputDiagnostics,
            diagnostic => diagnostic.Contains("conditional directive", StringComparison.Ordinal)
                && diagnostic.Contains("DEBUG", StringComparison.Ordinal));
    }

    [Fact]
    public void Reference_search_is_prefiltered_by_name_and_decided_by_symbol()
    {
        const string source = """
            namespace Demo;

            internal sealed class First
            {
                public void Target()
                {
                }
            }

            internal sealed class Second
            {
                public void Target()
                {
                }

                public void CallBoth(First first)
                {
                    first.Target();
                    Target();
                }
            }
            """;
        var analysis = Analyze(("Demo/Demo.cs", source));
        var first = FindType(analysis, "Demo", "First")!;

        var references = analysis.FindReferences("Target");
        var firstTargets = references
            .Where(reference => reference.Symbol is not null
                && SymbolEqualityComparer.Default.Equals(reference.Symbol.ContainingType, first))
            .ToList();

        Assert.Equal(2, references.Count);
        Assert.Single(firstTargets);
        Assert.Equal("void First.Target()", firstTargets[0].Symbol!.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
    }

    [Fact]
    public void Ambiguous_overload_is_a_diagnostic_instead_of_a_pick()
    {
        const string source = """
            namespace Demo;

            internal sealed class Manager
            {
                public int Entry() => 1;

                public int Entry(int unused) => unused;
            }
            """;
        var analysis = Analyze(("Demo/Demo.cs", source));
        var diagnostics = new List<string>();

        var method = analysis.FindDeclaredMethod(FindType(analysis, "Demo", "Manager")!, "Entry", diagnostics);

        Assert.Null(method);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Contains("an unambiguous signature is required", StringComparison.Ordinal));
    }

    [Fact]
    public void Method_body_scope_excludes_lambda_and_local_function_bodies()
    {
        const string source = """
            namespace Demo;

            internal sealed class Manager
            {
                public int Entry()
                {
                    System.Func<int> read = () => Helper();
                    int Local() => Helper();
                    _ = read;
                    _ = (System.Func<int>)Local;
                    return 1;
                }

                private int Helper() => 2;
            }
            """;
        var analysis = Analyze(("Demo/Demo.cs", source));
        var method = analysis.FindDeclaredMethod(FindType(analysis, "Demo", "Manager")!, "Entry", new List<string>())!;
        var (body, error) = MethodBodyScope.Create(analysis, method);

        Assert.Null(error);
        Assert.DoesNotContain(body!.Invocations, invocation => invocation.ToString().Contains("Helper", StringComparison.Ordinal));
        Assert.Empty(body.UnresolvedCalls());
    }

    [Fact]
    public void Unresolved_call_makes_reachability_undecidable()
    {
        const string source = """
            namespace Demo;

            internal sealed class Manager
            {
                public int Entry()
                {
                    dynamic target = this;
                    return target.Missing();
                }

                public int Wanted() => 1;
            }
            """;
        var analysis = Analyze(("Demo/Demo.cs", source));
        var type = FindType(analysis, "Demo", "Manager")!;
        var entry = analysis.FindDeclaredMethod(type, "Entry", new List<string>())!;
        var wanted = analysis.FindDeclaredMethod(type, "Wanted", new List<string>())!;

        var reachability = ReachabilityAnalysis.FindPath(
            analysis,
            entry,
            wanted.Name,
            symbol => SymbolEqualityComparer.Default.Equals(symbol.OriginalDefinition, wanted.OriginalDefinition));

        Assert.False(reachability.IsReachable);
        Assert.False(reachability.IsDecided);
        Assert.Contains(reachability.Diagnostics, diagnostic => diagnostic.Contains("does not resolve", StringComparison.Ordinal));
    }

    [Fact]
    public void Delegate_invocation_without_a_target_in_the_scope_is_recorded_as_a_limit()
    {
        const string source = """
            namespace Demo;

            internal sealed class Manager
            {
                public int Entry(System.Func<int> callback) => callback();

                public int Wanted() => 1;
            }
            """;
        var analysis = Analyze(("Demo/Demo.cs", source));
        var type = FindType(analysis, "Demo", "Manager")!;
        var entry = analysis.FindDeclaredMethod(type, "Entry", new List<string>())!;

        var reachability = ReachabilityAnalysis.FindPath(
            analysis,
            entry,
            "Wanted",
            symbol => symbol.Name == "Wanted");

        // A recorded limit is a construct the walk could not follow, so the result is undecided: an empty
        // path with a limit is "not shown", not evidence that the target is absent.
        Assert.False(reachability.IsReachable);
        Assert.False(reachability.IsDecided);
        Assert.Contains(reachability.Limits, limit => limit.Text.Contains("callback", StringComparison.Ordinal));
    }

    [Fact]
    public void Explicit_delegate_invocation_without_a_target_in_the_scope_is_a_limit()
    {
        // The explicit form has to be resolved by its receiver. `_callback.Invoke()` binds to the Invoke
        // member of the delegate type, which carries no target, so reading it as an ordinary call ended
        // the walk with no limit at all and a decided "not reachable". The bare control is the fact
        // above; both receiver spellings have to record the same limit and the same undecided verdict.
        const string source = """
            namespace Demo;

            internal sealed class Manager
            {
                private System.Func<int> _callback;

                public int Entry() => _callback.Invoke();

                public int Wanted() => 1;
            }
            """;
        const string parenthesizedSource = """
            namespace Demo;

            internal sealed class Manager
            {
                private System.Func<int> _callback;

                public int Entry() => (_callback).Invoke();

                public int Wanted() => 1;
            }
            """;

        AssertUnfollowedCallback(Reachability(Analyze(("Demo/Demo.cs", source))));

        // Parentheses are a wrapper the member is not bound through, so the walk unwraps them before
        // asking for the receiver symbol; without that, this spelling would fall back to an ordinary
        // call edge and hide the same limit.
        AssertUnfollowedCallback(Reachability(Analyze(("Demo/Demo.cs", parenthesizedSource))));

        static void AssertUnfollowedCallback(ReachabilityResult reachability)
        {
            Assert.False(reachability.IsReachable);
            Assert.False(reachability.IsDecided);
            Assert.Empty(reachability.Diagnostics);
            var limit = Assert.Single(reachability.Limits);
            Assert.Equal(ReachabilityLimitKind.TargetOutsideTheScope, limit.Kind);
            Assert.Contains("`_callback` invoked at", limit.Text, StringComparison.Ordinal);
            Assert.Contains("has no assignment inside the declared scope", limit.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Invoked_delegate_member_without_assignments_is_a_limit()
    {
        // Control for the delegate expansion: a member nothing assigns to is followed nowhere, and the
        // walk ends immediately with a recorded limit instead of a green "not reachable". The limit also
        // makes the walk undecided, because a target registered elsewhere could still be the forbidden call.
        const string source = """
            namespace Demo;

            internal sealed class Manager
            {
                private System.Func<int> _callback;

                public int Entry() => _callback();

                public int Wanted() => 1;
            }
            """;
        var reachability = Reachability(Analyze(("Demo/Demo.cs", source)));

        Assert.False(reachability.IsReachable);
        Assert.False(reachability.IsDecided);
        Assert.Empty(reachability.Diagnostics);
        var limit = Assert.Single(reachability.Limits);
        Assert.Equal(ReachabilityLimitKind.TargetOutsideTheScope, limit.Kind);
        Assert.Contains("`_callback` invoked at", limit.Text, StringComparison.Ordinal);
        Assert.Contains("has no assignment inside the declared scope", limit.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Delegate_invocation_through_a_computed_receiver_is_a_limit()
    {
        // `GetSeam().Invoke()` binds to `Invoke` of the delegate type, and the receiver is a call result
        // rather than a delegate member of the scope. The walk can follow neither the target nor the
        // absence of one there: an ordinary edge to the metadata `Invoke` carried no target, so the walk
        // ended with no limit and reported a decided "not reachable" while the registered target is
        // reached at run time. The construct is recorded as a limit, which is what makes the walk
        // undecided. The explicit form on a delegate member is the control: it is followed.
        const string computedReceiver = """
            namespace Demo;

            internal sealed class Manager
            {
                private System.Func<int> _callback;

                public int Entry() => GetSeam().Invoke();

                public System.Func<int> GetSeam() => _callback;

                public int Wanted() => 1;
            }
            """;
        var reachability = Reachability(Analyze(("Demo/Demo.cs", computedReceiver)));

        var limit = AssertUnfollowableInvocation(reachability);
        Assert.Contains("`GetSeam().Invoke()` at Demo/Demo.cs:7", limit.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Bare_delegate_invocation_of_a_computed_receiver_is_a_limit()
    {
        // `GetSeam()()` binds the outer invocation to a delegate-valued expression: the expression binds
        // to the method `GetSeam` and its type is the delegate, which the ordinary-call branch used to
        // read as a call of `GetSeam` itself — the same decided "not reachable" without a limit.
        const string source = """
            namespace Demo;

            internal sealed class Manager
            {
                private System.Func<int> _callback;

                public int Entry() => GetSeam()();

                public System.Func<int> GetSeam() => _callback;

                public int Wanted() => 1;
            }
            """;
        var reachability = Reachability(Analyze(("Demo/Demo.cs", source)));

        var limit = AssertUnfollowableInvocation(reachability);
        Assert.Contains("`GetSeam()()` at Demo/Demo.cs:7", limit.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Delegate_invocation_through_an_element_access_is_a_limit()
    {
        // An element of a delegate collection is a computed receiver in both forms. The explicit form used
        // to become an edge to the metadata `Invoke` and a decided "not reachable"; the bare form bound to
        // no symbol at all and was reported as an unresolved call, which is undecided for the wrong reason
        // and names a construct that does resolve — the invocation itself binds to `Invoke` of the element
        // type.
        const string explicitForm = """
            namespace Demo;

            internal sealed class Manager
            {
                private readonly System.Func<int>[] _seams = new System.Func<int>[1];

                public int Entry() => _seams[0].Invoke();

                public int Wanted() => 1;
            }
            """;
        const string bareForm = """
            namespace Demo;

            internal sealed class Manager
            {
                private readonly System.Func<int>[] _seams = new System.Func<int>[1];

                public int Entry() => _seams[0]();

                public int Wanted() => 1;
            }
            """;

        var invokeLimit = AssertUnfollowableInvocation(Reachability(Analyze(("Demo/Demo.cs", explicitForm))));
        Assert.Contains("`_seams[0].Invoke()` at Demo/Demo.cs:7", invokeLimit.Text, StringComparison.Ordinal);

        var bareLimit = AssertUnfollowableInvocation(Reachability(Analyze(("Demo/Demo.cs", bareForm))));
        Assert.Contains("`_seams[0]()` at Demo/Demo.cs:7", bareLimit.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Delegate_invoke_method_group_of_a_scope_member_is_followed()
    {
        // A method group taken from a delegate's invocation member (`copy = _callback.Invoke`) is a
        // delegate whose target is whatever the receiver holds, not the metadata member `Invoke`. Reading
        // it as an ordinary method target gave an edge to `Invoke` and a decided "not reachable"; the
        // receiver has an assignment inside the scope, so the walk has to follow it and reach the target.
        const string source = """
            namespace Demo;

            internal sealed class Manager
            {
                private System.Func<int> _callback;

                public int Entry()
                {
                    _callback = ReadTarget;
                    System.Func<int> copy = _callback.Invoke;
                    return copy();
                }

                public int ReadTarget() => Wanted();

                public int Wanted() => 1;
            }
            """;
        var reachability = Reachability(Analyze(("Demo/Demo.cs", source)));

        Assert.True(reachability.IsReachable);
        Assert.Empty(reachability.Diagnostics);
        Assert.Empty(reachability.Limits);
        Assert.Contains("Manager.Entry -> Manager.ReadTarget", reachability.DescribePath(), StringComparison.Ordinal);
    }

    [Fact]
    public void Delegate_invoke_method_group_of_a_computed_receiver_is_a_limit()
    {
        // The same method group taken from a computed receiver: the walk cannot attribute it to a member
        // of the scope, so it records a limit instead of the edge to `Invoke` that made this shape a
        // decided "not reachable".
        const string source = """
            namespace Demo;

            internal sealed class Manager
            {
                private System.Func<int> _callback;

                public int Entry()
                {
                    System.Func<int> copy = GetSeam().Invoke;
                    return copy();
                }

                public System.Func<int> GetSeam() => _callback;

                public int Wanted() => 1;
            }
            """;
        var reachability = Reachability(Analyze(("Demo/Demo.cs", source)));

        var limit = AssertUnfollowableInvocation(reachability);
        Assert.Contains("`GetSeam().Invoke` at Demo/Demo.cs:9", limit.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Delegate_dynamic_invoke_forms_are_followed_or_limited()
    {
        // `DynamicInvoke` is the same call declared on `System.Delegate`, a class rather than the delegate
        // type, so it must be recognized by its declaring type as well: on a scope member it follows the
        // receiver's assignments, on a computed receiver it is a limit. Both used to be ordinary edges to
        // a metadata member with no target and a decided "not reachable".
        const string onAMember = """
            namespace Demo;

            internal sealed class Manager
            {
                private System.Func<int> _callback;

                public int Entry()
                {
                    _callback = ReadTarget;
                    return (int)_callback.DynamicInvoke()!;
                }

                public int ReadTarget() => Wanted();

                public int Wanted() => 1;
            }
            """;
        const string onAComputedReceiver = """
            namespace Demo;

            internal sealed class Manager
            {
                private System.Func<int> _callback;

                public int Entry() => (int)GetSeam().DynamicInvoke()!;

                public System.Func<int> GetSeam() => _callback;

                public int Wanted() => 1;
            }
            """;

        var followed = Reachability(Analyze(("Demo/Demo.cs", onAMember)));
        Assert.True(followed.IsReachable);
        Assert.Empty(followed.Diagnostics);
        Assert.Empty(followed.Limits);
        Assert.Contains("Manager.Entry -> Manager.ReadTarget", followed.DescribePath(), StringComparison.Ordinal);

        var limited = AssertUnfollowableInvocation(Reachability(Analyze(("Demo/Demo.cs", onAComputedReceiver))));
        Assert.Contains("`GetSeam().DynamicInvoke()` at Demo/Demo.cs:7", limited.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Reflection_invoke_stays_an_ordinary_decided_call()
    {
        // The distinction is the declaring type, not the member name: `MethodBase.Invoke` belongs to a
        // class, so the call is an ordinary edge and the walk stays decided. A rule keyed on the name
        // alone would turn every reflection call into a limit and would refuse a check that contains one.
        const string source = """
            namespace Demo;

            internal sealed class Manager
            {
                public int Entry()
                {
                    var method = typeof(Manager).GetMethod(nameof(Wanted))!;
                    return (int)method.Invoke(this, null)!;
                }

                public int Wanted() => 1;
            }
            """;
        var reachability = Reachability(Analyze(("Demo/Demo.cs", source)));

        Assert.False(reachability.IsReachable);
        Assert.True(reachability.IsDecided);
        Assert.Empty(reachability.Diagnostics);
        Assert.Empty(reachability.Limits);
    }

    [Fact]
    public void Delegate_self_read_terminates_and_the_assigned_target_is_reached()
    {
        // `var copy = _callback;` is a read, not a target definition. It used to return the scanned name
        // itself as the assigned value, so the expansion re-enqueued the member it was expanding and
        // never returned; an xunit fact has no timeout, so this shape would hang the shard instead of
        // failing it. The assignment below it defines the target, so the walk has to reach the target
        // through the delegate instead of hanging or recording a limit.
        const string source = """
            namespace Demo;

            internal sealed class Manager
            {
                private System.Func<int> _callback;

                public int Entry()
                {
                    var copy = _callback;
                    _ = copy;
                    _callback = ReadTarget;
                    return _callback();
                }

                public int ReadTarget() => Wanted();

                public int Wanted() => 1;
            }
            """;
        var reachability = Reachability(Analyze(("Demo/Demo.cs", source)));

        Assert.True(reachability.IsReachable);
        Assert.Empty(reachability.Diagnostics);
        Assert.Empty(reachability.Limits);
        Assert.Contains("Manager.Entry -> Manager.ReadTarget", reachability.DescribePath(), StringComparison.Ordinal);
    }

    [Fact]
    public void Mutually_assigned_delegate_members_end_the_expansion()
    {
        // Two members assigned to each other: every assignment is inside the scope, so the walk ends
        // with a decided "not reachable" after each member is expanded once. Re-enqueueing an already
        // visited member is what made this shape spin.
        const string source = """
            namespace Demo;

            internal sealed class Manager
            {
                private System.Func<int> _first;
                private System.Func<int> _second;

                public int Entry()
                {
                    _first = _second;
                    _second = _first;
                    return _first();
                }

                public int Wanted() => 1;
            }
            """;
        var reachability = Reachability(Analyze(("Demo/Demo.cs", source)));

        Assert.False(reachability.IsReachable);
        Assert.Empty(reachability.Diagnostics);
        Assert.Empty(reachability.Limits);
    }

    [Fact]
    public void Delegate_assignment_to_a_method_group_is_followed()
    {
        // The assignment is inside the declared scope, so the invoked member has a traversable target:
        // reporting "no assignment inside the declared scope" here would be false.
        const string source = """
            namespace Demo;

            internal sealed class Manager
            {
                private System.Func<int> _callback;

                public int Entry()
                {
                    _callback = ReadTarget;
                    return _callback();
                }

                public int ReadTarget() => Wanted();

                public int Wanted() => 1;
            }
            """;
        var reachability = Reachability(Analyze(("Demo/Demo.cs", source)));

        Assert.True(reachability.IsReachable);
        Assert.Empty(reachability.Diagnostics);
        Assert.Empty(reachability.Limits);
        Assert.Contains("Manager.Entry -> Manager.ReadTarget", reachability.DescribePath(), StringComparison.Ordinal);
    }

    [Fact]
    public void Delegate_assignment_to_a_lambda_body_is_followed()
    {
        // The lambda is assigned in a method the walk never executes, so only the assignment lookup can
        // make its body an executed scope; the deferred-scope approximation alone does not reach it.
        const string source = """
            namespace Demo;

            internal sealed class Manager
            {
                private System.Func<int> _callback;

                public int Entry() => _callback();

                public void Register() => _callback = () => Wanted();

                public int Wanted() => 1;
            }
            """;
        var reachability = Reachability(Analyze(("Demo/Demo.cs", source)));

        Assert.True(reachability.IsReachable);
        Assert.Empty(reachability.Diagnostics);
        Assert.Empty(reachability.Limits);
        Assert.Contains("Manager.Entry -> Manager.Wanted", reachability.DescribePath(), StringComparison.Ordinal);
    }

    [Fact]
    public void Delegate_value_returned_by_an_accessor_is_followed()
    {
        // The assignment takes the result of a call, so the callee is not the value the member can hold:
        // the walk has to follow what `GetSeam` returns. Reading the callee as the assigned target left
        // the walk with an edge to `GetSeam` — no target, no limit, no diagnostic — and reported a decided
        // "not reached" for a delegate the accessor hands out inside the scope.
        const string source = """
            namespace Demo;

            internal sealed class Manager
            {
                private System.Func<int> _callback;

                public int Entry()
                {
                    _callback = ReadTarget;
                    System.Func<int> local = GetSeam();
                    return local();
                }

                public System.Func<int> GetSeam() => _callback;

                public int ReadTarget() => Wanted();

                public int Wanted() => 1;
            }
            """;

        AssertProducedDelegateReachesTarget(source);
    }

    [Fact]
    public void Delegate_value_returned_by_an_accessor_is_followed_in_the_explicit_forms()
    {
        // The same value through the two remaining call spellings: the explicit invocation member
        // `local.Invoke()` and the null-forgiving call `GetSeam()!`. Both used to end the same way as the
        // bare form — an edge to the accessor and a decided "not reached" — and both have to reach the
        // target of the delegate the local holds.
        const string explicitInvoke = """
            namespace Demo;

            internal sealed class Manager
            {
                private System.Func<int> _callback;

                public int Entry()
                {
                    _callback = ReadTarget;
                    System.Func<int> local = GetSeam();
                    return local.Invoke();
                }

                public System.Func<int> GetSeam() => _callback;

                public int ReadTarget() => Wanted();

                public int Wanted() => 1;
            }
            """;
        const string forgivingCall = """
            namespace Demo;

            internal sealed class Manager
            {
                private System.Func<int> _callback;

                public int Entry()
                {
                    _callback = ReadTarget;
                    System.Func<int> local = GetSeam()!;
                    return local();
                }

                public System.Func<int> GetSeam() => _callback;

                public int ReadTarget() => Wanted();

                public int Wanted() => 1;
            }
            """;

        AssertProducedDelegateReachesTarget(explicitInvoke);
        AssertProducedDelegateReachesTarget(forgivingCall);
    }

    [Fact]
    public void Delegate_value_returned_by_a_block_bodied_accessor_is_followed()
    {
        // The other body form of the accessor: the value sits in a `return` statement instead of an
        // expression body, and the walk has to read it there as well.
        const string source = """
            namespace Demo;

            internal sealed class Manager
            {
                private System.Func<int> _callback;

                public int Entry()
                {
                    _callback = ReadTarget;
                    System.Func<int> local = GetSeam();
                    return local();
                }

                public System.Func<int> GetSeam()
                {
                    return _callback;
                }

                public int ReadTarget() => Wanted();

                public int Wanted() => 1;
            }
            """;

        AssertProducedDelegateReachesTarget(source);
    }

    [Fact]
    public void Delegate_value_returned_by_an_accessor_is_followed_into_a_field()
    {
        // The value the accessor returns is stored in a field and invoked from there: the field takes the
        // place of the local copy, and the same shape has to reach the target.
        const string source = """
            namespace Demo;

            internal sealed class Manager
            {
                private System.Func<int> _callback;
                private System.Func<int> _stored;

                public int Entry()
                {
                    _callback = ReadTarget;
                    _stored = GetSeam();
                    return _stored();
                }

                public System.Func<int> GetSeam() => _callback;

                public int ReadTarget() => Wanted();

                public int Wanted() => 1;
            }
            """;

        AssertProducedDelegateReachesTarget(source);
    }

    [Fact]
    public void Delegate_value_returned_by_a_local_function_is_followed()
    {
        // The accessor is a local function declared in the entry body: the walk locates its declaration
        // through the called symbol, so the local function form needs no separate path.
        const string source = """
            namespace Demo;

            internal sealed class Manager
            {
                private System.Func<int> _callback;

                public int Entry()
                {
                    _callback = ReadTarget;
                    System.Func<int> GetSeam() => _callback;
                    System.Func<int> local = GetSeam();
                    return local();
                }

                public int ReadTarget() => Wanted();

                public int Wanted() => 1;
            }
            """;

        AssertProducedDelegateReachesTarget(source);
    }

    [Fact]
    public void Delegate_value_returned_by_an_accessor_without_a_scope_target_is_a_limit()
    {
        // Control for the facts above: the accessor returns a delegate member the scope never assigns, so
        // the walk follows the value and finds no target for it. That is a limit — "not shown" — and the
        // result is undecided. Before the fix the same source ended with an edge to the accessor, no limit
        // at all and a decided "not reached", which read the unknown target as absent.
        const string source = """
            namespace Demo;

            internal sealed class Manager
            {
                private System.Func<int> _callback;

                public int Entry()
                {
                    System.Func<int> local = GetSeam();
                    return local();
                }

                public System.Func<int> GetSeam() => _callback;

                public int Wanted() => 1;
            }
            """;
        var reachability = Reachability(Analyze(("Demo/Demo.cs", source)));

        Assert.False(reachability.IsReachable);
        Assert.False(reachability.IsDecided);
        Assert.Empty(reachability.Diagnostics);
        var limit = Assert.Single(reachability.Limits);
        Assert.Equal(ReachabilityLimitKind.TargetOutsideTheScope, limit.Kind);
        Assert.Contains("`_callback`, reached from the invoked delegate `local`", limit.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Delegate_value_computed_by_an_accessor_is_a_limit()
    {
        // A value the accessor computes — the result of another call, a conditional — is beyond what this
        // expansion follows: the target it can carry is unknown, so the walk records a limit instead of
        // reading the unknown target as absent. Both shapes used to end on an edge to the accessor with no
        // limit at all.
        const string computed = """
            namespace Demo;

            internal sealed class Manager
            {
                private System.Func<int> _callback;

                public int Entry()
                {
                    System.Func<int> local = GetSeam();
                    return local();
                }

                public System.Func<int> GetSeam() => MakeSeam();

                public System.Func<int> MakeSeam() => _callback;

                public int Wanted() => 1;
            }
            """;
        const string conditional = """
            namespace Demo;

            internal sealed class Manager
            {
                private bool _preferFirst;
                private System.Func<int> _callback;
                private System.Func<int> _fallback;

                public int Entry()
                {
                    System.Func<int> local = GetSeam();
                    return local();
                }

                public System.Func<int> GetSeam() => _preferFirst ? _callback : _fallback;

                public int Wanted() => 1;
            }
            """;

        var computedLimit = AssertUnfollowableInvocation(Reachability(Analyze(("Demo/Demo.cs", computed))));
        Assert.Contains("`MakeSeam()` at Demo/Demo.cs:13", computedLimit.Text, StringComparison.Ordinal);
        Assert.Contains("is the result of another call", computedLimit.Text, StringComparison.Ordinal);

        var conditionalLimit = AssertUnfollowableInvocation(Reachability(Analyze(("Demo/Demo.cs", conditional))));
        Assert.Contains("`_preferFirst ? _callback : _fallback` at Demo/Demo.cs:15", conditionalLimit.Text, StringComparison.Ordinal);
        Assert.Contains("returned by `GetSeam`", conditionalLimit.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Delegate_chain_within_the_bound_reaches_the_target()
    {
        var reachability = Reachability(Analyze(("Demo/Demo.cs", DelegateChainSource(8))));

        Assert.True(reachability.IsReachable);
        Assert.Empty(reachability.Diagnostics);
        Assert.Empty(reachability.Limits);
    }

    [Fact]
    public void Delegate_chain_longer_than_the_bound_is_an_undecidable_walk()
    {
        // A chain longer than the declared delegate bound must stop with a recorded diagnostic, which
        // makes the result undecidable: a cut walk is never reported as "not reachable".
        var reachability = Reachability(Analyze(("Demo/Demo.cs", DelegateChainSource(ReachabilityAnalysis.MaxVisitedDelegateMembers + 8))));

        Assert.False(reachability.IsDecided);
        Assert.Contains(
            reachability.Diagnostics,
            diagnostic => diagnostic.Contains("reachability walk stopped after", StringComparison.Ordinal)
                && diagnostic.Contains("delegate members", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_scope_is_usable_cached_and_covers_the_solution_manager()
    {
        var production = ProductionAnalysis.Instance;

        Assert.Same(production, ProductionAnalysis.Instance);
        Assert.Empty(production.InputDiagnostics);
        Assert.True(production.Scope.Files.Count > 100, $"unexpected production file count: {production.Scope.Files.Count}");
        Assert.Contains("production sources", production.Scope.Description, StringComparison.Ordinal);
        Assert.Contains(
            production.Scope.Files,
            file => file.Path.EndsWith("SolutionManager.cs", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(FindType(production, "RoslynMcpServer.Services", "SolutionManager"));
    }

    [Fact]
    public void Production_reachability_records_only_the_known_delegate_limits()
    {
        // Watcher callbacks add two test seams to the load walk because the analysis also visits the
        // event-handler lambdas registered during load. Like the load seam, their assignments live in
        // excluded test projects. TryRun's update parameter is another explicit limit: argument-to-
        // parameter flow is not modeled, although passed lambda bodies are checked at their call sites.
        // Keep an exact inventory rather than accepting any number of limits; an unexpected blind spot
        // must still fail, and these known limits must not turn into a decided absence of the sync entry.
        var production = ProductionAnalysis.Instance;
        var type = FindType(production, "RoslynMcpServer.Services", "SolutionManager")!;
        var asyncEntry = production.FindDeclaredMethod(type, SanitizedEntryChecks.AsyncEntry, new List<string>())!;
        var syncEntry = production.FindDeclaredMethod(type, SanitizedEntryChecks.SyncEntry, new List<string>())!;

        var reachability = ReachabilityAnalysis.FindPath(
            production,
            asyncEntry,
            syncEntry.Name,
            symbol => SymbolEqualityComparer.Default.Equals(symbol.OriginalDefinition, syncEntry.OriginalDefinition));

        Assert.False(reachability.IsReachable);
        Assert.False(reachability.IsDecided);
        Assert.Empty(reachability.Diagnostics);
        string[] expectedMembers =
        [
            "AfterPhysicalLoadBeforePrepareAsync",
            "BeforeDiskWatcherCallbackForTests",
            "AfterDiskWatcherChangeForTests",
            "update",
        ];
        Assert.Equal(expectedMembers.Length, reachability.Limits.Count);
        foreach (var member in expectedMembers)
        {
            var limit = Assert.Single(reachability.Limits,
                candidate => candidate.Text.StartsWith($"`{member}`", StringComparison.Ordinal));
            Assert.Equal(ReachabilityLimitKind.TargetOutsideTheScope, limit.Kind);
            var sourcePath = member == "update"
                ? Path.Combine("Services", "Models", "WorkspaceInputSession.cs")
                : Path.Combine("Services", "SolutionManager.cs");
            Assert.Contains(sourcePath, limit.Text, StringComparison.Ordinal);
        }
        Assert.Contains(reachability.Limits, limit => limit.Text.StartsWith("`AfterPhysicalLoadBeforePrepareAsync`",
            StringComparison.Ordinal) && limit.Text.Contains("`boundarySeam`", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Passed_lambda_is_checked_while_delegate_parameter_flow_remains_a_declared_limit(bool callsTarget)
    {
        // Passing a lambda does not assign the callee's parameter in this analysis. That limitation must
        // remain visible without hiding a forbidden call written inside the passed lambda itself.
        var source = $$"""
            namespace Demo;

            internal sealed class Manager
            {
                public int Entry() => Run(() => {{(callsTarget ? "Wanted()" : "42")}});

                private int Run(System.Func<int> callback) => callback();

                public int Wanted() => 1;
            }
            """;

        var reachability = Reachability(Analyze((DemoPath, source)));

        Assert.Equal(callsTarget, reachability.IsReachable);
        Assert.Empty(reachability.Diagnostics);
        if (callsTarget)
        {
            Assert.True(reachability.IsDecided);
            Assert.Contains(reachability.Path, step => step.Callee.Name == "Wanted");
        }
        else
        {
            Assert.False(reachability.IsDecided);
            var limit = Assert.Single(reachability.Limits);
            Assert.Equal(ReachabilityLimitKind.TargetOutsideTheScope, limit.Kind);
            Assert.StartsWith("`callback` invoked at Demo/Demo.cs:7", limit.Text, StringComparison.Ordinal);
        }
    }

    private static INamedTypeSymbol? FindType(SourceSetAnalysis analysis, string namespaceName, string typeName)
    {
        var diagnostics = new List<string>();
        var type = analysis.FindType(namespaceName, typeName, diagnostics);
        Assert.Empty(diagnostics);
        return type;
    }

    /// <summary>Reachability from <c>Manager.Entry</c> to <c>Manager.Wanted</c> of a demo source.</summary>
    private static ReachabilityResult Reachability(SourceSetAnalysis analysis)
    {
        var type = FindType(analysis, "Demo", "Manager")!;
        var entry = analysis.FindDeclaredMethod(type, "Entry", new List<string>())!;

        return ReachabilityAnalysis.FindPath(
            analysis,
            entry,
            "Wanted",
            symbol => symbol.Name == "Wanted");
    }

    /// <summary>
    /// Asserts the walk ended undecided on exactly one limit whose reason is the call site itself — not a
    /// member whose target is registered elsewhere — and returns it, so the fact can name the construct.
    /// </summary>
    private static ReachabilityLimit AssertUnfollowableInvocation(ReachabilityResult reachability)
    {
        Assert.False(reachability.IsReachable);
        Assert.False(reachability.IsDecided);
        Assert.Empty(reachability.Diagnostics);
        var limit = Assert.Single(reachability.Limits);
        Assert.Equal(ReachabilityLimitKind.DelegateReceiverNotInScope, limit.Kind);
        return limit;
    }

    /// <summary>
    /// Asserts the walk reached <c>Manager.Wanted</c> through a delegate the entry took from a member that
    /// returns it: the value is followed, so the target is shown as a path and nothing is left unresolved.
    /// </summary>
    private static void AssertProducedDelegateReachesTarget(string source)
    {
        var reachability = Reachability(Analyze(("Demo/Demo.cs", source)));

        Assert.True(reachability.IsReachable);
        Assert.Empty(reachability.Diagnostics);
        Assert.Empty(reachability.Limits);
        Assert.Contains("Manager.Entry -> Manager.ReadTarget", reachability.DescribePath(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Source with a chain of delegate members of the given length: every member is assigned the next
    /// one and the last member is assigned the target method.
    /// </summary>
    private static string DelegateChainSource(int length)
    {
        var text = new StringBuilder("namespace Demo;\n\ninternal sealed class Manager\n{\n");
        for (var index = 0; index < length; index++)
        {
            text.Append("    private System.Func<int> _step").Append(index).Append(";\n");
        }

        text.Append("\n    public int Entry()\n    {\n");
        for (var index = 0; index < length - 1; index++)
        {
            text.Append("        _step").Append(index).Append(" = _step").Append(index + 1).Append(";\n");
        }

        text.Append("        _step").Append(length - 1).Append(" = Wanted;\n");
        text.Append("        return _step0();\n    }\n\n    public int Wanted() => 1;\n}\n");
        return text.ToString();
    }

    private static int MethodLine(SourceSetAnalysis analysis, string namespaceName, string typeName, string methodName)
    {
        var type = FindType(analysis, namespaceName, typeName)!;
        var method = type.GetMembers(methodName).OfType<IMethodSymbol>().Single();
        return method.DeclaringSyntaxReferences.Single().GetSyntax().GetLocation().GetLineSpan().StartLinePosition.Line + 1;
    }

    private static SourceSetAnalysis Analyze(params (string Path, string Text)[] files)
    {
        var sources = files.Select(file => SourceFile.Create(file.Path, file.Text)).ToList();
        return SourceSetAnalysis.Create(SourceSetScope.Demo($"demo sources {DemoPath}", sources));
    }
}
