using System.Diagnostics;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Formatting;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.MSBuild;

namespace ParallelSearch;

/// <summary>
/// Opens a solution the way the server does: MSBuildWorkspace over the same MEF assemblies, so the same
/// projects load (C# only; the server has no Visual Basic language services).
/// </summary>
internal static class SolutionLoader
{
    /// <param name="msbuildLogger">Optional MSBuild logger for the design-time builds, such as a binary logger.</param>
    public static async Task<(Solution Solution, object Load)> LoadAsync(
        string solutionPath,
        Microsoft.Build.Framework.ILogger? msbuildLogger = null)
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
        });
        var watch = Stopwatch.StartNew();
        var solution = await workspace.OpenSolutionAsync(solutionPath, msbuildLogger: msbuildLogger).ConfigureAwait(false);
        watch.Stop();
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
