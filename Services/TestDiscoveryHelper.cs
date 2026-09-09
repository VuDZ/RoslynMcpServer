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

    public sealed record ListTestsResult(bool Success, string Payload, bool FiltersApplied)
    {
        public static ListTestsResult Ok(string json, bool filtersApplied) =>
            new(true, json, filtersApplied);

        public static ListTestsResult Fail(string errorMessage) =>
            new(false, errorMessage, FiltersApplied: true);
    }

    public static async Task<ListTestsResult> ListTestsJsonAsync(
        Solution solution,
        int maxResults,
        string? projectName,
        string? nameContains,
        CancellationToken cancellationToken)
    {
        maxResults = Math.Clamp(maxResults, 1, 500);
        var projectFilter = string.IsNullOrWhiteSpace(projectName) ? null : projectName.Trim();
        var nameFilter = string.IsNullOrWhiteSpace(nameContains) ? null : nameContains.Trim();
        var filtersApplied = projectFilter is not null || nameFilter is not null;

        var resolved = TryResolveProjects(solution, projectFilter);
        if (!resolved.Success)
        {
            return ListTestsResult.Fail(resolved.ErrorMessage ?? "Error: could not resolve `projectName`.");
        }

        var tests = new List<object>();

        foreach (var project in resolved.Projects)
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
                    var methodName = symbol.Name;
                    if (!MatchesNameContains(symbol, className, methodName, nameFilter))
                    {
                        continue;
                    }

                    tests.Add(new
                    {
                        projectName = project.Name,
                        className,
                        methodName,
                        fullyQualifiedName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        filePath = document.FilePath
                    });

                    if (tests.Count >= maxResults)
                    {
                        return ListTestsResult.Ok(
                            Serialize(tests, truncated: true, projectFilter, nameFilter),
                            filtersApplied);
                    }
                }
            }
        }

        return ListTestsResult.Ok(
            Serialize(tests, truncated: false, projectFilter, nameFilter),
            filtersApplied);
    }

    internal static ProjectResolveResult TryResolveProjects(Solution solution, string? projectName)
    {
        var projects = solution.Projects.ToList();
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return ProjectResolveResult.Ok(projects);
        }

        var needle = projectName.Trim();
        var matches = projects.Where(p => ProjectMatches(p, needle)).ToList();
        if (matches.Count == 1)
        {
            return ProjectResolveResult.Ok(matches);
        }

        var list = FormatProjectList(matches.Count == 0 ? projects : matches);
        if (matches.Count == 0)
        {
            return ProjectResolveResult.Fail(
                $"Error: `projectName` `{needle}` was not found in the loaded workspace. Projects:{Environment.NewLine}{list}");
        }

        return ProjectResolveResult.Fail(
            $"Error: `projectName` `{needle}` matches {matches.Count} projects. Pass a unique name, file name, or assembly name:{Environment.NewLine}{list}");
    }

    internal sealed record ProjectResolveResult(bool Success, IReadOnlyList<Project> Projects, string? ErrorMessage)
    {
        public static ProjectResolveResult Ok(IReadOnlyList<Project> projects) =>
            new(true, projects, null);

        public static ProjectResolveResult Fail(string errorMessage) =>
            new(false, Array.Empty<Project>(), errorMessage);
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

    private static bool ProjectMatches(Project project, string needle)
    {
        if (project.Name.Equals(needle, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(project.AssemblyName)
            && project.AssemblyName.Equals(needle, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(project.FilePath))
        {
            return false;
        }

        var fileName = Path.GetFileNameWithoutExtension(project.FilePath);
        return fileName.Equals(needle, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesNameContains(
        ISymbol symbol,
        string className,
        string methodName,
        string? nameFilter)
    {
        if (nameFilter is null)
        {
            return true;
        }

        var vstestFqn = TestFilterHelper.FormatVstestFullyQualifiedName(symbol);
        return vstestFqn.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)
               || className.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)
               || methodName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatProjectList(IEnumerable<Project> projects)
    {
        var sb = new StringBuilder();
        foreach (var project in projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("- ");
            sb.Append(project.Name);
            if (!string.IsNullOrWhiteSpace(project.FilePath))
            {
                sb.Append(" → ");
                sb.Append(project.FilePath);
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string Serialize(
        List<object> tests,
        bool truncated,
        string? projectFilter,
        string? nameContains)
    {
        var payload = new Dictionary<string, object?>
        {
            ["count"] = tests.Count,
            ["truncated"] = truncated,
            ["tests"] = tests
        };
        if (projectFilter is not null)
        {
            payload["projectFilter"] = projectFilter;
        }

        if (nameContains is not null)
        {
            payload["nameContains"] = nameContains;
        }

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }
}
