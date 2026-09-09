using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;

namespace RoslynMcpServer.Services;

/// <summary>
/// Diagnostic-only helper for <c>load_workspace</c>'s optional <c>logProjectOutputDiagnostics</c> flag.
/// Reports, per project, where MSBuildWorkspace design-time evaluation believes the compiled assembly and
/// generated-files output directory live, and whether each <see cref="AnalyzerReference"/> file
/// actually exists on disk. This targets a known pitfall: a repo-wide <c>Directory.Build.props</c> that
/// overrides <c>OutputPath</c> (e.g. into a shared "artifacts" folder) can leave analyzer/generator project
/// references pointing at a stale or missing path, silently disabling source generation.
/// Pure data collection is separated from logging so unit tests can assert on <see cref="Collect"/> without a
/// logging sink.
/// </summary>
public static class ProjectOutputDiagnosticsLogger
{
    public sealed record AnalyzerReferenceDiagnostic(
        string Display,
        string? FullPath,
        bool Exists,
        DateTime? LastWriteTimeUtc);

    public sealed record ProjectOutputDiagnostic(
        string ProjectName,
        string AssemblyName,
        string? OutputFilePath,
        bool OutputFileExists,
        DateTime? OutputFileLastWriteTimeUtc,
        string? GeneratedFilesOutputDirectory,
        bool GeneratedFilesOutputDirectoryExists,
        IReadOnlyList<AnalyzerReferenceDiagnostic> AnalyzerReferences);

    /// <summary>Collects output-path/analyzer-reference facts for every project in <paramref name="solution"/>.</summary>
    public static IReadOnlyList<ProjectOutputDiagnostic> Collect(Solution solution)
    {
        ArgumentNullException.ThrowIfNull(solution);

        var results = new List<ProjectOutputDiagnostic>();
        foreach (var project in solution.Projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var outputFilePath = project.CompilationOutputInfo.AssemblyPath ?? project.OutputFilePath;
            var outputFileExists = TryFileExists(outputFilePath);
            var generatedDir = project.CompilationOutputInfo.GeneratedFilesOutputDirectory;

            var analyzerReferences = project.AnalyzerReferences
                .Select(reference =>
                {
                    var fullPath = TryGetFullPath(reference);
                    var exists = TryFileExists(fullPath);
                    return new AnalyzerReferenceDiagnostic(
                        reference.Display,
                        fullPath,
                        exists,
                        exists ? TryGetLastWriteTimeUtc(fullPath) : null);
                })
                .ToList();

            results.Add(new ProjectOutputDiagnostic(
                project.Name,
                project.AssemblyName,
                outputFilePath,
                outputFileExists,
                outputFileExists ? TryGetLastWriteTimeUtc(outputFilePath) : null,
                generatedDir,
                TryDirectoryExists(generatedDir),
                analyzerReferences));
        }

        return results;
    }

    /// <summary>Logs <see cref="Collect"/> output at Information level, one line per project plus one per analyzer reference.</summary>
    public static void Log(Solution solution, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        foreach (var diagnostic in Collect(solution))
        {
            logger.LogInformation(
                "ProjectOutputDiagnostics project={ProjectName} assembly={AssemblyName} outputFilePath={OutputFilePath} outputFileExists={OutputFileExists} outputFileLastWriteUtc={OutputFileLastWriteUtc} generatedFilesOutputDirectory={GeneratedFilesOutputDirectory} generatedFilesOutputDirectoryExists={GeneratedFilesOutputDirectoryExists}",
                diagnostic.ProjectName,
                diagnostic.AssemblyName,
                diagnostic.OutputFilePath ?? "(null)",
                diagnostic.OutputFileExists,
                diagnostic.OutputFileLastWriteTimeUtc,
                diagnostic.GeneratedFilesOutputDirectory ?? "(null)",
                diagnostic.GeneratedFilesOutputDirectoryExists);

            foreach (var analyzerReference in diagnostic.AnalyzerReferences)
            {
                logger.LogInformation(
                    "ProjectOutputDiagnostics   analyzerReference project={ProjectName} display={Display} fullPath={FullPath} exists={Exists} lastWriteUtc={LastWriteUtc}",
                    diagnostic.ProjectName,
                    analyzerReference.Display,
                    analyzerReference.FullPath ?? "(null)",
                    analyzerReference.Exists,
                    analyzerReference.LastWriteTimeUtc);
            }
        }
    }

    private static string? TryGetFullPath(AnalyzerReference analyzerReference)
    {
        try
        {
            return analyzerReference.FullPath;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryFileExists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDirectoryExists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private static DateTime? TryGetLastWriteTimeUtc(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return null;
        }
    }
}
