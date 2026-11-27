using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// Blocking-member rules of the sync-over-async contract, resolved to symbols of the analyzed
/// compilation. Blocking a pool thread is the failure the contract prevents, so the rules are split by
/// what they actually prove:
/// <list type="bullet">
/// <item><c>Task&lt;T&gt;.Result</c> and <c>ValueTask&lt;T&gt;.Result</c> are matched by symbol, so a
/// user-defined <c>Result</c> property (or <c>prepared.Results</c>) is not a violation.</item>
/// <item><c>Task.Wait</c>, <c>Task.WaitAll</c> and <c>Task.WaitAny</c> are matched by symbol as well;
/// <c>SemaphoreSlim.Wait()</c> is a different type and stays allowed.</item>
/// <item><c>GetAwaiter</c> stays a name-based prohibition: obtaining an awaiter is a separate
/// assertion from blocking on it, and the contract forbids the acquisition outright. Switching to
/// semantics must not weaken it, so any member named <c>GetAwaiter</c> is reported.</item>
/// <item><c>GetResult</c> on an awaiter type is the blocking form and is matched by symbol.</item>
/// </list>
/// </summary>
internal sealed class BlockingMemberRules
{
    private BlockingMemberRules(
        ISymbol? taskResult,
        ISymbol? valueTaskResult,
        HashSet<ISymbol> taskWaits,
        HashSet<ISymbol> awaiterGetResults)
    {
        _taskResult = taskResult;
        _valueTaskResult = valueTaskResult;
        _taskWaits = taskWaits;
        _awaiterGetResults = awaiterGetResults;
    }

    public static BlockingMemberRules For(SourceSetAnalysis analysis)
    {
        var compilation = analysis.Compilation;
        var task = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
        var taskOfT = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
        var valueTaskOfT = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");

        var taskWaits = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var getResults = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var type in new[] { task, taskOfT })
        {
            if (type is null)
            {
                continue;
            }

            foreach (var member in type.GetMembers("Wait"))
            {
                taskWaits.Add(member.OriginalDefinition);
            }

            foreach (var member in type.GetMembers("WaitAll").Concat(type.GetMembers("WaitAny")))
            {
                taskWaits.Add(member.OriginalDefinition);
            }
        }

        foreach (var awaiterName in AwaiterMetadataNames)
        {
            var awaiter = compilation.GetTypeByMetadataName(awaiterName);
            if (awaiter is null)
            {
                continue;
            }

            foreach (var member in awaiter.GetMembers("GetResult"))
            {
                getResults.Add(member.OriginalDefinition);
            }
        }

        return new BlockingMemberRules(
            taskOfT?.GetMembers("Result").FirstOrDefault()?.OriginalDefinition,
            valueTaskOfT?.GetMembers("Result").FirstOrDefault()?.OriginalDefinition,
            taskWaits,
            getResults);
    }

    /// <summary>
    /// Rule for a blocking member name that did not bind. The name stays significant when the symbol is
    /// missing: an unresolved <c>Result</c> must not be read as "no blocking member here".
    /// </summary>
    public string? ClassifyUnresolvedName(string memberName) =>
        BlockingNames.Contains(memberName)
            ? $"unresolved blocking member name `{memberName}`"
            : null;

    /// <summary>Name of the violated rule, or null when the member is allowed in a synchronous entry.</summary>
    public string? Classify(ISymbol symbol)
    {
        if (string.Equals(symbol.Name, "GetAwaiter", StringComparison.Ordinal))
        {
            return "awaiter acquisition (`GetAwaiter`)";
        }

        var original = symbol.OriginalDefinition;
        if (_awaiterGetResults.Contains(original))
        {
            return "blocking awaiter call (`GetResult`)";
        }

        if (_taskWaits.Contains(original))
        {
            return "blocking wait on a task (`Wait`/`WaitAll`/`WaitAny`)";
        }

        if ((_taskResult is not null && SymbolEqualityComparer.Default.Equals(original, _taskResult))
            || (_valueTaskResult is not null && SymbolEqualityComparer.Default.Equals(original, _valueTaskResult)))
        {
            return "task result read (`Result`)";
        }

        return null;
    }

    /// <summary>Member names that block a thread or acquire an awaiter; unresolved uses are reported too.</summary>
    private static readonly HashSet<string> BlockingNames = new(StringComparer.Ordinal)    {
        "Result",
        "GetAwaiter",
        "GetResult",
        "Wait",
        "WaitAll",
        "WaitAny",
    };

    /// <summary>Awaiter types whose <c>GetResult</c> blocks the calling thread (nested types use <c>+</c>).</summary>
    private static readonly string[] AwaiterMetadataNames =
    {
        "System.Runtime.CompilerServices.TaskAwaiter",
        "System.Runtime.CompilerServices.TaskAwaiter`1",
        "System.Runtime.CompilerServices.ValueTaskAwaiter",
        "System.Runtime.CompilerServices.ValueTaskAwaiter`1",
        "System.Runtime.CompilerServices.ConfiguredTaskAwaitable+ConfiguredTaskAwaiter",
        "System.Runtime.CompilerServices.ConfiguredTaskAwaitable`1+ConfiguredTaskAwaiter",
        "System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable+ConfiguredValueTaskAwaiter",
        "System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable`1+ConfiguredValueTaskAwaiter",
    };

    private readonly ISymbol? _taskResult;
    private readonly ISymbol? _valueTaskResult;
    private readonly HashSet<ISymbol> _taskWaits;
    private readonly HashSet<ISymbol> _awaiterGetResults;
}
