using System.Collections.Concurrent;
using Microsoft.Build.Evaluation;

namespace BuildOrderProbe;

/// <summary>
/// Root evaluations of the whole reference closure ahead of the builds (<see cref="Models.LoadOrder.EvaluateClosure"/>).
/// Every finished evaluation queues the projects it references; worker threads take the most recently queued first,
/// so the expansion goes deep first like the post-order builds that need it. A caller that needs a project whose
/// evaluation has not started runs it on its own thread instead of waiting behind the queue.
/// </summary>
/// <remarks>
/// Probe runs showed that root evaluations in one project collection run as if one at a time: evaluating ahead pays
/// only if the evaluations happen while the builds run, not by having many of them at once. Each project's
/// references are read before its evaluation is handed out, because the build thread later switches its
/// TargetFramework and the Project must not be read on another thread then.
/// </remarks>
internal sealed class ClosureEvaluator : IDisposable
{
    public ClosureEvaluator(Func<string, Project?> evaluate, Func<Project, IReadOnlyList<string>> references, int workers)
    {
        _evaluate = evaluate;
        _references = references;
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

            if (_pending.TryPop(out var path) && _states[path].TryStart())
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

        evaluation.Result.SetResult(project);
        foreach (var reference in references)
        {
            Schedule(reference);
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
    private readonly List<Task> _workers = [];
    private readonly ConcurrentDictionary<string, Evaluation> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentStack<string> _pending = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _stop = new();
}
