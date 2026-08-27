using System.Text;
using RoslynMcpServer.Diagnostics;

namespace RoslynMcpServer.Services;

/// <summary>
/// Builds consistent "no workspace loaded" guidance with candidate .sln/.slnx paths.
/// Does not auto-load — agents must call <c>load_workspace</c>.
/// </summary>
public static class WorkspaceLoadGuidance
{
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        ".idea",
        "bin",
        "obj",
        "node_modules",
        "packages",
        ".nuget",
    };

    /// <summary>Default cap so MCP error text stays LLM-friendly.</summary>
    public const int DefaultMaxCandidates = 20;

    /// <summary>BFS depth under each search root.</summary>
    public const int DefaultMaxDepth = 5;

    public static string FormatNoWorkspaceLoadedMessage(string? leadingSentence = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine(leadingSentence ?? "Error: No workspace loaded.");
        sb.AppendLine(
            "Call `load_workspace` with the absolute path to your `.sln`, `.slnx`, or entry `.csproj`.");
        AppendSolutionCandidates(sb);
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Host/MCP client aborted <c>load_workspace</c> (not an MSBuild/project failure).
    /// Common with OpenCode default ~60s tool timeout on large solutions.
    /// </summary>
    public static string FormatClientCancelledWorkspaceLoadMessage(string workspacePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Workspace Load Cancelled (client abort)");
        sb.AppendLine();
        sb.AppendLine(
            "**This is not an MSBuild / NuGet failure.** The MCP host cancelled the in-flight "
            + "`load_workspace` call (AbortError / OperationCanceledException) before Roslyn finished opening the solution.");
        sb.AppendLine();
        sb.AppendLine($"- **Path:** `{workspacePath}`");
        sb.AppendLine("- **What to do:** raise the host MCP tool timeout (OpenCode: `\"timeout\": 600000` ms in mcp config), then call `load_workspace` again and wait until it completes.");
        sb.AppendLine(
            "- **Do not** treat NuGet audit / prune / TFM-compat lines (`NU190*`, GHSA, `NU1701`, `will not be pruned`) logged during load as the root cause of this cancel.");
        sb.AppendLine(
            "- Prefer loading a `.sln`/`.slnx` that contains the test projects (not a single helper `.csproj`) before `get_test_list` / `run_specific_test` Roslyn resolve.");
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// MSBuild design-time build left TargetFramework empty (ResolvePackageAssets).
    /// Not NU1701 — retry with solution Configuration/Platform, or this generated sln is not evaluable.
    /// </summary>
    public static string FormatMissingTargetFrameworkWorkspaceLoadMessage(
        string workspacePath,
        IReadOnlyList<string> diagnostics,
        string? configuration,
        string? platform)
    {
        const int maxSamplePaths = 5;
        var sb = new StringBuilder();
        sb.AppendLine("## Workspace Load Failed (empty TargetFramework)");
        sb.AppendLine();
        sb.AppendLine(
            "MSBuild design-time evaluation did not set `TargetFramework` (`ResolvePackageAssets` required parameter). "
            + "**This is not NU1701** and is not a harmless restore warning.");
        sb.AppendLine();
        sb.AppendLine($"- **Path:** `{workspacePath}`");
        sb.AppendLine(
            $"- **MSBuild Configuration:** {(string.IsNullOrWhiteSpace(configuration) ? "(SDK/workspace default — typically Debug)" : $"`{configuration}`")}");
        sb.AppendLine(
            $"- **MSBuild Platform:** {(string.IsNullOrWhiteSpace(platform) ? "(SDK/workspace default — typically AnyCPU)" : $"`{platform}`")}");
        sb.AppendLine();
        sb.AppendLine(
            "**What to do:** retry `load_workspace` with `configuration` and `platform` matching the solution config Visual Studio uses "
            + "(multi-config / Bazel IDE sln often is not Debug|AnyCPU). `run_dotnet_build` / `run_dotnet_test` then inherit those values unless you override them.");

        var configs = SolutionConfigurationCatalog.ListConfigurationPlatforms(workspacePath);
        if (configs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Solution configurations found (not auto-selected):");
            foreach (var entry in configs)
            {
                sb.AppendLine($"- `{entry}`");
            }
        }

        var samplePaths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var diagnostic in diagnostics)
        {
            if (!WorkspaceDiagnosticFormatter.IsMissingTargetFrameworkEvaluation(diagnostic))
            {
                continue;
            }

            var path = WorkspaceDiagnosticFormatter.TryGetProcessedProjectPath(diagnostic);
            if (string.IsNullOrWhiteSpace(path) || !seen.Add(path))
            {
                continue;
            }

            samplePaths.Add(path);
            if (samplePaths.Count >= maxSamplePaths)
            {
                break;
            }
        }

        if (samplePaths.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Sample projects ({samplePaths.Count}):");
            foreach (var path in samplePaths)
            {
                sb.AppendLine($"- `{path}`");
            }
        }

        if (LooksGenerated(workspacePath) || samplePaths.Exists(LooksGenerated))
        {
            sb.AppendLine();
            sb.AppendLine(
                "This path looks **Bazel/generated** (`generated`, `Bazel`, `_sln_`). "
                + "If the `.csproj` files have no `TargetFramework` even under the IDE configuration, "
                + "Roslyn `MSBuildWorkspace` cannot load this solution. Use `get_code_skeleton` / host Grep without a workspace; "
                + "do not expect `find_symbol_*` on this `.sln`.");
        }

        sb.Append(MsBuildEnvironmentInfo.FormatMarkdownSection());
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Outer SDK CrossTargeting evaluation has no <c>Compile</c> target (typical when
    /// <c>Directory.Build.props</c> sets <c>TargetFrameworks</c> without an inner <c>TargetFramework</c>).
    /// </summary>
    public static string FormatMissingCompileTargetWorkspaceLoadMessage(
        string workspacePath,
        IReadOnlyList<string> diagnostics,
        string? configuration,
        string? platform,
        string? targetFramework)
    {
        const int maxSamplePaths = 5;
        var sb = new StringBuilder();
        sb.AppendLine("## Workspace Load Failed (missing Compile target)");
        sb.AppendLine();
        sb.AppendLine(
            "MSBuild design-time evaluation ran as a **CrossTargeting outer** build (`TargetFrameworks` set, "
            + "`TargetFramework` empty). That evaluation has `Build` / `DispatchToInnerBuilds` but **no `Compile` target**, "
            + "so Roslyn `MSBuildWorkspace` cannot load the project. `dotnet build` can still succeed — it runs `Build`, not `Compile`.");
        sb.AppendLine();
        sb.AppendLine($"- **Path:** `{workspacePath}`");
        sb.AppendLine(
            $"- **MSBuild Configuration:** {(string.IsNullOrWhiteSpace(configuration) ? "(SDK/workspace default — typically Debug)" : $"`{configuration}`")}");
        sb.AppendLine(
            $"- **MSBuild Platform:** {(string.IsNullOrWhiteSpace(platform) ? "(SDK/workspace default — typically AnyCPU)" : $"`{platform}`")}");
        sb.AppendLine(
            $"- **MSBuild TargetFramework:** {(string.IsNullOrWhiteSpace(targetFramework) ? "(not passed — outer CrossTargeting evaluation)" : $"`{targetFramework}`")}");
        sb.AppendLine();

        if (string.IsNullOrWhiteSpace(targetFramework))
        {
            sb.AppendLine(
                "**What to do:** retry `load_workspace` with `targetFramework` set to one inner TFM "
                + "(same idea as `dotnet build -f net10.0`). This is **not** auto-selected. "
                + "Analyzer-only projects that do not implement that TFM may still fail; application/library projects usually load.");
        }
        else
        {
            sb.AppendLine(
                "**What to do:** the passed `targetFramework` still produced no `Compile` target. "
                + "Try another TFM from the list below, or use `get_code_skeleton` / host Grep without a workspace.");
        }

        var tfms = DirectoryBuildPropsReader.ListTargetFrameworks(workspacePath);
        if (tfms.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Target frameworks from nearest `Directory.Build.props` (not auto-selected):");
            foreach (var tfm in tfms)
            {
                sb.AppendLine($"- `{tfm}`");
            }
        }

        var samplePaths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var diagnostic in diagnostics)
        {
            if (!WorkspaceDiagnosticFormatter.IsMissingCompileTarget(diagnostic))
            {
                continue;
            }

            var path = WorkspaceDiagnosticFormatter.TryGetProcessedProjectPath(diagnostic);
            if (string.IsNullOrWhiteSpace(path) || !seen.Add(path))
            {
                continue;
            }

            samplePaths.Add(path);
            if (samplePaths.Count >= maxSamplePaths)
            {
                break;
            }
        }

        if (samplePaths.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Sample projects ({samplePaths.Count}):");
            foreach (var path in samplePaths)
            {
                sb.AppendLine($"- `{path}`");
            }
        }

        sb.AppendLine();
        sb.AppendLine(
            "Without a loaded workspace, use `get_code_skeleton` / host Grep; `run_dotnet_build` / `run_dotnet_test` still work (they invoke `dotnet`, not Roslyn Compile).");

        sb.Append(MsBuildEnvironmentInfo.FormatMarkdownSection());
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Roslyn net472 BuildHost + VS 2026 / MSBuild 18 assembly conflict
    /// (<c>XMakeElements</c> type initializer). Not <c>MCP_MSBUILD_SDK_MISMATCH</c>.
    /// </summary>
    public static bool IsRoslynMsBuildBuildHostFailure(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (IsRoslynMsBuildBuildHostFailure(current.Message))
            {
                return true;
            }

            if (current is TypeInitializationException typeInit
                && !string.IsNullOrEmpty(typeInit.TypeName)
                && typeInit.TypeName.Contains("XMakeElements", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.Flatten().InnerExceptions)
                {
                    if (!ReferenceEquals(inner, current) && IsRoslynMsBuildBuildHostFailure(inner))
                    {
                        return true;
                    }
                }
            }
        }

        return exception is not null && IsRoslynMsBuildBuildHostFailure(exception.ToString());
    }

    public static bool IsRoslynMsBuildBuildHostFailure(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (text.Contains("XMakeElements", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return text.Contains("Microsoft.Build.Evaluation.ProjectCollection", StringComparison.OrdinalIgnoreCase)
               && (text.Contains("TypeInitialization", StringComparison.OrdinalIgnoreCase)
                   || text.Contains("type initializer", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Dedicated agent-facing report so hosts do not treat this as an SDK pin / <c>MCP_MSBUILD_SDK_MISMATCH</c>.
    /// </summary>
    public static string FormatRoslynMsBuildBuildHostFailureMessage(string? workspacePath = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Workspace Load Failed (VS 2026 / MSBuild 18 BuildHost)");
        sb.AppendLine();
        sb.AppendLine(
            "Roslyn's out-of-process **.NET Framework BuildHost** crashed while initializing MSBuild "
            + "(`Microsoft.Build.Shared.XMakeElements`). This happens when the BuildHost registers "
            + "**Visual Studio 2026 (MSBuild 18.x)** and loads mismatched `System.Collections.Immutable` / "
            + "`System.Memory` from the BuildHost folder.");
        sb.AppendLine();
        sb.AppendLine(
            "**This is not** `MCP_MSBUILD_SDK_MISMATCH` and not a .NET SDK pin problem. "
            + "Do not change `global.json` or fall back to shell `dotnet` for this.");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(workspacePath))
        {
            sb.AppendLine($"- **Path:** `{workspacePath}`");
            sb.AppendLine();
        }

        sb.AppendLine("**What to do:**");
        sb.AppendLine(
            "- Use RoslynMcpServer **1.0.35+** (`Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.9+ isolates MSBuild in an AppDomain).");
        sb.AppendLine(
            "- If this binary is already 1.0.35+ and the error persists: `load_workspace` on a **single SDK-style `.csproj`** "
            + "instead of a mixed native/legacy `.sln` (netcore BuildHost).");
        sb.AppendLine(
            "- Semantic `find_symbol_*` needs a loaded workspace. `search_code` / host Grep remain usable as text fallback.");

        sb.Append(MsBuildEnvironmentInfo.FormatMarkdownSection());
        return sb.ToString().TrimEnd();
    }

    /// <summary>Prefers the dedicated BuildHost report when the exception matches; otherwise <paramref name="fallback"/>.</summary>
    public static string FormatCaughtException(Exception ex, string fallback, string? workspacePath = null)
    {
        if (ex is RoslynMsBuildBuildHostException hostEx)
        {
            return hostEx.Message;
        }

        if (IsRoslynMsBuildBuildHostFailure(ex))
        {
            return FormatRoslynMsBuildBuildHostFailureMessage(workspacePath);
        }

        return fallback;
    }

    private static bool LooksGenerated(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}generated{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains("/generated/", StringComparison.OrdinalIgnoreCase)
        || path.Contains("Bazel", StringComparison.OrdinalIgnoreCase)
        || path.Contains("_sln_", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Empty discovery result while a workspace is loaded — usually wrong scope (helper `.csproj` vs test `.sln`).
    /// </summary>
    public static string FormatEmptyTestListMessage(string? loadedWorkspacePath, int projectCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## No tests found in the loaded Roslyn workspace");
        sb.AppendLine();
        sb.AppendLine(
            "**Agent signal:** `get_test_list` scanned the currently loaded workspace and found **0** test methods. "
            + "This usually means the wrong project/solution is loaded — not that the repo has no tests.");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(loadedWorkspacePath))
        {
            sb.AppendLine($"- **Loaded workspace:** `{loadedWorkspacePath}`");
            var ext = Path.GetExtension(loadedWorkspacePath);
            if (string.Equals(ext, ".csproj", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine(
                    "- **Likely issue:** a single `.csproj` is loaded (often auto-loaded from a file under a helper/common project). "
                    + "Test assemblies may live in a sibling project under a parent `.sln`.");
            }
        }
        else
        {
            sb.AppendLine("- **Loaded workspace:** (unknown path)");
        }

        sb.AppendLine($"- **Projects in workspace:** {projectCount}");
        sb.AppendLine(
            "- **Next step:** call `load_workspace` with the absolute path to the `.sln`/`.slnx` that contains the test projects, then retry `get_test_list`.");
        AppendSolutionCandidates(sb);
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Extra agent-facing block when VSTest filter matched no test FQN.
    /// </summary>
    public static string FormatNoMatchingTestsAgentHint(
        string? loadedRoslynWorkspacePath,
        string? filterDescription,
        string? testTargetPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("### Agent diagnostics");
        sb.AppendLine();
        sb.AppendLine(
            "**Signal:** filter ran against `dotnet test`, but no test FQN matched. Treat this as a discovery/workspace problem before rewriting the test.");

        if (!string.IsNullOrWhiteSpace(testTargetPath))
        {
            sb.AppendLine($"- **`dotnet test` target:** `{testTargetPath}`");
        }

        if (!string.IsNullOrWhiteSpace(loadedRoslynWorkspacePath))
        {
            sb.AppendLine($"- **Roslyn workspace loaded:** `{loadedRoslynWorkspacePath}`");
            if (!string.IsNullOrWhiteSpace(testTargetPath)
                && !PathsEqual(loadedRoslynWorkspacePath, testTargetPath))
            {
                sb.AppendLine(
                    "- **Mismatch:** Roslyn workspace path ≠ `workspacePath` passed to the test tool. "
                    + "FQN resolve uses the Roslyn workspace; `dotnet test` uses `workspacePath`. "
                    + "Call `load_workspace` on the same `.sln`/`.slnx` you pass to `run_specific_test`.");
            }
        }
        else
        {
            sb.AppendLine(
                "- **Roslyn workspace loaded:** none — filter fell back to a name suffix (weaker). "
                + "Call `load_workspace` on the test `.sln` first so Roslyn can resolve the exact FQN.");
        }

        if (!string.IsNullOrWhiteSpace(filterDescription)
            && filterDescription.Contains("Name suffix", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine(
                "- **Filter mode:** name-suffix fallback (Roslyn did not resolve the type/method). "
                + "Common when the loaded workspace is a helper `.csproj` without the test class.");
        }
        else if (!string.IsNullOrWhiteSpace(filterDescription)
                 && filterDescription.Contains("Roslyn-resolved", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine(
                "- **Filter mode:** Roslyn-resolved FQN — names looked valid in the workspace; check nested types (`+`), wrong project in the solution, or Theory display names.");
        }

        sb.AppendLine(
            "- **Next:** `load_workspace` on the test solution → `get_test_list` → retry `run_specific_test` with the listed `className`/`methodName`.");
        return sb.ToString().TrimEnd();
    }

    private static void AppendSolutionCandidates(StringBuilder sb)
    {
        var candidates = DiscoverSolutionCandidates();
        if (candidates.Count == 0)
        {
            sb.AppendLine("No `.sln`/`.slnx` candidates found under `ROSLYN_MCP_WORKSPACE` or the current directory.");
            sb.AppendLine("Set MCP env `ROSLYN_MCP_WORKSPACE` to the repo root, or pass an absolute solution path.");
            return;
        }

        sb.AppendLine("Candidate solution files:");
        foreach (var path in candidates)
        {
            sb.AppendLine($"- `{path}`");
        }
    }

    public static IReadOnlyList<string> DiscoverSolutionCandidates(
        int maxCandidates = DefaultMaxCandidates,
        int maxDepth = DefaultMaxDepth)
    {
        if (maxCandidates <= 0)
        {
            return Array.Empty<string>();
        }

        var roots = new List<string>();
        var env = Environment.GetEnvironmentVariable("ROSLYN_MCP_WORKSPACE");
        if (!string.IsNullOrWhiteSpace(env))
        {
            try
            {
                var full = Path.GetFullPath(env.Trim());
                if (Directory.Exists(full))
                {
                    roots.Add(full);
                }
            }
            catch
            {
                // ignore invalid env
            }
        }

        try
        {
            var cwd = Path.GetFullPath(Environment.CurrentDirectory);
            if (Directory.Exists(cwd) && !roots.Exists(r => PathsEqual(r, cwd)))
            {
                roots.Add(cwd);
            }
        }
        catch
        {
            // ignore
        }

        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            CollectSolutions(root, maxDepth, maxCandidates, found, seen);
            if (found.Count >= maxCandidates)
            {
                break;
            }
        }

        return found;
    }

    private static void CollectSolutions(
        string root,
        int maxDepth,
        int maxCandidates,
        List<string> found,
        HashSet<string> seen)
    {
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0 && found.Count < maxCandidates)
        {
            var (current, depth) = queue.Dequeue();

            try
            {
                foreach (var file in Directory.EnumerateFiles(current))
                {
                    var ext = Path.GetExtension(file);
                    if (!ext.Equals(".sln", StringComparison.OrdinalIgnoreCase)
                        && !ext.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var full = Path.GetFullPath(file);
                    if (seen.Add(full))
                    {
                        found.Add(full);
                        if (found.Count >= maxCandidates)
                        {
                            return;
                        }
                    }
                }
            }
            catch
            {
                continue;
            }

            if (depth >= maxDepth)
            {
                continue;
            }

            IEnumerable<string> subDirs;
            try
            {
                subDirs = Directory.EnumerateDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (var sub in subDirs)
            {
                var name = Path.GetFileName(sub);
                if (ExcludedDirectoryNames.Contains(name))
                {
                    continue;
                }

                queue.Enqueue((sub, depth + 1));
            }
        }
    }

    private static bool PathsEqual(string a, string b)
    {
        return string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}

/// <summary>
/// Thrown when Roslyn's out-of-proc BuildHost crashes on VS 2026 / MSBuild 18
/// (<c>XMakeElements</c> type initializer). Not an SDK pin / <c>MCP_MSBUILD_SDK_MISMATCH</c>.
/// </summary>
public sealed class RoslynMsBuildBuildHostException : InvalidOperationException
{
    public RoslynMsBuildBuildHostException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
