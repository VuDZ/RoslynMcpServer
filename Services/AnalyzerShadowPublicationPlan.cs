namespace RoslynMcpServer.Services;

internal enum AnalyzerShadowPublicationKind
{
    Allowed = 0,
    Banned = 1,
    Unavailable = 2,
}

/// <summary>
/// Publication decision for one prepare/refresh. Distinct from "a file was
/// prepared": prepared bytes do not imply <c>Applied=true</c> in the snapshot.
/// </summary>
internal sealed record AnalyzerShadowPublicationPlan(
    AnalyzerShadowPublicationKind Kind,
    SemanticPublicationAdmission Admission,
    AnalyzerShadowMapping Mapping,
    IReadOnlyList<ExcludedAnalyzerReference> Exclusions,
    AnalyzerExecutionObservation Gate,
    bool OverlayEnabled,
    bool RefreshComplete,
    int PreparedCount,
    int AppliedCount,
    int StaleCount,
    int BlockedCount,
    string? Reason)
{
    public bool IsPartial => BlockedCount > 0 || StaleCount > 0 || !RefreshComplete;
}

/// <summary>
/// Decides a safe snapshot for every confirmed overlay reference. One successful
/// prepare never publishes another confirmed reference as real output.
/// </summary>
internal static class AnalyzerShadowPublicationPlanner
{
    public static bool IsConfirmedOverlay(AnalyzerShadowReferenceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.ReasonCode is AnalyzerReferenceReasonCodes.ProvenForeignPath
            or AnalyzerReferenceReasonCodes.ProvenanceUnconfirmed)
        {
            return false;
        }

        return entry.Applied
            || entry.SelectionBasis == AnalyzerReferenceSelectionBasis.LoadSessionProvenanceExactOutput
            || entry.ReasonCode is AnalyzerReferenceReasonCodes.ReferenceRewritten
                or AnalyzerReferenceReasonCodes.PreparationFailure
                or AnalyzerReferenceReasonCodes.AccessFailure
                or AnalyzerReferenceReasonCodes.SourceOutputMissing;
    }

    public static AnalyzerShadowPublicationPlan Evaluate(
        AnalyzerShadowPrepareOutcome prepared,
        InProcessAnalyzerAssemblyLoader loader,
        bool restartBanLatched)
    {
        ArgumentNullException.ThrowIfNull(loader);
        var mapping = prepared.Mapping;
        if (restartBanLatched)
        {
            return BanRestart(
                mapping,
                new AnalyzerExecutionObservation
                {
                    Status = AnalyzerExecutionStatus.RestartRequired,
                    HighestStage = AnalyzerPreparationStage.Prepared,
                    Reason = AnalyzerLoaderContract.RestartRequiredReason,
                    Action = AnalyzerLoaderContract.RestartAction,
                });
        }

        AnalyzerExecutionObservation? restart = null;
        AnalyzerExecutionObservation? firstPrepared = null;
        var exclusions = new List<ExcludedAnalyzerReference>();
        var preparedCount = 0;
        var appliedCount = 0;
        var staleCount = 0;
        var blockedCount = 0;
        var sanitized = new List<AnalyzerShadowReferenceEntry>(mapping.Entries.Count);

        foreach (var entry in mapping.Entries)
        {
            if (!IsConfirmedOverlay(entry))
            {
                sanitized.Add(entry);
                continue;
            }

            var filePrepared = entry.Applied
                && !string.IsNullOrWhiteSpace(entry.ShadowCopyPath)
                && !entry.StaleGeneration;
            if (filePrepared)
            {
                preparedCount++;
            }

            if (entry.Applied && !string.IsNullOrWhiteSpace(entry.ShadowCopyPath))
            {
                var observation = AnalyzerExecutionGate.EvaluatePreparedEntry(entry, loader);
                if (observation.RequiresRestart)
                {
                    restart ??= observation;
                    blockedCount++;
                    exclusions.Add(ToExclusion(entry));
                    sanitized.Add(BlockEntry(entry, observation.Reason ?? AnalyzerLoaderContract.RestartRequiredReason));
                    continue;
                }

                if (observation.PermitsExecution)
                {
                    firstPrepared ??= observation;
                    appliedCount++;
                    if (entry.StaleGeneration)
                    {
                        staleCount++;
                    }

                    sanitized.Add(entry);
                    continue;
                }

                blockedCount++;
                exclusions.Add(ToExclusion(entry));
                sanitized.Add(BlockEntry(entry, observation.Reason ?? AnalyzerReferenceReasonCodes.PreparationFailure));
                continue;
            }

            blockedCount++;
            exclusions.Add(ToExclusion(entry));
            sanitized.Add(entry.Applied ? BlockEntry(entry, entry.SkipReason ?? "prepare-failed") : entry);
        }

        var publishedMapping = new AnalyzerShadowMapping(mapping.SessionId, mapping.LoadedPath, sanitized);

        if (restart is not null && appliedCount == 0)
        {
            return BanRestart(publishedMapping, restart, preparedCount, blockedCount);
        }

        var refreshComplete = prepared.RefreshSucceeded && blockedCount == 0 && staleCount == 0;
        if (appliedCount > 0)
        {
            var gate = blockedCount > 0 || staleCount > 0
                ? firstPrepared! with
                {
                    Status = AnalyzerExecutionStatus.Prepared,
                    HighestStage = AnalyzerPreparationStage.Prepared,
                    Reason = firstPrepared.Reason ?? "partial-prepare",
                }
                : firstPrepared!.WithStage(
                    AnalyzerPreparationStage.ReferenceRewritten,
                    AnalyzerExecutionStatus.ReferenceRewritten);
            return new AnalyzerShadowPublicationPlan(
                AnalyzerShadowPublicationKind.Allowed,
                SemanticPublicationAdmission.AllowedMapping,
                publishedMapping,
                exclusions,
                gate,
                OverlayEnabled: true,
                RefreshComplete: refreshComplete,
                preparedCount,
                appliedCount,
                staleCount,
                blockedCount,
                blockedCount > 0 || staleCount > 0 ? "partial-prepare" : null);
        }

        if (blockedCount > 0)
        {
            var reason = prepared.FailureSummary
                ?? restart?.Reason
                ?? "opt-in-prepare-not-enabled";
            return new AnalyzerShadowPublicationPlan(
                AnalyzerShadowPublicationKind.Banned,
                SemanticPublicationAdmission.Banned,
                publishedMapping,
                exclusions,
                restart ?? new AnalyzerExecutionObservation
                {
                    Status = AnalyzerExecutionStatus.LoadFailed,
                    HighestStage = AnalyzerPreparationStage.LoadFailed,
                    Reason = reason,
                },
                OverlayEnabled: false,
                RefreshComplete: false,
                preparedCount,
                AppliedCount: 0,
                StaleCount: 0,
                blockedCount,
                reason);
        }

        return new AnalyzerShadowPublicationPlan(
            AnalyzerShadowPublicationKind.Allowed,
            SemanticPublicationAdmission.AllowedMapping,
            publishedMapping,
            Array.Empty<ExcludedAnalyzerReference>(),
            AnalyzerExecutionObservation.None,
            OverlayEnabled: false,
            RefreshComplete: prepared.RefreshSucceeded,
            PreparedCount: 0,
            AppliedCount: 0,
            StaleCount: 0,
            BlockedCount: 0,
            Reason: null);
    }

    public static IReadOnlyList<AnalyzerReferenceShadowCopier.RewriteResult> ToRewriteResults(
        IReadOnlyList<AnalyzerReferenceShadowCopier.RewriteResult> results,
        AnalyzerShadowPublicationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Kind is AnalyzerShadowPublicationKind.Banned or AnalyzerShadowPublicationKind.Unavailable)
        {
            return results.Select(result =>
            {
                if (!result.Applied)
                {
                    return result;
                }

                return result with
                {
                    Applied = false,
                    SkipReason = plan.Reason ?? result.SkipReason,
                    StaleGeneration = result.StaleGeneration,
                    ReasonCode = AnalyzerReferenceReasonCodes.PreparationFailure,
                };
            }).ToList();
        }

        if (plan.Exclusions.Count == 0)
        {
            return results;
        }

        return results.Select(result =>
        {
            if (!result.Applied || !IsExcludedResult(result, plan.Exclusions))
            {
                return result;
            }

            return result with
            {
                Applied = false,
                SkipReason = plan.Reason ?? result.SkipReason ?? "blocked-from-publication",
                ReasonCode = AnalyzerReferenceReasonCodes.PreparationFailure,
            };
        }).ToList();
    }

    public static string FormatLoadSummary(
        AnalyzerShadowPublicationPlan plan,
        AnalyzerExecutionObservation execution)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var sb = new System.Text.StringBuilder();
        sb.Append("- **Analyzer reference shadow copy:** prepared=")
            .Append(plan.PreparedCount)
            .Append(", applied=")
            .Append(plan.AppliedCount)
            .Append(", stale=")
            .Append(plan.StaleCount)
            .Append(", blocked=")
            .Append(plan.BlockedCount)
            .Append('.');

        if (plan.Kind is AnalyzerShadowPublicationKind.Banned or AnalyzerShadowPublicationKind.Unavailable)
        {
            sb.Append(" Publication ")
                .Append(plan.Admission.ToString().ToLowerInvariant())
                .Append("; prepared files were not applied.");
        }
        else if (plan.IsPartial)
        {
            sb.Append(" Partial prepare; not a complete refresh of every generator.");
        }
        else if (plan.AppliedCount == 0)
        {
            sb.Append(plan.PreparedCount == 0
                ? " No in-solution analyzer candidates; nothing rewritten."
                : " No overlay applied.");
        }

        if (!string.IsNullOrWhiteSpace(plan.Reason)
            && plan.Kind is AnalyzerShadowPublicationKind.Banned or AnalyzerShadowPublicationKind.Unavailable)
        {
            sb.Append(" Reason: ").Append(plan.Reason).Append('.');
        }

        if (execution.Status is not AnalyzerExecutionStatus.None)
        {
            sb.AppendLine();
            sb.Append("- **Analyzer execution:** ").Append(execution.Status);
            if (!string.IsNullOrWhiteSpace(execution.Reason))
            {
                sb.Append(" — ").Append(execution.Reason);
            }

            if (!string.IsNullOrWhiteSpace(execution.Action))
            {
                sb.Append(" Action: ").Append(execution.Action).Append('.');
            }
        }

        sb.AppendLine();
        sb.Append("- **Publication:** ").Append(plan.Admission);
        return sb.ToString().TrimEnd();
    }

    private static AnalyzerShadowPublicationPlan BanRestart(
        AnalyzerShadowMapping mapping,
        AnalyzerExecutionObservation restart,
        int preparedCount = 0,
        int blockedCount = 0)
    {
        var exclusions = mapping.Entries
            .Where(IsConfirmedOverlay)
            .Select(ToExclusion)
            .ToArray();
        var sanitized = mapping.Entries
            .Select(entry => IsConfirmedOverlay(entry)
                ? BlockEntry(entry, restart.Reason ?? AnalyzerLoaderContract.RestartRequiredReason)
                : entry)
            .ToList();
        var countedPrepared = preparedCount > 0
            ? preparedCount
            : mapping.Entries.Count(e => e.Applied && !e.StaleGeneration && !string.IsNullOrWhiteSpace(e.ShadowCopyPath));
        var countedBlocked = blockedCount > 0 ? blockedCount : exclusions.Length;
        return new AnalyzerShadowPublicationPlan(
            AnalyzerShadowPublicationKind.Banned,
            SemanticPublicationAdmission.Banned,
            new AnalyzerShadowMapping(mapping.SessionId, mapping.LoadedPath, sanitized),
            exclusions,
            restart,
            OverlayEnabled: false,
            RefreshComplete: false,
            countedPrepared,
            AppliedCount: 0,
            StaleCount: 0,
            countedBlocked,
            restart.Reason ?? AnalyzerLoaderContract.RestartRequiredReason);
    }

    private static AnalyzerShadowReferenceEntry BlockEntry(AnalyzerShadowReferenceEntry entry, string reason)
    {
        return entry with
        {
            Applied = false,
            SkipReason = reason,
            StaleGeneration = entry.StaleGeneration,
            ReasonCode = AnalyzerReferenceReasonCodes.PreparationFailure,
        };
    }

    private static ExcludedAnalyzerReference ToExclusion(AnalyzerShadowReferenceEntry entry)
    {
        return new ExcludedAnalyzerReference(entry.ProjectId, entry.OriginalFullPath, AnalyzerId: null);
    }

    private static bool IsExcludedResult(
        AnalyzerReferenceShadowCopier.RewriteResult result,
        IReadOnlyList<ExcludedAnalyzerReference> exclusions)
    {
        foreach (var excluded in exclusions)
        {
            if (!string.IsNullOrWhiteSpace(result.OriginalFullPath)
                && !string.IsNullOrWhiteSpace(excluded.FullPath)
                && AnalyzerShadowMapping.PathsEqual(result.OriginalFullPath, excluded.FullPath))
            {
                return true;
            }
        }

        return false;
    }
}
