// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Roslyn.Utilities;
using MSB = Microsoft.Build;

namespace Microsoft.CodeAnalysis.MSBuild;

/// <summary>
/// Opt-in "references first" builds (fork, BuildHost/task-02 part A). The 5.9.0 loader builds a project before it
/// loads the projects that project references. The design-time build then calls GetTargetFrameworks and
/// GetTargetPath on each reference, finds no configuration of it in the build manager, and MSBuild evaluates the
/// reference again inside the build, although the host evaluates it with the same global properties when the loader
/// asks for it. When the global property <see cref="ReferencesFirstProperty"/> or, without it, the environment
/// variable <see cref="ReferencesFirstVariable"/> is 1, a build request first
/// loads and builds every referenced project that has not been built yet, references of references first, and keeps
/// those results until the loader asks for them; the calls from the requested build then reach existing
/// configurations.
/// </summary>
/// <remarks>
/// <para>A build made ahead is handed out once, to the first request for that project, with the errors and warnings
/// it raised and the exception it threw, if any. Later requests build again, as without this mode.</para>
/// <para>Only C# and Visual Basic projects that exist on disk are built ahead: the loader loads those references
/// itself, and an unloadable one gets only its output path. The references are the ProjectReference items of the
/// requested project's evaluation, so a reference that only some target framework adds is not built ahead and is
/// evaluated inside the build as before.</para>
/// <para>The order of the builds changes where errors and warnings show up. Upstream shows those of a reference's
/// evaluation only through the evaluation inside the first build that reaches the reference's configuration, since
/// the host's own evaluations run in a project collection without loggers; and the targets a build calls on a
/// configuration run, with their errors, warnings and calls of their own, only for the first build that calls them,
/// later ones get cached results. This mode records the errors and warnings of every evaluation and, for every
/// build, its requests to configurations as a tree: each request with its errors and warnings and the requests its
/// targets made. A build is handed out in the loader's order, and its log is written from that tree as upstream would
/// have written it at that point: where the build reaches a configuration that no build handed out before has
/// reached, the errors and warnings of that configuration's evaluation are inserted, and where it calls targets that
/// no build handed out before has called on that configuration, the content of the targets' first run is
/// inserted.</para>
/// <para>Requests are served one at a time in this mode; the 5.9.0 loader sends them one at a time anyway.</para>
/// </remarks>
internal sealed partial class ProjectBuildManager
{
    public const string ReferencesFirstVariable = "ROSLYN_BUILDHOST_REFERENCES_FIRST";

    /// <summary>
    /// The global property in which the host's client turns this mode on (1) or off for its workspace; it wins over
    /// <see cref="ReferencesFirstVariable"/>, which is process-wide. Taken out before the project collection is created.
    /// </summary>
    public const string ReferencesFirstProperty = "RoslynMcpReferencesFirst";

    private bool _buildReferencesFirst = Environment.GetEnvironmentVariable(ReferencesFirstVariable) == "1";

    private readonly SemaphoreSlim _referencesFirstGate = new(initialCount: 1);

    /// <summary>
    /// Projects whose design-time build has started, at the loader's request or ahead of it.
    /// </summary>
    private readonly HashSet<MSB.Evaluation.Project> _builtProjects = [];

    /// <summary>
    /// Builds made ahead of the loader's request that it has not asked for yet.
    /// </summary>
    private readonly Dictionary<MSB.Evaluation.Project, PreparedBuild> _preparedBuilds = [];

    private readonly BuildEventRecorder _buildEventRecorder = new();

    /// <summary>
    /// Build manager configurations that the builds handed out so far built or reached: upstream would have
    /// evaluated them by now.
    /// </summary>
    private readonly HashSet<int> _reachedConfigurations = [];

    /// <summary>
    /// Targets, by configuration, that the builds handed out so far called since the configuration's last own build:
    /// upstream would have their results cached by now. A build of the configuration itself gives the build manager a
    /// new project instance, and that drops the cached results.
    /// </summary>
    private readonly Dictionary<int, HashSet<string>> _calledTargets = [];

    private void InitializeReferencesFirst(Dictionary<string, string> globalProperties)
    {
        if (TakeGlobalProperty(globalProperties, ReferencesFirstProperty) is { } value)
            _buildReferencesFirst = value == "1";
    }

    private void AddReferencesFirstLoggers(MSB.Execution.BuildParameters buildParameters)
    {
        if (!_buildReferencesFirst)
            return;

        buildParameters.Loggers = [.. buildParameters.Loggers, _buildEventRecorder.CreateLogger()];

        // Upstream gives the project collection no loggers; this one only records the errors and warnings of the
        // host's evaluations. The build parameters have already copied the collection's settings, so the build
        // still raises every event.
        _projectCollection.OnlyLogCriticalEvents = true;
        _projectCollection.RegisterLogger(_buildEventRecorder.CreateLogger());
    }

    public async Task<MSB.Execution.ProjectInstance[]> GetProjectInstancesAsync(
        MSB.Evaluation.Project project, DiagnosticLog log, CancellationToken cancellationToken)
    {
        if (!_buildReferencesFirst)
            return await BuildProjectInstancesAsync(project, log, cancellationToken).ConfigureAwait(false);

        using (await _referencesFirstGate.DisposableWaitAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_preparedBuilds.TryGetValue(project, out var prepared))
            {
                _preparedBuilds.Remove(project);

                using var span = BuildHostTrace.Start("prepared-build", project.FullPath);
                AddAsUpstream(prepared.Build, log, project.FullPath);

                prepared.Exception?.Throw();
                return prepared.Instances!;
            }

            _builtProjects.Add(project);
            await BuildReferencesAheadAsync(project, cancellationToken).ConfigureAwait(false);

            var build = new RecordedBuild();
            try
            {
                return await BuildRecordedAsync(project, build, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                AddAsUpstream(build, log, project.FullPath);
            }
        }
    }

    private async Task BuildReferencesAheadAsync(MSB.Evaluation.Project project, CancellationToken cancellationToken)
    {
        foreach (var path in GetLoadableProjectReferencePaths(project))
        {
            // The project collection holds the evaluation, so the loader's later load of this path gets the same
            // project. A project that fails to evaluate is left to that load, which reports the same failure.
            var (reference, _) = await LoadProjectAsync(path, cancellationToken).ConfigureAwait(false);

            // Projects on the current chain are already in the set, so a reference cycle stops here.
            if (reference is null || !_builtProjects.Add(reference))
                continue;

            await BuildReferencesAheadAsync(reference, cancellationToken).ConfigureAwait(false);

            using var span = BuildHostTrace.Start("build-ahead", reference.FullPath);
            if (!reference.GlobalProperties.TryGetValue(PropertyNames.TargetFramework, out var initialGlobalTargetFramework))
                initialGlobalTargetFramework = null;

            var build = new RecordedBuild();
            try
            {
                var instances = await BuildRecordedAsync(reference, build, cancellationToken).ConfigureAwait(false);
                _preparedBuilds[reference] = new PreparedBuild(instances, build, Exception: null);
            }
            catch (OperationCanceledException)
            {
                // The loader never asked for this build. Leave the project as the loader would find it without
                // this mode: not built, and with its own TargetFramework if the cancellation came between the
                // builds of its target frameworks.
                _builtProjects.Remove(reference);
                RestoreGlobalTargetFramework(reference, initialGlobalTargetFramework);
                throw;
            }
            catch (Exception e)
            {
                // The loader's own request would have thrown the same and left the project in the same state.
                _preparedBuilds[reference] = new PreparedBuild(Instances: null, build, ExceptionDispatchInfo.Capture(e));
            }
        }
    }

    private async Task<MSB.Execution.ProjectInstance[]> BuildRecordedAsync(
        MSB.Evaluation.Project project, RecordedBuild build, CancellationToken cancellationToken)
    {
        _buildEventRecorder.StartBuild(build.Log);
        try
        {
            return await BuildProjectInstancesAsync(project, build.Log, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            build.Aligned = _buildEventRecorder.FinishBuild(build.Submissions) && IsAligned(build);
        }
    }

    /// <summary>
    /// Adds the errors and warnings of <paramref name="build"/> to the loader's <paramref name="log"/> as upstream would
    /// have logged them for a build in this place of the loader's order.
    /// </summary>
    private void AddAsUpstream(RecordedBuild build, DiagnosticLog log, string path)
    {
        using var span = BuildHostTrace.Start("upstream-log", path);
        var source = build.Log;

        if (!build.Aligned)
        {
            // Some error or warning of the build has no place in the recorded tree. Keep the build's own log rather
            // than guess, and still count what it reached.
            span?.Set("aligned", false);
            foreach (var item in source)
                log.Add(item);

            foreach (var submission in build.Submissions.Where(submission => submission.Root is not null))
            {
                MarkBuilt(submission.Root!);
                MarkReached(submission.Root!);
            }

            return;
        }

        var inserted = 0;
        var next = 0;
        foreach (var submission in build.Submissions)
        {
            // The items in between are those the host itself added, such as a missing target.
            for (; next < submission.LogStart; next++)
                log.Add(source[next]);

            var root = submission.Root!;
            MarkBuilt(root);
            AddRequestEntries(root, log, ref inserted);

            next = submission.LogStart + submission.DiagnosticCount;
        }

        for (; next < source.Count; next++)
            log.Add(source[next]);

        span?.Set("inserted", inserted);
    }

    private void AddRequestEntries(RecordedRequest request, DiagnosticLog log, ref int inserted)
    {
        foreach (var entry in request.Entries)
        {
            if (entry.Item is not null)
            {
                log.Add(entry.Item);
                continue;
            }

            var reached = entry.Request!;
            if (_reachedConfigurations.Add(reached.ConfigurationId))
            {
                foreach (var item in _buildEventRecorder.GetEvaluationDiagnostics(reached.EvaluationId))
                {
                    log.Add(item);
                    inserted++;
                }
            }

            // Upstream runs the targets here, so they write here what their first run wrote wherever it ran.
            if (MarkCalled(reached) &&
                _buildEventRecorder.TryGetFirstRun(reached.ConfigurationId, reached.Targets, out var firstRun))
            {
                AddRequestEntries(firstRun, log, ref inserted);
            }
        }
    }

    private void MarkBuilt(RecordedRequest root)
    {
        _reachedConfigurations.Add(root.ConfigurationId);
        _calledTargets[root.ConfigurationId] = [root.Targets];
    }

    private bool MarkCalled(RecordedRequest request)
    {
        if (!_calledTargets.TryGetValue(request.ConfigurationId, out var targets))
            _calledTargets.Add(request.ConfigurationId, targets = []);

        return targets.Add(request.Targets);
    }

    private void MarkReached(RecordedRequest request)
    {
        foreach (var entry in request.Entries)
        {
            if (entry.Request is not { } reached)
                continue;

            _reachedConfigurations.Add(reached.ConfigurationId);
            MarkCalled(reached);
            MarkReached(reached);
        }
    }

    /// <summary>
    /// Whether every submission of <paramref name="build"/> has its request tree and knows where its errors and
    /// warnings start in the build's log, after those of the submission before.
    /// </summary>
    private static bool IsAligned(RecordedBuild build)
    {
        var next = 0;
        foreach (var submission in build.Submissions)
        {
            if (submission.Root is null || submission.LogStart < next)
                return false;

            next = submission.LogStart + submission.DiagnosticCount;
            if (next > build.Log.Count)
                return false;
        }

        return true;
    }

    private static IEnumerable<string> GetLoadableProjectReferencePaths(MSB.Evaluation.Project project)
        => project.GetItems(ItemNames.ProjectReference)
            .Select(item => Path.GetFullPath(Path.Combine(project.DirectoryPath, item.EvaluatedInclude)))
            .Where(path => (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)) && File.Exists(path))
            .Distinct(PathUtilities.Comparer)
            .ToArray();

    private static void RestoreGlobalTargetFramework(MSB.Evaluation.Project project, string? initialGlobalTargetFramework)
    {
        project.GlobalProperties.TryGetValue(PropertyNames.TargetFramework, out var current);
        if (current == initialGlobalTargetFramework)
            return;

        if (initialGlobalTargetFramework is null)
            project.RemoveGlobalProperty(PropertyNames.TargetFramework);
        else
            project.SetGlobalProperty(PropertyNames.TargetFramework, initialGlobalTargetFramework);

        project.ReevaluateIfNecessary();
    }

    private sealed record PreparedBuild(MSB.Execution.ProjectInstance[]? Instances, RecordedBuild Build, ExceptionDispatchInfo? Exception);

    /// <summary>
    /// The log of one build request before it is handed out, and the submissions that wrote to it.
    /// </summary>
    private sealed class RecordedBuild
    {
        public DiagnosticLog Log { get; } = new();
        public List<RecordedSubmission> Submissions { get; } = [];

        /// <summary>
        /// Whether every error and warning of the submissions has its place in their request trees.
        /// </summary>
        public bool Aligned { get; set; }
    }

    private sealed class RecordedSubmission(int logStart)
    {
        /// <summary>
        /// The size of the build's log when the submission started, or -1 if it raised an error or warning before.
        /// </summary>
        public int LogStart { get; } = logStart;

        /// <summary>
        /// The errors and warnings the submission added to the build's log, its evaluations' included.
        /// </summary>
        public int DiagnosticCount { get; set; }

        public RecordedRequest? Root { get; set; }
    }

    /// <summary>
    /// A request of a build to one configuration, the submission's own or one that a target made through the MSBuild
    /// task, with its errors and warnings and the requests its targets made, in the order they came.
    /// </summary>
    private sealed class RecordedRequest(int configurationId, int evaluationId, string targets)
    {
        public int ConfigurationId { get; } = configurationId;

        /// <summary>
        /// The evaluation behind the configuration when the request started.
        /// </summary>
        public int EvaluationId { get; } = evaluationId;

        public string Targets { get; } = targets;
        public List<RequestEntry> Entries { get; } = [];
    }

    /// <summary>
    /// An error or warning of a request, or a request it made.
    /// </summary>
    private readonly record struct RequestEntry(DiagnosticLogItem? Item, RecordedRequest? Request);

    /// <summary>
    /// Records the events the references-first mode needs: the errors and warnings of every evaluation, of the
    /// project collection and of the build manager; the first run of each target list on each configuration; and for
    /// each submission of the current build its request tree.
    /// </summary>
    private sealed class BuildEventRecorder
    {
        private readonly object _gate = new();
        private readonly Dictionary<int, List<DiagnosticLogItem>> _evaluationDiagnostics = [];
        private readonly Dictionary<(int ConfigurationId, string Targets), RecordedRequest> _firstRuns = [];

        private readonly Dictionary<int, RecordedSubmission> _submissions = [];
        private readonly List<RecordedSubmission> _buildSubmissions = [];
        private readonly Dictionary<int, RecordedRequest> _requestsByContext = [];
        private readonly HashSet<int> _evaluationsWithoutRequest = [];
        private DiagnosticLog? _buildLog;
        private bool _buildAligned;

        public MSB.Framework.ILogger CreateLogger()
            => new Logger(this);

        public void StartBuild(DiagnosticLog log)
        {
            lock (_gate)
            {
                _buildLog = log;
                _buildAligned = true;
            }
        }

        /// <summary>
        /// Moves the submissions of the current build to <paramref name="submissions"/> and returns whether each of
        /// their errors and warnings has its place in a request tree.
        /// </summary>
        public bool FinishBuild(List<RecordedSubmission> submissions)
        {
            lock (_gate)
            {
                // An evaluation inside the build that gave no configuration failed; its errors belong to no request.
                var aligned = _buildAligned && _evaluationsWithoutRequest.Count == 0;
                submissions.AddRange(_buildSubmissions);

                _buildSubmissions.Clear();
                _submissions.Clear();
                _requestsByContext.Clear();
                _evaluationsWithoutRequest.Clear();
                _buildLog = null;
                return aligned;
            }
        }

        public DiagnosticLogItem[] GetEvaluationDiagnostics(int evaluationId)
        {
            lock (_gate)
                return _evaluationDiagnostics.TryGetValue(evaluationId, out var items) ? items.ToArray() : [];
        }

        public bool TryGetFirstRun(int configurationId, string targets, out RecordedRequest firstRun)
        {
            lock (_gate)
                return _firstRuns.TryGetValue((configurationId, targets), out firstRun!);
        }

        private void OnProjectStarted(MSB.Framework.ProjectStartedEventArgs e)
        {
            var context = e.BuildEventContext;
            if (context is null || context.SubmissionId == MSB.Framework.BuildEventContext.InvalidSubmissionId)
                return;

            lock (_gate)
            {
                var request = new RecordedRequest(context.ProjectInstanceId, context.EvaluationId, e.TargetNames ?? "");
                _requestsByContext[context.ProjectContextId] = request;
                _evaluationsWithoutRequest.Remove(context.EvaluationId);

                var parent = e.ParentProjectBuildEventContext;
                if (parent is null || parent.ProjectContextId == MSB.Framework.BuildEventContext.InvalidProjectContextId)
                {
                    if (!_submissions.TryGetValue(context.SubmissionId, out var submission))
                        submission = AddSubmission(context.SubmissionId, _buildLog?.Count ?? -1);

                    _buildAligned &= submission.Root is null;
                    submission.Root = request;
                    return;
                }

                // The first request for these targets on this configuration runs them; later ones get the cached
                // results and record nothing under them.
                _firstRuns.TryAdd((request.ConfigurationId, request.Targets), request);

                if (_requestsByContext.TryGetValue(parent.ProjectContextId, out var parentRequest))
                    parentRequest.Entries.Add(new RequestEntry(Item: null, request));
                else
                    _buildAligned = false;
            }
        }

        private void OnDiagnosticRaised(DiagnosticLogItemKind kind, MSB.Framework.BuildEventContext? context, string? projectFile, string? message, string? file, int lineNumber, int columnNumber)
        {
            // The same items, from the same events, as MSBuildDiagnosticLogger adds to a build's log.
            if (context is null)
                return;

            var item = new MSBuildDiagnosticLogItem(kind, projectFile ?? "", message ?? "", file ?? "", lineNumber, columnNumber);
            var inSubmission = context.SubmissionId != MSB.Framework.BuildEventContext.InvalidSubmissionId;

            lock (_gate)
            {
                if (context.ProjectContextId == MSB.Framework.BuildEventContext.InvalidProjectContextId &&
                    context.EvaluationId != MSB.Framework.BuildEventContext.InvalidEvaluationId)
                {
                    if (!_evaluationDiagnostics.TryGetValue(context.EvaluationId, out var items))
                        _evaluationDiagnostics.Add(context.EvaluationId, items = []);

                    items.Add(item);
                    if (inSubmission)
                    {
                        GetSubmission(context.SubmissionId).DiagnosticCount++;
                        _evaluationsWithoutRequest.Add(context.EvaluationId);
                    }

                    return;
                }

                if (!inSubmission)
                    return;

                GetSubmission(context.SubmissionId).DiagnosticCount++;
                if (_requestsByContext.TryGetValue(context.ProjectContextId, out var request))
                    request.Entries.Add(new RequestEntry(item, Request: null));
                else
                    _buildAligned = false;
            }
        }

        private RecordedSubmission GetSubmission(int submissionId)
            => _submissions.TryGetValue(submissionId, out var submission) ? submission : AddSubmission(submissionId, logStart: -1);

        private RecordedSubmission AddSubmission(int submissionId, int logStart)
        {
            var submission = new RecordedSubmission(logStart);
            _submissions.Add(submissionId, submission);
            _buildSubmissions.Add(submission);
            return submission;
        }

        private sealed class Logger(BuildEventRecorder recorder) : MSB.Framework.ILogger
        {
            private MSB.Framework.IEventSource? _eventSource;

            public string? Parameters { get; set; }
            public MSB.Framework.LoggerVerbosity Verbosity { get; set; } = MSB.Framework.LoggerVerbosity.Quiet;

            public void Initialize(MSB.Framework.IEventSource eventSource)
            {
                _eventSource = eventSource;
                _eventSource.ProjectStarted += OnProjectStarted;
                _eventSource.ErrorRaised += OnErrorRaised;
                _eventSource.WarningRaised += OnWarningRaised;
            }

            public void Shutdown()
            {
                if (_eventSource is null)
                    return;

                _eventSource.ProjectStarted -= OnProjectStarted;
                _eventSource.ErrorRaised -= OnErrorRaised;
                _eventSource.WarningRaised -= OnWarningRaised;
                _eventSource = null;
            }

            private void OnProjectStarted(object sender, MSB.Framework.ProjectStartedEventArgs e)
                => recorder.OnProjectStarted(e);

            private void OnErrorRaised(object sender, MSB.Framework.BuildErrorEventArgs e)
                => recorder.OnDiagnosticRaised(DiagnosticLogItemKind.Error, e.BuildEventContext, e.ProjectFile, e.Message, e.File, e.LineNumber, e.ColumnNumber);

            private void OnWarningRaised(object sender, MSB.Framework.BuildWarningEventArgs e)
                => recorder.OnDiagnosticRaised(DiagnosticLogItemKind.Warning, e.BuildEventContext, e.ProjectFile, e.Message, e.File, e.LineNumber, e.ColumnNumber);
        }
    }
}
