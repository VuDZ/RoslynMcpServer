using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace RoslynMcpServer.Plugins;

/// <summary>
/// Contents of the <c>plugin.json</c> that lies next to a plugin entry assembly.
/// </summary>
/// <remarks>
/// <para>
/// Strict UTF-8 JSON: JSONC comments are not accepted here, unlike in <c>RoslynMcp.jsonc</c>. Every field
/// is required and must be non-empty after trimming.
/// </para>
/// <para>
/// Reference versions are deliberately absent from the manifest. They are read from the plugin's own
/// <c>deps.json</c>, so a manifest cannot claim a contract version the compiled references do not have.
/// </para>
/// </remarks>
public sealed record PluginManifest
{
    /// <summary>Manifest file name inside a plugin directory.</summary>
    public const string FileName = "plugin.json";

    /// <summary>
    /// Plugin id. Equals <see cref="IRoslynMcpPlugin.Name"/> and, for a drop-in plugin, the name of the
    /// directory it lies in.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Bare file name of the entry assembly inside the plugin directory. Discovery rejects a value with a
    /// directory part, <c>.</c> or <c>..</c>, so this always names a file the plugin directory owns.
    /// </summary>
    public required string Entry { get; init; }

    /// <summary>Full name of the one type the host asks from the entry assembly.</summary>
    public required string PluginType { get; init; }

    /// <summary>Prefix every tool name of this plugin must start with.</summary>
    public required string ToolPrefix { get; init; }

    /// <summary>Lowest host product version this plugin accepts: same major, and not newer than the host.</summary>
    public required Version MinHostVersion { get; init; }

    /// <summary>
    /// Reads a manifest file and validates every required field.
    /// </summary>
    /// <param name="path">Path of the <c>plugin.json</c>.</param>
    /// <param name="manifest">Parsed manifest, or <see langword="null"/> when the read failed.</param>
    /// <param name="error">Concrete cause of the failure; always set when this method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the file exists, parses, and holds every required field.</returns>
    /// <remarks>
    /// Never throws for file or content problems: a missing file, invalid UTF-8, broken JSON, a missing
    /// field, an empty field and an unparsable version all come back as a failed read with a reason, so a
    /// malformed manifest skips its own plugin instead of the whole discovery pass.
    /// </remarks>
    public static bool TryRead(
        string path,
        [NotNullWhen(true)] out PluginManifest? manifest,
        [NotNullWhen(false)] out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        manifest = null;

        if (!File.Exists(path))
        {
            error = $"manifest file '{path}' does not exist";
            return false;
        }

        JsonDocument document;
        try
        {
            // Parse the bytes rather than a decoded string: an invalid byte sequence fails here instead of
            // turning into replacement characters that would silently change a field value. The options are
            // spelled out because this file is plain JSON: unlike RoslynMcp.jsonc it accepts no comments.
            using var stream = File.OpenRead(path);
            document = JsonDocument.Parse(
                stream,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                });
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            error = $"manifest '{path}' is not valid UTF-8 JSON: {ex.Message}";
            return false;
        }

        using (document)
        {
            return TryReadFields(document.RootElement, path, out manifest, out error);
        }
    }

    private static bool TryReadFields(
        JsonElement root,
        string path,
        [NotNullWhen(true)] out PluginManifest? manifest,
        [NotNullWhen(false)] out string? error)
    {
        manifest = null;

        if (root.ValueKind != JsonValueKind.Object)
        {
            error = $"manifest '{path}' must be a JSON object";
            return false;
        }

        if (!TryReadTextField(root, path, "id", out var id, out error)
            || !TryReadTextField(root, path, "entry", out var entry, out error)
            || !TryReadTextField(root, path, "pluginType", out var pluginType, out error)
            || !TryReadTextField(root, path, "toolPrefix", out var toolPrefix, out error)
            || !TryReadTextField(root, path, "minHostVersion", out var minHostVersionText, out error))
        {
            return false;
        }

        if (!Version.TryParse(minHostVersionText, out var minHostVersion))
        {
            error = $"manifest '{path}' field 'minHostVersion' is not a version: '{minHostVersionText}'";
            return false;
        }

        manifest = new PluginManifest
        {
            Id = id,
            Entry = entry,
            PluginType = pluginType,
            ToolPrefix = toolPrefix,
            MinHostVersion = minHostVersion,
        };

        error = null;
        return true;
    }

    private static bool TryReadTextField(
        JsonElement root,
        string path,
        string fieldName,
        out string value,
        [NotNullWhen(false)] out string? error)
    {
        value = string.Empty;

        if (!root.TryGetProperty(fieldName, out var element))
        {
            error = $"manifest '{path}' has no '{fieldName}' field";
            return false;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            error = $"manifest '{path}' field '{fieldName}' must be a string";
            return false;
        }

        value = element.GetString()?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            error = $"manifest '{path}' field '{fieldName}' is empty";
            return false;
        }

        error = null;
        return true;
    }
}
