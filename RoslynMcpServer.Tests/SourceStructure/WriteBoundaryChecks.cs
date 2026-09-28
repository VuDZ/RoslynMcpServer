using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// The single write boundary to the workspace: <c>Workspace.TryApplyChanges</c> may be referenced from
/// exactly one place in the whole declared scope, and that place is the wrapper method that turns a
/// cleaned candidate solution into a workspace write.
/// <para>
/// The member is matched by symbol (<c>Microsoft.CodeAnalysis.Workspace.TryApplyChanges</c> on any
/// receiver of that type), so a same-named method of another type, a mention in a comment or a string
/// literal, and the property <c>FailNextTryApplyChanges</c> are not call sites. The check spans the
/// declared scope instead of one file, and a name reference that does not bind is reported as a
/// diagnostic: an unresolved <c>TryApplyChanges</c> could be the second write boundary.
/// </para>
/// </summary>
internal static class WriteBoundaryChecks
{
    public const string WorkspaceMetadataName = "Microsoft.CodeAnalysis.Workspace";
    public const string ApplyChangesMember = "TryApplyChanges";
    public const string WrapperMethod = "TryApplyWorkspaceChanges";
    public const string RevertedOverlayHelper = "RevertAnalyzerReferenceOverlayForApply";

    /// <summary>
    /// Violations of the write boundary in <paramref name="analysis"/>; an empty list means the
    /// contract holds. <paramref name="containerNamespace"/> and <paramref name="containerTypeName"/>
    /// name the only type allowed to hold the wrapper method.
    /// </summary>
    public static IReadOnlyList<string> Verify(
        SourceSetAnalysis analysis,
        string containerNamespace,
        string containerTypeName)
    {
        var violations = new List<string>(analysis.InputDiagnostics);
        var container = analysis.FindType(containerNamespace, containerTypeName, violations);
        var workspace = analysis.Compilation.GetTypeByMetadataName(WorkspaceMetadataName);
        if (workspace is null)
        {
            violations.Add($"{WorkspaceMetadataName}: type not found in the declared scope references");
            return violations;
        }

        if (container is null)
        {
            return violations;
        }

        if (analysis.FindDeclaredMethod(container, WrapperMethod, violations) is null)
        {
            return violations;
        }

        var references = analysis.FindReferences(ApplyChangesMember);
        var callSites = new List<SourceReference>();
        foreach (var reference in references)
        {
            if (!reference.IsResolved)
            {
                violations.Add(
                    $"{reference.Describe()}: reference to {ApplyChangesMember} does not bind, so the write-boundary count is undecidable");
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(reference.Symbol!.ContainingType?.OriginalDefinition, workspace))
            {
                callSites.Add(reference);
            }
        }

        if (callSites.Count == 0)
        {
            violations.Add($"{WorkspaceMetadataName}.{ApplyChangesMember}: no reference found in the declared scope");
        }
        else if (callSites.Count > 1)
        {
            violations.Add(
                $"{WorkspaceMetadataName}.{ApplyChangesMember}: {callSites.Count} reference sites in the declared scope "
                + $"({string.Join(", ", callSites.Select(site => $"{site.FilePath}:{site.Line}"))}); the write boundary must be single");
        }

        foreach (var callSite in callSites)
        {
            var enclosing = callSite.Node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            var symbol = enclosing is null ? null : analysis.ModelFor(enclosing.SyntaxTree).GetDeclaredSymbol(enclosing);
            if (symbol is null)
            {
                violations.Add($"{callSite.Describe()}: call site is not inside a method of the declared scope");
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(symbol.ContainingType.OriginalDefinition, container.OriginalDefinition)
                || !string.Equals(symbol.Name, WrapperMethod, StringComparison.Ordinal))
            {
                violations.Add(
                    $"{callSite.Describe()}: {ApplyChangesMember} is called from {symbol.ContainingType.Name}.{symbol.Name}, "
                    + $"not from {container.Name}.{WrapperMethod}");
            }
        }

        foreach (var declaration in analysis.FindDeclaredMembers(RevertedOverlayHelper))
        {
            violations.Add(
                $"{declaration.ToDisplayString()}: {RevertedOverlayHelper} exists in the declared scope; "
                + "the write boundary must not revert the analyzer overlay separately");
        }

        return violations;
    }
}
