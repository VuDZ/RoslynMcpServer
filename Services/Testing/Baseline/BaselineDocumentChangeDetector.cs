using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcpServer.Services.Testing.Baseline.Models;

namespace RoslynMcpServer.Services.Testing.Baseline;

internal static class BaselineDocumentChangeDetector
{
    internal static ImmutableArray<BaselineDocumentChange> Compare(
        BaselineDocumentSnapshot baseline,
        BaselineDocumentSnapshot current)
    {
        var before = baseline.Members.GroupBy(member => member.Key).ToDictionary(group => group.Key, group => group.ToArray());
        var after = current.Members.GroupBy(member => member.Key).ToDictionary(group => group.Key, group => group.ToArray());
        var memberChanges = ImmutableArray.CreateBuilder<BaselineDocumentChange>();
        var ambiguous = false;
        var ambiguousChanged = false;
        foreach (var key in before.Keys.Concat(after.Keys).Distinct())
        {
            var oldMembers = before.GetValueOrDefault(key) ?? [];
            var newMembers = after.GetValueOrDefault(key) ?? [];
            if (oldMembers.Length > 1 || newMembers.Length > 1)
            {
                ambiguous = true;
                ambiguousChanged |= !oldMembers.Select(member => member.SyntaxHash).Order(StringComparer.Ordinal)
                    .SequenceEqual(newMembers.Select(member => member.SyntaxHash).Order(StringComparer.Ordinal));
                continue;
            }

            AddMemberChanges(memberChanges, oldMembers.SingleOrDefault(), newMembers.SingleOrDefault());
        }

        var changes = OutsideChanges(baseline, current);
        var directivesChanged = baseline.DirectivesHash != current.DirectivesHash;
        if (changes.Count == 0 && memberChanges.Count == 0 && !ambiguousChanged && !directivesChanged
            && baseline.HasUnparseableContent == current.HasUnparseableContent)
        {
            return [];
        }

        AddFallbacks(changes, baseline, current, ambiguous, directivesChanged, memberChanges.Count);
        var broken = baseline.HasUnparseableContent || current.HasUnparseableContent;
        foreach (var change in memberChanges)
        {
            if (broken && !change.IsTestMethod && !change.IsOwnTestClassRoot)
            {
                continue;
            }
            changes.Add(change);
            AddBindingExpansion(changes, change, after);
        }
        return changes.ToImmutable();
    }

    private static void AddMemberChanges(
        ImmutableArray<BaselineDocumentChange>.Builder changes,
        BaselineMemberSnapshot? baseline,
        BaselineMemberSnapshot? current)
    {
        if (current == null)
        {
            changes.Add(new BaselineDocumentChange(BaselineChangeKind.MemberRemoved,
                BaselineChangeReason.MemberSyntax, baseline!.Key));
            return;
        }
        if (baseline != null && baseline.SyntaxHash == current.SyntaxHash)
        {
            return;
        }

        if (baseline != null && IsType(current.Key.Kind))
        {
            changes.Add(new BaselineDocumentChange(BaselineChangeKind.TypeChanged,
                BaselineChangeReason.TypeSurface, current.Key));
            return;
        }

        // R-17 (docs/rapid-tia/README.md): promoting a helper must not hide its possible callers outside this test class.
        var root = current.IsOwnTestClassRoot && (baseline == null || baseline.IsOwnTestClassRoot
            && (current.InitializerContextHash == null || current.InitializerContextHash == baseline.InitializerContextHash));
        var blocksNarrowing = current.IsTestMethod && (current.BlocksNarrowing
            || baseline != null && (!baseline.IsTestMethod || baseline.BlocksNarrowing));
        changes.Add(new BaselineDocumentChange(
            baseline == null ? BaselineChangeKind.MemberAdded : BaselineChangeKind.MemberModified,
            BaselineChangeReason.MemberSyntax, current.Key, current.IsTestMethod, root, blocksNarrowing));
    }

    private static ImmutableArray<BaselineDocumentChange>.Builder OutsideChanges(
        BaselineDocumentSnapshot baseline, BaselineDocumentSnapshot current)
    {
        var changes = ImmutableArray.CreateBuilder<BaselineDocumentChange>();
        if (baseline.ImportsAndNamespacesHash != current.ImportsAndNamespacesHash)
        {
            changes.Add(new BaselineDocumentChange(BaselineChangeKind.FileChanged, BaselineChangeReason.ImportsAndNamespaces));
        }
        if (baseline.GlobalUsingsHash != current.GlobalUsingsHash)
        {
            changes.Add(new BaselineDocumentChange(BaselineChangeKind.ProjectChanged, BaselineChangeReason.GlobalUsings));
        }
        if (baseline.AssemblyAndModuleAttributesHash != current.AssemblyAndModuleAttributesHash)
        {
            changes.Add(new BaselineDocumentChange(BaselineChangeKind.ProjectChanged,
                BaselineChangeReason.AssemblyAndModuleAttributes));
        }
        return changes;
    }

    private static void AddFallbacks(
        ImmutableArray<BaselineDocumentChange>.Builder changes,
        BaselineDocumentSnapshot baseline,
        BaselineDocumentSnapshot current,
        bool ambiguous,
        bool directivesChanged,
        int memberChanges)
    {
        if (baseline.HasUnparseableContent || current.HasUnparseableContent)
        {
            changes.Add(new BaselineDocumentChange(BaselineChangeKind.FileChanged, BaselineChangeReason.UnparseableContent));
        }
        if (ambiguous)
        {
            changes.Add(new BaselineDocumentChange(BaselineChangeKind.FileChanged, BaselineChangeReason.AmbiguousMemberKey));
        }
        if (directivesChanged && memberChanges == 0 && !changes.Any(change => change.Kind == BaselineChangeKind.FileChanged))
        {
            // Member hashes already cover directives inside their syntax; standalone directives need file expansion.
            changes.Add(new BaselineDocumentChange(BaselineChangeKind.FileChanged, BaselineChangeReason.Directives));
        }
    }

    private static void AddBindingExpansion(
        ImmutableArray<BaselineDocumentChange>.Builder changes,
        BaselineDocumentChange change,
        Dictionary<BaselineMemberKey, BaselineMemberSnapshot[]> current)
    {
        if (change.Kind is not (BaselineChangeKind.MemberAdded or BaselineChangeKind.MemberModified))
        {
            return;
        }

        if (change.Key!.Kind is SyntaxKind.OperatorDeclaration or SyntaxKind.ConversionOperatorDeclaration)
        {
            changes.Add(new BaselineDocumentChange(BaselineChangeKind.ProjectChanged,
                BaselineChangeReason.OperatorOrConversion, change.Key));
        }
        else if (change.Kind == BaselineChangeKind.MemberAdded && current[change.Key][0].IsExtensionMethod)
        {
            changes.Add(new BaselineDocumentChange(BaselineChangeKind.ProjectChanged,
                BaselineChangeReason.NewExtensionMethod, change.Key));
        }
    }

    private static bool IsType(SyntaxKind kind) => kind is SyntaxKind.ClassDeclaration or SyntaxKind.StructDeclaration
        or SyntaxKind.InterfaceDeclaration or SyntaxKind.RecordDeclaration or SyntaxKind.RecordStructDeclaration
        or SyntaxKind.EnumDeclaration or SyntaxKind.DelegateDeclaration;
}
