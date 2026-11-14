using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace AffectedTestsProbe;

internal static class SolutionLoader
{
    public static Task<MSBuildWorkspace> OpenAsync(
        string path,
        string configuration,
        string platform,
        CancellationToken cancellationToken) =>
        OpenAsync(path, configuration, platform, failures: null, cancellationToken);

    /// <summary>Opens the solution and also collects the workspace load diagnostics into <paramref name="failures"/>.</summary>
    public static async Task<MSBuildWorkspace> OpenAsync(
        string path,
        string configuration,
        string platform,
        List<WorkspaceDiagnostic>? failures,
        CancellationToken cancellationToken)
    {
        var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>
        {
            ["Configuration"] = configuration,
            ["Platform"] = platform,
        });
        workspace.RegisterWorkspaceFailedHandler(failure =>
        {
            Console.Error.WriteLine(failure.Diagnostic.ToString());
            lock (workspace)
            {
                failures?.Add(failure.Diagnostic);
            }
        });
        if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            await workspace.OpenProjectAsync(path, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await workspace.OpenSolutionAsync(path, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return workspace;
    }
}
