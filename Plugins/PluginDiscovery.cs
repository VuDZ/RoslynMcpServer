using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;

namespace RoslynMcpServer.Plugins;

/// <summary>
/// Turns the three plugin sources into load plans without opening a plugin assembly: the drop-in
/// subdirectories next to the host, the <c>plugins</c> key of the settings files, and the environment
/// variable.
/// </summary>
/// <remarks>
/// <para>
/// Sources are read in that order and the first occurrence of an id wins; a later source offering an
/// already accepted id is skipped with a reason. Nothing here copies or loads a file.
/// </para>
/// <para>
/// Every source is checked before the pass moves on: manifest fields, the host product version, and the
/// contract versions recorded in the plugin's <c>deps.json</c>. Each refusal becomes a skip with its
/// concrete cause instead of an exception, so one broken plugin leaves the others usable.
/// </para>
/// </remarks>
public static class PluginDiscovery
{
    /// <summary>Environment variable holding <see cref="Path.PathSeparator"/>-separated plugin paths.</summary>
    public const string EnvironmentVariableName = "ROSLYN_MCP_PLUGINS";

    /// <summary>Directory next to the host executable whose immediate subdirectories are drop-in plugins.</summary>
    public const string DropInDirectoryName = "plugins";

    private const string ModelContextProtocolAssembly = "ModelContextProtocol";
    private const string HostProductAssembly = "RoslynMcpServer";
    private const string RoslynAssembly = "Microsoft.CodeAnalysis";

    /// <summary>
    /// Reads the three plugin sources and returns one plan per accepted source.
    /// </summary>
    /// <param name="baseDirectory">Host base directory; the drop-in root is <c>{baseDirectory}/plugins</c>.</param>
    /// <param name="configuredPaths">Raw values of the <c>plugins</c> key of <c>RoslynMcp.jsonc</c>.</param>
    /// <param name="environmentPaths">
    /// Raw <c>ROSLYN_MCP_PLUGINS</c> value; <see langword="null"/> or empty means the variable is not set.
    /// </param>
    /// <param name="hostVersion">Product version of the running host, compared with <c>minHostVersion</c>.</param>
    /// <returns>Plans in discovery order, and one skip per refused source with its reason.</returns>
    public static PluginDiscoveryResult Discover(
        string baseDirectory,
        IReadOnlyList<string> configuredPaths,
        string? environmentPaths,
        Version hostVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentNullException.ThrowIfNull(configuredPaths);
        ArgumentNullException.ThrowIfNull(hostVersion);

        var pass = new DiscoveryPass(hostVersion);

        foreach (var pluginDirectory in EnumerateDropInDirectories(baseDirectory, pass.Skipped))
        {
            pass.AddDropIn(pluginDirectory);
        }

        foreach (var configuredPath in configuredPaths)
        {
            pass.AddExplicitPath(configuredPath);
        }

        foreach (var environmentPath in SplitEnvironmentPaths(environmentPaths))
        {
            pass.AddExplicitPath(environmentPath);
        }

        return new PluginDiscoveryResult
        {
            Plans = [.. pass.Plans],
            Skipped = [.. pass.Skipped],
        };
    }

    /// <summary>
    /// Reads <c>ROSLYN_MCP_PLUGINS</c> from the process environment and discovers plugins with it. The
    /// variable is an argument of <see cref="Discover"/>, so tests never have to change a process-wide
    /// value.
    /// </summary>
    public static PluginDiscoveryResult DiscoverFromEnvironment(
        string baseDirectory,
        IReadOnlyList<string> configuredPaths,
        Version hostVersion) =>
        Discover(baseDirectory, configuredPaths, Environment.GetEnvironmentVariable(EnvironmentVariableName), hostVersion);

    /// <summary>
    /// Immediate subdirectories of <c>{baseDirectory}/plugins</c>, in ordinal name order. Nested
    /// directories are dependencies of their plugin, not plugins of their own.
    /// </summary>
    /// <remarks>
    /// A missing drop-in directory is a normal layout with no drop-ins, not an error. A directory the host
    /// cannot list is reported as a skip rather than silently dropped.
    /// </remarks>
    private static IReadOnlyList<string> EnumerateDropInDirectories(string baseDirectory, List<PluginDiscoverySkip> skipped)
    {
        var dropInRoot = Path.Combine(baseDirectory, DropInDirectoryName);
        if (!Directory.Exists(dropInRoot))
        {
            return [];
        }

        try
        {
            // The file system order is arbitrary and the first source of an id wins, so sorting keeps the
            // plans of one layout reproducible.
            return
            [
                .. Directory.EnumerateDirectories(dropInRoot)
                    .OrderBy(path => Path.GetFileName(path) ?? string.Empty, StringComparer.Ordinal),
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            skipped.Add(new PluginDiscoverySkip
            {
                SourcePath = dropInRoot,
                Reason = $"drop-in directory '{dropInRoot}' could not be listed: {ex.Message}",
            });

            return [];
        }
    }

    /// <summary>Segments of the environment variable, split by <see cref="Path.PathSeparator"/>.</summary>
    private static IReadOnlyList<string> SplitEnvironmentPaths(string? environmentPaths)
    {
        if (string.IsNullOrWhiteSpace(environmentPaths))
        {
            return [];
        }

        return
        [
            .. environmentPaths
                .Split(Path.PathSeparator)
                .Select(segment => segment.Trim())
                .Where(segment => segment.Length > 0),
        ];
    }

    /// <summary>
    /// Checks the contract versions the plugin was compiled against, before anything is loaded.
    /// </summary>
    /// <remarks>
    /// The versions come from the compile graph of the plugin's own build, because the host shares these
    /// types with the plugin: a reference from another major line, or one newer than the loaded host
    /// assembly, would fail on the first shared type. The plugin's DLLs are never opened here, so a version
    /// the build did not record is a skip rather than a guess.
    /// </remarks>
    private static bool TryCheckReferenceVersions(string entryPath, [NotNullWhen(false)] out string? error)
    {
        var depsPath = Path.Combine(
            Path.GetDirectoryName(entryPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(entryPath) + ".deps.json");

        foreach (var assemblyName in _checkedAssemblies)
        {
            var referenceVersion = TryReadReferenceVersion(depsPath, assemblyName);
            if (referenceVersion is null)
            {
                error = $"no reference version for '{assemblyName}' in '{depsPath}'";
                return false;
            }

            var hostVersion = TryGetLoadedAssemblyVersion(assemblyName);
            if (hostVersion is null)
            {
                error = $"the host assembly '{assemblyName}' is not loaded and could not be resolved";
                return false;
            }

            if (!IsReferenceCompatible(assemblyName, referenceVersion, hostVersion, out error))
            {
                return false;
            }
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Compatibility rule of one contract assembly, applied at the same major only when the reference is
    /// not newer than the loaded host assembly.
    /// </summary>
    private static bool IsReferenceCompatible(
        string assemblyName,
        Version referenceVersion,
        Version hostVersion,
        [NotNullWhen(false)] out string? error)
    {
        if (assemblyName.Equals(ModelContextProtocolAssembly, StringComparison.Ordinal))
        {
            // The MCP tool contract may change inside a major line, so the Major.Minor pair must match
            // exactly; the patch of the host is not part of the promise.
            if (referenceVersion.Major != hostVersion.Major || referenceVersion.Minor != hostVersion.Minor)
            {
                error = $"reference '{assemblyName}' {referenceVersion} has different Major.Minor than the loaded host assembly {hostVersion}";
                return false;
            }
        }
        else if (referenceVersion.Major != hostVersion.Major)
        {
            error = $"reference '{assemblyName}' {referenceVersion} has major {referenceVersion.Major}, the loaded host assembly is {hostVersion}";
            return false;
        }
        else if (IsNewerThan(referenceVersion, hostVersion))
        {
            error = $"reference '{assemblyName}' {referenceVersion} is newer than the loaded host assembly {hostVersion}";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Reference version of one library in the plugin's <c>deps.json</c>: the <c>assemblyVersion</c> of the
    /// compile asset, then of the runtime asset, then the version part of the library key
    /// (<c>Name/Version</c>).
    /// </summary>
    /// <remarks>
    /// The record is identified by the asset file that is the assembly, not by the library key: the file
    /// name of a <c>compile</c> or <c>runtime</c> asset must be <c>{assemblyName}.dll</c>. A real build
    /// output delivers <c>Microsoft.CodeAnalysis.dll</c> from the library
    /// <c>Microsoft.CodeAnalysis.Common</c> and the host's own assembly from <c>RoslynMcpServer</c> with an
    /// asset object that records no version, so the library key is only the fallback. Any unreadable or
    /// malformed file means "no version", which the caller turns into a skip.
    /// </remarks>
    private static Version? TryReadReferenceVersion(string depsPath, string assemblyName)
    {
        if (!File.Exists(depsPath))
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllBytes(depsPath));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        using (document)
        {
            return TryReadReferenceVersion(document.RootElement, assemblyName);
        }
    }

    private static Version? TryReadReferenceVersion(JsonElement root, string assemblyName)
    {
        // A deps.json is third-party input, so every step of this walk checks the shape it is about to use.
        // A graph no build would write must end in "no version", not in an exception that stops the host
        // from starting: JsonElement.TryGetProperty throws when its receiver is not an object.
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("targets", out var targets)
            || targets.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var assetFileName = assemblyName + ".dll";
        Version? compileVersion = null;
        Version? runtimeVersion = null;
        Version? libraryKeyVersion = null;

        // One plugin is built for one target framework, but reading every target keeps the reader working
        // for a deps.json that lists more than one.
        foreach (var target in targets.EnumerateObject())
        {
            if (target.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var library in target.Value.EnumerateObject())
            {
                if (library.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                // The library key name is not the assembly name: Roslyn ships as
                // Microsoft.CodeAnalysis.Common and the host project as RoslynMcpServer, so the record is
                // the one whose asset file is this assembly. A library with a matching key but another
                // asset is a different assembly and contributes nothing.
                var hasCompileAsset = TryFindAssemblyAsset(library.Value, "compile", assetFileName, out var compileAssetVersion);
                var hasRuntimeAsset = TryFindAssemblyAsset(library.Value, "runtime", assetFileName, out var runtimeAssetVersion);
                if (!hasCompileAsset && !hasRuntimeAsset)
                {
                    continue;
                }

                compileVersion ??= compileAssetVersion;
                runtimeVersion ??= runtimeAssetVersion;
                libraryKeyVersion ??= ReadLibraryKeyVersion(library.Name);
            }
        }

        return compileVersion ?? runtimeVersion ?? libraryKeyVersion;
    }

    /// <summary>
    /// Finds the asset that is <paramref name="assetFileName"/> inside one <c>compile</c> or
    /// <c>runtime</c> section and reads its <c>assemblyVersion</c>.
    /// </summary>
    /// <param name="library">One library record; callers check that it is a JSON object before passing it.</param>
    /// <param name="sectionName">Section to look in, <c>compile</c> or <c>runtime</c>.</param>
    /// <param name="assetFileName">File name of the assembly the record is looked for.</param>
    /// <param name="version">Recorded version of that asset, or <see langword="null"/> when it records none.</param>
    /// <returns>
    /// <see langword="true"/> when the section carries that asset, even if the asset records no version: an
    /// asset without <c>assemblyVersion</c> is still the record of this assembly, whose version then has to
    /// come from the library key.
    /// </returns>
    private static bool TryFindAssemblyAsset(
        JsonElement library,
        string sectionName,
        string assetFileName,
        out Version? version)
    {
        version = null;

        if (!library.TryGetProperty(sectionName, out var section) || section.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // Asset keys are paths such as lib/net10.0/Microsoft.CodeAnalysis.dll; the folder part only says
        // which target the asset was built for, so the file name is what identifies the assembly.
        foreach (var asset in section.EnumerateObject())
        {
            var fileName = Path.GetFileName(asset.Name) ?? string.Empty;
            if (!fileName.Equals(assetFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (asset.Value.ValueKind == JsonValueKind.Object
                && asset.Value.TryGetProperty("assemblyVersion", out var element)
                && element.ValueKind == JsonValueKind.String)
            {
                version = TryParseVersion(element.GetString());
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Version part of a deps.json library key of the form <c>Name/Version</c>, or <see langword="null"/>
    /// when the key does not carry one.
    /// </summary>
    private static Version? ReadLibraryKeyVersion(string libraryKey)
    {
        var separator = libraryKey.LastIndexOf('/');
        return separator > 0 && separator < libraryKey.Length - 1
            ? TryParseVersion(libraryKey[(separator + 1)..])
            : null;
    }

    /// <summary>
    /// Parses a version as it appears in <c>deps.json</c>. A NuGet library key may carry a prerelease or
    /// build suffix (<c>1.3.0-preview.1+build</c>) that <see cref="Version"/> rejects, so the suffix is cut
    /// off before the last attempt.
    /// </summary>
    private static Version? TryParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        if (Version.TryParse(trimmed, out var version))
        {
            return version;
        }

        var suffix = trimmed.IndexOfAny(['-', '+']);
        return suffix > 0 && Version.TryParse(trimmed[..suffix], out version) ? version : null;
    }

    /// <summary>
    /// Version of the host assembly actually loaded in this process. The loaded set is asked first, so the
    /// check runs against the assembly that will serve the plugin; the load by simple name only covers an
    /// assembly the host references but has not touched yet.
    /// </summary>
    private static Version? TryGetLoadedAssemblyVersion(string simpleName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name?.Equals(simpleName, StringComparison.Ordinal) == true)
            {
                return assembly.GetName().Version;
            }
        }

        try
        {
            return Assembly.Load(new AssemblyName(simpleName)).GetName().Version;
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>True when <paramref name="entry"/> names a file inside the plugin directory, not a path.</summary>
    private static bool IsBareFileName(string entry) =>
        !Path.IsPathRooted(entry)
        && entry is not "." and not ".."
        && entry.Equals(Path.GetFileName(entry), StringComparison.Ordinal);

    /// <summary>Resolves a configured path against the process working directory.</summary>
    private static bool TryResolveFullPath(string configuredPath, out string fullPath)
    {
        try
        {
            fullPath = Path.GetFullPath(configuredPath.Trim());
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            fullPath = configuredPath;
            return false;
        }
    }

    /// <summary>
    /// Compares two versions as version numbers rather than as component lists: <c>5.8</c> and
    /// <c>5.8.0.0</c> are the same version, and a missing component counts as zero.
    /// </summary>
    private static bool IsNewerThan(Version candidate, Version reference) => Normalize(candidate) > Normalize(reference);

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));

    /// <summary>
    /// One discovery pass. Sources are offered in order, and the pass keeps the plan of every source that
    /// passes all gates plus a skip with the concrete reason for every source that does not.
    /// </summary>
    private sealed class DiscoveryPass
    {
        public List<PluginLoadPlan> Plans { get; } = [];

        public List<PluginDiscoverySkip> Skipped { get; } = [];

        public DiscoveryPass(Version hostVersion)
        {
            _hostVersion = hostVersion;
        }

        /// <summary>
        /// Drop-in source: a directory directly under the drop-in root. Its name is the plugin id, and the
        /// plugin is loaded where it lies.
        /// </summary>
        public void AddDropIn(string directoryPath)
        {
            if (!PluginManifest.TryRead(Path.Combine(directoryPath, PluginManifest.FileName), out var manifest, out var error))
            {
                Skipped.Add(Skip(directoryPath, pluginId: null, error));
                return;
            }

            var directoryName = Path.GetFileName(directoryPath);
            if (!directoryName.Equals(manifest.Id, StringComparison.Ordinal))
            {
                Skipped.Add(Skip(
                    directoryPath,
                    manifest.Id,
                    $"drop-in directory name '{directoryName}' does not equal the manifest id '{manifest.Id}'"));

                return;
            }

            AddPlan(
                sourcePath: directoryPath,
                pluginDirectory: directoryPath,
                explicitEntryPath: null,
                manifest: manifest,
                usesShadowCopy: false);
        }

        /// <summary>
        /// Explicit source from the settings key or the environment variable: either the plugin directory
        /// or the entry file itself. The directory name is not compared with the id here, because a
        /// development build directory is named after its target framework.
        /// </summary>
        public void AddExplicitPath(string configuredPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                Skipped.Add(Skip(configuredPath ?? string.Empty, pluginId: null, "configured plugin path is empty"));
                return;
            }

            if (!TryResolveFullPath(configuredPath, out var fullPath))
            {
                Skipped.Add(Skip(
                    configuredPath,
                    pluginId: null,
                    $"configured plugin path '{configuredPath}' is not a usable file system path"));

                return;
            }

            if (Directory.Exists(fullPath))
            {
                AddExplicitDirectory(fullPath);
                return;
            }

            if (File.Exists(fullPath))
            {
                AddExplicitFile(fullPath);
                return;
            }

            Skipped.Add(Skip(
                fullPath,
                pluginId: null,
                $"configured plugin path '{fullPath}' is neither an existing directory nor an existing file"));
        }

        private void AddExplicitDirectory(string directoryPath)
        {
            if (!PluginManifest.TryRead(Path.Combine(directoryPath, PluginManifest.FileName), out var manifest, out var error))
            {
                Skipped.Add(Skip(directoryPath, pluginId: null, error));
                return;
            }

            AddPlan(
                sourcePath: directoryPath,
                pluginDirectory: directoryPath,
                explicitEntryPath: null,
                manifest: manifest,
                usesShadowCopy: true);
        }

        private void AddExplicitFile(string filePath)
        {
            var directoryPath = Path.GetDirectoryName(filePath) ?? string.Empty;
            if (!PluginManifest.TryRead(Path.Combine(directoryPath, PluginManifest.FileName), out var manifest, out var error))
            {
                Skipped.Add(Skip(filePath, pluginId: null, error));
                return;
            }

            var fileName = Path.GetFileName(filePath);
            if (!fileName.Equals(manifest.Entry, StringComparison.Ordinal))
            {
                Skipped.Add(Skip(
                    filePath,
                    manifest.Id,
                    $"entry file '{fileName}' does not equal the manifest entry '{manifest.Entry}'"));

                return;
            }

            // The named file is the entry. Sibling assemblies are never inspected for a candidate.
            AddPlan(
                sourcePath: filePath,
                pluginDirectory: directoryPath,
                explicitEntryPath: filePath,
                manifest: manifest,
                usesShadowCopy: true);
        }

        /// <summary>
        /// Applies the gates of an explicitly offered source and records the plan, or records why this
        /// source produced none.
        /// </summary>
        /// <param name="sourcePath">Source as the user gave it; named in the skip.</param>
        /// <param name="pluginDirectory">Directory holding the manifest and the entry assembly.</param>
        /// <param name="explicitEntryPath">
        /// Entry file the user named, or <see langword="null"/> to build the path from the manifest.
        /// </param>
        /// <param name="manifest">Manifest of this source.</param>
        /// <param name="usesShadowCopy">Whether the accepted plan must be loaded from a copy.</param>
        private void AddPlan(
            string sourcePath,
            string pluginDirectory,
            string? explicitEntryPath,
            PluginManifest manifest,
            bool usesShadowCopy)
        {
            if (_claimedIds.Contains(manifest.Id))
            {
                Skipped.Add(Skip(
                    sourcePath,
                    manifest.Id,
                    $"plugin id '{manifest.Id}' is already accepted from an earlier source"));

                return;
            }

            if (!IsBareFileName(manifest.Entry))
            {
                Skipped.Add(Skip(sourcePath, manifest.Id, $"manifest entry '{manifest.Entry}' is not a bare file name"));
                return;
            }

            if (!TryCheckHostVersion(manifest.MinHostVersion, out var versionError))
            {
                Skipped.Add(Skip(sourcePath, manifest.Id, versionError));
                return;
            }

            var entryPath = explicitEntryPath ?? Path.Combine(pluginDirectory, manifest.Entry);
            if (!TryCheckReferenceVersions(entryPath, out var referenceError))
            {
                Skipped.Add(Skip(sourcePath, manifest.Id, referenceError));
                return;
            }

            // Only an accepted plan claims the id, so a source refused here cannot shadow a later copy of
            // the same plugin that would load.
            _claimedIds.Add(manifest.Id);
            Plans.Add(new PluginLoadPlan
            {
                Id = manifest.Id,
                Directory = pluginDirectory,
                EntryPath = entryPath,
                PluginType = manifest.PluginType,
                ToolPrefix = manifest.ToolPrefix,
                UsesShadowCopy = usesShadowCopy,
            });
        }

        /// <summary>
        /// <c>minHostVersion</c> is the product lower bound: the major must match and the host must not be
        /// older. One product version does not guarantee the MCP and Roslyn types, which is why the
        /// references are checked separately.
        /// </summary>
        private bool TryCheckHostVersion(Version minHostVersion, [NotNullWhen(false)] out string? error)
        {
            if (minHostVersion.Major != _hostVersion.Major)
            {
                error = $"manifest minHostVersion '{minHostVersion}' has major {minHostVersion.Major}, the host is '{_hostVersion}'";
                return false;
            }

            if (IsNewerThan(minHostVersion, _hostVersion))
            {
                error = $"host version '{_hostVersion}' is older than the required minHostVersion '{minHostVersion}'";
                return false;
            }

            error = null;
            return true;
        }

        private static PluginDiscoverySkip Skip(string sourcePath, string? pluginId, string reason) =>
            new()
            {
                SourcePath = sourcePath,
                PluginId = pluginId,
                Reason = reason,
            };

        private readonly Version _hostVersion;
        private readonly HashSet<string> _claimedIds = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Contract assemblies whose reference version gates loading. Each is checked by
    /// <see cref="TryCheckReferenceVersions"/>.
    /// </summary>
    private static readonly string[] _checkedAssemblies =
    [
        ModelContextProtocolAssembly,
        HostProductAssembly,
        RoslynAssembly,
    ];
}
