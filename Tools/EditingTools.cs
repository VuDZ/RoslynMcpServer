using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;

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
    [Description(
        "Writes full file content. Creates a **new file** if `filePath` does not exist, or overwrites an existing file. **Creates all missing parent directories** (e.g. `Services/NewFolder/IUserRepository.cs`). Safe for refactoring that adds new types; no separate mkdir tool is required.")]
    public async Task<string> WriteFile(
        [Description("Target file path (new or existing). Same JSON key `filePath` as get_file_content / apply_patch.")]
        string filePath,
        [Description("Complete file body to write (UTF-8).")]
        string content,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                _logger.LogWarning("WriteFile rejected: empty file path.");
                return ToolTelemetry.TraceAndReturn(nameof(WriteFile), "File path is empty.");
            }

            var fullPath = Path.GetFullPath(filePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var text = content ?? string.Empty;
            await File.WriteAllTextAsync(fullPath, text, cancellationToken);

            try
            {
                await _solutionManager.UpdateDocumentInMemoryAsync(fullPath, text, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "In-memory workspace sync failed after write for {FilePath}", fullPath);
            }

            return ToolTelemetry.TraceAndReturn(
                nameof(WriteFile),
                $"Successfully wrote `{fullPath}` ({text.Length} characters).");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied writing {FilePath}", filePath);
            return ToolTelemetry.TraceAndReturn(
                nameof(WriteFile),
                $"Failed to write file `{filePath}`: access denied ({ex.Message}).");
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "I/O error writing {FilePath}", filePath);
            return ToolTelemetry.TraceAndReturn(
                nameof(WriteFile),
                $"Failed to write file `{filePath}`: file may be locked or in use ({ex.Message}).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error writing {FilePath}", filePath);
            return ToolTelemetry.TraceAndReturn(
                nameof(WriteFile),
                $"Failed to write file `{filePath}`: {ex.Message}");
        }
    }
}
