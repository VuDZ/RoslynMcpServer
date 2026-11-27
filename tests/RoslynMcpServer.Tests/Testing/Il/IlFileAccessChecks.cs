using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMcpServer.Tests.SourceStructure;

namespace RoslynMcpServer.Tests.Testing.Il;

/// <summary>
/// Shape check of the IL graph code: files are opened in one place, the image loader, and there only by a
/// <c>FileStream</c> built from the loader's open options. The sharing mode those options carry is what lets
/// a build write an assembly while it is read; the mode itself is checked by executing the options, and this
/// check proves that no other way to open a file exists next to it. On Linux the behavior test cannot tell
/// the modes apart, so the two checks together stand in for it there.
/// <para>
/// References are decided by symbol binding: a type named <c>File</c> of another namespace, a comment and
/// a string are not references, while an unresolved name of a file type makes the verdict undecidable.
/// </para>
/// </summary>
internal static class IlFileAccessChecks
{
    /// <summary>Violations in the files of <paramref name="analysis"/> that <paramref name="inScope"/> accepts; empty means the shape holds.</summary>
    public static IReadOnlyList<string> Verify(
        SourceSetAnalysis analysis,
        Func<string, bool> inScope,
        string loaderNamespace,
        string loaderTypeName,
        string openOptionsMethodName)
    {
        var violations = new List<string>(analysis.InputDiagnostics);
        var loader = analysis.FindType(loaderNamespace, loaderTypeName, violations);
        var openOptions = loader is null ? null : analysis.FindDeclaredMethod(loader, openOptionsMethodName, violations);
        var trees = analysis.Compilation.SyntaxTrees.Where(tree => inScope(tree.FilePath)).ToList();
        if (trees.Count == 0)
        {
            violations.Add($"no file of the scope is under the checked directory ({analysis.Scope.Description})");
        }

        if (violations.Count > 0 || loader is null || openOptions is null)
        {
            return violations;
        }

        var fileTypes = _fileTypeNames
            .Select(analysis.Compilation.GetTypeByMetadataName)
            .OfType<INamedTypeSymbol>()
            .ToHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var opens = new List<SyntaxNode>();
        foreach (var tree in trees)
        {
            var model = analysis.ModelFor(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                var type = FileTypeUsedBy(model, node, fileTypes, violations);
                if (type is null)
                {
                    continue;
                }

                var site = $"{SourceSetAnalysis.Describe(node)} `{node}`";
                if (!SymbolEqualityComparer.Default.Equals(EnclosingType(model, node), loader))
                {
                    violations.Add($"{site}: {type.ToDisplayString()} is used outside {loader.Name}; only the loader opens files");
                }
                else if (type.Name != nameof(FileStream))
                {
                    violations.Add($"{site}: {loader.Name} opens files only with a FileStream built from {openOptions.Name}()");
                }
                else if (CreationOf(node) is { } creation)
                {
                    opens.Add(creation);
                }
            }
        }

        if (opens.Count != 1)
        {
            violations.Add($"{loader.Name}: expected one place that opens a file, found {opens.Count}");
        }

        violations.AddRange(opens.Where(open => !UsesOpenOptions(analysis.ModelFor(open.SyntaxTree), open, openOptions))
            .Select(open => $"{SourceSetAnalysis.Describe(open)} `{open}`: the file is not opened with {openOptions.Name}()"));
        return violations;
    }

    /// <summary>
    /// The file type a node names or constructs, or null. A static member used without its type, as after
    /// <c>using static System.IO.File</c>, counts as well.
    /// </summary>
    private static INamedTypeSymbol? FileTypeUsedBy(SemanticModel model, SyntaxNode node, HashSet<INamedTypeSymbol> fileTypes, List<string> violations)
    {
        if (node is ImplicitObjectCreationExpressionSyntax)
        {
            return model.GetSymbolInfo(node).Symbol?.ContainingType is { } created && fileTypes.Contains(created) ? created : null;
        }

        if (node is not SimpleNameSyntax name)
        {
            return null;
        }

        var symbol = model.GetSymbolInfo(name).Symbol;
        if (symbol is null && fileTypes.Any(type => type.Name == name.Identifier.ValueText))
        {
            violations.Add($"{SourceSetAnalysis.Describe(name)} `{name}`: the name of a file type does not resolve, so the check cannot decide");
            return null;
        }

        return symbol switch
        {
            INamedTypeSymbol type when fileTypes.Contains(type) => type,
            IMethodSymbol { IsStatic: true, ContainingType: { } owner } when fileTypes.Contains(owner) && !IsQualified(name) => owner,
            _ => null,
        };
    }

    /// <summary>True for the member name of <c>File.OpenRead</c>: its type name is counted on its own.</summary>
    private static bool IsQualified(SimpleNameSyntax name) =>
        name.Parent is MemberAccessExpressionSyntax access && access.Name == name;

    private static INamedTypeSymbol? EnclosingType(SemanticModel model, SyntaxNode node)
    {
        var declaration = node.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();
        return declaration is null ? null : model.GetDeclaredSymbol(declaration);
    }

    private static SyntaxNode? CreationOf(SyntaxNode node) => node switch
    {
        ImplicitObjectCreationExpressionSyntax implicitCreation => implicitCreation,
        { Parent: ObjectCreationExpressionSyntax creation } when creation.Type == node => creation,
        { Parent: QualifiedNameSyntax { Parent: ObjectCreationExpressionSyntax qualified } } => qualified,
        _ => null,
    };

    private static bool UsesOpenOptions(SemanticModel model, SyntaxNode creation, IMethodSymbol openOptions)
    {
        var arguments = (creation as BaseObjectCreationExpressionSyntax)?.ArgumentList?.Arguments;
        return arguments is { Count: 2 }
            && arguments.Value[1].Expression is InvocationExpressionSyntax invocation
            && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(invocation).Symbol, openOptions);
    }

    private static readonly string[] _fileTypeNames =
    [
        "System.IO.File",
        "System.IO.FileStream",
        "System.IO.FileInfo",
        "System.IO.StreamReader",
        "System.IO.StreamWriter",
        "System.IO.MemoryMappedFiles.MemoryMappedFile",
    ];
}
