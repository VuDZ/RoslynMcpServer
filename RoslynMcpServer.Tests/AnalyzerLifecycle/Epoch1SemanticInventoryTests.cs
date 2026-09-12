using System.Text.RegularExpressions;
using Xunit;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

/// <summary>
/// E1-S4 inventory of semantic entry points. New callers of GetCurrentSolution /
/// FindDocumentAsync / GetPublishedSolutionAfterDiskSyncAsync / workspace.CurrentSolution
/// compilation must be classified here; getter callers stay in the contract.
/// </summary>
[Trait("Category", "AnalyzerLifecycle")]
public sealed class Epoch1SemanticInventoryTests
{
    private static readonly (string File, string Kind, string Notes)[] Expected =
    {
        ("Tools/WorkspaceTools.cs", "atomic-load", "LoadWorkspace uses one LoadAndPrepareAsync boundary; diagnostics and summary run after publish."),
        ("Tools/ServerLifecycleTools.cs", "getter", "get_mcp_server_info reads overlay snapshot; no flush."),
        ("Tools/UtilityTools.cs", "getter", "Non-rename tools read GetCurrentSolution without flush."),
        ("Tools/UtilityTools.cs", "serialized-document", "RenameSymbol keeps document.Project.Solution as the base after serialized FindDocumentAsync."),
        ("Tools/CodeAnalysisTools.cs", "flush", "get_diagnostics_for_file / get_class_skeleton use FindDocumentAsync."),
        ("Tools/CodeAnalysisTools.cs", "getter", "explore_assembly / decompile_* / skeleton resolve via GetCurrentSolution (no compilation of overlay generators)."),
        ("Tools/CodeFixTools.cs", "flush-then-apply", "FindDocumentAsync then ApplySolutionChangesToDiskAsync."),
        ("Tools/RefactoringTools.cs", "flush-then-apply", "FindDocumentAsync then ApplySolutionChangesToDiskAsync."),
        ("Tools/AstTools.cs", "flush-then-apply", "FindDocumentAsync then ApplySolutionChangesToDiskAsync."),
        ("Tools/EditingTools.cs", "write-then-update", "UpdateDocumentInMemoryAsync writes after preflight; non-workspace files still write directly."),
        ("Tools/TestTools.cs", "serialized-flush", "GetPublishedSolutionAfterDiskSyncAsync and FindDocumentAsync."),
        ("Tools/NavigationTools.cs", "serialized-document", "FindSymbolReferences keeps document.Project.Solution from serialized FindDocumentAsync."),
        ("Tools/NavigationTools.cs", "serialized-flush", "FindUsages / implementations / definition use GetPublishedSolutionAfterDiskSyncAsync."),
        ("Services/SolutionManager.cs", "published-accessor", "Semantic accessors wait for the load/prepare lock and return only _solution."),
        ("Services/SolutionManager.cs", "overlay-read", "GetCurrentSolution remains a non-semantic lock-free info accessor."),
        ("Services/SolutionManager.cs", "raw-workspace", "workspace.CurrentSolution is the write-boundary base and overlay source; the only production TryApplyChanges is TryApplyWorkspaceChanges."),
    };

    [Fact]
    public void Semantic_entry_points_are_inventoried_and_getter_callers_are_not_excluded()
    {
        var repoRoot = FindRepoRoot();
        var hits = new List<string>();
        foreach (var relative in Directory.GetFiles(Path.Combine(repoRoot, "Tools"), "*.cs", SearchOption.TopDirectoryOnly)
                     .Concat(new[] { Path.Combine(repoRoot, "Services", "SolutionManager.cs") }))
        {
            var text = File.ReadAllText(relative);
            var rel = Path.GetRelativePath(repoRoot, relative).Replace('\\', '/');
            if (Regex.IsMatch(text, @"GetCurrentSolution\s*\("))
            {
                hits.Add(rel + "::GetCurrentSolution");
            }

            if (Regex.IsMatch(text, @"GetPublishedSolutionAfterDiskSyncAsync\s*\("))
            {
                hits.Add(rel + "::GetPublishedSolutionAfterDiskSyncAsync");
            }

            if (Regex.IsMatch(text, @"GetPublishedSolutionAsync\s*\("))
            {
                hits.Add(rel + "::GetPublishedSolutionAsync");
            }

            if (Regex.IsMatch(text, @"FindDocumentAsync\s*\("))
            {
                hits.Add(rel + "::FindDocumentAsync");
            }

            if (text.Contains("workspace.CurrentSolution", StringComparison.Ordinal)
                || text.Contains("_workspace.CurrentSolution", StringComparison.Ordinal)
                || text.Contains("workspace.CurrentSolution", StringComparison.Ordinal))
            {
                hits.Add(rel + "::workspace.CurrentSolution");
            }
        }

        Assert.Contains(hits, h => h.StartsWith("Tools/UtilityTools.cs::GetCurrentSolution", StringComparison.Ordinal));
        Assert.Contains(hits, h => h.StartsWith("Tools/CodeAnalysisTools.cs::GetCurrentSolution", StringComparison.Ordinal));
        Assert.Contains(hits, h => h.StartsWith("Tools/NavigationTools.cs::FindDocumentAsync", StringComparison.Ordinal));
        Assert.Contains(hits, h => h.Contains("GetPublishedSolutionAfterDiskSyncAsync", StringComparison.Ordinal));

        foreach (var (file, _, _) in Expected)
        {
            Assert.True(
                File.Exists(Path.Combine(repoRoot, file.Replace('/', Path.DirectorySeparatorChar))),
                "Inventoried file missing: " + file);
        }

        var classifiedFiles = Expected.Select(e => e.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unclassified = hits
            .Select(h => h.Split("::")[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(f => !classifiedFiles.Contains(f))
            .Where(f => !f.Contains("Tests", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(
            unclassified.Count == 0,
            "Unclassified semantic entry-point files (add to E1-S4 inventory): " + string.Join(", ", unclassified));
    }

    [Fact]
    public void Inventory_covers_serialized_read_transform_and_decompile_getters()
    {
        Assert.Contains(Expected, e => e.File == "Tools/UtilityTools.cs" && e.Kind == "serialized-document");
        Assert.Contains(Expected, e => e.File == "Tools/NavigationTools.cs" && e.Kind == "serialized-document");
        Assert.Contains(Expected, e => e.File == "Tools/CodeAnalysisTools.cs" && e.Kind == "getter");
        Assert.Contains(Expected, e => e.Kind == "getter");
        Assert.Contains(Expected, e => e.Kind == "serialized-flush");
    }

    [Fact]
    public void Production_code_has_no_explicit_raw_workspace_semantic_reader()
    {
        var repoRoot = FindRepoRoot();
        var productionFiles = Directory
            .GetFiles(Path.Combine(repoRoot, "Tools"), "*.cs", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(Path.Combine(repoRoot, "Services"), "*.cs", SearchOption.TopDirectoryOnly))
            .ToArray();

        foreach (var file in productionFiles)
        {
            var text = File.ReadAllText(file);
            if (!file.EndsWith(
                    Path.Combine("Services", "SolutionManager.cs"),
                    StringComparison.OrdinalIgnoreCase))
            {
                Assert.DoesNotContain("GetWorkspaceCurrentSolution(", text, StringComparison.Ordinal);
            }
        }

        var manager = File.ReadAllText(Path.Combine(repoRoot, "Services", "SolutionManager.cs"));
        Assert.DoesNotMatch(
            new Regex(@"await\s+[^;\r\n]*Get(?:Compilation|SemanticModel)Async\s*\(", RegexOptions.CultureInvariant),
            manager);
    }

    [Fact]
    public void Production_semantic_calls_have_no_nearby_lock_free_solution_getter()
    {
        var repoRoot = FindRepoRoot();
        var productionFiles = Directory
            .GetFiles(Path.Combine(repoRoot, "Tools"), "*.cs", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(Path.Combine(repoRoot, "Services"), "*.cs", SearchOption.TopDirectoryOnly));
        var semanticCall = new Regex(
            @"await\s+[^;\r\n]*Get(?:Compilation|SemanticModel)Async\s*\(",
            RegexOptions.CultureInvariant);

        foreach (var file in productionFiles)
        {
            var text = File.ReadAllText(file);
            foreach (Match match in semanticCall.Matches(text))
            {
                var windowStart = Math.Max(0, match.Index - 2_000);
                var precedingWindow = text[windowStart..match.Index];
                Assert.DoesNotMatch(
                    new Regex(@"Get(?:Workspace)?CurrentSolution\s*\(\s*\)", RegexOptions.CultureInvariant),
                    precedingWindow);
            }
        }
    }

    [Fact]
    public void Workspace_tool_uses_single_atomic_manager_load_workflow()
    {
        var repoRoot = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "Tools", "WorkspaceTools.cs"));

        Assert.Contains("LoadAndPrepareAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "await _solutionManager.ShadowCopyInSolutionAnalyzerReferencesAsync(",
            source,
            StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RoslynMcpServer.csproj")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
    }
}
