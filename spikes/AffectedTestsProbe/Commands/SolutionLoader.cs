using Microsoft.CodeAnalysis.MSBuild;

namespace AffectedTestsProbe;

internal static class SolutionLoader
{
    public static async Task<MSBuildWorkspace> OpenAsync(
        string path,
        string configuration,
        string platform,
        CancellationToken cancellationToken)
    {
        var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>
        {
            ["Configuration"] = configuration,
            ["Platform"] = platform,
        });
        workspace.RegisterWorkspaceFailedHandler(failure => Console.Error.WriteLine(failure.Diagnostic.ToString()));
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
