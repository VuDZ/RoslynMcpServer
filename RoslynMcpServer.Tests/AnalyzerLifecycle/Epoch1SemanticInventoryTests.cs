using System.Text.RegularExpressions;
using Xunit;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

/// <summary>
/// E1-S4 inventory of semantic entry points. New callers of GetCurrentSolution /
/// FindDocumentAsync / GetCurrentSolutionAfterDiskSyncAsync / workspace.CurrentSolution
/// compilation must be classified here; getter callers stay in the contract.
/// </summary>
[Trait("Category", "AnalyzerLifecycle")]
public sealed class Epoch1SemanticInventoryTests
{
    private static readonly (string File, string Kind, string Notes)[] Expected =
    {
        ("Tools/WorkspaceTools.cs", "getter-after-enable", "LoadWorkspace enable→GetCurrentSolution for summary; same load session."),
        ("Tools/ServerLifecycleTools.cs", "getter", "get_mcp_server_info reads overlay snapshot; no flush."),
        ("Tools/UtilityTools.cs", "getter", "Non-rename tools read GetCurrentSolution without flush."),
        ("Tools/UtilityTools.cs", "flush-then-reget", "RenameSymbol: FindDocumentAsync (flush) then GetCurrentSolution() again after symbol resolution — must keep one base."),
        ("Tools/CodeAnalysisTools.cs", "flush", "get_diagnostics_for_file / get_class_skeleton use FindDocumentAsync."),
        ("Tools/CodeAnalysisTools.cs", "getter", "explore_assembly / decompile_* / skeleton resolve via GetCurrentSolution (no compilation of overlay generators)."),
        ("Tools/CodeFixTools.cs", "flush-then-apply", "FindDocumentAsync then ApplySolutionChangesToDiskAsync."),
        ("Tools/RefactoringTools.cs", "flush-then-apply", "FindDocumentAsync then ApplySolutionChangesToDiskAsync."),
        ("Tools/AstTools.cs", "flush-then-apply", "FindDocumentAsync then ApplySolutionChangesToDiskAsync."),
        ("Tools/EditingTools.cs", "write-then-update", "File write + UpdateDocumentInMemoryAsync; no flush."),
        ("Tools/TestTools.cs", "flush-getter", "GetCurrentSolutionAfterDiskSyncAsync and FindDocumentAsync."),
        ("Tools/NavigationTools.cs", "flush-then-reget", "FindSymbolReferences: FindDocumentAsync then GetCurrentSolution()."),
        ("Tools/NavigationTools.cs", "flush-getter", "FindUsages / implementations / definition use GetCurrentSolutionAfterDiskSyncAsync."),
        ("Services/SolutionManager.cs", "overlay-read", "GetCurrentSolution returns _solution overlay or workspace fallback; does not prepare or load assemblies."),
        ("Services/SolutionManager.cs", "raw-workspace", "workspace.CurrentSolution used for TryApplyChanges, document id lookup, and overlay source — not a semantic compilation entry except via tests."),
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

            if (Regex.IsMatch(text, @"GetCurrentSolutionAfterDiskSyncAsync\s*\("))
            {
                hits.Add(rel + "::GetCurrentSolutionAfterDiskSyncAsync");
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
        Assert.Contains(hits, h => h.Contains("GetCurrentSolutionAfterDiskSyncAsync", StringComparison.Ordinal));

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
    public void Inventory_covers_rename_two_phase_read_transform_and_decompile_getters()
    {
        Assert.Contains(Expected, e => e.File == "Tools/UtilityTools.cs" && e.Kind == "flush-then-reget");
        Assert.Contains(Expected, e => e.File == "Tools/NavigationTools.cs" && e.Kind == "flush-then-reget");
        Assert.Contains(Expected, e => e.File == "Tools/CodeAnalysisTools.cs" && e.Kind == "getter");
        Assert.Contains(Expected, e => e.Kind == "getter");
        Assert.Contains(Expected, e => e.Kind == "flush-getter");
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
