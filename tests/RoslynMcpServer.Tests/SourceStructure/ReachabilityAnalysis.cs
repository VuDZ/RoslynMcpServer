using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// Reachability over executed calls inside the analyzed sources. The walk follows call symbols, so an
/// overload or a same-named member of another type is a different edge, and it terminates on cycles.
/// <para>
/// Executed means: statements of the method body, bodies of local functions that are actually invoked,
/// lambda bodies (a passed lambda is normally invoked by its receiver, so skipping it would hide real
/// violations), and methods assigned to a delegate member that is invoked on the walk. An uncalled
/// local function is not executed. Unresolved call sites make the walk undecidable and are reported
/// instead of being read as absence.
/// </para>
/// <para>
/// A delegate member is followed through the assignments and initializers written inside the declared
/// scope: a method group, a lambda, another delegate member, or a value a call hands to the member, whose
/// own <c>return</c> expressions are classified the same way. That expansion is a graph of its own, so
/// it carries its own bound and terminates on cycles the same way the method walk does; a member whose
/// value is not defined in the scope, and a value the scope cannot resolve, are recorded as limits. The
/// call forms of a produced value are followed as well: a member returned by an accessor inside the scope
/// reaches its target, and an accessor whose value stays unresolved — another call, a conditional, an
/// element access, a member outside the scope — is a limit rather than a decided absence. Every spelling
/// of a delegate call reaches
/// that expansion when the receiver is a delegate member of the scope: the bare form <c>_seam()</c>, the
/// explicit forms <c>_seam.Invoke()</c>, <c>_seam?.Invoke()</c> and <c>_seam!.Invoke()</c> (also
/// <c>BeginInvoke</c>, <c>EndInvoke</c> and <c>DynamicInvoke</c>), because the compiler binds them to the
/// invocation member of the delegate type rather than to the member, and a method group taken from such
/// an invocation member (<c>Func&lt;T&gt; copy = _seam.Invoke;</c>), because the delegate it creates
/// calls whatever the receiver holds. A call the walk cannot attribute to such a member — a computed
/// receiver (<c>GetSeam().Invoke()</c>, <c>_seams[0].Invoke()</c>) or a delegate-valued expression
/// (<c>GetSeam()()</c>) — is recorded as a limit naming the construct instead of an edge to the metadata
/// invocation member, which carries no target.
/// </para>
/// <para>
/// Limits: the analysis follows what the compiler binds. A virtual or interface call is the edge to the
/// statically bound member, not to every override. A recorded limit makes the result undecided, so a
/// construct the walk could not follow is never read as proof that a forbidden call is absent. What is
/// not recorded as a limit: a call the compiler binds to an ordinary member of a non-delegate type, which
/// includes reflection (<c>MethodInfo.Invoke</c>), and a virtual or interface call. Those are crossed as
/// the bound member only, so a target they reach at run time is not shown and the result can still be a
/// decided "not reached" — a documented limitation of the analysis rather than silence about a delegate.
/// This is not a general control-flow analysis and proves nothing about runtime deadlocks.
/// </para>
/// </summary>
internal static class ReachabilityAnalysis
{
    /// <summary>Upper bound of visited methods; hitting it is reported as an undecidable walk, never as absence.</summary>
    public const int MaxVisitedMethods = 2048;

    /// <summary>
    /// Upper bound of expanded delegate members of one walk. The delegate graph is separate from the
    /// call graph, so it carries its own bound; hitting it is reported as an undecidable walk, never as
    /// absence.
    /// </summary>
    public const int MaxVisitedDelegateMembers = 256;

    public static ReachabilityResult FindPath(
        SourceSetAnalysis analysis,
        IMethodSymbol entry,
        string targetMemberName,
        Func<ISymbol, bool> isTarget)
    {
        return new Walk(analysis, targetMemberName, isTarget).Run(entry);
    }

    /// <summary>One reachability query; holds the caches and diagnostics of a single walk.</summary>
    private sealed class Walk
    {
        public Walk(SourceSetAnalysis analysis, string targetMemberName, Func<ISymbol, bool> isTarget)
        {
            _analysis = analysis;
            _targetMemberName = targetMemberName;
            _isTarget = isTarget;
        }

        public ReachabilityResult Run(IMethodSymbol entry)
        {
            var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default) { entry };
            var previous = new Dictionary<IMethodSymbol, SourceCallStep>(SymbolEqualityComparer.Default);
            var queue = new Queue<IMethodSymbol>();
            queue.Enqueue(entry);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var step in ExecutedCalls(current))
                {
                    if (_isTarget(step.Callee))
                    {
                        var path = BuildPath(previous, current);
                        path.Add(step);
                        return new ReachabilityResult(path, _diagnostics, _limits);
                    }

                    if (!visited.Add(step.Callee))
                    {
                        continue;
                    }

                    if (visited.Count > MaxVisitedMethods)
                    {
                        _diagnostics.Add(
                            $"{entry.Name}: reachability walk stopped after {MaxVisitedMethods} methods; "
                            + "the call graph is larger than the declared analysis bound");
                        return new ReachabilityResult(Array.Empty<SourceCallStep>(), _diagnostics, _limits);
                    }

                    previous[step.Callee] = step;
                    queue.Enqueue(step.Callee);
                }
            }

            return new ReachabilityResult(Array.Empty<SourceCallStep>(), _diagnostics, _limits);
        }

        private static List<SourceCallStep> BuildPath(
            Dictionary<IMethodSymbol, SourceCallStep> previous,
            IMethodSymbol current)
        {
            var reversed = new List<SourceCallStep>();
            var cursor = current;
            while (previous.TryGetValue(cursor, out var step))
            {
                reversed.Add(step);
                cursor = step.Caller;
            }

            reversed.Reverse();
            return reversed;
        }

        /// <summary>
        /// Executed calls of one method: its own statements, the bodies of invoked local functions and
        /// the bodies of lambdas written in an executed scope.
        /// </summary>
        private IReadOnlyList<SourceCallStep> ExecutedCalls(IMethodSymbol method)
        {
            var calls = new List<SourceCallStep>();
            var body = TryGetBodyScope(method);
            if (body is null)
            {
                return calls;
            }

            var model = _analysis.ModelFor(body.SyntaxTree);
            var pendingLocalFunctions = new Dictionary<IMethodSymbol, SyntaxNode>(SymbolEqualityComparer.Default);
            var scopes = new Queue<(SyntaxNode Root, IMethodSymbol Owner)>();
            scopes.Enqueue((body, method));
            var visitedScopes = new HashSet<SyntaxNode>();

            while (scopes.Count > 0)
            {
                var (root, owner) = scopes.Dequeue();
                if (!visitedScopes.Add(root))
                {
                    continue;
                }

                foreach (var node in MethodBodyScope.DirectNodes(root))
                {
                    if (node is not InvocationExpressionSyntax invocation || MethodBodyScope.IsNameOf(invocation))
                    {
                        continue;
                    }

                    var symbol = model.GetSymbolInfo(invocation.Expression).Symbol;

                    // A delegate call is routed through the member it goes through. The receiver is the
                    // whole decision: only a delegate member of the scope has the assignment list the
                    // expansion follows, so a call the walk cannot attribute to one is recorded as a
                    // limit. Reading it as an ordinary call instead ended the walk with an edge to the
                    // metadata `Invoke` — no target, no limit, no diagnostic — and reported a decided
                    // "not reachable" for a delegate that is invoked at run time. A same-named member of
                    // a non-delegate type (a reflection call such as `methodInfo.Invoke(...)`) is not a
                    // delegate call and keeps its ordinary edge.
                    if (IsDelegateCall(model, invocation, symbol, out var delegateMember))
                    {
                        if (delegateMember is null)
                        {
                            _limits.Add(ReachabilityLimit.DelegateReceiverNotInScope(DescribeUnfollowedInvocation(invocation)));
                        }
                        else
                        {
                            AddDelegateTargets(owner, delegateMember, invocation, calls, scopes);
                        }

                        continue;
                    }

                    if (symbol is IMethodSymbol called)
                    {
                        var callee = MethodBodyScope.DeclaredForm(called);
                        calls.Add(SourceCallStep.At(owner, callee, invocation));
                        if (pendingLocalFunctions.TryGetValue(callee, out var localFunctionBody))
                        {
                            pendingLocalFunctions.Remove(callee);
                            scopes.Enqueue((localFunctionBody, callee));
                        }

                        continue;
                    }

                    _diagnostics.Add(
                        $"{owner.ContainingType.Name}.{owner.Name}: call at {SourceSetAnalysis.Describe(invocation)} does not resolve; "
                        + "reachability is undecidable from here");
                }

                foreach (var deferred in DeferredScopes(root))
                {
                    if (deferred is LocalFunctionStatementSyntax localFunction)
                    {
                        if (model.GetDeclaredSymbol(localFunction) is IMethodSymbol localSymbol)
                        {
                            pendingLocalFunctions[localSymbol] = MethodBodyScope.ScopeBody(localFunction);
                        }

                        continue;
                    }

                    // A passed lambda is normally invoked by its receiver, so its body is treated as
                    // executed: skipping it would hide a real violation, which is the worse error here.
                    scopes.Enqueue((MethodBodyScope.ScopeBody(deferred), owner));
                }
            }

            return calls;
        }

        /// <summary>
        /// Routes one invocation of a delegate member through the delegate expansion: an assigned method
        /// becomes a call edge from the invoking method, an assigned lambda body becomes an executed
        /// scope. Every call form of the same member uses this path, so the bare form, the explicit
        /// invocation form and a method group taken from its invocation member give the same edges, the
        /// same limits and the same diagnostics for the same source.
        /// </summary>
        private void AddDelegateTargets(
            IMethodSymbol owner,
            ISymbol delegateMember,
            InvocationExpressionSyntax invocation,
            List<SourceCallStep> calls,
            Queue<(SyntaxNode Root, IMethodSymbol Owner)> scopes)
        {
            foreach (var target in DelegateTargets(delegateMember, invocation))
            {
                if (target.Method is not null)
                {
                    calls.Add(SourceCallStep.At(owner, target.Method, invocation));
                }
                else if (target.Body is not null)
                {
                    scopes.Enqueue((target.Body, owner));
                }
            }
        }

        /// <summary>
        /// Classifies one invocation as a call of a delegate value and names the delegate-typed member it
        /// goes through, or null when there is no such member. The compiler binds three shapes to a
        /// delegate rather than to a method: the delegate-typed member itself (<c>_seam()</c>), the
        /// invocation member of a delegate type (<c>_seam.Invoke()</c>, <c>GetSeam().Invoke()</c>,
        /// <c>_seam?.Invoke()</c>) and a delegate-valued expression that is not a member at all
        /// (<c>GetSeam()()</c>, <c>_seams[0]()</c>). An ordinary member named <c>Invoke</c> of a
        /// non-delegate type (<c>MethodInfo.Invoke</c>) is not a delegate call: only a delegate-typed
        /// receiver, member or result counts, and that declaring type is what separates the two.
        /// <para>
        /// Only a member of the scope returns a member, because only a member carries the assignment list
        /// the expansion reads. Everything else is a delegate call the walk cannot attribute to one, which
        /// the caller records as a limit.
        /// </para>
        /// </summary>
        private static bool IsDelegateCall(
            SemanticModel model,
            InvocationExpressionSyntax invocation,
            ISymbol? symbol,
            out ISymbol? delegateMember)
        {
            delegateMember = null;

            if (DelegateTypeOf(symbol) is not null)
            {
                delegateMember = symbol;
                return true;
            }

            if (symbol is IMethodSymbol called && IsDelegateInvocationMember(called))
            {
                delegateMember = ReceiverMember(model, invocation.Expression);
                return true;
            }

            if (model.GetTypeInfo(invocation.Expression).Type is { TypeKind: TypeKind.Delegate })
            {
                delegateMember = ReceiverMember(model, invocation.Expression);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Member an expression the compiler bound to a delegate goes through, or null when it goes
        /// through anything else: a computed expression, an element access, a cast.
        /// </summary>
        private static ISymbol? ReceiverMember(SemanticModel model, ExpressionSyntax expression)
        {
            var receiver = ReceiverExpression(expression);
            if (receiver is null)
            {
                return null;
            }

            var receiverSymbol = model.GetSymbolInfo(Unwrap(receiver)).Symbol;
            return DelegateTypeOf(receiverSymbol) is null ? null : receiverSymbol;
        }

        /// <summary>
        /// Receiver expression a member access or a member binding is resolved against, or null for any
        /// other expression shape. <c>receiver?.Invoke()</c> writes the member binding inside the
        /// conditional access, which owns the receiver expression the binding belongs to.
        /// </summary>
        private static ExpressionSyntax? ReceiverExpression(ExpressionSyntax expression) => Unwrap(expression) switch
        {
            MemberAccessExpressionSyntax access => access.Expression,
            MemberBindingExpressionSyntax binding => binding.Ancestors().OfType<ConditionalAccessExpressionSyntax>().FirstOrDefault()?.Expression,
            _ => null,
        };

        /// <summary>
        /// Invocation member of a delegate the compiler binds a call of a delegate value to. A delegate
        /// type declares <c>Invoke</c>, <c>BeginInvoke</c> and <c>EndInvoke</c>; <c>DynamicInvoke</c> is
        /// the same call declared on <c>System.Delegate</c>, which is a class, so the declaring type is
        /// checked for each group instead of the name alone.
        /// </summary>
        private static bool IsDelegateInvocationMember(IMethodSymbol method) => method.Name switch
        {
            "Invoke" or "BeginInvoke" or "EndInvoke" => method.ContainingType is { TypeKind: TypeKind.Delegate },
            "DynamicInvoke" => method.ContainingType?.SpecialType is SpecialType.System_Delegate or SpecialType.System_MulticastDelegate,
            _ => false,
        };

        /// <summary>
        /// Operand of the wrappers a receiver or a delegate expression is written through. A <c>!</c>
        /// (null-forgiving) and parentheses carry no member of their own, so the bound expression is the
        /// operand; asking for the symbol of the wrapper itself returns nothing.
        /// </summary>
        private static ExpressionSyntax Unwrap(ExpressionSyntax expression) => expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => Unwrap(parenthesized.Expression),
            PostfixUnaryExpressionSyntax suppression when suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression) =>
                Unwrap(suppression.Operand),
            _ => expression,
        };

        /// <summary>
        /// Targets a delegate member can hold inside the declared scope: methods and lambdas assigned to
        /// it, delegate members it is assigned from, including a method group taken from another
        /// delegate's invocation member, and the delegate values returned by a member the assignment
        /// calls. Every member is expanded at most once per walk and every assignment chain ends at a
        /// member already visited, so a self-referential read (<c>var copy = _callback;</c>) and a
        /// mutual assignment cycle end the expansion instead of re-enqueueing a member forever. A member
        /// with no assignment in the scope is recorded as a limit, an assignment that does not resolve is
        /// a diagnostic.
        /// </summary>
        private IReadOnlyList<DelegateTarget> DelegateTargets(ISymbol delegateMember, InvocationExpressionSyntax invocation)
        {
            var expansion = new DelegateExpansion();
            if (!_inspectedDelegateMembers.Add(delegateMember))
            {
                return expansion.Targets;
            }

            EnqueueDelegateMember(expansion, delegateMember);
            while (expansion.Pending.Count > 0)
            {
                if (_visitedDelegateMembers > MaxVisitedDelegateMembers)
                {
                    ReportDelegateBound(delegateMember);
                    return expansion.Targets;
                }

                _visitedDelegateMembers++;
                var member = expansion.Pending.Dequeue();
                var assignments = 0;
                foreach (var value in AssignedValues(member))
                {
                    assignments++;
                    ClassifyDelegateValue(value, member, DelegateValueOrigin.Assignment, expansion);
                }

                if (assignments == 0)
                {
                    _limits.Add(ReachabilityLimit.TargetOutsideTheScope(DescribeUnfollowedMember(member, delegateMember, invocation)));
                }
            }

            return expansion.Targets;
        }

        /// <summary>
        /// What one delegate value the expansion receives gives it: a lambda body becomes an executed
        /// scope, a method group becomes a call edge, a delegate member or a delegate invocation member
        /// joins the same expansion, and a value a call produces is followed through what the called
        /// member returns. The origin decides the answer for a value the scope cannot resolve: an
        /// assignment that does not bind is a diagnostic, while a returned value is recorded as a limit,
        /// because the call site the value came from is written nowhere else.
        /// </summary>
        private void ClassifyDelegateValue(
            ExpressionSyntax value,
            ISymbol member,
            DelegateValueOrigin origin,
            DelegateExpansion expansion)
        {
            // A null or default value clears the delegate: it carries no target, and reporting it as an
            // unresolved target would turn a legitimate reset into a false violation.
            if (IsEmptyDelegateValue(value))
            {
                return;
            }

            var valueModel = _analysis.ModelFor(value.SyntaxTree);
            var unwrapped = Unwrap(value);
            if (unwrapped is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
            {
                expansion.Targets.Add(DelegateTarget.FromBody(MethodBodyScope.ScopeBody(unwrapped)));
                return;
            }

            var valueSymbol = valueModel.GetSymbolInfo(unwrapped).Symbol;
            if (valueSymbol is IMethodSymbol method && IsDelegateInvocationMember(method))
            {
                FollowInvocationMemberReceiver(unwrapped, valueModel, expansion);
                return;
            }

            // A delegate value produced by a call is the result of that call, not a method group: the
            // callee is not what the member can hold. Reading the callee as an assigned target ended the
            // walk with an edge to an accessor that returns the delegate — no limit, no diagnostic — and
            // reported a decided "not reached" for a delegate the accessor hands out inside the scope.
            if (unwrapped is InvocationExpressionSyntax produced
                && valueModel.GetSymbolInfo(produced.Expression).Symbol is IMethodSymbol called
                && valueModel.GetTypeInfo(produced.Expression).Type is not { TypeKind: TypeKind.Delegate })
            {
                FollowProducedValue(produced, called, origin, expansion);
                return;
            }

            if (valueSymbol is IMethodSymbol assignedMethod)
            {
                expansion.Targets.Add(DelegateTarget.FromMethod(assignedMethod));
                return;
            }

            if (DelegateTypeOf(valueSymbol) is not null)
            {
                EnqueueDelegateMember(expansion, valueSymbol!);
                return;
            }

            RecordUnresolvedValue(value, member, origin);
        }

        /// <summary>
        /// Joins a delegate member to the expansion. A cycle ends here: a member already visited defines no
        /// target this expansion has not seen, so it is not enqueued again.
        /// </summary>
        private static void EnqueueDelegateMember(DelegateExpansion expansion, ISymbol member)
        {
            if (expansion.Visited.Add(member))
            {
                expansion.Pending.Enqueue(member);
            }
        }

        /// <summary>
        /// Follows a delegate value a call produces: the <c>return</c> expressions of the called member's
        /// body, or its expression body, are classified the same way an assigned value is, with the same
        /// visited set and the same bound. Only one call is followed; a value that another call produces,
        /// a conditional, an element access and a member outside the scope stay unresolved and are
        /// recorded as limits, because reading any of them as an absence is what the walk must not do.
        /// </summary>
        private void FollowProducedValue(
            InvocationExpressionSyntax produced,
            IMethodSymbol called,
            DelegateValueOrigin origin,
            DelegateExpansion expansion)
        {
            if (origin == DelegateValueOrigin.Return)
            {
                _limits.Add(ReachabilityLimit.DelegateReceiverNotInScope(
                    $"`{produced}` at {SourceSetAnalysis.Describe(produced)}: the delegate value it hands out is the result "
                    + "of another call, which this walk does not follow"));
                return;
            }

            var body = TryGetBodyScope(called);
            if (body is null)
            {
                _limits.Add(ReachabilityLimit.DelegateReceiverNotInScope(
                    $"`{produced}` at {SourceSetAnalysis.Describe(produced)}: the called member `{called.Name}` has no body "
                    + "inside the declared scope, so the delegate value it returns is not followed"));
                return;
            }

            var returned = ReturnedValues(body);
            if (returned.Count == 0)
            {
                _limits.Add(ReachabilityLimit.DelegateReceiverNotInScope(
                    $"`{produced}` at {SourceSetAnalysis.Describe(produced)}: the called member `{called.Name}` has no return "
                    + "statement inside the declared scope, so the delegate value it returns is not followed"));
                return;
            }

            foreach (var value in returned)
            {
                ClassifyDelegateValue(value, called, DelegateValueOrigin.Return, expansion);
            }
        }

        /// <summary>
        /// Values a member body returns: the expression of an expression body, or every <c>return</c>
        /// expression of a block body. Returns written inside a local function or a lambda of that body
        /// belong to those scopes, not to the member.
        /// </summary>
        private static IReadOnlyList<ExpressionSyntax> ReturnedValues(SyntaxNode body)
        {
            if (body is ExpressionSyntax expression)
            {
                return new[] { expression };
            }

            return MethodBodyScope.DirectNodes(body)
                .OfType<ReturnStatementSyntax>()
                .Select(statement => statement.Expression)
                .OfType<ExpressionSyntax>()
                .ToList();
        }

        /// <summary>
        /// Records a value the expansion cannot resolve. An assignment that does not bind at all is a
        /// diagnostic; a value a member returns is a limit, because that call site is not visible to any
        /// other rule and a limit makes the walk undecided instead of absent.
        /// </summary>
        private void RecordUnresolvedValue(ExpressionSyntax value, ISymbol member, DelegateValueOrigin origin)
        {
            if (origin == DelegateValueOrigin.Return)
            {
                _limits.Add(ReachabilityLimit.DelegateReceiverNotInScope(
                    $"`{value}` at {SourceSetAnalysis.Describe(value)}: the delegate value returned by `{member.Name}` is not "
                    + "a delegate member of the declared scope, so the targets it can hold are not followed"));
                return;
            }

            _diagnostics.Add(
                $"{SourceSetAnalysis.Describe(value)}: delegate target assigned to `{member.Name}` does not resolve; "
                + "reachability is undecidable from here");
        }

        /// <summary>
        /// Value assigned to a delegate member that is a method group taken from another delegate's
        /// invocation member (<c>Func&lt;T&gt; copy = _seam.Invoke;</c>). The delegate it creates calls
        /// whatever the receiver holds at that moment, so the receiver joins the same expansion the call
        /// <c>_seam()</c> uses. A receiver that is not a delegate member of the scope is recorded as a
        /// limit naming the construct: an ordinary edge to the metadata <c>Invoke</c> instead ended the
        /// walk with a decided "not reachable" for a delegate that is invoked at run time.
        /// </summary>
        private void FollowInvocationMemberReceiver(
            ExpressionSyntax value,
            SemanticModel valueModel,
            DelegateExpansion expansion)
        {
            if (ReceiverMember(valueModel, value) is { } receiver)
            {
                EnqueueDelegateMember(expansion, receiver);
                return;
            }

            _limits.Add(ReachabilityLimit.DelegateReceiverNotInScope(DescribeUnfollowedInvocation(value)));
        }

        /// <summary>
        /// Values the member receives inside the declared scope: the right side of every assignment that
        /// targets it, plus the initializer of its own declaration (a local, a field or an auto-property,
        /// whose name is not a reference site and would otherwise be invisible).
        /// </summary>
        private IEnumerable<ExpressionSyntax> AssignedValues(ISymbol member)
        {
            foreach (var reference in ReferencesNamed(member.Name))
            {
                if (reference.Symbol is not null
                    && SymbolEqualityComparer.Default.Equals(reference.Symbol, member)
                    && AssignedValue(reference.Node) is { } assigned)
                {
                    yield return assigned;
                }
            }

            foreach (var declaration in member.DeclaringSyntaxReferences)
            {
                var node = declaration.GetSyntax();
                if (_analysis.FileFor(node.SyntaxTree) is null)
                {
                    continue;
                }

                var initializer = node switch
                {
                    VariableDeclaratorSyntax declarator => declarator.Initializer?.Value,
                    PropertyDeclarationSyntax property => property.Initializer?.Value,
                    _ => null,
                };
                if (initializer is not null)
                {
                    yield return initializer;
                }
            }
        }

        private void ReportDelegateBound(ISymbol invokedMember)
        {
            if (_delegateBoundReported)
            {
                return;
            }

            _delegateBoundReported = true;
            _diagnostics.Add(
                $"{invokedMember.Name}: reachability walk stopped after {MaxVisitedDelegateMembers} delegate members; "
                + "the delegate graph is larger than the declared analysis bound");
        }

        /// <summary>
        /// Limit text of a delegate member the walk cannot follow. The invoked member names its call
        /// site, a member reached from it names its source: claiming "no assignment in the scope" for a
        /// member that is assigned but only leads to a value registered elsewhere would be false.
        /// </summary>
        private static string DescribeUnfollowedMember(ISymbol member, ISymbol invokedMember, InvocationExpressionSyntax invocation)
        {
            if (SymbolEqualityComparer.Default.Equals(member, invokedMember))
            {
                return $"`{member.Name}` invoked at {SourceSetAnalysis.Describe(invocation)} has no assignment inside the declared scope; "
                    + "targets registered outside the scope are not followed";
            }

            return $"`{member.Name}`, reached from the invoked delegate `{invokedMember.Name}` at {SourceSetAnalysis.Describe(invocation)}, "
                + "has no assignment inside the declared scope; targets registered outside the scope are not followed";
        }

        /// <summary>
        /// Limit text of a delegate call the walk cannot attribute to a delegate member of the scope. The
        /// construct and its site are named, because this limit says nothing about a member the target was
        /// registered on: the call site itself is what could not be resolved.
        /// </summary>
        private static string DescribeUnfollowedInvocation(SyntaxNode node) =>
            $"`{node}` at {SourceSetAnalysis.Describe(node)}: the invoked delegate is not a delegate member of the "
            + "declared scope, so the targets it can hold are not followed";

        private static bool IsEmptyDelegateValue(SyntaxNode value) => value switch
        {
            LiteralExpressionSyntax literal => literal.IsKind(SyntaxKind.NullLiteralExpression)
                || literal.IsKind(SyntaxKind.DefaultLiteralExpression),
            DefaultExpressionSyntax => true,
            _ => false,
        };

        private IReadOnlyList<SourceReference> ReferencesNamed(string name)
        {
            if (_referencesByName.TryGetValue(name, out var cached))
            {
                return cached;
            }

            var references = _analysis.FindReferences(name);
            _referencesByName.Add(name, references);
            return references;
        }

        /// <summary>
        /// Value the member receives at a reference site: the right side of an assignment whose target is
        /// the member. Every other site, including a read of the member, defines no target — reading a
        /// member as a value (the initializer of another member, <c>var copy = _callback;</c>) used to
        /// return the scanned name itself, which re-enqueued the member being expanded forever.
        /// </summary>
        private static ExpressionSyntax? AssignedValue(SimpleNameSyntax node)
        {
            SyntaxNode? current = node;
            while (current?.Parent is not null)
            {
                var parent = current.Parent;
                switch (parent)
                {
                    case AssignmentExpressionSyntax assignment:
                        return assignment.Left.Span.Contains(node.Span) ? assignment.Right : null;
                    case StatementSyntax or MemberDeclarationSyntax or LambdaExpressionSyntax or AnonymousMethodExpressionSyntax:
                        return null;
                }

                current = parent;
            }

            return null;
        }

        private static ITypeSymbol? DelegateTypeOf(ISymbol? symbol) => symbol switch
        {
            IFieldSymbol field => DelegateType(field.Type),
            IPropertySymbol property => DelegateType(property.Type),
            ILocalSymbol local => DelegateType(local.Type),
            IParameterSymbol parameter => DelegateType(parameter.Type),
            _ => null,
        };

        private static ITypeSymbol? DelegateType(ITypeSymbol type) => type.TypeKind == TypeKind.Delegate ? type : null;

        private SyntaxNode? TryGetBodyScope(IMethodSymbol method)
        {
            foreach (var reference in MethodBodyScope.DeclaredForm(method).DeclaringSyntaxReferences)
            {
                var node = reference.GetSyntax();
                if (_analysis.FileFor(node.SyntaxTree) is null)
                {
                    continue;
                }

                return node switch
                {
                    MethodDeclarationSyntax declaration => (SyntaxNode?)declaration.Body ?? declaration.ExpressionBody?.Expression,
                    LocalFunctionStatementSyntax localFunction => (SyntaxNode?)localFunction.Body ?? localFunction.ExpressionBody?.Expression,
                    AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody?.Expression,
                    _ => null,
                };
            }

            return null;
        }

        /// <summary>Deferred scopes written directly in a scope body, without entering nested deferred scopes.</summary>
        private static IEnumerable<SyntaxNode> DeferredScopes(SyntaxNode root) =>
            MethodBodyScope.DirectNodes(root).Where(MethodBodyScope.IsDeferredScope);

        /// <summary>Target a delegate member can hold: an assigned method or an assigned lambda body.</summary>
        private sealed record DelegateTarget(IMethodSymbol? Method, SyntaxNode? Body)
        {
            public static DelegateTarget FromMethod(IMethodSymbol method) => new(method, null);

            public static DelegateTarget FromBody(SyntaxNode body) => new(null, body);
        }

        /// <summary>
        /// Targets and pending members of one delegate expansion. The visited set and the bound belong to
        /// the expansion, so a value followed from a call joins the same graph and cannot extend it
        /// without limit.
        /// </summary>
        private sealed class DelegateExpansion
        {
            public List<DelegateTarget> Targets { get; } = new();

            public Queue<ISymbol> Pending { get; } = new();

            public HashSet<ISymbol> Visited { get; } = new(SymbolEqualityComparer.Default);
        }

        /// <summary>
        /// Where a delegate value the expansion receives is written. Only the answer for a value the scope
        /// cannot resolve depends on it: an assignment is a call site that did not bind at all, while a
        /// returned value is a construct no other rule of a check can see.
        /// </summary>
        private enum DelegateValueOrigin
        {
            /// <summary>Right side of an assignment to the expanded member, or its declaration initializer.</summary>
            Assignment,

            /// <summary>Value a member returns, reached through the call the assignment is written with.</summary>
            Return,
        }

        private readonly SourceSetAnalysis _analysis;
        private readonly string _targetMemberName;
        private readonly Func<ISymbol, bool> _isTarget;
        private readonly List<string> _diagnostics = new();
        private readonly List<ReachabilityLimit> _limits = new();
        private readonly Dictionary<string, IReadOnlyList<SourceReference>> _referencesByName = new(StringComparer.Ordinal);
        private readonly HashSet<ISymbol> _inspectedDelegateMembers = new(SymbolEqualityComparer.Default);
        private int _visitedDelegateMembers;
        private bool _delegateBoundReported;
    }
}
