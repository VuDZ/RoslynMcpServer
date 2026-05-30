using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Formatting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;

namespace RoslynMcpServer.Services;

public static class InterfaceImplementationHelper
{
    public static async Task<Document> ImplementInterfaceAsync(
        Document document,
        string className,
        string interfaceName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null)
        {
            throw new InvalidOperationException("Could not obtain syntax tree or semantic model.");
        }

        var classDecl = TypeSyntaxHelper.FindClassDeclaration(root, className.Trim())
            ?? throw new InvalidOperationException($"Class `{className}` not found in file.");

        var classSymbol = semanticModel.GetDeclaredSymbol(classDecl, cancellationToken) as INamedTypeSymbol
            ?? throw new InvalidOperationException($"Could not resolve symbol for class `{className}`.");

        var interfaceSymbol = ResolveInterfaceSymbol(semanticModel, interfaceName.Trim())
            ?? throw new InvalidOperationException($"Interface `{interfaceName}` was not found.");

        var membersToAdd = new List<MemberDeclarationSyntax>();
        foreach (var member in interfaceSymbol.GetMembers())
        {
            if (member.IsStatic || member.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            if (member is not (IMethodSymbol or IPropertySymbol or IEventSymbol))
            {
                continue;
            }

            if (ClassAlreadyImplementsMember(classSymbol, member))
            {
                continue;
            }

            MemberDeclarationSyntax? stub = member switch
            {
                IMethodSymbol method => CreateNotImplementedMethod(method),
                IPropertySymbol property => CreateNotImplementedProperty(property),
                IEventSymbol eventSymbol => CreateNotImplementedEvent(eventSymbol),
                _ => null
            };

            if (stub is not null)
            {
                membersToAdd.Add(stub);
            }
        }

        if (membersToAdd.Count == 0)
        {
            throw new InvalidOperationException($"Class `{className}` already implements all members of `{interfaceSymbol.Name}`.");
        }

        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        var withBase = AddTypeToBaseList(classDecl, interfaceSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
        editor.ReplaceNode(classDecl, withBase);
        foreach (var member in membersToAdd)
        {
            editor.AddMember(classDecl, member);
        }

        var changed = editor.GetChangedDocument();
        return await Formatter.FormatAsync(changed, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public static async Task<Document> AddTypeToClassBasesAsync(
        Document document,
        string className,
        string typeName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            throw new InvalidOperationException("Could not obtain syntax tree.");
        }

        var classDecl = TypeSyntaxHelper.FindClassDeclaration(root, className.Trim())
            ?? throw new InvalidOperationException($"Class `{className}` not found in file.");

        var updated = AddTypeToBaseList(classDecl, typeName.Trim());
        var newDoc = document.WithSyntaxRoot(root.ReplaceNode(classDecl, updated));
        return await Formatter.FormatAsync(newDoc, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static INamedTypeSymbol? ResolveInterfaceSymbol(SemanticModel semanticModel, string interfaceName)
    {
        var compilation = semanticModel.Compilation;
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var symbols = model.LookupNamespacesAndTypes(0, null, interfaceName);
            foreach (var symbol in symbols.OfType<INamedTypeSymbol>())
            {
                if (symbol.TypeKind == TypeKind.Interface)
                {
                    return symbol;
                }
            }
        }

        return compilation.GetTypeByMetadataName(interfaceName)
            ?? compilation.GetTypeByMetadataName($"global::{interfaceName}");
    }

    private static bool ClassAlreadyImplementsMember(INamedTypeSymbol classSymbol, ISymbol interfaceMember)
    {
        return interfaceMember switch
        {
            IMethodSymbol method => classSymbol.GetMembers(method.Name)
                .OfType<IMethodSymbol>()
                .Any(m => m.Parameters.Length == method.Parameters.Length),
            IPropertySymbol property => classSymbol.GetMembers(property.Name).OfType<IPropertySymbol>().Any(),
            IEventSymbol eventSymbol => classSymbol.GetMembers(eventSymbol.Name).OfType<IEventSymbol>().Any(),
            _ => false
        };
    }

    private static MethodDeclarationSyntax CreateNotImplementedMethod(IMethodSymbol method)
    {
        var decl = SyntaxFactory.MethodDeclaration(
                SyntaxFactory.ParseTypeName(method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)),
                method.Name)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithParameterList(SyntaxFactory.ParseParameterList(
                $"({string.Join(", ", method.Parameters.Select(p => $"{p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {p.Name}"))})"))
            .WithBody(SyntaxFactory.Block(
                SyntaxFactory.ThrowStatement(
                    SyntaxFactory.ObjectCreationExpression(
                            SyntaxFactory.ParseTypeName("NotImplementedException"))
                        .WithArgumentList(SyntaxFactory.ParseArgumentList("()")))));

        if (method.TypeParameters.Length > 0)
        {
            decl = decl.WithTypeParameterList(
                SyntaxFactory.TypeParameterList(
                    SyntaxFactory.SeparatedList(method.TypeParameters.Select(tp =>
                        SyntaxFactory.TypeParameter(tp.Name)))));
        }

        return decl;
    }

    private static PropertyDeclarationSyntax CreateNotImplementedProperty(IPropertySymbol property)
    {
        var type = SyntaxFactory.ParseTypeName(property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
        if (property.IsReadOnly)
        {
            return SyntaxFactory.PropertyDeclaration(type, property.Name)
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithBody(SyntaxFactory.Block(
                            SyntaxFactory.ThrowStatement(
                                SyntaxFactory.ObjectCreationExpression(SyntaxFactory.ParseTypeName("NotImplementedException"))
                                    .WithArgumentList(SyntaxFactory.ParseArgumentList("()"))))))));
        }

        return SyntaxFactory.PropertyDeclaration(type, property.Name)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.List(new[]
            {
                SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
            })));
    }

    private static EventDeclarationSyntax CreateNotImplementedEvent(IEventSymbol eventSymbol)
    {
        return SyntaxFactory.EventDeclaration(
                SyntaxFactory.ParseTypeName(eventSymbol.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)),
                eventSymbol.Name)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.List(new[]
            {
                SyntaxFactory.AccessorDeclaration(SyntaxKind.AddAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                SyntaxFactory.AccessorDeclaration(SyntaxKind.RemoveAccessorDeclaration)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
            })));
    }

    private static ClassDeclarationSyntax AddTypeToBaseList(ClassDeclarationSyntax classDecl, string typeName)
    {
        var baseType = SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(typeName));
        if (classDecl.BaseList is null)
        {
            return classDecl.WithBaseList(SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(baseType)));
        }

        if (classDecl.BaseList.Types.Any(t => string.Equals(t.ToString(), typeName, StringComparison.Ordinal)))
        {
            return classDecl;
        }

        return classDecl.WithBaseList(classDecl.BaseList.AddTypes(baseType));
    }
}
