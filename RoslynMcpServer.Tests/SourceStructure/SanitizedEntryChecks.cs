using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// Shape checks for the three entries into the cached sanitized snapshot: the async entry, the
/// synchronous retry callback and the raw entry after disk-sync. Only the immediate method body is
/// checked: comments, string literals and the contents of local functions and lambdas do not count
/// as calls. Symbol binding, transitive calls and behavior are out of scope here, so a user-defined
/// property with the exact name <c>Result</c> stays a limitation of this check.
/// </summary>
internal static class SanitizedEntryChecks
{
    private const string AsyncEntry = "GetSanitizedPublishedSolutionAsync";
    private const string SyncEntry = "GetSanitizedPublishedSolution";
    private const string Helper = "GetOrCreateSanitizedPublishedSolution";
    private const string RawEntry = "GetPublishedSolutionAfterDiskSyncAsync";
    private const string WorkspaceLock = "_workspaceLock";
    private const string PublishedSolution = "_solution";

    private static readonly string[] BlockingMembers = { "Result", "GetAwaiter" };

    private static readonly string[] RawForbiddenNames =
    {
        Helper,
        "RemoveUnresolvedAnalyzers",
        "SetPublishedSolution",
    };

    /// <summary>
    /// Contract violations of the async entry and the synchronous entry; an empty list means the
    /// contract holds. Parse diagnostics and a missing method are returned as violations too.
    /// </summary>
    public static IReadOnlyList<string> VerifySanitizedEntries(SourceTarget target)
    {
        var (type, error) = SourceMethodLocator.LocateType(target);
        if (error is not null)
        {
            return new[] { error };
        }

        var violations = new List<string>();
        var (asyncEntry, asyncError) = SourceMethodLocator.LocateMethod(type!, target.TypeName, AsyncEntry);
        if (asyncError is not null)
        {
            violations.Add(asyncError);
        }
        else
        {
            CollectAsyncEntryViolations(target.TypeName, asyncEntry!, violations);
        }

        var (syncEntry, syncError) = SourceMethodLocator.LocateMethod(type!, target.TypeName, SyncEntry);
        if (syncError is not null)
        {
            violations.Add(syncError);
        }
        else
        {
            CollectSyncEntryViolations(target.TypeName, syncEntry!, violations);
        }

        return violations;
    }

    /// <summary>Contract violations of the raw entry after disk-sync; an empty list means success.</summary>
    public static IReadOnlyList<string> VerifyRawPublishedEntry(SourceTarget target)
    {
        var (type, error) = SourceMethodLocator.LocateType(target);
        if (error is not null)
        {
            return new[] { error };
        }

        var (rawEntry, rawError) = SourceMethodLocator.LocateMethod(type!, target.TypeName, RawEntry);
        if (rawError is not null)
        {
            return new[] { rawError };
        }

        var display = $"{target.TypeName}.{RawEntry}";
        var scope = SourceMethodLocator.TryGetScope(rawEntry!);
        if (scope is null)
        {
            return new[] { $"{display}: method has no body to check" };
        }

        var violations = new List<string>();
        var returnsPublishedSolution = SourceMethodLocator.DirectNodes(scope)
            .OfType<ReturnStatementSyntax>()
            .Any(statement => IsPublishedSolutionReference(statement.Expression));
        if (!returnsPublishedSolution)
        {
            violations.Add($"{display}: body does not return {PublishedSolution} — the raw snapshot was replaced with a sanitized one");
        }

        foreach (var name in RawForbiddenNames)
        {
            var references = SourceMethodLocator.DirectNameReferences(scope, name);
            if (references.Count > 0)
            {
                violations.Add(
                    $"{display}: forbidden reference to {name} at line {SourceMethodLocator.LineOf(references[0])}");
            }
        }

        return violations;
    }

    private static void CollectAsyncEntryViolations(
        string typeName,
        MethodDeclarationSyntax method,
        List<string> violations)
    {
        var display = $"{typeName}.{AsyncEntry}";
        var scope = SourceMethodLocator.TryGetScope(method);
        if (scope is null)
        {
            violations.Add($"{display}: method has no body to check");
            return;
        }

        var callsHelper = SourceMethodLocator.DirectInvocations(scope)
            .Any(invocation => SourceMethodLocator.IsSelfCall(invocation, Helper));
        if (!callsHelper)
        {
            violations.Add($"{display}: no direct call to {Helper}() in the method body");
        }

        var syncReferences = SourceMethodLocator.DirectNameReferences(scope, SyncEntry);
        if (syncReferences.Count > 0)
        {
            var line = SourceMethodLocator.LineOf(syncReferences[0]);
            violations.Add(
                $"{display}: reference to the public synchronous entry {SyncEntry} at line {line}: "
                + $"re-entering the non-recursive {WorkspaceLock} — deadlock");
        }
    }

    private static void CollectSyncEntryViolations(
        string typeName,
        MethodDeclarationSyntax method,
        List<string> violations)
    {
        var display = $"{typeName}.{SyncEntry}";
        var scope = SourceMethodLocator.TryGetScope(method);
        if (scope is null)
        {
            violations.Add($"{display}: method has no body to check");
            return;
        }

        foreach (var member in BlockingMembers)
        {
            var references = SourceMethodLocator.DirectNameReferences(scope, member);
            if (references.Count > 0)
            {
                var line = SourceMethodLocator.LineOf(references[0]);
                violations.Add($"{display}: sync-over-async — member access {member} at line {line}");
            }
        }

        var invocations = SourceMethodLocator.DirectInvocations(scope).ToList();
        var protectedTry = FindProtectedTry(method);
        if (protectedTry is null)
        {
            violations.Add($"{display}: no call to {WorkspaceLock}.Release() in the finally of the protected try");
        }

        var waits = invocations
            .Where(invocation => SourceMethodLocator.InvokedMemberName(invocation) == "Wait")
            .ToList();
        var lockWaits = waits
            .Where(invocation => SourceMethodLocator.ReceiverText(invocation) == WorkspaceLock)
            .ToList();
        if (lockWaits.Count == 0)
        {
            violations.Add(waits.Count == 0
                ? $"{display}: no call to {WorkspaceLock}.Wait() in the method body"
                : $"{display}: Wait() is called on {DescribeReceivers(waits)}, not on {WorkspaceLock}");
        }
        else if (protectedTry is not null && !IsLockTakenBeforeTry(method, protectedTry, lockWaits))
        {
            violations.Add($"{display}: {WorkspaceLock}.Wait() must be taken before the protected try");
        }

        if (protectedTry is not null
            && !SourceMethodLocator.DirectInvocations(protectedTry.Block)
                .Any(invocation => SourceMethodLocator.IsSelfCall(invocation, Helper)))
        {
            violations.Add($"{display}: the snapshot read ({Helper}()) must happen inside the protected try");
        }
    }

    /// <summary>
    /// The protected <c>try</c> is the one whose <c>finally</c> releases <c>_workspaceLock</c>.
    /// </summary>
    private static TryStatementSyntax? FindProtectedTry(MethodDeclarationSyntax method) =>
        method.Body?.Statements
            .OfType<TryStatementSyntax>()
            .FirstOrDefault(statement => statement.Finally is not null
                && SourceMethodLocator.DirectInvocations(statement.Finally.Block)
                    .Any(invocation => SourceMethodLocator.IsMemberCall(invocation, WorkspaceLock, "Release")));

    private static bool IsLockTakenBeforeTry(
        MethodDeclarationSyntax method,
        TryStatementSyntax protectedTry,
        IReadOnlyList<InvocationExpressionSyntax> lockWaits)
    {
        var statements = method.Body!.Statements;
        var tryIndex = statements.IndexOf(protectedTry);
        if (tryIndex <= 0)
        {
            return false;
        }

        var beforeTry = statements.Take(tryIndex);
        return lockWaits.Any(wait => beforeTry.Any(statement => SourceMethodLocator.IsInside(wait, statement)));
    }

    private static bool IsPublishedSolutionReference(ExpressionSyntax? expression)
    {
        var text = expression?.ToString();
        return text == PublishedSolution || text == "this." + PublishedSolution;
    }

    private static string DescribeReceivers(IReadOnlyList<InvocationExpressionSyntax> invocations) =>
        string.Join(
            ", ",
            invocations
                .Select(invocation => SourceMethodLocator.ReceiverText(invocation) ?? "<no receiver>")
                .Distinct(StringComparer.Ordinal));
}
