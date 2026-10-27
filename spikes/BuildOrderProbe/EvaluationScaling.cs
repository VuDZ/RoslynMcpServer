using System.Collections.Concurrent;
using System.Diagnostics;
using BuildOrderProbe.Models;

namespace BuildOrderProbe;

/// <summary>
/// Evaluates a fixed list of projects on dedicated worker threads, with no builds and no build manager, to see whether
/// root evaluations in one process run in parallel at all (BuildHost/task-02). With one shared project collection
/// the workers share its caches and locks; with one collection per worker only what is global to the process is
/// shared, such as the SDK resolvers and MSBuild's static caches. ProjectCollection.LoadProject, which the probe used
/// before, holds the collection's write lock for the whole evaluation; it is kept as a variant to compare.
/// </summary>
/// <remarks>
/// Dedicated threads, not the thread pool, so the pool's ramp-up does not limit the workers. Each worker takes the
/// next path from one queue in the list's order.
/// </remarks>
internal static class EvaluationScaling
{
    public static ScalingRun Run(string solutionPath, IReadOnlyList<string> paths, int workers, bool separateCollections, bool useLoadProject)
    {
        var collections = Enumerable.Range(0, separateCollections ? workers : 1)
            .Select(_ => HostSession.CreateCollection(solutionPath))
            .ToArray();
        var queue = new ConcurrentQueue<string>(paths);
        var steps = new ConcurrentBag<TimedStep>();
        var notEvaluated = 0;
        var wall = Stopwatch.StartNew();

        var threads = Enumerable.Range(0, workers)
            .Select(index => new Thread(() =>
            {
                var collection = collections[separateCollections ? index : 0];
                while (queue.TryDequeue(out var path))
                {
                    var start = wall.Elapsed.TotalMilliseconds;
                    var project = useLoadProject
                        ? HostSession.EvaluateWithLoadProject(collection, path)
                        : HostSession.Evaluate(collection, path);
                    var end = wall.Elapsed.TotalMilliseconds;
                    if (project is null)
                    {
                        Interlocked.Increment(ref notEvaluated);
                        continue;
                    }

                    steps.Add(new TimedStep(path, OrderedLoad.IsMultiTargeting(project), start, end, Environment.CurrentManagedThreadId));
                }
            }))
            .ToArray();

        foreach (var thread in threads)
        {
            thread.Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        wall.Stop();
        foreach (var collection in collections)
        {
            collection.Dispose();
        }

        return new ScalingRun(
            workers,
            separateCollections,
            useLoadProject,
            Environment.ProcessorCount,
            wall.Elapsed.TotalMilliseconds,
            steps.Count,
            notEvaluated,
            steps.OrderBy(step => step.StartMs).ToArray());
    }
}
