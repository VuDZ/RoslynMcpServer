namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// In-memory mirror of the cached-snapshot contract: an async entry that takes a non-recursive
/// <see cref="System.Threading.SemaphoreSlim"/> and calls a private helper, a public synchronous entry
/// that takes the same lock, and the raw entry after disk-sync. The same text is used by two
/// independent checks of the same failure: the static corpus proves that
/// <see cref="AsyncCallsPublicSync"/> is detected, and the runtime witness compiles it and proves that
/// the mutation really hangs.
/// <para>
/// The text is normalized once, before any mutation anchor is applied: a raw string literal takes the
/// line endings of the working copy, so on a CRLF checkout an anchor written with <c>\n</c> would not
/// match and the mutation would silently not apply.
/// </para>
/// </summary>
internal static class SanitizedEntryDemoSource
{
    public const string Path = "Demo/DemoManager.cs";
    public const string NamespaceName = "Demo";
    public const string TypeName = "DemoManager";
    public const string WitnessTypeName = "Demo.DemoWitness";
    public const string WitnessMethodName = "RunAsync";

    /// <summary>Value the correct asynchronous entry returns; the runtime witness asserts this result.</summary>
    public const int ExpectedValue = 42;

    private const string AsyncHelperCall = "            return GetOrCreateSanitizedPublishedSolution();";

    /// <summary>Correct source: the asynchronous entry calls the private helper, never the sync entry.</summary>
    public static string Baseline { get; } = SourceFile.Normalize(RawBaseline);

    /// <summary>
    /// Compilable mutation of the contract: the async entry calls the public synchronous entry while it
    /// already holds the non-recursive lock. Statically detected by the corpus, and a real hang at run time.
    /// </summary>
    public static string AsyncCallsPublicSync { get; } = Mutate(AsyncHelperCall, "            return GetSanitizedPublishedSolution();");

    /// <summary>
    /// Replaces the first occurrence of <paramref name="anchor"/> in the normalized baseline; a missing
    /// anchor fails instead of silently leaving the source unmutated. The async entry is written before
    /// the synchronous one, so a shared statement is mutated in the async entry.
    /// </summary>
    private static string Mutate(string anchor, string replacement)
    {
        var index = Baseline.IndexOf(anchor, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new InvalidOperationException($"Demo source anchor was not found: `{anchor}`");
        }

        return string.Concat(Baseline.AsSpan(0, index), replacement, Baseline.AsSpan(index + anchor.Length));
    }

    private const string RawBaseline = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Demo;

        internal sealed class DemoManager
        {
            private readonly SemaphoreSlim _workspaceLock = new(1, 1);
            private readonly SemaphoreSlim _otherLock = new(1, 1);
            private Snapshot? _solution = new Snapshot(42, "published");

            public async Task<Snapshot?> GetSanitizedPublishedSolutionAsync(CancellationToken cancellationToken = default)
            {
                await _workspaceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    return GetOrCreateSanitizedPublishedSolution();
                }
                finally
                {
                    _workspaceLock.Release();
                }
            }

            public Snapshot? GetSanitizedPublishedSolution()
            {
                _workspaceLock.Wait();
                try
                {
                    return GetOrCreateSanitizedPublishedSolution();
                }
                finally
                {
                    _workspaceLock.Release();
                }
            }

            public async Task<Snapshot?> GetPublishedSolutionAfterDiskSyncAsync(CancellationToken cancellationToken = default)
            {
                await _workspaceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    return _solution;
                }
                finally
                {
                    _workspaceLock.Release();
                }
            }

            private Snapshot? GetOrCreateSanitizedPublishedSolution()
            {
                return _solution;
            }
        }

        internal static class DemoWitness
        {
            public static async Task<int> RunAsync()
            {
                var manager = new DemoManager();
                var snapshot = await manager.GetSanitizedPublishedSolutionAsync().ConfigureAwait(false);
                return snapshot?.Value ?? -1;
            }
        }

        internal sealed class Snapshot
        {
            public Snapshot(int value, string label)
            {
                Value = value;
                Label = label;
            }

            public int Value { get; }

            public string Label { get; }

            public override string ToString() => $"snapshot:{Label}:{Value}";
        }

        internal sealed class PrepareOutcome
        {
            public IReadOnlyList<int> Results { get; } = new List<int>();
        }
        """;
}
