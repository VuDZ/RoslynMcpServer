using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tools;

public sealed class EditingTools
{
    private readonly ILogger<EditingTools> _logger;
    private readonly SolutionManager _solutionManager;

    public EditingTools(ILogger<EditingTools> logger, SolutionManager solutionManager)
    {
        _logger = logger;
        _solutionManager = solutionManager;
    }

    [McpServerTool(Name = "update_file_content", Title = "Write / overwrite file")]
    [Description("Creates or overwrites a file, creating missing parent directories. Writes files.")]
    public async Task<string> WriteFile(
        [Description("Target file path, new or existing.")]
        string filePath,
        [Description("Complete file body to write.")]
        string content,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                _logger.LogWarning("WriteFile rejected: empty file path.");
                return ToolTelemetry.TraceFailureAndReturn(nameof(WriteFile), "File path is empty.");
            }

            var fullPath = _solutionManager.ResolvePathAgainstWorkspace(filePath);
            var text = content ?? string.Empty;
            var write = await _solutionManager.UpdateDocumentInMemoryAsync(fullPath, text, writeUnregisteredPath: true, cancellationToken);
            if (write.Status == WorkspaceWriteStatus.Skipped)
            {
                var refused = string.Equals(write.Reason, "missing-on-disk", StringComparison.Ordinal)
                    ? $"Refused to recreate `{fullPath}`: the file disappeared from disk and remains in the loaded workspace snapshot until reload. Call `reset_workspace` then `load_workspace` (or `load_workspace` alone — a stale project graph skips the load cache)."
                    : write.FormatAdapterMessage($"Failed to write `{fullPath}`.");
                return ToolTelemetry.TraceFailureAndReturn(nameof(WriteFile), _solutionManager.WithDiskSyncNotes(refused));
            }
            else if (!write.IsFullSuccess)
            {
                return ToolTelemetry.TraceFailureAndReturn(
                    nameof(WriteFile),
                    write.FormatAdapterMessage($"Failed to write `{fullPath}`."));
            }

            var success = $"Successfully wrote `{fullPath}` ({text.Length} characters).";
            return ToolTelemetry.TraceAndReturn(nameof(WriteFile), !write.WorkspaceApplied && WorkspaceDiskPathFilter.IsCSharpSource(fullPath)
                ? _solutionManager.WithDiskSyncNotes(success)
                : success);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied writing {FilePath}", filePath);
            return ToolTelemetry.TraceFailureAndReturn(
                nameof(WriteFile),
                $"Failed to write file `{filePath}`: access denied ({ex.Message}).");
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "I/O error writing {FilePath}", filePath);
            return ToolTelemetry.TraceFailureAndReturn(
                nameof(WriteFile),
                $"Failed to write file `{filePath}`: file may be locked or in use ({ex.Message}).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error writing {FilePath}", filePath);
            return ToolTelemetry.TraceFailureAndReturn(
                nameof(WriteFile),
                $"Failed to write file `{filePath}`: {ex.Message}");
        }
    }
}
