using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RoslynMcpServer.Services;

/// <summary>
/// Binding gate for epoch 3. File preparation does not permit execution when the
/// process already holds the same assembly identity or the generator needs a
/// private helper. Does not guess a dependency-set from output <c>*.dll</c>.
/// </summary>
internal static class AnalyzerExecutionGate
{
    public static AnalyzerExecutionObservation EvaluatePreparedEntry(
        AnalyzerShadowReferenceEntry entry,
        InProcessAnalyzerAssemblyLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);

        if (!entry.Applied || string.IsNullOrWhiteSpace(entry.ShadowCopyPath))
        {
            var missing = IsMissingPreparedFile(entry.SkipReason);
            return new AnalyzerExecutionObservation
            {
                Status = missing ? AnalyzerExecutionStatus.LoadFailed : AnalyzerExecutionStatus.None,
                HighestStage = missing ? AnalyzerPreparationStage.LoadFailed : AnalyzerPreparationStage.None,
                Reason = missing
                    ? (entry.SkipReason ?? "prepared file missing")
                    : entry.SkipReason,
                ProjectName = entry.ProjectName,
                GeneratorName = entry.MatchedProjectName,
                GenerationId = entry.GenerationId,
                ExpectedPath = entry.ShadowCopyPath ?? entry.OriginalFullPath,
            };
        }

        return EvaluateAssemblyPath(
            entry.ShadowCopyPath,
            entry.ProjectName,
            entry.MatchedProjectName,
            entry.GenerationId,
            loader);
    }

    /// <summary>Test counter: path evaluations (identity/dependency probing). Ordinary publication must not increment this.</summary>
    internal static int AssemblyEvaluationCount;

    public static AnalyzerExecutionObservation EvaluateAssemblyPath(
        string assemblyPath,
        string? projectName,
        string? generatorName,
        string? generationId,
        InProcessAnalyzerAssemblyLoader loader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentNullException.ThrowIfNull(loader);
        AssemblyEvaluationCount++;

        var inspection = AnalyzerPrivateDependencyInspector.Inspect(assemblyPath);
        if (inspection.FailureReason == "source-missing")
        {
            return new AnalyzerExecutionObservation
            {
                Status = AnalyzerExecutionStatus.LoadFailed,
                HighestStage = AnalyzerPreparationStage.LoadFailed,
                Reason = "prepared file missing: " + assemblyPath,
                ProjectName = projectName,
                GeneratorName = generatorName,
                GenerationId = generationId,
                ExpectedPath = assemblyPath,
            };
        }

        if (inspection.PrivateReferences.Count > 0)
        {
            var dependency = inspection.PrivateReferences[0].Name;
            return new AnalyzerExecutionObservation
            {
                Status = AnalyzerExecutionStatus.DependencyUnsupported,
                HighestStage = AnalyzerPreparationStage.Prepared,
                Reason = AnalyzerLoaderContract.DependencyUnsupportedReason + ": " + dependency,
                Action = AnalyzerLoaderContract.DependencyUnsupportedAction,
                ProjectName = projectName,
                GeneratorName = generatorName,
                GenerationId = generationId,
                DependencyName = dependency,
                ExpectedPath = assemblyPath,
                AssemblyIdentity = FormatIdentity(inspection.Identity),
            };
        }

        if (loader.TryGetIdentityCollision(assemblyPath, out var loaded)
            || TryGetProcessIdentityCollision(assemblyPath, out loaded))
        {
            return new AnalyzerExecutionObservation
            {
                Status = loaded.RequestedPath.Length == 0
                    ? AnalyzerExecutionStatus.IdentityCollision
                    : AnalyzerExecutionStatus.RestartRequired,
                HighestStage = AnalyzerPreparationStage.Prepared,
                Reason = PathsEqual(loaded.Location, assemblyPath)
                    ? AnalyzerLoaderContract.RestartRequiredReason
                    : AnalyzerLoaderContract.IdentityCollisionReason,
                Action = AnalyzerLoaderContract.RestartAction,
                ProjectName = projectName,
                GeneratorName = generatorName,
                GenerationId = generationId,
                ExpectedPath = assemblyPath,
                LoadedPath = loaded.Location,
                AssemblyIdentity = loaded.Identity,
            };
        }

        return new AnalyzerExecutionObservation
        {
            Status = AnalyzerExecutionStatus.Prepared,
            HighestStage = AnalyzerPreparationStage.Prepared,
            ProjectName = projectName,
            GeneratorName = generatorName,
            GenerationId = generationId,
            ExpectedPath = assemblyPath,
            AssemblyIdentity = FormatIdentity(inspection.Identity),
        };
    }

    public static AnalyzerExecutionObservation EvaluateInSolutionAnalyzers(
        Solution solution,
        AnalyzerProvenanceSnapshot? provenanceSnapshot,
        Guid sessionId,
        InProcessAnalyzerAssemblyLoader loader)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(loader);

        AnalyzerExecutionObservation? firstBlock = null;
        foreach (var item in AnalyzerReferenceShadowCopier.EnumerateInSolutionAnalyzerRefs(
                     solution,
                     provenanceSnapshot,
                     sessionId))
        {
            var path = item.MatchedProject.CompilationOutputInfo.AssemblyPath
                ?? item.MatchedProject.OutputFilePath
                ?? item.AnalyzerReference.FullPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                continue;
            }

            var observation = EvaluateAssemblyPath(
                Path.GetFullPath(path),
                item.Project.Name,
                item.MatchedProject.Name,
                generationId: null,
                loader);
            if (!observation.PermitsExecution)
            {
                firstBlock ??= observation;
            }
        }

        return firstBlock ?? AnalyzerExecutionObservation.None;
    }

    public static Solution StripInSolutionAnalyzerReferences(
        Solution solution,
        AnalyzerProvenanceSnapshot? provenanceSnapshot,
        Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(solution);

        var blockedByProject = AnalyzerReferenceShadowCopier
            .EnumerateInSolutionAnalyzerRefs(solution, provenanceSnapshot, sessionId)
            .GroupBy(item => item.Project.Id)
            .ToDictionary(
                g => g.Key,
                g => g.Select(item => item.AnalyzerReference).ToList());
        if (blockedByProject.Count == 0)
        {
            return solution;
        }

        foreach (var (projectId, blocked) in blockedByProject)
        {
            var project = solution.GetProject(projectId);
            if (project is null)
            {
                continue;
            }

            var kept = project.AnalyzerReferences.Except(blocked).ToList();
            if (kept.Count != project.AnalyzerReferences.Count)
            {
                solution = solution.WithProjectAnalyzerReferences(projectId, kept);
            }
        }

        return solution;
    }

    public static Solution BlockUnsupportedReferences(
        Solution solution,
        AnalyzerProvenanceSnapshot? provenanceSnapshot,
        Guid sessionId,
        InProcessAnalyzerAssemblyLoader loader)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(loader);

        var pendingByProject = AnalyzerReferenceShadowCopier
            .EnumerateInSolutionAnalyzerRefs(solution, provenanceSnapshot, sessionId)
            .GroupBy(item => item.Project.Id)
            .ToDictionary(group => group.Key, group => group.ToList());
        foreach (var (projectId, pending) in pendingByProject)
        {
            var project = solution.GetProject(projectId);
            if (project is null || project.AnalyzerReferences.Count == 0)
            {
                continue;
            }

            var changed = false;
            var kept = new List<AnalyzerReference>(project.AnalyzerReferences.Count);
            foreach (var reference in project.AnalyzerReferences)
            {
                var item = pending.FirstOrDefault(candidate =>
                    candidate.AnalyzerReference == reference
                    || (!string.IsNullOrWhiteSpace(candidate.AnalyzerReference.FullPath)
                        && !string.IsNullOrWhiteSpace(reference.FullPath)
                        && AnalyzerShadowMapping.PathsEqual(
                            candidate.AnalyzerReference.FullPath,
                            reference.FullPath)));
                if (item.Project is not null
                    && ShouldBlock(reference, item.MatchedProject, loader))
                {
                    changed = true;
                    continue;
                }

                kept.Add(reference);
            }

            if (changed)
            {
                solution = solution.WithProjectAnalyzerReferences(projectId, kept);
            }
        }

        return solution;
    }

    public static AnalyzerExecutionObservation ObserveFirstUse(
        Project project,
        AnalyzerExecutionObservation current,
        InProcessAnalyzerAssemblyLoader loader)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(loader);

        if (!current.PermitsExecution)
        {
            return current;
        }

        foreach (var reference in project.AnalyzerReferences)
        {
            if (string.IsNullOrWhiteSpace(reference.FullPath) || !File.Exists(reference.FullPath))
            {
                continue;
            }

            var pathObservation = EvaluateAssemblyPath(
                reference.FullPath,
                project.Name,
                Path.GetFileNameWithoutExtension(reference.FullPath),
                current.GenerationId,
                loader);
            if (!pathObservation.PermitsExecution)
            {
                return pathObservation with { HighestStage = AnalyzerPreparationStage.LoadFailed };
            }

            try
            {
                _ = reference.GetAnalyzers(project.Language);
            }
            catch (Exception ex)
            {
                return new AnalyzerExecutionObservation
                {
                    Status = AnalyzerExecutionStatus.LoadFailed,
                    HighestStage = AnalyzerPreparationStage.LoadFailed,
                    Reason = "analyzer-load:" + ex.GetType().Name + ":" + ex.Message,
                    ProjectName = project.Name,
                    GeneratorName = Path.GetFileNameWithoutExtension(reference.FullPath),
                    GenerationId = current.GenerationId,
                    ExpectedPath = reference.FullPath,
                    AssemblyIdentity = current.AssemblyIdentity,
                };
            }
        }

        return current.WithStage(
            AnalyzerPreparationStage.ExecutionObserved,
            AnalyzerExecutionStatus.ExecutionObserved);
    }

    private static bool ShouldBlock(
        AnalyzerReference reference,
        Project matchedProject,
        InProcessAnalyzerAssemblyLoader loader)
    {
        if (string.IsNullOrWhiteSpace(reference.FullPath))
        {
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath(reference.FullPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (!File.Exists(full))
        {
            return false;
        }

        var observation = EvaluateAssemblyPath(
            full,
            matchedProject.Name,
            matchedProject.AssemblyName,
            generationId: null,
            loader);
        return !observation.PermitsExecution;
    }

    internal static bool TryGetProcessIdentityCollision(
        string assemblyPath,
        out InProcessAnalyzerAssemblyLoader.LoadedAnalyzerAssembly loaded)
    {
        loaded = default;
        AssemblyName identity;
        try
        {
            identity = AssemblyName.GetAssemblyName(assemblyPath);
        }
        catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or ArgumentException)
        {
            return false;
        }

        if (string.IsNullOrEmpty(identity.Name))
        {
            return false;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            var name = assembly.GetName();
            if (!string.Equals(name.Name, identity.Name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!Equals(name.Version, identity.Version))
            {
                continue;
            }

            if (string.IsNullOrEmpty(assembly.Location))
            {
                continue;
            }

            if (PathsEqual(assembly.Location, assemblyPath))
            {
                return false;
            }

            loaded = new InProcessAnalyzerAssemblyLoader.LoadedAnalyzerAssembly(
                assembly.Location,
                assembly.GetName().FullName ?? assembly.FullName ?? identity.Name,
                assembly.Location);
            return true;
        }

        return false;
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(left, right, comparison);
        }
    }

    private static bool IsMissingPreparedFile(string? skipReason)
    {
        if (string.IsNullOrWhiteSpace(skipReason))
        {
            return false;
        }

        return skipReason.Contains("no existing resolved output", StringComparison.OrdinalIgnoreCase)
            || skipReason.Contains("source-missing", StringComparison.OrdinalIgnoreCase)
            || skipReason.Contains("prepared file missing", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FormatIdentity(AssemblyName? name)
    {
        return name?.FullName ?? name?.Name;
    }
}
