// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MSB = Microsoft.Build;

namespace Microsoft.CodeAnalysis.MSBuild;

/// <summary>
/// Opt-in shared SDK resolution per project (fork, BuildHost/task-04 candidate C). The host evaluates a project
/// several times: its root evaluation, one evaluation per TargetFramework switch and one after the switch back, and the
/// late evaluation that the first GetAllGlobs makes after the builds. Without an evaluation context each of them gets a
/// context of its own and resolves every SDK again; the workload SDK resolver loads its manifests again each time,
/// which is about half of an evaluation of Roslyn.slnx on Windows. When the global property
/// <see cref="SharedSdkCacheProperty"/> or, without it, the environment variable <see cref="SharedSdkCacheVariable"/>
/// is 1, all evaluations of one project share one <see cref="MSB.Evaluation.Context.EvaluationContext.SharingPolicy.SharedSDKCache"/>
/// context.
/// </summary>
/// <remarks>
/// <para>Such a context shares the SDK resolver service, its results and the resolvers' state, and nothing else:
/// every evaluation still gets its own file system cache, so a later evaluation sees files a build created.</para>
/// <para>The service caches results by SDK name only. Within one project the other inputs of a resolver are the same
/// for every evaluation: the project path, the solution, global.json and NuGet.Config. Only the reference itself can
/// differ, when an import of the same SDK asks for another version or minimum version under another
/// TargetFramework. So after each evaluation that reused the context the host checks that every SDK import got a
/// result resolved for exactly the reference it asked for; if not, it evaluates the project again in a context of its
/// own and stops sharing for that project. The discarded evaluation's warnings never reach the loader: a build reaches
/// only the evaluation it was made from.</para>
/// <para>The late evaluation of GetAllGlobs is made right after the builds of the project, before the ProjectFileInfo
/// of its first build is read, where the first GetAllGlobs would have made it; nothing writes files in between. The
/// GetAllGlobs of each ProjectFileInfo then reads that evaluation.</para>
/// </remarks>
internal sealed partial class ProjectBuildManager
{
    public const string SharedSdkCacheVariable = "ROSLYN_BUILDHOST_SHARED_SDK_CACHE";

    /// <summary>
    /// The global property in which the host's client turns this mode on (1) or off for its workspace; it wins over
    /// <see cref="SharedSdkCacheVariable"/>, which is process-wide. Taken out before the project collection is created.
    /// </summary>
    public const string SharedSdkCacheProperty = "RoslynMcpSharedSdkCache";

    private bool _shareSdkCache = Environment.GetEnvironmentVariable(SharedSdkCacheVariable) == "1";

    private readonly ConditionalWeakTable<MSB.Evaluation.Project, MSB.Evaluation.Context.EvaluationContext> _evaluationContexts = new();

    private void InitializeSharedSdkCache(Dictionary<string, string> globalProperties)
    {
        if (TakeGlobalProperty(globalProperties, SharedSdkCacheProperty) is { } value)
            _shareSdkCache = value == "1";
    }

    private MSB.Evaluation.Project CreateProject(MSB.Construction.ProjectRootElement xml, MSB.Evaluation.ProjectLoadSettings projectLoadSettings)
    {
        if (!_shareSdkCache)
            return new MSB.Evaluation.Project(xml, globalProperties: null, toolsVersion: null, _projectCollection, projectLoadSettings);

        // The first evaluation in a new context resolves every SDK as an evaluation in a context of its own does.
        var context = MSB.Evaluation.Context.EvaluationContext.Create(MSB.Evaluation.Context.EvaluationContext.SharingPolicy.SharedSDKCache);
        var project = MSB.Evaluation.Project.FromProjectRootElement(xml, new MSB.Definition.ProjectOptions
        {
            ProjectCollection = _projectCollection,
            LoadSettings = projectLoadSettings,
            EvaluationContext = context,
        });

        _evaluationContexts.AddOrUpdate(project, context);
        return project;
    }

    private void ReevaluateIfNecessary(MSB.Evaluation.Project project)
    {
        if (!_evaluationContexts.TryGetValue(project, out var context))
        {
            project.ReevaluateIfNecessary();
            return;
        }

        project.ReevaluateIfNecessary(context);
        EnsureSdkResultsMatchReferences(project);
    }

    /// <summary>
    /// Makes the late evaluation that the first GetAllGlobs of a project makes, in the project's shared context.
    /// Without one it does nothing, and GetAllGlobs evaluates as upstream.
    /// </summary>
    public void EvaluateForGlobs(MSB.Evaluation.Project project)
    {
        if (!_evaluationContexts.TryGetValue(project, out var context))
            return;

        using var span = BuildHostTrace.Start("evaluate-globs", project.FullPath);
        project.GetAllGlobs(context);
        EnsureSdkResultsMatchReferences(project);
    }

    private void EnsureSdkResultsMatchReferences(MSB.Evaluation.Project project)
    {
        if (SdkResultsMatchReferences(project))
            return;

        using var span = BuildHostTrace.Start("evaluate-isolated", project.FullPath);
        _evaluationContexts.Remove(project);
        project.MarkDirty();
        project.ReevaluateIfNecessary();
    }

    /// <summary>
    /// Whether every SDK import of the last evaluation got a result resolved for the reference it asked for, rather
    /// than one cached for another reference to an SDK of the same name.
    /// </summary>
    private static bool SdkResultsMatchReferences(MSB.Evaluation.Project project)
    {
        foreach (var import in project.Imports)
        {
            var element = import.ImportingElement;
            if (string.IsNullOrEmpty(element.Sdk) || import.SdkResult is not { } result)
                continue;

            if (result.SdkReference is not { } resolvedFor ||
                !string.Equals(resolvedFor.Name, element.Sdk, StringComparison.OrdinalIgnoreCase) ||
                !SameVersion(resolvedFor.Version, element.Version) ||
                !SameVersion(resolvedFor.MinimumVersion, element.MinimumVersion))
            {
                return false;
            }
        }

        return true;

        // An absent attribute reads as an empty string, while a reference parsed from Sdk="Name" has a null version.
        static bool SameVersion(string? left, string? right)
            => string.Equals(left ?? "", right ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
