using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynMcpServer.Services;

internal static class TypeSyntaxHelper
{
    public static ClassDeclarationSyntax? FindClassDeclaration(SyntaxNode root, string className)
    {
        return root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => string.Equals(c.Identifier.Text, className, StringComparison.Ordinal));
    }

    public static TypeDeclarationSyntax? FindTypeDeclaration(SyntaxNode root, string typeName)
    {
        return root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => string.Equals(t.Identifier.Text, typeName, StringComparison.Ordinal));
    }

    public static CompilationUnitSyntax RequireCompilationUnit(SyntaxNode? root)
    {
        if (root is CompilationUnitSyntax compilationUnit)
        {
            return compilationUnit;
        }

        throw new InvalidOperationException("Could not obtain compilation unit.");
    }
}
