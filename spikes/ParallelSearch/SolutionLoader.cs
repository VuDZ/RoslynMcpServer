using System.Diagnostics;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Formatting;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.MSBuild;
using ParallelSearch.Models;

namespace ParallelSearch;

/// <summary>
/// Opens a solution the way the server does: MSBuildWorkspace over the same MEF assemblies, so the same
/// projects load (C# only; the server has no Visual Basic language services).
/// </summary>
internal static class SolutionLoader
{
    /// <param name="msbuildLogger">Optional MSBuild logger for the design-time builds, such as a binary logger.</param>
    /// <param name="diagnostics">Optional sink for every workspace diagnostic as "Kind: message".</param>
    /// <param name="progress">Optional receiver of the loader's per-project operations and their durations.</param>
    /// <param name="window">Optional receiver of the Stopwatch timestamps around OpenSolutionAsync.</param>
    public static async Task<(Solution Solution, object Load)> LoadAsync(
        string solutionPath,
        Microsoft.Build.Framework.ILogger? msbuildLogger = null,
        ICollection<string>? diagnostics = null,
        IProgress<ProjectLoadProgress>? progress = null,
        LoadWindow? window = null,
        CancellationToken cancellationToken = default)
    {
        var host = MefHostServices.Create(new[]
        {
            typeof(Workspace).Assembly,
            typeof(CSharpFormattingOptions).Assembly,
            typeof(MSBuildWorkspace).Assembly,
            Assembly.Load(new AssemblyName("Microsoft.CodeAnalysis.Features")),
            Assembly.Load(new AssemblyName("Microsoft.CodeAnalysis.CSharp.Features")),
        });
        var workspace = MSBuildWorkspace.Create(host);
        var failures = 0;
        workspace.RegisterWorkspaceFailedHandler(args =>
        {
            if (args.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            {
                Interlocked.Increment(ref failures);
            }

            if (diagnostics is not null)
            {
                lock (diagnostics)
                {
                    diagnostics.Add(args.Diagnostic.Kind + ": " + args.Diagnostic.Message);
                }
            }
        });
        var watch = Stopwatch.StartNew();
        if (window is not null)
        {
            window.OpenStart = Stopwatch.GetTimestamp();
        }

        var solution = await workspace.OpenSolutionAsync(solutionPath, msbuildLogger, progress, cancellationToken).ConfigureAwait(false);
        watch.Stop();
        if (window is not null)
        {
            window.OpenEnd = Stopwatch.GetTimestamp();
        }
        return (solution, new
        {
            loadMs = watch.Elapsed.TotalMilliseconds,
            projects = solution.ProjectIds.Count,
            documents = solution.Projects.Sum(project => project.DocumentIds.Count),
            workspaceFailures = failures,
            workingSetMb = Environment.WorkingSet / 1048576.0,
        });
    }
}
