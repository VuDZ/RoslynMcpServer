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
/// asks for it. When the environment variable <see cref="ReferencesFirstVariable"/> is 1, a build request first
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
/// <para>Requests are served one at a time in this mode; the 5.9.0 loader sends them one at a time anyway.</para>
/// </remarks>
internal sealed partial class ProjectBuildManager
{
    public const string ReferencesFirstVariable = "ROSLYN_BUILDHOST_REFERENCES_FIRST";

    private static readonly bool s_buildReferencesFirst = Environment.GetEnvironmentVariable(ReferencesFirstVariable) == "1";

    private readonly SemaphoreSlim _referencesFirstGate = new(initialCount: 1);

    /// <summary>
    /// Projects whose design-time build has started, at the loader's request or ahead of it.
    /// </summary>
    private readonly HashSet<MSB.Evaluation.Project> _builtProjects = [];

    /// <summary>
    /// Builds made ahead of the loader's request that it has not asked for yet.
    /// </summary>
    private readonly Dictionary<MSB.Evaluation.Project, PreparedBuild> _preparedBuilds = [];

    public async Task<MSB.Execution.ProjectInstance[]> GetProjectInstancesAsync(
        MSB.Evaluation.Project project, DiagnosticLog log, CancellationToken cancellationToken)
    {
        if (!s_buildReferencesFirst)
            return await BuildProjectInstancesAsync(project, log, cancellationToken).ConfigureAwait(false);

        using (await _referencesFirstGate.DisposableWaitAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_preparedBuilds.TryGetValue(project, out var prepared))
            {
                _preparedBuilds.Remove(project);

                using var span = BuildHostTrace.Start("prepared-build", project.FullPath);
                foreach (var item in prepared.Log)
                    log.Add(item);

                prepared.Exception?.Throw();
                return prepared.Instances!;
            }

            _builtProjects.Add(project);
            await BuildReferencesAheadAsync(project, cancellationToken).ConfigureAwait(false);
            return await BuildProjectInstancesAsync(project, log, cancellationToken).ConfigureAwait(false);
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

            var log = new DiagnosticLog();
            try
            {
                var instances = await BuildProjectInstancesAsync(reference, log, cancellationToken).ConfigureAwait(false);
                _preparedBuilds[reference] = new PreparedBuild(instances, log, Exception: null);
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
                _preparedBuilds[reference] = new PreparedBuild(Instances: null, log, ExceptionDispatchInfo.Capture(e));
            }
        }
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

    private sealed record PreparedBuild(MSB.Execution.ProjectInstance[]? Instances, DiagnosticLog Log, ExceptionDispatchInfo? Exception);
}
