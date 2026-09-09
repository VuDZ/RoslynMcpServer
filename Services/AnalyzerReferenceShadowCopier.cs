using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RoslynMcpServer.Services;

/// <summary>
/// Rewrites <see cref="AnalyzerReference"/>s that point at another project's build output <b>inside the same
/// solution</b> so they load from a private shadow-copy folder instead of the analyzer/generator project's real
/// output path. Fixes two related issues confirmed via an external repro (see
/// <c>docs/ARCHITECTURE.md</c> and <c>ProjectOutputDiagnosticsLogger</c>) when a repo-wide
/// <c>Directory.Build.props</c> overrides <c>OutputPath</c> for a project referenced with
/// <c>OutputItemType="Analyzer"</c>:
/// <list type="number">
/// <item>MSBuildWorkspace's design-time-resolved <see cref="AnalyzerReference.FullPath"/> for that
/// <c>ProjectReference</c> can disagree with the referenced project's own resolved
/// <see cref="Project.CompilationOutputInfo"/>/<see cref="Project.OutputFilePath"/>, pointing at a path that does
/// not exist and silently disabling source generation for the referencing project.</item>
/// <item>Even when the path is correct, loading the analyzer assembly directly from its real build output keeps
/// that file locked for the life of the MCP process, which then breaks a later <c>dotnet build</c> of the
/// analyzer/generator project (MSB3027).</item>
/// </list>
/// This type only touches <see cref="AnalyzerReference"/>s whose file name (without extension) matches another
/// project's <see cref="Project.AssemblyName"/> in the same <see cref="Solution"/>; ambiguous assembly names
/// (two projects sharing one) are left untouched rather than guessed. The source file copied is always the
/// matched project's own resolved output (<see cref="Project.CompilationOutputInfo"/>'s <c>AssemblyPath</c>, or
/// <see cref="Project.OutputFilePath"/>), not the (possibly broken) original reference path — that resolved
/// output already has to exist on disk for anything useful to happen. Pure rewrite logic is separated from the
/// caller so it is unit-testable without a live workspace. The caller (<see cref="SolutionManager"/>) must never
/// apply the result via <see cref="Workspace.TryApplyChanges(Solution)"/> against the real
/// <see cref="MSBuildWorkspace"/> — see <see cref="SolutionManager.ShadowCopyInSolutionAnalyzerReferencesAsync"/>
/// for why (it persists analyzer reference changes back to the <c>.csproj</c> on disk).
/// </summary>
public static class AnalyzerReferenceShadowCopier
{
    public sealed record RewriteResult(
        string ProjectName,
        string AnalyzerDisplay,
        string? OriginalFullPath,
        string MatchedProjectName,
        string? ShadowCopyPath,
        bool Applied,
        string? SkipReason);

    /// <summary>
    /// Computes a stable, human-readable shadow-copy root directory for a loaded solution/project path, under
    /// the OS temp directory. Distinct loaded paths never collide; the same path always maps to the same
    /// directory so repeated loads reuse (and overwrite) prior shadow copies.
    /// </summary>
    public static string GetDefaultShadowRootDirectory(string loadedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loadedPath);

        var normalized = Path.GetFullPath(loadedPath).ToUpperInvariant();
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized)))[..8];
        var name = Path.GetFileNameWithoutExtension(loadedPath);
        return Path.Combine(Path.GetTempPath(), "RoslynMcpServer.AnalyzerShadowCopy", $"{name}_{hash}");
    }

    /// <summary>
    /// Rewrites every in-solution <see cref="AnalyzerReference"/> found across <paramref name="solution"/>'s
    /// projects. Returns the (possibly unchanged) solution plus one <see cref="RewriteResult"/> per matched
    /// reference, applied or not. Never throws for a single failed copy/match — failures are reported via
    /// <see cref="RewriteResult.SkipReason"/> so the caller can log them and leave that reference untouched.
    /// </summary>
    public static (Solution Solution, IReadOnlyList<RewriteResult> Results) ShadowCopyInSolutionAnalyzerReferences(
        Solution solution,
        string shadowRootDirectory,
        IAnalyzerAssemblyLoader loader)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentException.ThrowIfNullOrWhiteSpace(shadowRootDirectory);
        ArgumentNullException.ThrowIfNull(loader);

        var assemblyNameToProjectId = solution.Projects
            .GroupBy(p => p.AssemblyName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var results = new List<RewriteResult>();

        foreach (var projectId in solution.Projects.Select(p => p.Id).ToList())
        {
            var project = solution.GetProject(projectId);
            if (project is null)
            {
                continue;
            }

            var toRemove = new List<AnalyzerReference>();
            var toAdd = new List<AnalyzerReference>();

            foreach (var analyzerReference in project.AnalyzerReferences)
            {
                var fileName = TryGetFileNameWithoutExtension(analyzerReference.FullPath);
                if (fileName is null
                    || !assemblyNameToProjectId.TryGetValue(fileName, out var matchedProjectId)
                    || matchedProjectId == projectId)
                {
                    continue;
                }

                var matchedProject = solution.GetProject(matchedProjectId);
                if (matchedProject is null)
                {
                    continue;
                }

                var sourcePath = matchedProject.CompilationOutputInfo.AssemblyPath ?? matchedProject.OutputFilePath;
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    results.Add(new RewriteResult(
                        project.Name,
                        analyzerReference.Display,
                        analyzerReference.FullPath,
                        matchedProject.Name,
                        ShadowCopyPath: null,
                        Applied: false,
                        SkipReason: $"matched project '{matchedProject.Name}' has no existing resolved output file (build it first)"));
                    continue;
                }

                string shadowPath;
                try
                {
                    shadowPath = CopyToShadowDirectory(sourcePath, shadowRootDirectory, matchedProject.Name);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    results.Add(new RewriteResult(
                        project.Name,
                        analyzerReference.Display,
                        analyzerReference.FullPath,
                        matchedProject.Name,
                        ShadowCopyPath: null,
                        Applied: false,
                        SkipReason: $"shadow copy failed: {ex.Message}"));
                    continue;
                }

                toRemove.Add(analyzerReference);
                toAdd.Add(new AnalyzerFileReference(shadowPath, loader));
                results.Add(new RewriteResult(
                    project.Name,
                    analyzerReference.Display,
                    analyzerReference.FullPath,
                    matchedProject.Name,
                    shadowPath,
                    Applied: true,
                    SkipReason: null));
            }

            if (toRemove.Count == 0)
            {
                continue;
            }

            var updatedReferences = project.AnalyzerReferences
                .Where(r => !toRemove.Contains(r))
                .Concat(toAdd)
                .ToList();
            solution = solution.WithProjectAnalyzerReferences(projectId, updatedReferences);
        }

        return (solution, results);
    }

    /// <summary>
    /// Copies <paramref name="sourcePath"/> (and its <c>.pdb</c>, best-effort) into
    /// <c>{shadowRootDirectory}\{matchedProjectName}\{sourceLastWriteTicks}\{fileName}</c>. Namespacing by the
    /// source file's last-write time means a rebuilt analyzer gets a fresh path — and therefore a fresh
    /// <see cref="IAnalyzerAssemblyLoader.LoadFromPath"/> load — on the next <c>load_workspace</c>, instead of
    /// silently reusing a process-lifetime-cached stale assembly.
    /// </summary>
    private static string CopyToShadowDirectory(string sourcePath, string shadowRootDirectory, string matchedProjectName)
    {
        var generation = File.GetLastWriteTimeUtc(sourcePath).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var shadowDirectory = Path.Combine(shadowRootDirectory, matchedProjectName, generation);
        Directory.CreateDirectory(shadowDirectory);

        var shadowPath = Path.Combine(shadowDirectory, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, shadowPath, overwrite: true);

        TryCopySidecar(sourcePath, shadowPath, ".pdb");
        return shadowPath;
    }

    private static void TryCopySidecar(string sourcePath, string shadowPath, string sidecarExtension)
    {
        try
        {
            var sourceSidecar = Path.ChangeExtension(sourcePath, sidecarExtension);
            if (!File.Exists(sourceSidecar))
            {
                return;
            }

            var shadowSidecar = Path.ChangeExtension(shadowPath, sidecarExtension);
            File.Copy(sourceSidecar, shadowSidecar, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sidecar files (.pdb) are a debugging nicety only; a missing/locked one must not block the fix.
        }
    }

    private static string? TryGetFileNameWithoutExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFileNameWithoutExtension(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
