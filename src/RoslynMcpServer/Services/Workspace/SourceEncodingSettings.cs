using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynMcpServer.Config;
using RoslynMcpServer.Services.Workspace.Models;

namespace RoslynMcpServer.Services.Workspace;

/// <summary>Resolves live source encoding declarations while the workspace write lock is held.</summary>
internal sealed class SourceEncodingSettings(RoslynMcpFileSettings settings)
{
    internal DeclaredSourceEncoding? Resolve(string fullPath)
    {
        var editorConfig = FindEditorConfig(fullPath);
        if (editorConfig is not null)
        {
            return editorConfig;
        }

        foreach (var path in _settingsPaths)
        {
            if (ReadConfig(path, editorConfig: false)?.SourceEncoding is { } name)
            {
                return new DeclaredSourceEncoding(ParseEncoding(name, path, RoslynMcpFileSettings.SourceEncodingKey),
                    path, RoslynMcpFileSettings.SourceEncodingKey);
            }
        }

        return null;
    }

    internal static Encoding ParseCodePage(string value, string path, string key)
    {
        try
        {
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var page) && page > 0)
            {
                return SourceTextEncoding.WithoutBom(page);
            }

            if (key == RoslynMcpFileSettings.SourceEncodingKey)
            {
                var encoding = CodePagesEncodingProvider.Instance.GetEncoding(value) ?? Encoding.GetEncoding(value);
                return SourceTextEncoding.WithoutBom(encoding.CodePage);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw InvalidEncoding(path, key, value, ex);
        }

        throw InvalidEncoding(path, key, value);
    }

    private DeclaredSourceEncoding? FindEditorConfig(string fullPath)
    {
        for (var directory = Path.GetDirectoryName(fullPath); directory is not null; directory = Path.GetDirectoryName(directory))
        {
            var path = Path.Combine(directory, ".editorconfig");
            var cached = ReadConfig(path, editorConfig: true);
            if (cached?.EditorConfig is not { } config)
            {
                continue;
            }

            var options = config.GetOptionsForSourcePath(fullPath).AnalyzerOptions;
            if (options.TryGetValue("charset", out var value))
            {
                return value.Equals("unset", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : new DeclaredSourceEncoding(ParseEncoding(value, path, "charset"), path, "charset");
            }

            if (cached.IsRoot)
            {
                break;
            }
        }

        return null;
    }

    private CachedConfig? ReadConfig(string path, bool editorConfig)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            _cache.Remove(path);
            return null;
        }

        // An editor can change the declaration without changing the file length or timestamp.
        var text = File.ReadAllText(path);
        if (_cache.TryGetValue(path, out var cached) && string.Equals(cached.Text, text, StringComparison.Ordinal))
        {
            return cached;
        }

        if (editorConfig)
        {
            var config = AnalyzerConfig.Parse(text, path);
            cached = new CachedConfig(text, AnalyzerConfigSet.Create<AnalyzerConfig[]>([config]), IsRootConfig(text), null);
        }
        else
        {
            cached = new CachedConfig(text, null, false, ReadJsonSetting(text, path));
        }

        _cache[path] = cached;
        return cached;
    }

    private static bool IsRootConfig(string text)
    {
        // Roslyn applies root internally but does not expose it on AnalyzerConfig in the pinned version.
        using var reader = new StringReader(text);
        var root = false;
        while (reader.ReadLine() is { } rawLine)
        {
            var line = rawLine.AsSpan().Trim();
            if (line.StartsWith("[", StringComparison.Ordinal))
            {
                break;
            }

            var equals = line.IndexOf('=');
            if (equals >= 0 && line[..equals].Trim().Equals("root", StringComparison.OrdinalIgnoreCase))
            {
                root = line[(equals + 1)..].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            }
        }

        return root;
    }

    private static string? ReadJsonSetting(string text, string path)
    {
        // As in startup settings, a malformed document does not hide a valid lower-priority file.
        if (!RoslynMcpJsoncParser.TryParse(text, out var document, out _) || document is null)
        {
            return null;
        }

        using (document)
        {
            var key = RoslynMcpFileSettings.SourceEncodingKey;
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(key, out var value)
                || value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString()!.Trim(),
                JsonValueKind.Number => value.GetRawText(),
                _ => throw InvalidEncoding(path, key, value.GetRawText()),
            };
        }
    }

    private static Encoding ParseEncoding(string value, string path, string key) => value.ToLowerInvariant() switch
    {
        "utf-8" => SourceTextEncoding.BomFreeUtf8,
        "utf-8-bom" => Encoding.UTF8,
        "latin1" => Encoding.Latin1,
        "utf-16le" when key == "charset" => Encoding.Unicode,
        "utf-16be" when key == "charset" => Encoding.BigEndianUnicode,
        _ when key == RoslynMcpFileSettings.SourceEncodingKey => ParseCodePage(value, path, key),
        _ => throw InvalidEncoding(path, key, value),
    };

    private static InvalidDataException InvalidEncoding(string path, string key, string value, Exception? inner = null) =>
        new($"Unknown source encoding '{value}' for '{key}' in {path}.", inner);

    private sealed record CachedConfig(
        string Text,
        AnalyzerConfigSet? EditorConfig,
        bool IsRoot,
        string? SourceEncoding);

    private readonly string[] _settingsPaths = new[]
        { settings.WorkingDirectoryFilePath, settings.UserFilePath, settings.ExecutableFilePath }
        .OfType<string>()
        .Distinct(InputPathCanon.Comparer)
        .ToArray();
    private readonly Dictionary<string, CachedConfig> _cache = new(InputPathCanon.Comparer);
}
