using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AffectedTestsProbe;

internal sealed record Mutation(bool Applied, string? Reason, SyntaxNode? Root);

/// <summary>
/// Inserts the oracle throw. A field initializer cannot be a bare throw expression (CS8115);
/// the conditional keeps the type of the original expression.
/// </summary>
internal static class MemberMutator
{
    public static Mutation Apply(ISymbol symbol)
    {
        if (symbol.IsImplicitlyDeclared)
        {
            return Skip("synthesized");
        }

        if (symbol.IsAbstract || symbol.IsExtern)
        {
            return Skip("abstract or extern");
        }

        if (symbol is IFieldSymbol { IsConst: true })
        {
            return Skip("const");
        }

        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            var syntax = reference.GetSyntax();
            var rewritten = Rewrite(syntax);
            if (rewritten is null)
            {
                continue;
            }

            if (rewritten.Value.Reason is { } reason)
            {
                return Skip(reason);
            }

            var replacement = rewritten.Value.Node.WithTriviaFrom(syntax);
            return new Mutation(true, null, syntax.SyntaxTree.GetRoot().ReplaceNode(syntax, replacement));
        }

        return Skip("no executable body");
    }

    private static Mutation Skip(string reason) => new(false, reason, null);

    private static (SyntaxNode Node, string? Reason)? Rewrite(SyntaxNode syntax) => syntax switch
    {
        MethodDeclarationSyntax method => RewriteBody(method.Body, method.ExpressionBody, method.WithBody),
        ConstructorDeclarationSyntax constructor => RewriteBody(constructor.Body, constructor.ExpressionBody, constructor.WithBody),
        OperatorDeclarationSyntax op => RewriteBody(op.Body, op.ExpressionBody, op.WithBody),
        ConversionOperatorDeclarationSyntax conversion => RewriteBody(conversion.Body, conversion.ExpressionBody, conversion.WithBody),
        AccessorDeclarationSyntax accessor => RewriteBody(accessor.Body, accessor.ExpressionBody, accessor.WithBody),
        DestructorDeclarationSyntax destructor => RewriteBody(destructor.Body, destructor.ExpressionBody, destructor.WithBody),
        VariableDeclaratorSyntax variable => RewriteInitializer(variable.Initializer, value => variable.WithInitializer(variable.Initializer!.WithValue(value))),
        PropertyDeclarationSyntax property when property.Initializer is not null => RewriteInitializer(
            property.Initializer,
            value => property.WithInitializer(property.Initializer.WithValue(value))),
        PropertyDeclarationSyntax property when property.ExpressionBody is not null => (ExpressionProperty(property), null),
        _ => null,
    };

    private static (SyntaxNode Node, string? Reason)? RewriteBody<T>(
        BlockSyntax? body,
        ArrowExpressionClauseSyntax? expressionBody,
        Func<BlockSyntax, T> withBody)
        where T : SyntaxNode
    {
        if (body is not null)
        {
            var statement = ThrowStatement();
            return (withBody(body.WithStatements(body.Statements.Insert(0, statement))), null);
        }

        if (expressionBody is not null)
        {
            return (WithoutExpressionBody(withBody(SyntaxFactory.Block(ThrowStatement()))), null);
        }

        return null;
    }

    private static SyntaxNode WithoutExpressionBody(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax method => method.WithExpressionBody(null).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None)),
        ConstructorDeclarationSyntax constructor => constructor.WithExpressionBody(null).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None)),
        OperatorDeclarationSyntax op => op.WithExpressionBody(null).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None)),
        ConversionOperatorDeclarationSyntax conversion => conversion.WithExpressionBody(null).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None)),
        AccessorDeclarationSyntax accessor => accessor.WithExpressionBody(null).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None)),
        DestructorDeclarationSyntax destructor => destructor.WithExpressionBody(null).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None)),
        _ => node,
    };

    private static (SyntaxNode Node, string? Reason)? RewriteInitializer(EqualsValueClauseSyntax? initializer, Func<ExpressionSyntax, SyntaxNode> replace)
    {
        if (initializer is null)
        {
            return null;
        }

        if (initializer.Value is InitializerExpressionSyntax)
        {
            return (initializer, "not an expression initializer");
        }

        return (replace(ConditionalThrow(initializer.Value)), null);
    }

    private static PropertyDeclarationSyntax ExpressionProperty(PropertyDeclarationSyntax property)
    {
        var getter = SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration, SyntaxFactory.Block(ThrowStatement()));
        return property
            .WithExpressionBody(null)
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None))
            .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(getter)));
    }

    // Factory tokens carry elastic trivia, and ToFullString drops it, so the file becomes
    // `thrownewSystem...` and the mutated project does not compile. The parsed text keeps real
    // spaces. Normalize only this node: normalizing the whole file would rewrite untouched code.
    private static StatementSyntax ThrowStatement() =>
        SyntaxFactory.ParseStatement("throw new System.InvalidOperationException(\"tia-probe\");")
            .NormalizeWhitespace();

    private static ExpressionSyntax ConditionalThrow(ExpressionSyntax expression)
    {
        var text = "true ? throw new System.InvalidOperationException(\"tia-probe\") : ("
            + expression.ToFullString()
            + ")";
        return SyntaxFactory.ParseExpression(text).NormalizeWhitespace();
    }
}
