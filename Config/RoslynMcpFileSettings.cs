using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace RoslynMcpServer.Config;

/// <summary>
/// Optional <c>RoslynMcp.jsonc</c> settings (exe, per-user directory, then process cwd; cwd wins).
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
        "search-prewarm",
        "buildhost-optimizations",
        "buildhost-evaluate-ahead-workers",
        "ripgrep-path",
        "plugins",
    };

    public static RoslynMcpFileSettings Empty { get; } = new(
        executableFilePath: null,
        workingDirectoryFilePath: null,
        userFilePath: null,
        userFilePresent: false,
        executableFilePresent: false,
        workingDirectoryFilePresent: false,
        workspacePath: null,
        configuration: null,
        platform: null,
        targetFramework: null,
        maxResults: null,
        preview: null,
        searchPrewarm: null,
        buildHostOptimizations: null,
        buildHostEvaluateAheadWorkers: null,
        ripgrepPath: null,
        plugins: Array.Empty<string>(),
        unknownKeys: Array.Empty<string>(),
        parseFailures: Array.Empty<RoslynMcpConfigParseFailure>());

    private RoslynMcpFileSettings(
        string? executableFilePath,
        string? workingDirectoryFilePath,
        string? userFilePath,
        bool userFilePresent,
        bool executableFilePresent,
        bool workingDirectoryFilePresent,
        string? workspacePath,
        string? configuration,
        string? platform,
        string? targetFramework,
        int? maxResults,
        bool? preview,
        bool? searchPrewarm,
        bool? buildHostOptimizations,
        int? buildHostEvaluateAheadWorkers,
        string? ripgrepPath,
        IReadOnlyList<string> plugins,
        IReadOnlyList<string> unknownKeys,
        IReadOnlyList<RoslynMcpConfigParseFailure> parseFailures)
    {
        ExecutableFilePath = executableFilePath;
        WorkingDirectoryFilePath = workingDirectoryFilePath;
        UserFilePath = userFilePath;
        UserFilePresent = userFilePresent;
        ExecutableFilePresent = executableFilePresent;
        WorkingDirectoryFilePresent = workingDirectoryFilePresent;
        WorkspacePath = workspacePath;
        Configuration = configuration;
        Platform = platform;
        TargetFramework = targetFramework;
        MaxResults = maxResults;
        Preview = preview;
        SearchPrewarm = searchPrewarm;
        BuildHostOptimizations = buildHostOptimizations;
        BuildHostEvaluateAheadWorkers = buildHostEvaluateAheadWorkers;
        RipgrepPath = ripgrepPath;
        Plugins = plugins;
        UnknownKeys = unknownKeys;
        ParseFailures = parseFailures;
    }

    public string? ExecutableFilePath { get; }
    public string? WorkingDirectoryFilePath { get; }
    public string? UserFilePath { get; }
    public bool UserFilePresent { get; }
    public bool ExecutableFilePresent { get; }
    public bool WorkingDirectoryFilePresent { get; }

    /// <summary>Raw <c>workspace-path</c> from the merged file (may be relative).</summary>
    public string? WorkspacePath { get; }

    public string? Configuration { get; }
    public string? Platform { get; }
    public string? TargetFramework { get; }
    public int? MaxResults { get; }
    public bool? Preview { get; }

    /// <summary>
    /// <c>search-prewarm</c>: background index prewarm after a load. <c>ROSLYN_MCP_SEARCH_PREWARM</c> wins;
    /// with neither set the prewarm is on.
    /// </summary>
    public bool? SearchPrewarm { get; }

    /// <summary>
    /// <c>buildhost-optimizations</c>: references first, evaluation ahead and the shared SDK resolution cache in the forked BuildHost.
    /// <c>ROSLYN_MCP_BUILDHOST_OPTIMIZATIONS</c> wins; with neither set they are on.
    /// </summary>
    public bool? BuildHostOptimizations { get; }

    /// <summary>
    /// <c>buildhost-evaluate-ahead-workers</c>: worker threads of the evaluation ahead, 0 turns only it off.
    /// <c>ROSLYN_MCP_BUILDHOST_EVALUATE_AHEAD_WORKERS</c> wins; with neither set it is 4.
    /// </summary>
    public int? BuildHostEvaluateAheadWorkers { get; }

    /// <summary>Stored for stage 7; does not switch the default search engine by itself.</summary>
    public string? RipgrepPath { get; }

    /// <summary>
    /// Raw values of the <c>plugins</c> key: directories holding a <c>plugin.json</c> or entry DLL paths.
    /// Empty when the key is absent, so an empty array of the cwd file really means zero explicit paths.
    /// </summary>
    public IReadOnlyList<string> Plugins { get; }

    public IReadOnlyList<string> UnknownKeys { get; }
    public IReadOnlyList<RoslynMcpConfigParseFailure> ParseFailures { get; }

    public bool HasAnyFile => ExecutableFilePresent || UserFilePresent || WorkingDirectoryFilePresent;

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
        return LoadFromDirectories(exeDir, cwd, ServerUserDirectories.FromEnvironment().ConfigurationDirectory, logger);
    }

    public static RoslynMcpFileSettings LoadFromDirectories(
        string? executableDirectory,
        string? workingDirectory,
        ILogger? logger = null) =>
        LoadFromDirectories(executableDirectory, workingDirectory, userDirectory: null, logger);

    /// <summary>Reads each distinct file once and merges exe, user and cwd settings in priority order.</summary>
    public static RoslynMcpFileSettings LoadFromDirectories(
        string? executableDirectory,
        string? workingDirectory,
        string? userDirectory,
        ILogger? logger)
    {
        var exePath = GetSettingsPath(executableDirectory);
        var userPath = GetSettingsPath(userDirectory);
        var cwdPath = GetSettingsPath(workingDirectory);
        var failures = new List<RoslynMcpConfigParseFailure>();
        var cache = new Dictionary<string, PartialSettings?>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        var exe = Read(exePath, out var exePresent);
        var user = Read(userPath, out var userPresent);
        var cwd = Read(cwdPath, out var cwdPresent);
        var merged = Merge(Merge(exe, user), cwd);
        foreach (var key in merged.UnknownKeys)
        {
            logger?.LogWarning("RoslynMcp.jsonc unknown key ignored: {Key}", key);
        }

        return new RoslynMcpFileSettings(
            executableFilePath: exePath,
            workingDirectoryFilePath: cwdPath,
            userFilePath: userPath,
            userFilePresent: userPresent && !PathsEqual(userPath, exePath),
            executableFilePresent: exePresent,
            workingDirectoryFilePresent: cwdPresent && !PathsEqual(cwdPath, exePath) && !PathsEqual(cwdPath, userPath),
            workspacePath: merged.WorkspacePath,
            configuration: merged.Configuration,
            platform: merged.Platform,
            targetFramework: merged.TargetFramework,
            maxResults: merged.MaxResults,
            preview: merged.Preview,
            searchPrewarm: merged.SearchPrewarm,
            buildHostOptimizations: merged.BuildHostOptimizations,
            buildHostEvaluateAheadWorkers: merged.BuildHostEvaluateAheadWorkers,
            ripgrepPath: merged.RipgrepPath,
            plugins: merged.Plugins ?? Array.Empty<string>(),
            unknownKeys: merged.UnknownKeys,
            parseFailures: failures);

        PartialSettings? Read(string? path, out bool present)
        {
            present = path is not null && File.Exists(path);
            if (!present)
            {
                return null;
            }

            if (cache.TryGetValue(path!, out var cached))
            {
                return cached;
            }

            PartialSettings? settings = null;
            if (TryReadFile(path!, out var partial, out var error))
            {
                settings = partial;
            }
            else
            {
                failures.Add(new RoslynMcpConfigParseFailure(path!, error ?? "Unknown parse error."));
                logger?.LogWarning("RoslynMcp.jsonc failed to parse at {Path}: {Error}", path, error);
            }

            cache[path!] = settings;
            return settings;
        }
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
            // Wholesale, not key by key: the cwd array replaces the exe array, and an empty cwd array
            // therefore means zero explicit paths instead of falling back to the exe entries.
            plugins: workingDirectory.Plugins ?? executable.Plugins,
            unknownKeys: unknown,
            searchPrewarm: workingDirectory.SearchPrewarm ?? executable.SearchPrewarm,
            buildHostOptimizations: workingDirectory.BuildHostOptimizations ?? executable.BuildHostOptimizations,
            buildHostEvaluateAheadWorkers: workingDirectory.BuildHostEvaluateAheadWorkers ?? executable.BuildHostEvaluateAheadWorkers);
    }

    private static string? GetSettingsPath(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(Path.Combine(directory, FileName));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
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
            try
            {
                settings = ParseDocument(document);
                return true;
            }
            catch (JsonException ex)
            {
                // A known key with a value of the wrong type is a failure of this file, not of the host:
                // the parse is reported and the successfully read sibling file keeps merging by the
                // cwd-over-exe rule. Letting this escape would take the process down at startup.
                error = ex.Message;
                return false;
            }
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
        bool? searchPrewarm = null;
        bool? buildHostOptimizations = null;
        int? buildHostEvaluateAheadWorkers = null;
        string? ripgrepPath = null;
        IReadOnlyList<string>? plugins = null;
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
                    preview = ReadBool(property.Value, name);
                    break;
                case "search-prewarm":
                    searchPrewarm = ReadBool(property.Value, name);
                    break;
                case "buildhost-optimizations":
                    buildHostOptimizations = ReadBool(property.Value, name);
                    break;
                case "buildhost-evaluate-ahead-workers":
                    buildHostEvaluateAheadWorkers = ReadNonNegativeInt(property.Value, name);
                    break;
                case "ripgrep-path":
                    ripgrepPath = ReadString(property.Value);
                    break;
                case "plugins":
                    plugins = ReadStringArray(property.Value);
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
            plugins,
            unknown,
            searchPrewarm,
            buildHostOptimizations,
            buildHostEvaluateAheadWorkers);
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

    /// <summary>
    /// Reads the <c>plugins</c> array. A missing value is <see langword="null"/>; a value of another type,
    /// or an element that is not a string, throws so the file is reported as a parse failure.
    /// </summary>
    private static IReadOnlyList<string>? ReadStringArray(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException($"plugins must be an array of strings, got {element.ValueKind}.");
        }

        var items = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new JsonException($"plugins must be an array of strings, got an element of kind {item.ValueKind}.");
            }

            items.Add(item.GetString()?.Trim() ?? string.Empty);
        }

        return items;
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

    private static int? ReadNonNegativeInt(JsonElement element, string key)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var n) && n >= 0)
        {
            return n;
        }

        throw new JsonException($"{key} must be a non-negative integer.");
    }

    private static bool? ReadBool(JsonElement element, string key)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return element.GetBoolean();
        }

        throw new JsonException($"{key} must be a boolean.");
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
            null, null, null, null, null, null, null, null, Array.Empty<string>());

        public PartialSettings(
            string? workspacePath,
            string? configuration,
            string? platform,
            string? targetFramework,
            int? maxResults,
            bool? preview,
            string? ripgrepPath,
            IReadOnlyList<string>? plugins,
            IReadOnlyList<string> unknownKeys,
            bool? searchPrewarm = null,
            bool? buildHostOptimizations = null,
            int? buildHostEvaluateAheadWorkers = null)
        {
            WorkspacePath = workspacePath;
            Configuration = configuration;
            Platform = platform;
            TargetFramework = targetFramework;
            MaxResults = maxResults;
            Preview = preview;
            RipgrepPath = ripgrepPath;
            Plugins = plugins;
            UnknownKeys = unknownKeys;
            SearchPrewarm = searchPrewarm;
            BuildHostOptimizations = buildHostOptimizations;
            BuildHostEvaluateAheadWorkers = buildHostEvaluateAheadWorkers;
        }

        public string? WorkspacePath { get; }
        public string? Configuration { get; }
        public string? Platform { get; }
        public string? TargetFramework { get; }
        public int? MaxResults { get; }
        public bool? Preview { get; }
        public bool? SearchPrewarm { get; }
        public bool? BuildHostOptimizations { get; }
        public int? BuildHostEvaluateAheadWorkers { get; }
        public string? RipgrepPath { get; }

        /// <summary><see langword="null"/> when the file has no <c>plugins</c> key.</summary>
        public IReadOnlyList<string>? Plugins { get; }

        public IReadOnlyList<string> UnknownKeys { get; }
    }
}

public sealed record RoslynMcpConfigParseFailure(string Path, string Message);
