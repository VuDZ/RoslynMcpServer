using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace RoslynMcpServer.Config;

/// <summary>
/// Optional <c>RoslynMcp.jsonc</c> settings (exe directory then process cwd; cwd wins).
/// Not a fork <c>WorkspaceConfig</c> and does not publish a raw MSBuild solution.
/// </summary>
public sealed class RoslynMcpFileSettings
{
    public const string FileName = "RoslynMcp.jsonc";

    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
    {
        "workspace-path",
        "configuration",
        "platform",
        "target-framework",
        "max-results",
        "preview",
        "ripgrep-path",
    };

    public static RoslynMcpFileSettings Empty { get; } = new(
        executableFilePath: null,
        workingDirectoryFilePath: null,
        executableFilePresent: false,
        workingDirectoryFilePresent: false,
        workspacePath: null,
        configuration: null,
        platform: null,
        targetFramework: null,
        maxResults: null,
        preview: null,
        ripgrepPath: null,
        unknownKeys: Array.Empty<string>(),
        parseFailures: Array.Empty<RoslynMcpConfigParseFailure>());

    private RoslynMcpFileSettings(
        string? executableFilePath,
        string? workingDirectoryFilePath,
        bool executableFilePresent,
        bool workingDirectoryFilePresent,
        string? workspacePath,
        string? configuration,
        string? platform,
        string? targetFramework,
        int? maxResults,
        bool? preview,
        string? ripgrepPath,
        IReadOnlyList<string> unknownKeys,
        IReadOnlyList<RoslynMcpConfigParseFailure> parseFailures)
    {
        ExecutableFilePath = executableFilePath;
        WorkingDirectoryFilePath = workingDirectoryFilePath;
        ExecutableFilePresent = executableFilePresent;
        WorkingDirectoryFilePresent = workingDirectoryFilePresent;
        WorkspacePath = workspacePath;
        Configuration = configuration;
        Platform = platform;
        TargetFramework = targetFramework;
        MaxResults = maxResults;
        Preview = preview;
        RipgrepPath = ripgrepPath;
        UnknownKeys = unknownKeys;
        ParseFailures = parseFailures;
    }

    public string? ExecutableFilePath { get; }
    public string? WorkingDirectoryFilePath { get; }
    public bool ExecutableFilePresent { get; }
    public bool WorkingDirectoryFilePresent { get; }

    /// <summary>Raw <c>workspace-path</c> from the merged file (may be relative).</summary>
    public string? WorkspacePath { get; }

    public string? Configuration { get; }
    public string? Platform { get; }
    public string? TargetFramework { get; }
    public int? MaxResults { get; }
    public bool? Preview { get; }

    /// <summary>Stored for stage 7; does not switch the default search engine by itself.</summary>
    public string? RipgrepPath { get; }

    public IReadOnlyList<string> UnknownKeys { get; }
    public IReadOnlyList<RoslynMcpConfigParseFailure> ParseFailures { get; }

    public bool HasAnyFile => ExecutableFilePresent || WorkingDirectoryFilePresent;

    /// <summary>
    /// Absolute path for lazy load when <see cref="WorkspacePath"/> is set.
    /// Relative values resolve against the process working directory (not the exe directory).
    /// </summary>
    public string? ResolveWorkspacePathAgainstWorkingDirectory()
    {
        if (string.IsNullOrWhiteSpace(WorkspacePath))
        {
            return null;
        }

        var trimmed = WorkspacePath.Trim();
        return Path.IsPathRooted(trimmed)
            ? Path.GetFullPath(trimmed)
            : Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, trimmed));
    }

    public static RoslynMcpFileSettings LoadFromDefaultLocations(ILogger? logger = null)
    {
        var exeDir = AppContext.BaseDirectory;
        var cwd = Environment.CurrentDirectory;
        return LoadFromDirectories(exeDir, cwd, logger);
    }

    public static RoslynMcpFileSettings LoadFromDirectories(
        string? executableDirectory,
        string? workingDirectory,
        ILogger? logger = null)
    {
        var exePath = string.IsNullOrWhiteSpace(executableDirectory)
            ? null
            : Path.Combine(executableDirectory, FileName);
        var cwdPath = string.IsNullOrWhiteSpace(workingDirectory)
            ? null
            : Path.Combine(workingDirectory, FileName);

        PartialSettings? exe = null;
        PartialSettings? cwd = null;
        var failures = new List<RoslynMcpConfigParseFailure>();

        if (exePath is not null && File.Exists(exePath))
        {
            if (TryReadFile(exePath, out var partial, out var error))
            {
                exe = partial;
            }
            else
            {
                failures.Add(new RoslynMcpConfigParseFailure(exePath, error ?? "Unknown parse error."));
                logger?.LogWarning("RoslynMcp.jsonc failed to parse at {Path}: {Error}", exePath, error);
            }
        }

        if (cwdPath is not null
            && File.Exists(cwdPath)
            && !PathsEqual(cwdPath, exePath))
        {
            if (TryReadFile(cwdPath, out var partial, out var error))
            {
                cwd = partial;
            }
            else
            {
                failures.Add(new RoslynMcpConfigParseFailure(cwdPath, error ?? "Unknown parse error."));
                logger?.LogWarning("RoslynMcp.jsonc failed to parse at {Path}: {Error}", cwdPath, error);
            }
        }

        var merged = Merge(exe, cwd);
        foreach (var key in merged.UnknownKeys)
        {
            logger?.LogWarning("RoslynMcp.jsonc unknown key ignored: {Key}", key);
        }

        var exePresent = exePath is not null && File.Exists(exePath);
        var cwdPresent = cwdPath is not null && File.Exists(cwdPath);
        return new RoslynMcpFileSettings(
            executableFilePath: exePath,
            workingDirectoryFilePath: cwdPath,
            executableFilePresent: exePresent,
            workingDirectoryFilePresent: cwdPresent && !PathsEqual(cwdPath, exePath),
            workspacePath: merged.WorkspacePath,
            configuration: merged.Configuration,
            platform: merged.Platform,
            targetFramework: merged.TargetFramework,
            maxResults: merged.MaxResults,
            preview: merged.Preview,
            ripgrepPath: merged.RipgrepPath,
            unknownKeys: merged.UnknownKeys,
            parseFailures: failures);
    }

    /// <summary>Test/helper: merge two already-parsed partial objects (cwd overrides exe).</summary>
    internal static PartialSettings Merge(PartialSettings? executable, PartialSettings? workingDirectory)
    {
        if (executable is null && workingDirectory is null)
        {
            return PartialSettings.Empty;
        }

        if (executable is null)
        {
            return workingDirectory!;
        }

        if (workingDirectory is null)
        {
            return executable;
        }

        var unknown = new List<string>();
        AddUnique(unknown, executable.UnknownKeys);
        AddUnique(unknown, workingDirectory.UnknownKeys);

        return new PartialSettings(
            workspacePath: workingDirectory.WorkspacePath ?? executable.WorkspacePath,
            configuration: workingDirectory.Configuration ?? executable.Configuration,
            platform: workingDirectory.Platform ?? executable.Platform,
            targetFramework: workingDirectory.TargetFramework ?? executable.TargetFramework,
            maxResults: workingDirectory.MaxResults ?? executable.MaxResults,
            preview: workingDirectory.Preview ?? executable.Preview,
            ripgrepPath: workingDirectory.RipgrepPath ?? executable.RipgrepPath,
            unknownKeys: unknown);
    }

    private static bool TryReadFile(string path, out PartialSettings settings, out string? error)
    {
        settings = PartialSettings.Empty;
        error = null;
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        if (!RoslynMcpJsoncParser.TryParse(text, out var document, out error) || document is null)
        {
            return false;
        }

        using (document)
        {
            settings = ParseDocument(document);
            return true;
        }
    }

    private static PartialSettings ParseDocument(JsonDocument document)
    {
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("RoslynMcp.jsonc root must be a JSON object.");
        }

        string? workspacePath = null;
        string? configuration = null;
        string? platform = null;
        string? targetFramework = null;
        int? maxResults = null;
        bool? preview = null;
        string? ripgrepPath = null;
        var unknown = new List<string>();

        foreach (var property in document.RootElement.EnumerateObject())
        {
            var name = property.Name;
            if (!KnownKeys.Contains(name))
            {
                unknown.Add(name);
                continue;
            }

            switch (name)
            {
                case "workspace-path":
                    workspacePath = ReadString(property.Value);
                    break;
                case "configuration":
                    configuration = ReadString(property.Value);
                    break;
                case "platform":
                    platform = ReadString(property.Value);
                    break;
                case "target-framework":
                    targetFramework = ReadString(property.Value);
                    break;
                case "max-results":
                    maxResults = ReadPositiveInt(property.Value);
                    break;
                case "preview":
                    preview = ReadBool(property.Value);
                    break;
                case "ripgrep-path":
                    ripgrepPath = ReadString(property.Value);
                    break;
            }
        }

        return new PartialSettings(
            workspacePath,
            configuration,
            platform,
            targetFramework,
            maxResults,
            preview,
            ripgrepPath,
            unknown);
    }

    private static string? ReadString(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"Expected a string, got {element.ValueKind}.");
        }

        var value = element.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int? ReadPositiveInt(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var n) && n > 0)
        {
            return n;
        }

        if (element.ValueKind == JsonValueKind.String
            && int.TryParse(element.GetString(), out n)
            && n > 0)
        {
            return n;
        }

        throw new JsonException("max-results must be a positive integer.");
    }

    private static bool? ReadBool(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return element.GetBoolean();
        }

        throw new JsonException("preview must be a boolean.");
    }

    private static void AddUnique(List<string> target, IReadOnlyList<string> source)
    {
        foreach (var item in source)
        {
            if (!target.Contains(item, StringComparer.Ordinal))
            {
                target.Add(item);
            }
        }
    }

    private static bool PathsEqual(string? a, string? b)
    {
        if (a is null || b is null)
        {
            return false;
        }

        return string.Equals(
            Path.GetFullPath(a),
            Path.GetFullPath(b),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    internal sealed class PartialSettings
    {
        public static PartialSettings Empty { get; } = new(
            null, null, null, null, null, null, null, Array.Empty<string>());

        public PartialSettings(
            string? workspacePath,
            string? configuration,
            string? platform,
            string? targetFramework,
            int? maxResults,
            bool? preview,
            string? ripgrepPath,
            IReadOnlyList<string> unknownKeys)
        {
            WorkspacePath = workspacePath;
            Configuration = configuration;
            Platform = platform;
            TargetFramework = targetFramework;
            MaxResults = maxResults;
            Preview = preview;
            RipgrepPath = ripgrepPath;
            UnknownKeys = unknownKeys;
        }

        public string? WorkspacePath { get; }
        public string? Configuration { get; }
        public string? Platform { get; }
        public string? TargetFramework { get; }
        public int? MaxResults { get; }
        public bool? Preview { get; }
        public string? RipgrepPath { get; }
        public IReadOnlyList<string> UnknownKeys { get; }
    }
}

public sealed record RoslynMcpConfigParseFailure(string Path, string Message);
