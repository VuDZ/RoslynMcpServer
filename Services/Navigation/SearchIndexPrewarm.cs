using System.Diagnostics;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;

namespace RoslynMcpServer.Services.Navigation;

/// <summary>
/// Builds or validates the syntax indexes of every project right after a load, so the first name-based
/// search finds them ready. The work is the index part of that search without the compilation: a name no
/// declaration can have makes Roslyn's per-project check read every document's index and stop there.
/// </summary>
/// <remarks>
/// On the office solution this part was 5.5 s of a 13.6 s first search with a warm index cache and 8.4 s
/// with a cold one (task-16 on branch research/workspace-load-performance). A search that starts while the
/// prewarm runs shares Roslyn's per-document state with it.
/// </remarks>
internal static class SearchIndexPrewarm
{
    /// <summary>Environment variable that turns the prewarm off when set to 0 or false.</summary>
    public const string EnvironmentVariableName = "ROSLYN_MCP_SEARCH_PREWARM";

    /// <summary>
    /// The environment variable wins over the <c>search-prewarm</c> key of RoslynMcp.jsonc, as for max-results;
    /// with neither set the prewarm is on. It measured no slowdown of a search right after the load.
    /// </summary>
    public static bool IsEnabled(bool? fileSetting)
    {
        var value = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return !(value.Trim() == "0" || string.Equals(value.Trim(), "false", StringComparison.OrdinalIgnoreCase));
        }

        return fileSetting ?? true;
    }

    /// <summary>Never throws: the outcome, including cancellation and failure, goes to one log line.</summary>
    public static async Task RunAsync(Solution solution, ILogger logger, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var completedProjects = 0;
        var outcome = "failure";
        string? error = null;
        try
        {
            foreach (var project in solution.Projects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await SymbolFinder.FindSourceDeclarationsAsync(
                        project,
                        AbsentName,
                        ignoreCase: true,
                        SymbolFilter.Type | SymbolFilter.Member,
                        cancellationToken)
                    .ConfigureAwait(false);
                completedProjects++;
            }

            outcome = "success";
        }
        catch (OperationCanceledException)
        {
            outcome = "cancelled";
        }
        catch (Exception ex)
        {
            error = ex.GetType().FullName;
        }
        finally
        {
            logger.LogInformation("workspace_search_prewarm {Json:l}", JsonSerializer.Serialize(new
            {
                outcome,
                elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                projects = solution.ProjectIds.Count,
                completedProjects,
                error,
            }));
        }
    }

    // C# and VB identifiers cannot contain '-', so no declaration matches and no compilation is built.
    private const string AbsentName = "roslyn-mcp-search-prewarm";
}
