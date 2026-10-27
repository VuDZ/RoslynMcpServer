using System.Collections.Concurrent;
using Microsoft.Build.Evaluation;

namespace BuildOrderProbe;

/// <summary>
/// Root evaluations of the whole reference closure ahead of the builds (<see cref="Models.LoadOrder.EvaluateClosure"/>
/// and <see cref="Models.LoadOrder.EvaluateClosurePreorder"/>). Every finished evaluation queues the projects it
/// references. Without traversal roots, worker threads take the most recently queued first, so the expansion goes
/// deep first. With them, a worker takes the first queued project not started yet in the depth-first pre-order from
/// the roots over the references known so far: the order in which the build thread will ask for them. A caller that
/// needs a project whose evaluation has not started runs it on its own thread instead of waiting behind the queue.
/// </summary>
/// <remarks>
/// Each project's references are read before its evaluation is handed out, because the build thread later switches
/// its TargetFramework and the Project must not be read on another thread then.
/// </remarks>
internal sealed class ClosureEvaluator : IDisposable
{
    public ClosureEvaluator(
        Func<string, Project?> evaluate,
        Func<Project, IReadOnlyList<string>> references,
        int workers,
        IReadOnlyList<string>? traversalRoots = null)
    {
        _evaluate = evaluate;
        _references = references;
        _traversalRoots = traversalRoots;
        for (var i = 0; i < workers; i++)
        {
            _workers.Add(Task.Factory.StartNew(Work, TaskCreationOptions.LongRunning));
        }
    }

    /// <summary>Queues the evaluation of a project unless it is queued, running or done.</summary>
    public void Schedule(string path)
    {
        if (_states.TryAdd(path, new Evaluation()))
        {
            _pending.Push(path);
            _signal.Release();
        }
    }

    /// <summary>The project's root evaluation: runs it here if no worker has started it, otherwise waits for it.</summary>
    public Project? Require(string path)
    {
        Schedule(path);
        var evaluation = _states[path];
        if (evaluation.TryStart())
        {
            Run(path, evaluation);
        }

        return evaluation.Result.Task.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _stop.Cancel();
        Task.WaitAll([.. _workers]);
        _stop.Dispose();
        _signal.Dispose();
    }

    private void Work()
    {
        while (true)
        {
            try
            {
                _signal.Wait(_stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_traversalRoots is not null)
            {
                if (NextInTraversal() is { } next)
                {
                    Run(next, _states[next]);
                }
            }
            else if (_pending.TryPop(out var path) && _states[path].TryStart())
            {
                Run(path, _states[path]);
            }
        }
    }

    private void Run(string path, Evaluation evaluation)
    {
        Project? project;
        IReadOnlyList<string> references;
        try
        {
            project = _evaluate(path);
            references = project is null ? [] : _references(project);
        }
        catch (Exception e)
        {
            evaluation.Result.SetException(e);
            return;
        }

        _knownReferences[path] = references;
        evaluation.Result.SetResult(project);
        foreach (var reference in references)
        {
            Schedule(reference);
        }
    }

    // The depth-first pre-order of the build thread's traversal over the references known so far, from the roots
    // that are queued; returns the first queued project not started yet, already marked started. A project whose
    // evaluation has not finished has no known references, so the walk does not go below it.
    private string? NextInTraversal()
    {
        lock (_traversalLock)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<string>(_traversalRoots!.Reverse());
            while (stack.TryPop(out var path))
            {
                if (!seen.Add(path) || !_states.TryGetValue(path, out var evaluation))
                {
                    continue;
                }

                if (evaluation.TryStart())
                {
                    return path;
                }

                if (_knownReferences.TryGetValue(path, out var references))
                {
                    for (var i = references.Count - 1; i >= 0; i--)
                    {
                        stack.Push(references[i]);
                    }
                }
            }

            return null;
        }
    }

    private sealed class Evaluation
    {
        public TaskCompletionSource<Project?> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool TryStart() => Interlocked.Exchange(ref _started, 1) == 0;

        private int _started;
    }

    private readonly Func<string, Project?> _evaluate;
    private readonly Func<Project, IReadOnlyList<string>> _references;
    private readonly IReadOnlyList<string>? _traversalRoots;
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _knownReferences = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _traversalLock = new();
    private readonly List<Task> _workers = [];
    private readonly ConcurrentDictionary<string, Evaluation> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentStack<string> _pending = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _stop = new();
}
