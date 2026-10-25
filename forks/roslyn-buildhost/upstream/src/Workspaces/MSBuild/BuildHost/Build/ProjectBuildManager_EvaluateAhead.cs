// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Roslyn.Utilities;
using MSB = Microsoft.Build;

namespace Microsoft.CodeAnalysis.MSBuild;

/// <summary>
/// Opt-in evaluations ahead (fork, BuildHost/task-02 part B). The 5.9.0 loader sends its requests one at a time, and
/// the host evaluates each project only when the loader asks for it, while the evaluations of the projects it will
/// ask for next could run beside the current build. When the environment variable <see cref="EvaluateAheadVariable"/>
/// is a number of worker threads, those threads evaluate ahead, in the host's project collection and with the host's
/// settings, every project the evaluations so far reference, and the entries of the solution being loaded. A request
/// for a project that a worker is evaluating waits for that evaluation instead of starting a second one; a request
/// for a project no worker has started evaluates it on the request's thread, as without this mode.
/// </summary>
/// <remarks>
/// <para>A worker takes the queued project that the loader will ask for first: the first one not started in the
/// depth-first pre-order of the solution entries, then of the other requested projects, over the references known
/// so far. A project's references are read on the worker before its evaluation is handed out, since the build later
/// switches its TargetFramework.</para>
/// <para>The project collection holds an evaluation made ahead, so the loader's load of that path gets the same
/// project, as it gets a project the host loaded before. An evaluation that fails ahead is not kept: the loader's own
/// load evaluates the project again and reports the same failure in its log.</para>
/// <para>The loader passes only the property SolutionDir, not the solution. The host takes the solution entries only
/// when SolutionDir holds exactly one .sln or .slnx file and no .slnf file, MSBuild reads it, and the loader's first
/// request is the first entry the loader would send to this host: a C# or Visual Basic project that exists and that
/// the loader's BuildHostProcessManager.GetKindForProject gives to the .NET host. Otherwise only references are
/// evaluated ahead. <see cref="EvaluateAheadSolutionVariable"/> sets how many entries after the latest requested one
/// are queued (all of them by default), or turns the entries off.</para>
/// <para>Only C# and Visual Basic projects that exist on disk and that the loader would send to this host are
/// evaluated ahead.</para>
/// </remarks>
internal sealed partial class ProjectBuildManager
{
    public const string EvaluateAheadVariable = "ROSLYN_BUILDHOST_EVALUATE_AHEAD";

    /// <summary>
    /// <c>all</c> (the default), a number of entries after the latest requested one, or <c>off</c>.
    /// </summary>
    public const string EvaluateAheadSolutionVariable = "ROSLYN_BUILDHOST_EVALUATE_AHEAD_SOLUTION";

    private static readonly int s_evaluateAheadWorkers =
        int.TryParse(Environment.GetEnvironmentVariable(EvaluateAheadVariable), NumberStyles.None, CultureInfo.InvariantCulture, out var workers) ? workers : 0;

    private static readonly int? s_evaluateAheadSolutionWindow = Environment.GetEnvironmentVariable(EvaluateAheadSolutionVariable) switch
    {
        null or "" or "all" => 0,
        "off" => null,
        var value => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var window) && window > 0 ? window : null,
    };

    private readonly object _aheadGate = new();
    private readonly Dictionary<string, AheadEvaluation> _aheadEvaluations = new(PathUtilities.Comparer);

    /// <summary>
    /// Where the pre-order walk of the workers starts: the queued solution entries in solution order, then the other
    /// projects the host was asked to load, in the order of the first request.
    /// </summary>
    private readonly List<string> _aheadRoots = [];
    private readonly HashSet<string> _aheadRootSet = new(PathUtilities.Comparer);

    private readonly SemaphoreSlim _aheadSignal = new(initialCount: 0);
    private readonly CancellationTokenSource _aheadStop = new();
    private readonly List<Thread> _aheadWorkers = [];

    private string? _solutionDirectory;
    private bool _aheadStarted;
    private string[] _solutionEntries = [];
    private Dictionary<string, int> _solutionEntryIndexes = new(PathUtilities.Comparer);
    private int _queuedSolutionEntries;

    private void InitializeEvaluateAhead(Dictionary<string, string> globalProperties)
    {
        if (s_evaluateAheadWorkers <= 0)
            return;

        globalProperties.TryGetValue("SolutionDir", out _solutionDirectory);
    }

    /// <summary>
    /// Called before the host loads <paramref name="path"/>: waits for a worker that is evaluating it, or marks it
    /// started so that no worker does. The first call reads the solution and starts the workers.
    /// </summary>
    private async Task WaitForEvaluationAheadAsync(string path, CancellationToken cancellationToken)
    {
        if (s_evaluateAheadWorkers <= 0)
            return;

        Task? running;
        lock (_aheadGate)
        {
            if (!_aheadStarted)
                StartEvaluateAhead(path);

            QueueSolutionEntriesAfter(path);
            AddAheadRoot(path);

            if (!_aheadEvaluations.TryGetValue(path, out var evaluation))
                _aheadEvaluations.Add(path, evaluation = new AheadEvaluation());

            evaluation.Requested = true;
            if (evaluation.State == AheadState.Queued)
                evaluation.State = AheadState.OnRequest;

            running = evaluation.State == AheadState.OnWorker ? evaluation.Completion.Task : null;
        }

        if (running is not null)
        {
            using var span = BuildHostTrace.Start("wait-evaluation-ahead", path);
            await running.ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Called after the host evaluated <paramref name="path"/> on the request's thread: queues its references.
    /// </summary>
    private void OnEvaluatedOnRequest(string path, MSB.Evaluation.Project? project)
    {
        if (s_evaluateAheadWorkers <= 0 || project is null)
            return;

        var references = GetAheadReferencePaths(project);
        lock (_aheadGate)
        {
            if (_aheadEvaluations.TryGetValue(path, out var evaluation))
                evaluation.References = references;

            foreach (var reference in references)
                QueueAhead(reference);
        }
    }

    /// <summary>
    /// Called before the host loads <paramref name="path"/> from content the loader sent: an evaluation made ahead
    /// from the file that the loader has not asked for is unloaded, so that the collection does not hold the path
    /// twice. Upstream holds it twice only if the loader loaded the file itself.
    /// </summary>
    private void UnloadEvaluationAhead(string path)
    {
        if (s_evaluateAheadWorkers <= 0)
            return;

        Task? running;
        lock (_aheadGate)
        {
            if (!_aheadEvaluations.TryGetValue(path, out var evaluation))
                return;

            running = evaluation.State == AheadState.OnWorker ? evaluation.Completion.Task : null;
        }

        running?.Wait();

        lock (_aheadGate)
        {
            var evaluation = _aheadEvaluations[path];
            if (!evaluation.Requested && evaluation.Project is { } project)
            {
                _projectCollection.UnloadProject(project);
                evaluation.Project = null;
            }
        }
    }

    private void StopEvaluateAhead()
    {
        if (s_evaluateAheadWorkers <= 0)
            return;

        // A worker in the middle of an evaluation finishes it first.
        _aheadStop.Cancel();
        foreach (var worker in _aheadWorkers)
            worker.Join();
    }

    private void StartEvaluateAhead(string firstPath)
    {
        _aheadStarted = true;

        using (var span = BuildHostTrace.Start("evaluate-ahead-start", firstPath))
        {
            var (entries, reason) = ReadSolutionEntries(firstPath);
            span?.Set("workers", s_evaluateAheadWorkers);
            span?.Set("solution", reason);
            span?.Set("entries", entries.Length);

            _solutionEntries = entries;
            for (var i = 0; i < entries.Length; i++)
                _solutionEntryIndexes[entries[i]] = i;
        }

        for (var i = 0; i < s_evaluateAheadWorkers; i++)
        {
            var worker = new Thread(EvaluateAheadWorker)
            {
                IsBackground = true,
                Name = "Evaluate ahead " + i.ToString(CultureInfo.InvariantCulture),
            };
            _aheadWorkers.Add(worker);
            worker.Start();
        }
    }

    /// <summary>
    /// The C# and Visual Basic entries of the solution in SolutionDir, in solution order, and why they were or were not
    /// taken.
    /// </summary>
    private (string[] Entries, string Reason) ReadSolutionEntries(string firstPath)
    {
        if (s_evaluateAheadSolutionWindow is null)
            return ([], "off");

        if (RoslynString.IsNullOrEmpty(_solutionDirectory) || !Directory.Exists(_solutionDirectory))
            return ([], "no SolutionDir");

        try
        {
            // Not a search pattern: on Windows "*.sln" also matches .slnx and .slnf files.
            var files = Directory.EnumerateFiles(_solutionDirectory).ToArray();
            if (files.Any(file => HasExtension(file, ".slnf")))
                return ([], "solution filter in SolutionDir");

            var solutions = files.Where(file => HasExtension(file, ".sln") || HasExtension(file, ".slnx")).ToArray();
            if (solutions.Length != 1)
                return ([], solutions.Length + " solutions in SolutionDir");

            var entries = MSB.Construction.SolutionFile.Parse(solutions[0]).ProjectsInOrder
                .Where(project => project.ProjectType != MSB.Construction.SolutionProjectType.SolutionFolder)
                .Select(project => Path.GetFullPath(project.AbsolutePath))
                .Where(IsLoadableProjectPath)
                .Distinct(PathUtilities.Comparer)
                .ToArray();

            var first = entries.FirstOrDefault(IsForThisHost);
            if (first is null || !PathUtilities.Comparer.Equals(first, Path.GetFullPath(firstPath)))
                return ([], "first request is not the first entry");

            return (entries, "taken");
        }
        catch (Exception e)
        {
            return ([], "unreadable: " + e.GetType().Name);
        }
    }

    private void QueueSolutionEntriesAfter(string path)
    {
        if (_solutionEntries.Length == 0)
            return;

        var end = _solutionEntries.Length;
        if (s_evaluateAheadSolutionWindow is int window and > 0)
        {
            if (!_solutionEntryIndexes.TryGetValue(path, out var index))
                return;

            end = Math.Min(_solutionEntries.Length, index + window + 1);
        }

        for (; _queuedSolutionEntries < end; _queuedSolutionEntries++)
        {
            var entry = _solutionEntries[_queuedSolutionEntries];
            AddAheadRoot(entry);
            QueueAhead(entry);
        }
    }

    private void AddAheadRoot(string path)
    {
        if (_aheadRootSet.Add(path))
            _aheadRoots.Add(path);
    }

    private void QueueAhead(string path)
    {
        if (_aheadEvaluations.ContainsKey(path))
            return;

        _aheadEvaluations.Add(path, new AheadEvaluation());
        _aheadSignal.Release();
    }

    private void EvaluateAheadWorker()
    {
        while (true)
        {
            try
            {
                _aheadSignal.Wait(_aheadStop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // One signal per queued project, but a worker takes whatever comes first in the walk, so it keeps
            // taking until nothing is left; later signals then find nothing.
            while (!_aheadStop.IsCancellationRequested && TryStartNextAhead(out var path, out var evaluation))
                EvaluateAhead(path, evaluation);
        }
    }

    private bool TryStartNextAhead(out string path, out AheadEvaluation evaluation)
    {
        lock (_aheadGate)
        {
            var seen = new HashSet<string>(PathUtilities.Comparer);
            var stack = new Stack<string>(Enumerable.Reverse(_aheadRoots));
            while (stack.Count > 0)
            {
                path = stack.Pop();
                if (!seen.Add(path) || !_aheadEvaluations.TryGetValue(path, out evaluation!))
                    continue;

                if (evaluation.State == AheadState.Queued)
                {
                    evaluation.State = AheadState.OnWorker;
                    return true;
                }

                // A project whose evaluation has not finished has no known references yet.
                if (evaluation.References is { } references)
                {
                    for (var i = references.Length - 1; i >= 0; i--)
                        stack.Push(references[i]);
                }
            }

            path = null!;
            evaluation = null!;
            return false;
        }
    }

    private void EvaluateAhead(string path, AheadEvaluation evaluation)
    {
        MSB.Evaluation.Project? project = null;
        string[] references = [];
        try
        {
            using var span = BuildHostTrace.Start("evaluate-ahead", path);
            var forThisHost = IsForThisHost(path);
            span?.Set("forThisHost", forThisHost);
            if (forThisHost)
            {
                using var stream = FileUtilities.OpenRead(path);
                using var readStream = new MemoryStream();
                stream.CopyTo(readStream);
                readStream.Position = 0;

                (project, _) = LoadProjectCore(path, readStream, new DiagnosticLog());
                span?.Set("evaluated", project is not null);
                if (project is not null)
                    references = GetAheadReferencePaths(project);
            }
        }
        catch (Exception)
        {
            // The loader's own load reads and evaluates the project again and reports what goes wrong.
        }

        lock (_aheadGate)
        {
            evaluation.Project = project;
            evaluation.References = references;
            evaluation.State = AheadState.Done;
            foreach (var reference in references)
                QueueAhead(reference);
        }

        evaluation.Completion.TrySetResult(true);
    }

    private static string[] GetAheadReferencePaths(MSB.Evaluation.Project project)
        => [.. GetLoadableProjectReferencePaths(project)];

    private static bool IsLoadableProjectPath(string path)
        => (HasExtension(path, ".csproj") || HasExtension(path, ".vbproj")) && File.Exists(path);

    private static bool HasExtension(string path, string extension)
        => string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the 5.9.0 loader sends this project to the .NET build host: the rules of its
    /// BuildHostProcessManager.GetKindForProject.
    /// </summary>
    private static bool IsForThisHost(string path)
    {
        XDocument document;
        try
        {
            using var stream = FileUtilities.OpenRead(path);
            using var reader = XmlReader.Create(stream, s_xmlReaderSettings);
            document = XDocument.Load(reader);
        }
        catch (Exception e) when (e is IOException or XmlException)
        {
            return true;
        }

        var root = document.Root;
        if (root is null)
            return true;

        if (root.Attribute("Sdk") is not null || root.Elements("Import").Attributes("Sdk").Any() || root.Elements("Sdk").Any())
            return true;

        var propertyGroups = document.Descendants("PropertyGroup");
        return propertyGroups.Elements("TargetFramework").Any() || propertyGroups.Elements("TargetFrameworks").Any();
    }

    private enum AheadState
    {
        Queued,

        /// <summary>
        /// A request reached the project before any worker: the request's thread evaluates it.
        /// </summary>
        OnRequest,

        OnWorker,
        Done,
    }

    private sealed class AheadEvaluation
    {
        public AheadState State { get; set; }

        /// <summary>
        /// Whether the host was asked to load this path; an evaluation made ahead is then the host's own.
        /// </summary>
        public bool Requested { get; set; }

        /// <summary>
        /// The evaluation a worker made, while the project collection holds it.
        /// </summary>
        public MSB.Evaluation.Project? Project { get; set; }

        /// <summary>
        /// The loadable references of the evaluation, once it has finished.
        /// </summary>
        public string[]? References { get; set; }

        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
