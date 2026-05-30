using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Formatting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;

namespace RoslynMcpServer.Services;

public static class TestDiscoveryHelper
{
    private static readonly HashSet<string> TestAttributes = new(StringComparer.Ordinal)
    {
        "Fact", "Theory", "Test", "TestMethod", "DataTestMethod", "TestCase"
    };

    public static async Task<string> ListTestsJsonAsync(Solution solution, int maxResults, CancellationToken cancellationToken)
    {
        maxResults = Math.Clamp(maxResults, 1, 500);
        var tests = new List<object>();

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                if (document.FilePath is null || !document.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                if (root is null || model is null)
                {
                    continue;
                }

                foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    if (!HasTestAttribute(method, model))
                    {
                        continue;
                    }

                    var symbol = model.GetDeclaredSymbol(method, cancellationToken);
                    if (symbol is null)
                    {
                        continue;
                    }

                    var className = symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? "?";
                    tests.Add(new
                    {
                        className,
                        methodName = symbol.Name,
                        fullyQualifiedName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        filePath = document.FilePath
                    });

                    if (tests.Count >= maxResults)
                    {
                        return Serialize(tests, truncated: true);
                    }
                }
            }
        }

        return Serialize(tests, truncated: false);
    }

    public static async Task<Document> GenerateTestMethodStubAsync(
        Document document,
        string className,
        string methodName,
        string? testFramework,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(methodName))
        {
            throw new ArgumentException("methodName is empty.");
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            throw new InvalidOperationException("Could not obtain syntax tree.");
        }

        var classDecl = TypeSyntaxHelper.FindClassDeclaration(root, className.Trim())
            ?? throw new InvalidOperationException($"Test class `{className}` not found.");

        var framework = string.IsNullOrWhiteSpace(testFramework) ? "xunit" : testFramework.Trim().ToLowerInvariant();
        var methodSource = framework switch
        {
            "nunit" => $"[Test] public void {methodName.Trim()}() {{ Assert.Fail(\"Not implemented\"); }}",
            "mstest" => $"[TestMethod] public void {methodName.Trim()}() {{ Assert.Fail(\"Not implemented\"); }}",
            _ => $"[Fact] public void {methodName.Trim()}() {{ throw new NotImplementedException(); }}"
        };

        var member = SyntaxFactory.ParseMemberDeclaration(methodSource)
            ?? throw new InvalidOperationException("Failed to parse generated test stub.");

        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        editor.AddMember(classDecl, member);
        var changed = editor.GetChangedDocument();
        return await Formatter.FormatAsync(changed, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static bool HasTestAttribute(MethodDeclarationSyntax method, SemanticModel model)
    {
        foreach (var attrList in method.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var symbol = model.GetSymbolInfo(attr).Symbol ?? model.GetTypeInfo(attr).Type;
                var name = symbol?.Name ?? attr.Name.ToString();
                if (TestAttributes.Contains(name) || name.EndsWith("Fact", StringComparison.Ordinal) || name.EndsWith("Theory", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string Serialize(List<object> tests, bool truncated)
    {
        var payload = new Dictionary<string, object?>
        {
            ["count"] = tests.Count,
            ["truncated"] = truncated,
            ["tests"] = tests
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }
}
