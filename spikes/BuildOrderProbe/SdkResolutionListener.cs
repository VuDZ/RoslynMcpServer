using System.Diagnostics;
using System.Diagnostics.Tracing;

namespace BuildOrderProbe;

/// <summary>
/// Adds up the time MSBuild spends in SDK resolution in this process, per kind of step the probe is in
/// (<see cref="Phase"/>) and per resolver, from the Start and Stop events of MSBuild's event source.
/// </summary>
/// <remarks>
/// The events carry no duration: the callback runs synchronously on the thread that resolves, so the listener takes a
/// timestamp there and pairs Start with Stop per thread. CachedSdkResolverServiceResolveSdk wraps the resolution of
/// one SDK reference, the service's cache lookup included; SdkResolverResolveSdk wraps one resolver's attempt inside
/// it, and its Stop names the resolver. Every event of the source reaches the listener, conditions and globs too,
/// so a run with it is slower: such a run attributes time and is not compared with timed runs.
/// </remarks>
internal sealed class SdkResolutionListener : EventListener
{
    /// <summary>The kind of step the probe is in; set by the probe's thread before each step.</summary>
    public string Phase { get; set; } = "other";

    public int Resolutions { get; private set; }

    public int CachedResolutions { get; private set; }

    public IReadOnlyDictionary<string, double> ByPhase
    {
        get
        {
            lock (_lock)
            {
                return new SortedDictionary<string, double>(_byPhase, StringComparer.Ordinal);
            }
        }
    }

    public IReadOnlyDictionary<string, double> ByResolver
    {
        get
        {
            lock (_lock)
            {
                return new SortedDictionary<string, double>(_byResolver, StringComparer.Ordinal);
            }
        }
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == "Microsoft-Build")
        {
            EnableEvents(eventSource, EventLevel.Verbose, EventKeywords.All);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        switch (eventData.EventName)
        {
            case "CachedSdkResolverServiceResolveSdkStart":
                _references.Value!.Push(Stopwatch.GetTimestamp());
                break;
            case "CachedSdkResolverServiceResolveSdkStop" when _references.Value!.Count > 0:
            {
                var ms = Stopwatch.GetElapsedTime(_references.Value.Pop()).TotalMilliseconds;
                lock (_lock)
                {
                    _byPhase[Phase] = _byPhase.GetValueOrDefault(Phase) + ms;
                    Resolutions++;
                    if (eventData.Payload is [.., bool wasResultCached] && wasResultCached)
                    {
                        CachedResolutions++;
                    }
                }

                break;
            }
            case "SdkResolverResolveSdkStart":
                _resolvers.Value!.Push(Stopwatch.GetTimestamp());
                break;
            case "SdkResolverResolveSdkStop" when _resolvers.Value!.Count > 0:
            {
                var ms = Stopwatch.GetElapsedTime(_resolvers.Value.Pop()).TotalMilliseconds;
                var resolver = eventData.Payload is [string name, ..] ? name : "unknown";
                lock (_lock)
                {
                    _byResolver[resolver] = _byResolver.GetValueOrDefault(resolver) + ms;
                }

                break;
            }
        }
    }

    private readonly Lock _lock = new();
    private readonly Dictionary<string, double> _byPhase = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _byResolver = new(StringComparer.Ordinal);
    private readonly ThreadLocal<Stack<long>> _references = new(() => new Stack<long>());
    private readonly ThreadLocal<Stack<long>> _resolvers = new(() => new Stack<long>());
}
