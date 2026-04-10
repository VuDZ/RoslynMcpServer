using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMcpServer.Diagnostics;

namespace RoslynMcpServer.Tools;

public sealed class RoslynTools
{
    private readonly ILogger<RoslynTools> _logger;

    public RoslynTools(ILogger<RoslynTools> logger)
    {
        _logger = logger;
    }

    [McpServerTool(Name = "get_file_content", Title = "Read source file")]
    [Description("Reads full file content with context-window protection for large files. Uses `filePath` — the same argument name as read_file_range, get_method_body, get_diagnostics_for_file, apply_patch, etc.")]
    public Task<string> GetFileContent(
        [Description("Absolute or relative path to the file to read.")]
        string filePath,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(GetFileContent), "Error: `filePath` is empty."));
            }

            var fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(GetFileContent), $"File not found: `{filePath}`"));
            }

            var content = File.ReadAllText(fullPath);
            var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var lineCount = lines.Length;

            if (lineCount <= 500)
            {
                return Task.FromResult(ToolTelemetry.TraceAndReturn(nameof(GetFileContent), content));
            }

            var preview = string.Join(Environment.NewLine, lines.Take(100));
            var warning = $"[!] File is too large ({lineCount} lines). To prevent context overflow, use `get_class_skeleton` or `get_method_body` for targeted reads.";
            return Task.FromResult(
                ToolTelemetry.TraceAndReturn(nameof(GetFileContent), $"{preview}{Environment.NewLine}{Environment.NewLine}{warning}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read file content for {FilePath}", filePath);
            return Task.FromResult(
                ToolTelemetry.TraceAndReturn(nameof(GetFileContent), $"Failed to read file `{filePath}`: {ex.Message}"));
        }
    }
}
