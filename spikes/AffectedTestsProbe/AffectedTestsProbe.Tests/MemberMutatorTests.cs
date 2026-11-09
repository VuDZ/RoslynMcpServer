using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace AffectedTestsProbe.Tests;

public sealed class MemberMutatorTests
{
    [Fact]
    public async Task FieldInitializer_BecomesConditionalThrowAndCompiles()
    {
        var symbol = await FindAsync(
            """
            class Holder
            {
                public int Value = 1 + 2;
            }
            """,
            "Value");
        var mutation = MemberMutator.Apply(symbol);

        Assert.True(mutation.Applied);
        var conditional = Assert.Single(mutation.Root!.DescendantNodes().OfType<ConditionalExpressionSyntax>());
        Assert.Equal(SyntaxKind.TrueLiteralExpression, conditional.Condition.Kind());
        Assert.IsType<ThrowExpressionSyntax>(conditional.WhenTrue);
        AssertNoErrors(mutation.Root);
    }

    [Fact]
    public async Task ExpressionBody_BecomesThrowingBlock()
    {
        var symbol = await FindAsync(
            """
            class Holder
            {
                public int Body() => 1;
            }
            """,
            "Body");
        var mutation = MemberMutator.Apply(symbol);

        var method = Assert.Single(mutation.Root!.DescendantNodes().OfType<MethodDeclarationSyntax>());
        Assert.Null(method.ExpressionBody);
        Assert.IsType<ThrowStatementSyntax>(method.Body!.Statements[0]);
        AssertNoErrors(mutation.Root);
    }

    [Fact]
    public async Task CollectionInitializer_IsNotMutable()
    {
        var symbol = await FindAsync(
            """
            class Holder
            {
                public int[] Items = { 1, 2 };
            }
            """,
            "Items");
        var mutation = MemberMutator.Apply(symbol);

        Assert.False(mutation.Applied);
        Assert.Equal("not an expression initializer", mutation.Reason);
    }

    private static async Task<ISymbol> FindAsync(string source, string name)
    {
        using var session = ProbeSession.Open(source);
        var document = session.Solution.Projects.Single().Documents.Single();
        var model = await document.GetSemanticModelAsync();
        var root = await document.GetSyntaxRootAsync();
        foreach (var node in root!.DescendantNodes())
        {
            if (node is VariableDeclaratorSyntax variable && variable.Identifier.ValueText == name)
            {
                return model!.GetDeclaredSymbol(variable)!;
            }

            if (node is MethodDeclarationSyntax method && method.Identifier.ValueText == name)
            {
                return model!.GetDeclaredSymbol(method)!;
            }
        }

        throw new InvalidOperationException(name);
    }

    private static void AssertNoErrors(SyntaxNode root)
    {
        var compilation = CSharpCompilation.Create(
            "Mutation",
            [root.SyntaxTree],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(errors.Select(diagnostic => diagnostic.ToString()));
    }
}
