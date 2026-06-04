using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tools;

public sealed class NuGetTools
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    private readonly ILogger<NuGetTools> _logger;

    public NuGetTools(ILogger<NuGetTools> logger)
    {
        _logger = logger;
    }

    [McpServerTool(Name = "list_nuget_packages", Title = "List NuGet packages")]
    [Description(
        "Lists installed NuGet packages for a .sln or .csproj as structured JSON, grouped by project and target framework. " +
        "Includes transitive dependencies when `includeTransitive` is true. Use before adding or upgrading packages — " +
        "do not guess installed versions via `execute_dotnet_command`.")]
    public async Task<string> ListNuGetPackages(
        [Description("Path to .sln, .csproj, or directory containing them (same shape as run_dotnet_build / load_workspace).")]
        string workspacePath,
        [Description("When true (default), includes transitive dependencies in the output.")]
        bool includeTransitive = true,
        [Description("When true, adds `--outdated` to highlight packages with newer versions on configured feeds.")]
        bool includeOutdated = false,
        [Description("When true, adds `--vulnerable` to list packages with known vulnerabilities.")]
        bool includeVulnerable = false,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(ListNuGetPackages);

        try
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return ToolTelemetry.TraceAndReturn(toolName, "Error: `workspacePath` is empty.");
            }

            var fullPath = Path.GetFullPath(workspacePath);
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            {
                return ToolTelemetry.TraceAndReturn(toolName, $"Error: Path not found: `{fullPath}`");
            }

            var args = new StringBuilder("list ");
            args.Append('"').Append(fullPath).Append('"');
            args.Append(" package --format json");
            if (includeTransitive)
            {
                args.Append(" --include-transitive");
            }

            if (includeOutdated)
            {
                args.Append(" --outdated");
            }

            if (includeVulnerable)
            {
                args.Append(" --vulnerable");
            }

            var workingDirectory = Directory.Exists(fullPath)
                ? fullPath
                : Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;

            var (exitCode, output) = await DotNetCliRunner.RunAsync(
                args.ToString(),
                workingDirectory,
                cancellationToken).ConfigureAwait(false);

            if (exitCode != 0)
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    $"`dotnet list package` failed (exit code {exitCode}).\n\n{output}");
            }

            if (!TryPrettyPrintJson(output, out var prettyJson))
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    $"Unexpected output from `dotnet list package` (not valid JSON).\n\n{output}");
            }

            var projectCount = CountProjects(prettyJson);
            var header = new StringBuilder();
            header.AppendLine("## Installed NuGet packages (JSON)");
            header.AppendLine();
            header.AppendLine($"- **Path:** `{fullPath}`");
            header.AppendLine($"- **Projects:** {projectCount}");
            header.AppendLine($"- **Include transitive:** {includeTransitive}");
            header.AppendLine($"- **Include outdated:** {includeOutdated}");
            header.AppendLine($"- **Include vulnerable:** {includeVulnerable}");
            header.AppendLine();
            header.AppendLine("```json");
            header.AppendLine(prettyJson);
            header.Append("```");

            return ToolTelemetry.TraceAndReturn(toolName, header.ToString().TrimEnd());
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(toolName, "`list_nuget_packages` was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ListNuGetPackages failed for {WorkspacePath}", workspacePath);
            return ToolTelemetry.TraceAndReturn(toolName, $"Failed to list NuGet packages: {ex.Message}");
        }
    }

    [McpServerTool(Name = "run_nuget_audit", Title = "Run NuGet vulnerability audit")]
    [Description(
        "Runs `dotnet list package --vulnerable --include-transitive` and returns a compact table "
        + "(severity, package, version, project, GHSA/advisory URL). Separate from compile errors in `run_dotnet_build`.")]
    public async Task<string> RunNuGetAudit(
        [Description("Path to .sln, .csproj, or directory (same as run_dotnet_build / load_workspace).")]
        string workspacePath,
        [Description("Maximum vulnerable entries in the table. Default 40.")]
        int maxEntries = 40,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(RunNuGetAudit);

        try
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return ToolTelemetry.TraceAndReturn(toolName, "Error: `workspacePath` is empty.");
            }

            var fullPath = Path.GetFullPath(workspacePath);
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            {
                return ToolTelemetry.TraceAndReturn(toolName, $"Error: Path not found: `{fullPath}`");
            }

            var args = new StringBuilder("list \"");
            args.Append(fullPath);
            args.Append("\" package --vulnerable --include-transitive --format json");

            var workingDirectory = Directory.Exists(fullPath)
                ? fullPath
                : WorkspaceRootResolver.ResolveDotNetWorkingDirectory(fullPath);

            var (exitCode, output) = await DotNetCliRunner.RunAsync(
                args.ToString(),
                workingDirectory,
                cancellationToken).ConfigureAwait(false);

            if (exitCode != 0)
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    $"`dotnet list package --vulnerable` failed (exit code {exitCode}).\n\n{output}");
            }

            if (!TryPrettyPrintJson(output, out _))
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    $"Unexpected output (not JSON).\n\n{output}");
            }

            var entries = NuGetAuditReportParser.Parse(output);
            var report = NuGetAuditReportParser.FormatMarkdownReport(fullPath, entries, maxEntries);
            return ToolTelemetry.TraceAndReturn(toolName, report);
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(toolName, "`run_nuget_audit` was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RunNuGetAudit failed for {WorkspacePath}", workspacePath);
            return ToolTelemetry.TraceAndReturn(toolName, $"Failed NuGet audit: {ex.Message}");
        }
    }

    [McpServerTool(Name = "list_outdated_packages", Title = "List outdated NuGet packages")]
    [Description("Runs dotnet list package --outdated --format json.")]
    public Task<string> ListOutdatedPackages(
        [Description("Path to .sln, .csproj, or directory containing them.")]
        string workspacePath,
        CancellationToken cancellationToken = default) =>
        ListNuGetPackages(workspacePath, includeTransitive: false, includeOutdated: true, includeVulnerable: false, cancellationToken);

    [McpServerTool(Name = "search_nuget_registry", Title = "Search NuGet registry")]
    [Description(
        "Searches nuget.org (and other configured feeds) for package names and latest stable versions. " +
        "Use before `dotnet add package` to verify a package exists and pick a real version — " +
        "do not invent package ids or versions and do not use raw `execute_dotnet_command` for discovery.")]
    public async Task<string> SearchNuGetRegistry(
        [Description("Package id or search term, e.g. `Moq` or `Microsoft.Extensions.Logging`.")]
        string query,
        [Description("When true (default), returns only packages whose id exactly matches `query` (case-insensitive).")]
        bool exactMatch = true,
        [Description("Maximum results when `exactMatch` is false. Ignored for exact match. Default 10.")]
        int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(SearchNuGetRegistry);

        try
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return ToolTelemetry.TraceAndReturn(toolName, "Error: `query` is empty.");
            }

            var trimmedQuery = query.Trim();
            IReadOnlyList<NuGetSearchResultParser.RegistryPackage> packages;

            if (exactMatch)
            {
                var broadArgs = new StringBuilder("package search ");
                broadArgs.Append('"').Append(trimmedQuery.Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
                broadArgs.Append(" --format json --take 20");

                var (broadExit, broadOutput) = await DotNetCliRunner.RunAsync(
                    broadArgs.ToString(),
                    workingDirectory: null,
                    cancellationToken).ConfigureAwait(false);

                if (broadExit != 0)
                {
                    return ToolTelemetry.TraceAndReturn(
                        toolName,
                        $"`dotnet package search` failed (exit code {broadExit}).\n\n{broadOutput}");
                }

                packages = NuGetSearchResultParser.ParseSearchJson(broadOutput, exactMatch: true, trimmedQuery);

                if (packages.Count == 0)
                {
                    var exactArgs = new StringBuilder("package search ");
                    exactArgs.Append('"').Append(trimmedQuery.Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
                    exactArgs.Append(" --exact-match --format json");

                    var (exactExit, exactOutput) = await DotNetCliRunner.RunAsync(
                        exactArgs.ToString(),
                        workingDirectory: null,
                        cancellationToken).ConfigureAwait(false);

                    if (exactExit != 0)
                    {
                        return ToolTelemetry.TraceAndReturn(
                            toolName,
                            $"`dotnet package search --exact-match` failed (exit code {exactExit}).\n\n{exactOutput}");
                    }

                    var latest = NuGetSearchResultParser.TryGetLatestStableFromExactMatchJson(exactOutput, trimmedQuery);
                    if (!string.IsNullOrWhiteSpace(latest))
                    {
                        packages =
                        [
                            new NuGetSearchResultParser.RegistryPackage(trimmedQuery, latest, "nuget.org", null)
                        ];
                    }
                }
            }
            else
            {
                var take = Math.Clamp(maxResults, 1, 50);
                var args = new StringBuilder("package search ");
                args.Append('"').Append(trimmedQuery.Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
                args.Append(" --format json --take ").Append(take);

                var (exitCode, output) = await DotNetCliRunner.RunAsync(
                    args.ToString(),
                    workingDirectory: null,
                    cancellationToken).ConfigureAwait(false);

                if (exitCode != 0)
                {
                    return ToolTelemetry.TraceAndReturn(
                        toolName,
                        $"`dotnet package search` failed (exit code {exitCode}).\n\n{output}");
                }

                packages = NuGetSearchResultParser.ParseSearchJson(output, exactMatch: false, trimmedQuery);
            }

            if (packages.Count == 0)
            {
                return ToolTelemetry.TraceAndReturn(
                    toolName,
                    exactMatch
                        ? $"No package with id `{trimmedQuery}` found on configured NuGet feeds."
                        : $"No packages matched `{trimmedQuery}` on configured NuGet feeds.");
            }

            var payload = new JsonObject
            {
                ["query"] = trimmedQuery,
                ["exactMatch"] = exactMatch,
                ["packages"] = new JsonArray(packages.Select(p => new JsonObject
                {
                    ["id"] = p.Id,
                    ["latestStableVersion"] = p.LatestStableVersion,
                    ["source"] = p.Source,
                    ["totalDownloads"] = p.TotalDownloads
                }).ToArray())
            };

            var prettyJson = payload.ToJsonString(PrettyJson);
            var sb = new StringBuilder();
            sb.AppendLine("## NuGet registry search (JSON)");
            sb.AppendLine();
            sb.AppendLine($"- **Query:** `{trimmedQuery}`");
            sb.AppendLine($"- **Exact match:** {exactMatch}");
            sb.AppendLine();
            sb.AppendLine("```json");
            sb.AppendLine(prettyJson);
            sb.Append("```");

            return ToolTelemetry.TraceAndReturn(toolName, sb.ToString().TrimEnd());
        }
        catch (OperationCanceledException)
        {
            return ToolTelemetry.TraceAndReturn(toolName, "`search_nuget_registry` was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SearchNuGetRegistry failed for query {Query}", query);
            return ToolTelemetry.TraceAndReturn(toolName, $"Failed to search NuGet registry: {ex.Message}");
        }
    }

    private static bool TryPrettyPrintJson(string raw, out string pretty)
    {
        pretty = string.Empty;
        try
        {
            var node = JsonNode.Parse(raw);
            if (node is null)
            {
                return false;
            }

            pretty = node.ToJsonString(PrettyJson);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int CountProjects(string prettyJson)
    {
        try
        {
            var root = JsonNode.Parse(prettyJson);
            return root?["projects"]?.AsArray()?.Count ?? 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }
}
