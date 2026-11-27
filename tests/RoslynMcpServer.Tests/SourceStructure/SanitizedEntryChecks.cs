using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// Contract of the three entries into the cached sanitized snapshot, checked on the semantic model of
/// the declared scope:
/// <list type="bullet">
/// <item>the async entry calls the private helper directly and never reaches the public synchronous
/// entry, which would re-enter the non-recursive <c>_workspaceLock</c> and hang forever;</item>
/// <item>the synchronous entry takes that same lock field, reads the snapshot inside the protected
/// <c>try</c>, releases the lock in its <c>finally</c> and performs no sync-over-async member
/// access;</item>
/// <item>the raw entry returns the published solution field and touches neither the sanitizer nor the
/// publication path.</item>
/// </list>
/// <para>
/// The lock is the field symbol, not the name <c>_workspaceLock</c>: another <c>SemaphoreSlim</c> field
/// does not satisfy the requirement, and a same-named member of another type does not satisfy it
/// either. Call forms are classified: a direct call, a reachable path through other methods, a method
/// group and a <c>nameof</c> mention are reported separately, and all of them are violations because a
/// mention can be turned into a call later. Input compilation errors, unresolved calls, ambiguous
/// targets and out-of-scope conditional directives are returned as violations, never as success. A
/// reachability walk that stopped on a delegate call it could not attribute to a delegate member of the
/// scope is a violation as well: no other rule of this check can see such a call site.
/// </para>
/// </summary>
internal static class SanitizedEntryChecks
{
    public const string AsyncEntry = "GetSanitizedPublishedSolutionAsync";
    public const string SyncEntry = "GetSanitizedPublishedSolution";
    public const string Helper = "GetOrCreateSanitizedPublishedSolution";
    public const string RawEntry = "GetPublishedSolutionAfterDiskSyncAsync";
    public const string WorkspaceLock = "_workspaceLock";
    public const string PublishedSolution = "_solution";

    public static readonly string[] RawForbiddenNames =
    {
        Helper,
        "RemoveUnresolvedAnalyzers",
        "SetPublishedSolution",
    };

    /// <summary>
    /// Violations of the async entry and the synchronous entry; an empty list means the contract holds.
    /// Scope diagnostics and unresolved call sites are returned as violations too.
    /// </summary>
    public static IReadOnlyList<string> VerifySanitizedEntries(SourceSetAnalysis analysis, SourceTarget target)
    {
        var violations = new List<string>(analysis.InputDiagnostics);
        var type = analysis.FindType(target.NamespaceName, target.TypeName, violations);
        if (type is null)
        {
            return violations;
        }

        var asyncEntry = analysis.FindDeclaredMethod(type, AsyncEntry, violations);
        var syncEntry = analysis.FindDeclaredMethod(type, SyncEntry, violations);
        if (asyncEntry is not null)
        {
            VerifyAsyncEntry(analysis, asyncEntry, syncEntry, violations);
        }

        if (syncEntry is not null)
        {
            VerifySyncEntry(analysis, syncEntry, violations);
        }

        return violations;
    }

    /// <summary>Violations of the raw entry after disk-sync; an empty list means the contract holds.</summary>
    public static IReadOnlyList<string> VerifyRawPublishedEntry(SourceSetAnalysis analysis, SourceTarget target)
    {
        var violations = new List<string>(analysis.InputDiagnostics);
        var type = analysis.FindType(target.NamespaceName, target.TypeName, violations);
        if (type is null)
        {
            return violations;
        }

        var rawEntry = analysis.FindDeclaredMethod(type, RawEntry, violations);
        if (rawEntry is null)
        {
            return violations;
        }

        var (body, error) = MethodBodyScope.Create(analysis, rawEntry);
        if (body is null)
        {
            violations.Add(error!);
            return violations;
        }

        violations.AddRange(body.UnresolvedCalls());
        var publishedSolution = Field(type, PublishedSolution);
        var returnsPublishedSolution = publishedSolution is not null
            && body.Nodes.OfType<ReturnStatementSyntax>()
                .Select(statement => statement.Expression)
                .Any(expression => expression is not null && IsFieldReference(body, expression, publishedSolution));
        if (!returnsPublishedSolution)
        {
            violations.Add($"{body.Display}: body does not return {PublishedSolution} — the raw snapshot was replaced with a sanitized one");
        }

        foreach (var name in RawForbiddenNames)
        {
            var references = body.ReferencesNamed(name);
            if (references.Count > 0)
            {
                violations.Add($"{body.Display}: forbidden reference to {name} at {references[0].FilePath}:{references[0].Line}");
            }
        }

        return violations;
    }

    private static void VerifyAsyncEntry(
        SourceSetAnalysis analysis,
        IMethodSymbol asyncEntry,
        IMethodSymbol? syncEntry,
        List<string> violations)
    {
        var (body, error) = MethodBodyScope.Create(analysis, asyncEntry);
        if (body is null)
        {
            violations.Add(error!);
            return;
        }

        violations.AddRange(body.UnresolvedCalls());
        VerifyHelperCall(analysis, body, violations);

        if (syncEntry is null)
        {
            return;
        }

        ReportMentions(body, syncEntry, violations);
        var reachability = ReachabilityAnalysis.FindPath(
            analysis,
            asyncEntry,
            syncEntry.Name,
            symbol => SymbolEqualityComparer.Default.Equals(symbol.OriginalDefinition, syncEntry.OriginalDefinition));
        violations.AddRange(reachability.Diagnostics);
        if (reachability.IsReachable)
        {
            violations.Add(
                $"{body.Display}: reaches the public synchronous entry {SyncEntry} while holding the non-recursive "
                + $"{WorkspaceLock} — deadlock; path: {reachability.DescribePath()}");
        }

        ReportUndecidedInvocations(body, reachability, violations);
        ReportBlockingMembers(analysis, body, violations);
    }

    /// <summary>
    /// Refuses a walk that stopped on a delegate call it could not attribute to a delegate member of the
    /// scope. Such a call site is invisible to every other rule of this check — the target is registered
    /// elsewhere and the receiver is computed elsewhere — so a green verdict on it would be unproven
    /// rather than proven. A limit of the other kind, a delegate member merely reached from the walk, keeps
    /// its verdict: the production test seam is registered outside the declared scope by design, and the
    /// walk did follow the call to the member (see <see cref="ReachabilityLimitKind"/>).
    /// </summary>
    private static void ReportUndecidedInvocations(
        MethodBodyScope body,
        ReachabilityResult reachability,
        List<string> violations)
    {
        foreach (var limit in reachability.Limits.Where(limit => limit.Kind == ReachabilityLimitKind.DelegateReceiverNotInScope))
        {
            violations.Add($"{body.Display}: reachability is undecided — {limit.Text}");
        }
    }

    private static void VerifyHelperCall(SourceSetAnalysis analysis, MethodBodyScope body, List<string> violations)
    {
        var helper = body.Method.ContainingType.GetMembers(Helper).OfType<IMethodSymbol>().ToList();
        if (helper.Count == 0)
        {
            violations.Add($"{body.Display}: the private helper {Helper} was not found in {body.Method.ContainingType.ToDisplayString()}");
            return;
        }

        var callsHelper = body.Invocations.Any(invocation =>
            body.CalledMethod(invocation) is { } called
            && helper.Any(candidate => SymbolEqualityComparer.Default.Equals(called.OriginalDefinition, candidate.OriginalDefinition)));
        if (!callsHelper)
        {
            violations.Add($"{body.Display}: no direct call to {Helper}() in the method body");
        }
    }

    private static void VerifySyncEntry(SourceSetAnalysis analysis, IMethodSymbol syncEntry, List<string> violations)
    {
        var (body, error) = MethodBodyScope.Create(analysis, syncEntry);
        if (body is null)
        {
            violations.Add(error!);
            return;
        }

        violations.AddRange(body.UnresolvedCalls());
        ReportBlockingMembers(analysis, body, violations);

        var lockField = Field(syncEntry.ContainingType, WorkspaceLock);
        if (lockField is null)
        {
            violations.Add($"{body.Display}: field {WorkspaceLock} was not found in {syncEntry.ContainingType.ToDisplayString()}");
            return;
        }

        if (!IsSemaphore(analysis, lockField))
        {
            violations.Add(
                $"{body.Display}: {WorkspaceLock} is of type {lockField.Type.ToDisplayString()}, not SemaphoreSlim; "
                + "the lock contract is stated for a non-recursive SemaphoreSlim");
        }

        var waits = body.Invocations
            .Where(invocation => IsCallOn(body, invocation, lockField, "Wait"))
            .ToList();
        if (waits.Count == 0)
        {
            var otherWaits = body.Invocations
                .Where(invocation => CalledName(body, invocation) == "Wait")
                .Select(invocation => (invocation.Expression as MemberAccessExpressionSyntax)?.Expression.ToString()
                    ?? invocation.Expression.ToString())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            violations.Add(otherWaits.Count == 0
                ? $"{body.Display}: no call to {WorkspaceLock}.Wait() in the method body"
                : $"{body.Display}: Wait() is called on {string.Join(", ", otherWaits)}, not on {WorkspaceLock}");
        }

        var protectedTry = FindProtectedTry(body, lockField);
        if (protectedTry is null)
        {
            violations.Add($"{body.Display}: no call to {WorkspaceLock}.Release() in the finally of the protected try");
            return;
        }

        if (waits.Count > 0 && !IsLockTakenBeforeTry(body, protectedTry, waits))
        {
            violations.Add($"{body.Display}: {WorkspaceLock}.Wait() must be taken before the protected try");
        }

        var helper = syncEntry.ContainingType.GetMembers(Helper).OfType<IMethodSymbol>().ToList();
        var helperInsideTry = MethodBodyScope.DirectNodes(protectedTry.Block)
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation => body.CalledMethod(invocation))
            .Any(called => called is not null
                && helper.Any(candidate => SymbolEqualityComparer.Default.Equals(called.OriginalDefinition, candidate.OriginalDefinition)));
        if (!helperInsideTry)
        {
            violations.Add($"{body.Display}: the snapshot read ({Helper}()) must happen inside the protected try");
        }
    }

    /// <summary>
    /// Reference forms of the public synchronous entry inside the async entry that are not calls: a
    /// method group can be invoked through a delegate later, and a <c>nameof</c> mention is reported as
    /// the weaker form it is. A call is reported exactly once, by the reachability query with its path,
    /// so the form is read from the invocation the name belongs to: <c>this.M()</c> and <c>x?.M()</c>
    /// are calls too, and looking only at the parent of the name reported them a second time as a
    /// mention.
    /// </summary>
    private static void ReportMentions(MethodBodyScope body, IMethodSymbol syncEntry, List<string> violations)
    {
        foreach (var reference in body.ReferencesTo(syncEntry))
        {
            if (IsInvokedMember(reference.Node))
            {
                continue;
            }

            var enclosingInvocation = reference.Node.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault();
            var form = enclosingInvocation is not null && MethodBodyScope.IsNameOf(enclosingInvocation)
                ? "nameof mention"
                : "method group";
            violations.Add(
                $"{body.Display}: {form} of the public synchronous entry {SyncEntry} at {reference.FilePath}:{reference.Line} — "
                + $"re-entering the non-recursive {WorkspaceLock} is possible: deadlock");
        }
    }

    /// <summary>
    /// True when the name is the invoked member itself: a bare name (<c>M()</c>), the name of a member
    /// access (<c>this.M()</c>, <c>x.M()</c>) or the name of a conditional-access member binding
    /// (<c>x?.M()</c>), each being the expression of the invocation it is called through.
    /// </summary>
    private static bool IsInvokedMember(SimpleNameSyntax node)
    {
        ExpressionSyntax expression = node.Parent switch
        {
            MemberAccessExpressionSyntax access when access.Name == node => access,
            MemberBindingExpressionSyntax binding when binding.Name == node => binding,
            _ => node,
        };

        return expression.Parent is InvocationExpressionSyntax invocation && invocation.Expression == expression;
    }

    private static void ReportBlockingMembers(SourceSetAnalysis analysis, MethodBodyScope body, List<string> violations)
    {
        var rules = BlockingMemberRules.For(analysis);
        foreach (var node in body.Nodes.OfType<SimpleNameSyntax>())
        {
            var symbol = body.SymbolOf(node);
            var rule = symbol is null
                ? rules.ClassifyUnresolvedName(node.Identifier.ValueText)
                : rules.Classify(symbol);
            if (rule is null)
            {
                continue;
            }

            var bound = symbol is null
                ? string.Empty
                : $" (`{node}` resolves to {symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)})";
            violations.Add($"{body.Display}: sync-over-async — {rule} at {SourceSetAnalysis.Describe(node)}{bound}");
        }
    }

    /// <summary>
    /// The protected <c>try</c> is the direct statement of the body whose <c>finally</c> releases the
    /// lock field; a <c>finally</c> that releases another lock does not protect this one.
    /// </summary>
    private static TryStatementSyntax? FindProtectedTry(MethodBodyScope body, IFieldSymbol lockField) =>
        (body.Body as BlockSyntax)?.Statements
            .OfType<TryStatementSyntax>()
            .FirstOrDefault(statement => statement.Finally is not null
                && MethodBodyScope.DirectNodes(statement.Finally.Block)
                    .OfType<InvocationExpressionSyntax>()
                    .Any(invocation => IsCallOn(body, invocation, lockField, "Release")));

    private static bool IsLockTakenBeforeTry(
        MethodBodyScope body,
        TryStatementSyntax protectedTry,
        IReadOnlyList<InvocationExpressionSyntax> waits)
    {
        var statements = ((BlockSyntax)body.Body).Statements;
        var tryIndex = statements.IndexOf(protectedTry);
        if (tryIndex <= 0)
        {
            return false;
        }

        var beforeTry = statements.Take(tryIndex).ToList();
        return waits.Any(wait => beforeTry.Any(statement => statement.Span.Contains(wait.Span)));
    }

    private static bool IsCallOn(MethodBodyScope body, InvocationExpressionSyntax invocation, IFieldSymbol field, string methodName)
    {
        if (CalledName(body, invocation) != methodName)
        {
            return false;
        }

        var receiver = (invocation.Expression as MemberAccessExpressionSyntax)?.Expression;
        return receiver is not null && IsFieldReference(body, receiver, field);
    }

    private static string? CalledName(MethodBodyScope body, InvocationExpressionSyntax invocation) =>
        body.CalledMethod(invocation)?.Name;

    private static bool IsFieldReference(MethodBodyScope body, ExpressionSyntax expression, IFieldSymbol field) =>
        body.SymbolOf(expression) is { } symbol && SymbolEqualityComparer.Default.Equals(symbol, field);

    private static IFieldSymbol? Field(INamedTypeSymbol type, string name) =>
        type.GetMembers(name).OfType<IFieldSymbol>().FirstOrDefault();

    private static bool IsSemaphore(SourceSetAnalysis analysis, IFieldSymbol field)
    {
        var semaphore = analysis.Compilation.GetTypeByMetadataName("System.Threading.SemaphoreSlim");
        if (semaphore is null || SymbolEqualityComparer.Default.Equals(field.Type, semaphore))
        {
            return semaphore is not null;
        }

        for (var current = field.Type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, semaphore))
            {
                return true;
            }
        }

        return false;
    }
}
