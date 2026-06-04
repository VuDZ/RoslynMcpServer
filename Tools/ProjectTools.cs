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

    [McpServerTool(Name = "add_package_reference", Title = "Add NuGet package reference")]
    [Description("Adds a PackageReference to a .csproj file. Verify package id/version with search_nuget_registry first.")]
    public async Task<string> AddPackageReference(
        [Description("Path to .csproj file.")] string projectPath,
        [Description("NuGet package id, e.g. Moq.")] string packageId,
        [Description("Optional version. Omit to add without Version attribute.")] string? version = null,
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
    [Description("Removes a PackageReference from a .csproj file.")]
    public async Task<string> RemovePackageReference(
        string projectPath,
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

}
