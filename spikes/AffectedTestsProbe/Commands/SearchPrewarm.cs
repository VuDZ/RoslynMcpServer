using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace AffectedTestsProbe;

/// <summary>
/// Same absent-name query the server uses to finish search-index startup before the first real query.
/// It does not empty the persistent index cache.
/// </summary>
internal static class SearchPrewarm
{
    public static async Task WarmAsync(Solution solution, CancellationToken cancellationToken)
    {
        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (project.Language != LanguageNames.CSharp)
            {
                continue;
            }

            _ = await SymbolFinder.FindSourceDeclarationsAsync(
                    project,
                    "roslyn-mcp-search-prewarm",
                    ignoreCase: true,
                    SymbolFilter.Type | SymbolFilter.Member,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
