using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Formatting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;

namespace RoslynMcpServer.Services;

public static class AstModificationHelper
{
    public static async Task<Document> AddUsingAsync(
        Document document,
        string namespaceName,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeNamespace(namespaceName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Namespace name is empty.");
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is not CompilationUnitSyntax compilationUnit)
        {
            throw new InvalidOperationException("Could not obtain compilation unit.");
        }

        if (HasUsing(compilationUnit, normalized))
        {
            throw new InvalidOperationException($"Using `{normalized}` is already present in the file.");
        }

        var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(normalized))
            .WithTrailingTrivia(SyntaxFactory.EndOfLine(Environment.NewLine));

        var newRoot = compilationUnit.WithUsings(compilationUnit.Usings.Add(usingDirective));
        var updated = document.WithSyntaxRoot(newRoot);
        return await Formatter.FormatAsync(updated, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Document> AddMethodToClassAsync(
        Document document,
        string className,
        string methodSource,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            throw new ArgumentException("Class name is empty.");
        }

        if (string.IsNullOrWhiteSpace(methodSource))
        {
            throw new ArgumentException("Method source is empty.");
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            throw new InvalidOperationException("Could not obtain syntax tree.");
        }

        var classDecl = FindTopLevelClass(root, className.Trim())
            ?? throw new InvalidOperationException($"Class `{className}` was not found as a top-level declaration in the file.");

        if (IsPartial(classDecl))
        {
            throw new InvalidOperationException("add_method_to_class does not support partial classes.");
        }

        var methodDecl = ParseMethodDeclaration(methodSource.Trim());
        if (HasMethodWithSameSignature(classDecl, methodDecl))
        {
            throw new InvalidOperationException(
                $"Class `{className}` already contains a method `{methodDecl.Identifier.Text}` with the same parameter signature.");
        }

        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        editor.AddMember(classDecl, methodDecl);
        var changed = editor.GetChangedDocument();
        return await Formatter.FormatAsync(changed, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public static string NormalizeNamespaceForDisplay(string raw) => NormalizeNamespace(raw);

    private static MethodDeclarationSyntax ParseMethodDeclaration(string methodSource)
    {
        var wrapped = methodSource.Contains('{', StringComparison.Ordinal)
            || methodSource.TrimEnd().EndsWith(";", StringComparison.Ordinal)
            ? methodSource
            : methodSource + " { throw new NotImplementedException(); }";

        var member = SyntaxFactory.ParseMemberDeclaration(wrapped);
        if (member is null)
        {
            throw new InvalidOperationException("Could not parse `methodSource` as a C# method declaration.");
        }

        if (member is not MethodDeclarationSyntax method)
        {
            throw new InvalidOperationException("`methodSource` must be a method declaration (not property, field, or constructor).");
        }

        return method;
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

    private static bool HasMethodWithSameSignature(ClassDeclarationSyntax classDecl, MethodDeclarationSyntax newMethod)
    {
        var newParams = GetParameterSignature(newMethod);
        foreach (var existing in classDecl.Members.OfType<MethodDeclarationSyntax>())
        {
            if (!string.Equals(existing.Identifier.Text, newMethod.Identifier.Text, StringComparison.Ordinal))
            {
                continue;
            }

            if (newParams.SequenceEqual(GetParameterSignature(existing), StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> GetParameterSignature(MethodDeclarationSyntax method) =>
        method.ParameterList.Parameters
            .Select(p => p.Type?.ToString() ?? string.Empty)
            .ToList();

    private static ClassDeclarationSyntax? FindTopLevelClass(SyntaxNode root, string className)
    {
        if (root is not CompilationUnitSyntax compilationUnit)
        {
            return null;
        }

        return GetTopLevelTypeDeclarations(compilationUnit)
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => string.Equals(c.Identifier.Text, className, StringComparison.Ordinal));
    }

    private static IEnumerable<TypeDeclarationSyntax> GetTopLevelTypeDeclarations(CompilationUnitSyntax root)
    {
        if (root.Members.Any(static m => m is FileScopedNamespaceDeclarationSyntax))
        {
            return root.Members.OfType<TypeDeclarationSyntax>();
        }

        foreach (var member in root.Members)
        {
            if (member is NamespaceDeclarationSyntax ns)
            {
                return ns.Members.OfType<TypeDeclarationSyntax>();
            }
        }

        return root.Members.OfType<TypeDeclarationSyntax>();
    }

    private static bool IsPartial(TypeDeclarationSyntax typeDecl) =>
        typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword);
}
