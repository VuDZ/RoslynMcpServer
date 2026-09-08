using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tools;

public sealed class ProjectTools
{
    private readonly SolutionManager _solutionManager;
    private readonly ILogger<ProjectTools> _logger;

    public ProjectTools(SolutionManager solutionManager, ILogger<ProjectTools> logger)
    {
        _solutionManager = solutionManager;
        _logger = logger;
    }

    [McpServerTool(Name = "rename_project", Title = "Rename MSBuild project")]
    [Description(
        "Renames an SDK-style project folder and .csproj. Default dryRun=true. Does not rename C# types — use rename_symbol after reload.")]
    public Task<string> RenameProject(
        [Description("Path to the .csproj to rename.")] string projectPath,
        [Description("New project name (single path segment).")] string newProjectName,
        [Description("When true (default), preview planned moves without writing.")] bool dryRun = true,
        [Description("Optional root to search for sibling projects and solutions.")] string? searchRoot = null,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(RenameProject);
        _ = cancellationToken;

        try
        {
            if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(newProjectName))
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(
                    toolName,
                    "Error: `projectPath` and `newProjectName` are required."));
            }

            var resolvedProject = _solutionManager.ResolvePathAgainstWorkspace(projectPath);
            var resolvedSearchRoot = string.IsNullOrWhiteSpace(searchRoot)
                ? null
                : _solutionManager.ResolvePathAgainstWorkspace(searchRoot);

            var plan = ProjectRenameHelper.CreatePlan(resolvedProject, newProjectName, resolvedSearchRoot);
            if (dryRun)
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(toolName, ProjectRenameHelper.FormatPlan(plan, dryRun: true)));
            }

            var result = ProjectRenameHelper.Apply(plan);
            return ClearWorkspaceAndReturnAsync(toolName, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RenameProject failed for {ProjectPath} -> {NewName}", projectPath, newProjectName);
            return Task.FromResult(ToolTelemetry.TraceAndReturn(toolName, $"Failed: {ex.Message}"));
        }
    }

    [McpServerTool(Name = "add_package_reference", Title = "Add NuGet package reference")]
    [Description(
        "Adds a PackageReference to a .csproj. Writes the file. Verify id/version with search_nuget_registry first. Call load_workspace after.")]
    public async Task<string> AddPackageReference(
        [Description("Path to the .csproj.")] string projectPath,
        [Description("NuGet package id.")] string packageId,
        [Description("Optional version. Omit to add without a Version attribute.")] string? version = null,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(AddPackageReference);
        try
        {
            var message = await ProjectFileHelper.AddPackageReferenceAsync(projectPath, packageId, version, cancellationToken)
                .ConfigureAwait(false);
            await _solutionManager.ClearWorkspaceAsync(cancellationToken).ConfigureAwait(false);
            return ToolTelemetry.TraceAndReturn(toolName, message + " Call `load_workspace` to refresh Roslyn state.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddPackageReference failed for {PackageId}", packageId);
            return ToolTelemetry.TraceAndReturn(toolName, $"Failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "remove_package_reference", Title = "Remove NuGet package reference")]
    [Description("Removes a PackageReference from a .csproj. Writes the file. Call load_workspace after.")]
    public async Task<string> RemovePackageReference(
        [Description("Path to the .csproj.")]
        string projectPath,
        [Description("NuGet package id to remove.")]
        string packageId,
        CancellationToken cancellationToken = default)
    {
        const string toolName = nameof(RemovePackageReference);
        try
        {
            var message = await ProjectFileHelper.RemovePackageReferenceAsync(projectPath, packageId, cancellationToken)
                .ConfigureAwait(false);
            await _solutionManager.ClearWorkspaceAsync(cancellationToken).ConfigureAwait(false);
            return ToolTelemetry.TraceAndReturn(toolName, message + " Call `load_workspace` to refresh Roslyn state.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RemovePackageReference failed for {PackageId}", packageId);
            return ToolTelemetry.TraceAndReturn(toolName, $"Failed: {ex.Message}");
        }
    }

    private async Task<string> ClearWorkspaceAndReturnAsync(string toolName, string message)
    {
        try
        {
            await _solutionManager.ClearWorkspaceAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ClearWorkspace after RenameProject failed");
        }

        return ToolTelemetry.TraceAndReturn(
            toolName,
            message + Environment.NewLine + Environment.NewLine
            + "Workspace cache cleared. Call `load_workspace` on the solution, then `run_dotnet_build`.");
    }
}
