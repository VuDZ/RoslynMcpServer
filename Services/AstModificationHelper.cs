using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Formatting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;

namespace RoslynMcpServer.Services;

public static class AstModificationHelper
{
    public static Task<Document> AddUsingAsync(Document document, string namespaceName, CancellationToken cancellationToken) =>
        MutateAsync(document, async (doc, root, ct) =>
        {
            var compilationUnit = TypeSyntaxHelper.RequireCompilationUnit(root);
            var normalized = NormalizeNamespace(namespaceName);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new ArgumentException("Namespace name is empty.");
            }

            if (HasUsing(compilationUnit, normalized))
            {
                throw new InvalidOperationException($"Using `{normalized}` is already present in the file.");
            }

            var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(normalized))
                .WithTrailingTrivia(SyntaxFactory.EndOfLine(Environment.NewLine));
            return doc.WithSyntaxRoot(compilationUnit.WithUsings(compilationUnit.Usings.Add(usingDirective)));
        }, cancellationToken);

    public static Task<Document> RemoveUsingAsync(Document document, string namespaceName, CancellationToken cancellationToken) =>
        MutateAsync(document, (doc, root, _) =>
        {
            var compilationUnit = TypeSyntaxHelper.RequireCompilationUnit(root);
            var normalized = NormalizeNamespace(namespaceName);
            var target = SyntaxFactory.ParseName(normalized).ToString();
            UsingDirectiveSyntax? toRemove = null;
            foreach (var u in compilationUnit.Usings)
            {
                if (u.Name is not null && string.Equals(u.Name.ToString(), target, StringComparison.Ordinal))
                {
                    toRemove = u;
                    break;
                }
            }

            if (toRemove is null)
            {
                throw new InvalidOperationException($"Using `{normalized}` was not found.");
            }

            return Task.FromResult(doc.WithSyntaxRoot(compilationUnit.RemoveNode(toRemove, SyntaxRemoveOptions.KeepNoTrivia)!));
        }, cancellationToken);

    public static Task<Document> OrganizeUsingsAsync(Document document, bool removeUnused, CancellationToken cancellationToken) =>
        MutateAsync(document, async (doc, root, ct) =>
        {
            var compilationUnit = TypeSyntaxHelper.RequireCompilationUnit(root);
            var model = await doc.GetSemanticModelAsync(ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Could not obtain semantic model.");

            var usings = compilationUnit.Usings.ToList();
            if (removeUnused)
            {
                usings = usings.Where(u => !IsUsingUnused(u, compilationUnit, model)).ToList();
            }

            usings = usings
                .OrderBy(u => u.Name?.ToString(), StringComparer.OrdinalIgnoreCase)
                .Distinct(UsingDirectiveComparer.Instance)
                .ToList();

            return doc.WithSyntaxRoot(compilationUnit.WithUsings(new SyntaxList<UsingDirectiveSyntax>(usings)));
        }, cancellationToken);

    public static Task<Document> AddMethodToClassAsync(Document document, string className, string methodSource, CancellationToken cancellationToken) =>
        AddMemberAsync(document, className, methodSource, m => m is MethodDeclarationSyntax, "method", cancellationToken);

    public static Task<Document> AddPropertyToClassAsync(Document document, string className, string propertySource, CancellationToken cancellationToken) =>
        AddMemberAsync(document, className, propertySource, m => m is PropertyDeclarationSyntax, "property", cancellationToken);

    public static Task<Document> AddFieldToClassAsync(Document document, string className, string fieldSource, CancellationToken cancellationToken) =>
        AddMemberAsync(document, className, fieldSource, m => m is FieldDeclarationSyntax, "field", cancellationToken);

    public static Task<Document> RemoveMemberAsync(Document document, string className, string memberName, CancellationToken cancellationToken) =>
        MutateAsync(document, async (doc, root, ct) =>
        {
            var classDecl = TypeSyntaxHelper.FindClassDeclaration(root, className.Trim())
                ?? throw new InvalidOperationException($"Class `{className}` not found.");

            var member = classDecl.Members.FirstOrDefault(m => GetMemberName(m) == memberName.Trim())
                ?? throw new InvalidOperationException($"Member `{memberName}` not found in class `{className}`.");

            var editor = await DocumentEditor.CreateAsync(doc, ct).ConfigureAwait(false);
            editor.RemoveNode(member);
            return editor.GetChangedDocument();
        }, cancellationToken);

    public static string NormalizeNamespaceForDisplay(string raw) => NormalizeNamespace(raw);

    private static async Task<Document> AddMemberAsync(
        Document document,
        string className,
        string memberSource,
        Func<MemberDeclarationSyntax, bool> kindCheck,
        string kindLabel,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(memberSource))
        {
            throw new ArgumentException($"{kindLabel} source is empty.");
        }

        var member = SyntaxFactory.ParseMemberDeclaration(memberSource.Trim())
            ?? throw new InvalidOperationException($"Could not parse {kindLabel} source.");

        if (!kindCheck(member))
        {
            throw new InvalidOperationException($"Source is not a {kindLabel} declaration.");
        }

        return await MutateAsync(document, async (doc, root, ct) =>
        {
            var classDecl = TypeSyntaxHelper.FindClassDeclaration(root, className.Trim())
                ?? throw new InvalidOperationException($"Class `{className}` not found in file.");

            var editor = await DocumentEditor.CreateAsync(doc, ct).ConfigureAwait(false);
            editor.AddMember(classDecl, member);
            return editor.GetChangedDocument();
        }, cancellationToken);
    }

    private static async Task<Document> MutateAsync(
        Document document,
        Func<Document, SyntaxNode, CancellationToken, Task<Document>> mutate,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Could not obtain syntax tree.");
        var updated = await mutate(document, root, cancellationToken).ConfigureAwait(false);
        return await Formatter.FormatAsync(updated, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static string? GetMemberName(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax m => m.Identifier.Text,
        PropertyDeclarationSyntax p => p.Identifier.Text,
        EventDeclarationSyntax e => e.Identifier.Text,
        FieldDeclarationSyntax f => f.Declaration.Variables.FirstOrDefault()?.Identifier.Text,
        _ => null
    };

    private static bool IsUsingUnused(UsingDirectiveSyntax usingDirective, CompilationUnitSyntax compilationUnit, SemanticModel model)
    {
        if (usingDirective.Name is null)
        {
            return false;
        }

        var symbolInfo = model.GetSymbolInfo(usingDirective.Name);
        if (symbolInfo.Symbol is not INamespaceSymbol namespaceSymbol)
        {
            return false;
        }

        foreach (var node in compilationUnit.Members.SelectMany(m => m.DescendantNodesAndSelf()))
        {
            if (node is UsingDirectiveSyntax)
            {
                continue;
            }

            if (node is IdentifierNameSyntax id)
            {
                var info = model.GetSymbolInfo(id);
                if (SymbolBelongsToNamespace(info.Symbol, namespaceSymbol))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool SymbolBelongsToNamespace(ISymbol? symbol, INamespaceSymbol ns)
    {
        if (symbol is null)
        {
            return false;
        }

        var containing = symbol.ContainingNamespace;
        while (containing is not null && !containing.IsGlobalNamespace)
        {
            if (SymbolEqualityComparer.Default.Equals(containing, ns))
            {
                return true;
            }

            containing = containing.ContainingNamespace;
        }

        return false;
    }

    private static string NormalizeNamespace(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("using ", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["using ".Length..].Trim();
        }

        if (trimmed.EndsWith(';'))
        {
            trimmed = trimmed[..^1].Trim();
        }

        return trimmed;
    }

    private static bool HasUsing(CompilationUnitSyntax compilationUnit, string namespaceName)
    {
        var target = SyntaxFactory.ParseName(namespaceName).ToString();
        return compilationUnit.Usings.Any(u =>
            u.Alias is null
            && u.StaticKeyword.IsKind(SyntaxKind.None)
            && u.GlobalKeyword.IsKind(SyntaxKind.None)
            && u.Name is not null
            && string.Equals(u.Name.ToString(), target, StringComparison.Ordinal));
    }

    private sealed class UsingDirectiveComparer : IEqualityComparer<UsingDirectiveSyntax>
    {
        public static UsingDirectiveComparer Instance { get; } = new();

        public bool Equals(UsingDirectiveSyntax? x, UsingDirectiveSyntax? y) =>
            string.Equals(x?.Name?.ToString(), y?.Name?.ToString(), StringComparison.Ordinal);

        public int GetHashCode(UsingDirectiveSyntax obj) =>
            obj.Name?.ToString().GetHashCode(StringComparison.Ordinal) ?? 0;
    }
}
