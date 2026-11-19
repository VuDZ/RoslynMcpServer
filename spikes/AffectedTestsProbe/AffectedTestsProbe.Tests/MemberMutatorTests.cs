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
        AssertWrittenTextCompiles(mutation.Root);
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
        AssertWrittenTextCompiles(mutation.Root);
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

    [Fact]
    public async Task WrittenText_CompilesForEachExecutableForm()
    {
        const string source =
            """
            class Base
            {
                public Base(int value) { }
            }

            class Holder : Base
            {
                public Holder(int value) : base(value)
                {
                    return;
                }

                public int Body() => 1;

                public int Arrow => 2;

                public int Auto { get; set; } = 3;

                public int Value = 1 + 2;

                public int Stored
                {
                    get { return 1; }
                    set { _ = value; }
                }
            }
            """;

        foreach (var name in new[] { "Holder", "Body", "Arrow", "Auto", "Value", "StoredGet" })
        {
            var mutation = MemberMutator.Apply(await FindAsync(source, name));
            Assert.True(mutation.Applied, name + " " + mutation.Reason);
            var text = mutation.Root!.ToFullString();
            Assert.Contains("throw new ", text, StringComparison.Ordinal);
            AssertWrittenTextCompiles(mutation.Root);
        }
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

            if (node is ConstructorDeclarationSyntax constructor && constructor.Identifier.ValueText == name)
            {
                return model!.GetDeclaredSymbol(constructor)!;
            }

            if (node is PropertyDeclarationSyntax property && property.Identifier.ValueText == name)
            {
                return model!.GetDeclaredSymbol(property)!;
            }

            if (node is AccessorDeclarationSyntax accessor
                && name == "StoredGet"
                && accessor.Parent?.Parent is PropertyDeclarationSyntax stored
                && stored.Identifier.ValueText == "Stored"
                && accessor.Kind() == SyntaxKind.GetAccessorDeclaration)
            {
                return model!.GetDeclaredSymbol(accessor)!;
            }
        }

        throw new InvalidOperationException(name);
    }

    private static void AssertWrittenTextCompiles(SyntaxNode root)
    {
        var parsed = CSharpSyntaxTree.ParseText(root.ToFullString());
        var runtime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var compilation = CSharpCompilation.Create(
            "Mutation",
            [parsed],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(runtime, "System.Runtime.dll")),
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(errors.Select(diagnostic => diagnostic.ToString()));
    }
}
