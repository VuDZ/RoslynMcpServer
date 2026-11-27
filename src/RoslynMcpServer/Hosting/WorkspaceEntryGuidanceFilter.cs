using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcpServer.Services.Workspace;

namespace RoslynMcpServer.Hosting;

internal static class WorkspaceEntryGuidanceFilter
{
    internal static void Register(McpServerOptions options)
    {
        // Custom guidance is added after usage statistics has seen the undecorated response.
        options.Filters.Request.CallToolFilters.Insert(0, next => async (context, cancellationToken) =>
        {
            // The handler that triggered a lazy load may return unrelated diagnostics or a syntax fallback.
            // Keep the guidance scoped to that request even if other MCP calls are concurrently loading.
            var previous = WorkspaceEntryGuidance.Current;
            var guidance = new WorkspaceEntryGuidance();
            WorkspaceEntryGuidance.Current = guidance;
            try
            {
                var result = await next(context, cancellationToken).ConfigureAwait(false);
                if (!cancellationToken.IsCancellationRequested
                    && guidance.Message is { } message
                    && !result.Content.OfType<TextContentBlock>()
                        .Any(block => block.Text.Contains(message, StringComparison.Ordinal)))
                {
                    result.Content = [.. result.Content, new TextContentBlock { Text = message }];
                }

                return result;
            }
            finally
            {
                WorkspaceEntryGuidance.Current = previous;
            }
        });
    }
}
