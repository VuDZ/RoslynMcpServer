using System.Reflection;
using System.Text.Json;
using RoslynMcpServer.Config;
using RoslynMcpServer.Plugins;
using Xunit;

namespace RoslynMcpServer.Tests.Plugins;

/// <summary>
/// Plugin discovery seam: the three ordered sources become load plans and skips without any plugin
/// assembly being opened. Every fixture is a temporary directory holding a hand-written <c>plugin.json</c>
/// and <c>deps.json</c>; the host assemblies the reference versions are compared against are the ones
/// loaded in this test process, so the expectations follow whatever the pinned packages provide.
/// </summary>
public sealed class PluginDiscoveryTests : IDisposable
{
    private const string PluginId = "sample";
    private const string PluginType = "Sample.SamplePlugin";
    private const string ToolPrefix = "sample_";
    private const string EntryFileName = "sample.dll";

    public PluginDiscoveryTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }

    [Fact]
    public void Missing_drop_in_directory_is_not_an_error()
    {
        var result = Discover();

        Assert.Empty(result.Plans);
        Assert.Empty(result.Skipped);
    }

    [Fact]
    public void Drop_in_directory_yields_one_plan_without_shadow_copy()
    {
        // The entry file is never created: discovery reports where it will load from, not that it exists.
        var pluginDirectory = CreateDropInPlugin("sample");

        var result = Discover();

        Assert.Empty(result.Skipped);
        var plan = Assert.Single(result.Plans);
        Assert.Equal(PluginId, plan.Id);
        Assert.Equal(pluginDirectory, plan.Directory);
        Assert.Equal(Path.Combine(pluginDirectory, EntryFileName), plan.EntryPath);
        Assert.Equal(PluginType, plan.PluginType);
        Assert.Equal(ToolPrefix, plan.ToolPrefix);
        Assert.False(plan.UsesShadowCopy);
    }

    [Fact]
    public void Nested_drop_in_directory_is_not_a_plugin()
    {
        var pluginDirectory = CreateDropInPlugin("sample");
        var nestedDirectory = Path.Combine(pluginDirectory, "runtimes", "win");
        Directory.CreateDirectory(nestedDirectory);
        WriteManifest(nestedDirectory, id: "win", entry: EntryFileName, minHostVersion: "1.0.0");

        var result = Discover();

        Assert.Empty(result.Skipped);
        var plan = Assert.Single(result.Plans);
        Assert.Equal("sample", plan.Id);
        Assert.DoesNotContain(result.Plans, candidate => candidate.Id == "win");
    }

    [Fact]
    public void Drop_in_directory_name_must_equal_the_manifest_id()
    {
        // Only the name mismatch is under test, so the entry file does not exist on disk.
        CreateDropInPlugin("sample", id: "other");

        var result = Discover();

        Assert.Empty(result.Plans);
        var skip = Assert.Single(result.Skipped);
        Assert.Equal("other", skip.PluginId);
        Assert.Contains("sample", skip.SourcePath, StringComparison.Ordinal);
        Assert.Contains("does not equal the manifest id", skip.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_manifest_skips_the_directory_without_reading_sibling_files()
    {
        var pluginDirectory = Path.Combine(_root, PluginDiscovery.DropInDirectoryName, "sample");
        Directory.CreateDirectory(pluginDirectory);

        // Not a managed assembly: a discovery pass that opened the sibling DLL would fail on it.
        File.WriteAllBytes(Path.Combine(pluginDirectory, EntryFileName), [0x4D, 0x5A, 0x00, 0x01]);

        var result = Discover();

        Assert.Empty(result.Plans);
        var skip = Assert.Single(result.Skipped);
        Assert.Equal(pluginDirectory, skip.SourcePath);
        Assert.Null(skip.PluginId);
        Assert.Contains(PluginManifest.FileName, skip.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2.0.0")]
    [InlineData("0.9.0")]
    public void Min_host_version_of_another_major_is_skipped(string minHostVersion)
    {
        CreateDropInPlugin("sample", minHostVersion: minHostVersion);

        var result = Discover();

        Assert.Empty(result.Plans);
        var skip = Assert.Single(result.Skipped);
        Assert.Equal(PluginId, skip.PluginId);
        Assert.Contains("major", skip.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Host_older_than_min_host_version_is_skipped()
    {
        CreateDropInPlugin("sample", minHostVersion: "1.5.0");

        var result = Discover();

        Assert.Empty(result.Plans);
        var skip = Assert.Single(result.Skipped);
        Assert.Contains("older than the required minHostVersion", skip.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1.4.17")]
    [InlineData("1.4.0")]
    public void Min_host_version_at_the_same_major_not_newer_than_the_host_is_a_plan(string minHostVersion)
    {
        var pluginDirectory = CreateDropInPlugin("sample", minHostVersion: minHostVersion);

        var result = Discover();

        Assert.Empty(result.Skipped);
        Assert.Equal(pluginDirectory, Assert.Single(result.Plans).Directory);
    }

    [Fact]
    public void ModelContextProtocol_reference_of_another_major_minor_is_skipped()
    {
        var pluginDirectory = CreateDropInPlugin("sample");
        WriteDepsJson(
            pluginDirectory,
            EntryFileName,
            modelContextProtocol: new Version(_mcpHostVersion.Major + 1, 1, 0));

        var result = Discover();

        Assert.Empty(result.Plans);
        var skip = Assert.Single(result.Skipped);
        Assert.Equal(PluginId, skip.PluginId);
        Assert.Contains("ModelContextProtocol", skip.Reason, StringComparison.Ordinal);
        Assert.Contains("Major.Minor", skip.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelContextProtocol_reference_with_the_same_major_minor_and_another_patch_is_a_plan()
    {
        var pluginDirectory = CreateDropInPlugin("sample");
        WriteDepsJson(
            pluginDirectory,
            EntryFileName,
            modelContextProtocol: new Version(_mcpHostVersion.Major, _mcpHostVersion.Minor, _mcpHostVersion.Build + 9));

        var result = Discover();

        Assert.Empty(result.Skipped);
        Assert.Equal(PluginId, Assert.Single(result.Plans).Id);
    }

    [Fact]
    public void Missing_deps_json_skips_the_plugin_without_a_recorded_reference_version()
    {
        var pluginDirectory = Path.Combine(_root, PluginDiscovery.DropInDirectoryName, "sample");
        Directory.CreateDirectory(pluginDirectory);
        WriteManifest(pluginDirectory, PluginId, EntryFileName, "1.0.0");

        var result = Discover();

        Assert.Empty(result.Plans);
        var skip = Assert.Single(result.Skipped);
        Assert.Equal(PluginId, skip.PluginId);
        Assert.Contains("no reference version", skip.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Valid JSON that no build writes. Every case damages the earliest thing the reference-version walk
    /// examines, so the guard it pins is really evaluated: the malformed section or asset belongs to
    /// <c>ModelContextProtocol</c>, the first checked assembly, and that record carries no readable
    /// version. The case then ends in the "no reference version" skip, and removing the guard it pins makes
    /// it throw out of <see cref="PluginDiscovery.Discover"/> instead.
    /// </summary>
    [Theory]
    // targets is not an object.
    [InlineData("""{"targets":[{"Some.Lib/1.0.0":{}}]}""")]
    // a target value is not an object.
    [InlineData("""{"targets":{"t":7}}""")]
    // a library value is null.
    [InlineData("""{"targets":{"t":{"Some.Lib/1.0.0":null}}}""")]
    // a library value is not an object.
    [InlineData("""{"targets":{"t":{"Some.Lib/1.0.0":"not a record"}}}""")]
    // a compile section of the first checked assembly is not an object.
    [InlineData("""{"targets":{"t":{"ModelContextProtocol":{"compile":"not a section"}}}}""")]
    // an asset of the first checked assembly is not an object.
    [InlineData("""{"targets":{"t":{"ModelContextProtocol":{"runtime":{"lib/net10.0/ModelContextProtocol.dll":"not an asset record"}}}}}""")]
    // an assemblyVersion of the first checked assembly is not a string.
    [InlineData("""{"targets":{"t":{"ModelContextProtocol":{"runtime":{"lib/net10.0/ModelContextProtocol.dll":{"assemblyVersion":5}}}}}}""")]
    // the root is null.
    [InlineData("null")]
    // the root is an array.
    [InlineData("[]")]
    public void Structurally_malformed_deps_json_skips_instead_of_throwing(string depsJson)
    {
        var pluginDirectory = Path.Combine(_root, PluginDiscovery.DropInDirectoryName, "sample");
        Directory.CreateDirectory(pluginDirectory);
        WriteManifest(pluginDirectory, PluginId, EntryFileName, "1.0.0");

        // Each of these means "no record to read a version from", never an exception that takes the host
        // down while it starts.
        File.WriteAllText(Path.Combine(pluginDirectory, "sample.deps.json"), depsJson);

        var result = Discover();

        Assert.Empty(result.Plans);
        var skip = Assert.Single(result.Skipped);
        Assert.Equal(PluginId, skip.PluginId);
        Assert.Contains("no reference version", skip.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Reference_version_falls_back_to_the_runtime_asset_and_the_library_key()
    {
        var pluginDirectory = Path.Combine(_root, PluginDiscovery.DropInDirectoryName, "sample");
        Directory.CreateDirectory(pluginDirectory);
        WriteManifest(pluginDirectory, PluginId, EntryFileName, "1.0.0");

        // Assets of this build carry no assemblyVersion property, so only the library keys name a version.
        // The keys are the ones a real build writes: Roslyn ships as Microsoft.CodeAnalysis.Common.
        File.WriteAllText(
            Path.Combine(pluginDirectory, "sample.deps.json"),
            $$"""
            {
              "targets": {
                ".NETCoreApp,Version=v10.0": {
                  "ModelContextProtocol/{{_mcpHostVersion}}": {
                    "runtime": { "lib/net10.0/ModelContextProtocol.dll": {} }
                  },
                  "RoslynMcpServer/{{_productHostVersion}}": {
                    "runtime": { "RoslynMcpServer.dll": {} }
                  },
                  "Microsoft.CodeAnalysis.Common/{{_roslynHostVersion}}": {
                    "runtime": { "lib/net10.0/Microsoft.CodeAnalysis.dll": {} }
                  }
                }
              }
            }
            """);

        var result = Discover();

        Assert.Empty(result.Skipped);
        Assert.Equal(PluginId, Assert.Single(result.Plans).Id);
    }

    [Fact]
    public void Reference_version_comes_from_the_asset_file_not_from_the_library_key_name()
    {
        var pluginDirectory = Path.Combine(_root, PluginDiscovery.DropInDirectoryName, "sample");
        Directory.CreateDirectory(pluginDirectory);
        WriteManifest(pluginDirectory, PluginId, EntryFileName, "1.0.0");

        // Shape of a real build output, taken from a host deps.json: Roslyn arrives as the library
        // Microsoft.CodeAnalysis.Common and the asset file names the assembly. The first library key does
        // carry the assembly's own name but delivers another file, so a reader that matched key names
        // would take its newer version and refuse this plugin.
        File.WriteAllText(
            Path.Combine(pluginDirectory, "sample.deps.json"),
            $$"""
            {
              "targets": {
                ".NETCoreApp,Version=v10.0": {
                  "Microsoft.CodeAnalysis/{{_roslynHostVersion.Major + 1}}.0.0": {
                    "runtime": { "lib/net10.0/decoy.dll": {} }
                  },
                  "Microsoft.CodeAnalysis.Common/{{_roslynHostVersion}}": {
                    "compile": {
                      "lib/net10.0/Microsoft.CodeAnalysis.dll": { "assemblyVersion": "{{_roslynHostVersion}}" }
                    }
                  },
                  "ModelContextProtocol/{{_mcpHostVersion}}": {
                    "runtime": {
                      "lib/net10.0/ModelContextProtocol.dll": { "assemblyVersion": "{{_mcpHostVersion}}" }
                    }
                  },
                  "RoslynMcpServer/{{_productHostVersion}}": {
                    "runtime": { "RoslynMcpServer.dll": {} }
                  }
                }
              }
            }
            """);

        var result = Discover();

        Assert.Empty(result.Skipped);
        Assert.Equal(PluginId, Assert.Single(result.Plans).Id);
    }

    [Fact]
    public void Host_product_reference_of_another_major_is_skipped()
    {
        var pluginDirectory = CreateDropInPlugin("sample");
        WriteDepsJson(pluginDirectory, EntryFileName, hostProduct: new Version(_productHostVersion.Major + 1, 0, 0));

        var result = Discover();

        Assert.Empty(result.Plans);
        var skip = Assert.Single(result.Skipped);
        Assert.Contains("RoslynMcpServer", skip.Reason, StringComparison.Ordinal);
        Assert.Contains("has major", skip.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Host_product_reference_newer_than_the_host_is_skipped()
    {
        // Same major, so only the "newer reference" branch can produce this skip.
        var pluginDirectory = CreateDropInPlugin("sample");
        WriteDepsJson(
            pluginDirectory,
            EntryFileName,
            hostProduct: new Version(_productHostVersion.Major, _productHostVersion.Minor, _productHostVersion.Build + 50));

        var result = Discover();

        Assert.Empty(result.Plans);
        var skip = Assert.Single(result.Skipped);
        Assert.Contains("RoslynMcpServer", skip.Reason, StringComparison.Ordinal);
        Assert.Contains("is newer than the loaded host assembly", skip.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("has major", skip.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Host_product_reference_older_than_the_host_is_a_plan()
    {
        var pluginDirectory = CreateDropInPlugin("sample");
        WriteDepsJson(pluginDirectory, EntryFileName, hostProduct: new Version(_productHostVersion.Major, 0, 0));

        var result = Discover();

        Assert.Empty(result.Skipped);
        Assert.Equal(PluginId, Assert.Single(result.Plans).Id);
    }

    [Fact]
    public void Roslyn_reference_of_another_major_is_skipped()
    {
        var pluginDirectory = CreateDropInPlugin("sample");
        WriteDepsJson(pluginDirectory, EntryFileName, roslyn: new Version(_roslynHostVersion.Major + 1, 0, 0));

        var result = Discover();

        Assert.Empty(result.Plans);
        var skip = Assert.Single(result.Skipped);
        Assert.Contains("Microsoft.CodeAnalysis", skip.Reason, StringComparison.Ordinal);
        Assert.Contains("has major", skip.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Roslyn_reference_with_a_newer_minor_is_skipped()
    {
        // 5.10 against the pinned 5.9 host.
        AssertRoslynReferenceIsNewerThanTheHost(new Version(_roslynHostVersion.Major, _roslynHostVersion.Minor + 1, 0));
    }

    [Fact]
    public void Roslyn_reference_with_a_newer_build_is_skipped()
    {
        // 5.9.2 against the pinned 5.9.0 host: the whole version decides, not just major and minor.
        AssertRoslynReferenceIsNewerThanTheHost(
            new Version(_roslynHostVersion.Major, _roslynHostVersion.Minor, _roslynHostVersion.Build + 1));
    }

    [Fact]
    public void Roslyn_reference_with_an_older_minor_is_a_plan()
    {
        // 5.8 against the pinned 5.9 host: an older reference of the same major is allowed, so exact
        // minor equality is not required.
        var pluginDirectory = CreateDropInPlugin("sample");
        WriteDepsJson(
            pluginDirectory,
            EntryFileName,
            roslyn: new Version(_roslynHostVersion.Major, _roslynHostVersion.Minor - 1, 0));

        var result = Discover();

        Assert.Empty(result.Skipped);
        Assert.Equal(PluginId, Assert.Single(result.Plans).Id);
    }

    [Fact]
    public void Duplicate_id_across_sources_keeps_the_first_plan()
    {
        var dropInDirectory = CreateDropInPlugin("sample");
        var explicitDirectory = CreatePlugin(Path.Combine(_root, "net10.0"), PluginId, EntryFileName, "1.0.0");

        var result = Discover(configuredPaths: [explicitDirectory]);

        var plan = Assert.Single(result.Plans);
        Assert.Equal(dropInDirectory, plan.Directory);
        Assert.False(plan.UsesShadowCopy);

        var skip = Assert.Single(result.Skipped);
        Assert.Equal(PluginId, skip.PluginId);
        Assert.Equal(explicitDirectory, skip.SourcePath);
        Assert.Contains("already accepted from an earlier source", skip.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_id_inside_the_settings_array_keeps_the_first_path()
    {
        var first = CreatePlugin(Path.Combine(_root, "first"), PluginId, EntryFileName, "1.0.0");
        var second = CreatePlugin(Path.Combine(_root, "second"), PluginId, EntryFileName, "1.0.0");

        var result = Discover(configuredPaths: [first, second]);

        Assert.Equal(first, Assert.Single(result.Plans).Directory);
        var skip = Assert.Single(result.Skipped);
        Assert.Equal(second, skip.SourcePath);
    }

    [Fact]
    public void Explicit_entry_file_with_another_name_is_skipped()
    {
        var pluginDirectory = CreatePlugin(Path.Combine(_root, "bin"), PluginId, EntryFileName, "1.0.0");
        var otherFile = Path.Combine(pluginDirectory, "other.dll");
        File.WriteAllBytes(otherFile, [0x4D, 0x5A]);

        var result = Discover(configuredPaths: [otherFile]);

        Assert.Empty(result.Plans);
        var skip = Assert.Single(result.Skipped);
        Assert.Equal(otherFile, skip.SourcePath);
        Assert.Contains("does not equal the manifest entry", skip.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_entry_file_with_the_manifest_name_needs_a_shadow_copy()
    {
        var pluginDirectory = CreatePlugin(Path.Combine(_root, "bin"), PluginId, EntryFileName, "1.0.0");
        var entryPath = Path.Combine(pluginDirectory, EntryFileName);
        File.WriteAllBytes(entryPath, [0x4D, 0x5A]);

        var result = Discover(configuredPaths: [entryPath]);

        Assert.Empty(result.Skipped);
        var plan = Assert.Single(result.Plans);
        Assert.Equal(PluginId, plan.Id);
        Assert.Equal(entryPath, plan.EntryPath);
        Assert.Equal(pluginDirectory, plan.Directory);
        Assert.True(plan.UsesShadowCopy);
    }

    [Fact]
    public void Relative_configured_path_resolves_against_the_process_working_directory()
    {
        var pluginDirectory = CreatePlugin(Path.Combine(_root, "nested", "bin"), PluginId, EntryFileName, "1.0.0");
        var relativePath = Path.Combine("nested", "bin");
        var previousDirectory = Environment.CurrentDirectory;
        try
        {
            // The fixture lives under the temp root, and on a machine whose temp directory is on another
            // drive Path.GetRelativePath would hand back an absolute path and this test would prove
            // nothing. So the process working directory is moved to the fixture's own root for the length
            // of this call, and the path fed to discovery is verified to be genuinely relative.
            Environment.CurrentDirectory = _root;
            Assert.False(Path.IsPathRooted(relativePath));

            var result = Discover(configuredPaths: [relativePath]);

            Assert.Empty(result.Skipped);
            var plan = Assert.Single(result.Plans);
            Assert.Equal(pluginDirectory, plan.Directory);
            Assert.Equal(Path.Combine(pluginDirectory, EntryFileName), plan.EntryPath);
            Assert.True(plan.UsesShadowCopy);
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
        }
    }

    [Fact]
    public void Configured_path_that_exists_neither_as_directory_nor_as_file_is_skipped()
    {
        var missingPath = Path.Combine(_root, "not-there");

        var result = Discover(configuredPaths: [missingPath]);

        Assert.Empty(result.Plans);
        var skip = Assert.Single(result.Skipped);
        Assert.Equal(missingPath, skip.SourcePath);
        Assert.Contains("neither an existing directory nor an existing file", skip.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Environment_variable_with_two_paths_yields_two_plans_that_need_a_shadow_copy()
    {
        var first = CreatePlugin(Path.Combine(_root, "first"), "first", EntryFileName, "1.0.0");
        var second = CreatePlugin(Path.Combine(_root, "second"), "second", EntryFileName, "1.0.0");

        // The variable value travels as an argument, so the process environment stays untouched.
        var result = Discover(environmentPaths: string.Join(Path.PathSeparator, first, second));

        Assert.Empty(result.Skipped);
        Assert.Equal(["first", "second"], result.Plans.Select(plan => plan.Id).ToArray());
        Assert.Equal([first, second], result.Plans.Select(plan => plan.Directory).ToArray());
        Assert.All(result.Plans, plan => Assert.True(plan.UsesShadowCopy));
    }

    [Fact]
    public void Environment_variable_empty_segments_are_not_paths()
    {
        var pluginDirectory = CreatePlugin(Path.Combine(_root, "first"), "first", EntryFileName, "1.0.0");
        var environmentPaths = Path.PathSeparator + "   " + Path.PathSeparator + pluginDirectory + Path.PathSeparator;

        var result = Discover(environmentPaths: environmentPaths);

        Assert.Empty(result.Skipped);
        Assert.Equal(pluginDirectory, Assert.Single(result.Plans).Directory);
    }

    [Fact]
    public void Plugins_key_of_the_settings_file_becomes_a_plan_that_needs_a_shadow_copy()
    {
        var pluginDirectory = CreatePlugin(Path.Combine(_root, "bin"), PluginId, EntryFileName, "1.0.0");
        File.WriteAllText(
            Path.Combine(_root, RoslynMcpFileSettings.FileName),
            $$"""
            { "plugins": [{{JsonSerializer.Serialize(pluginDirectory)}}] }
            """);

        var settings = RoslynMcpFileSettings.LoadFromDirectories(executableDirectory: null, workingDirectory: _root);
        var result = Discover(configuredPaths: settings.Plugins);

        Assert.Empty(settings.ParseFailures);
        Assert.Empty(result.Skipped);
        var plan = Assert.Single(result.Plans);
        Assert.Equal(pluginDirectory, plan.Directory);
        Assert.True(plan.UsesShadowCopy);
    }

    /// <summary>Discovers with the drop-in root of this test and no other source unless one is passed.</summary>
    private PluginDiscoveryResult Discover(
        IReadOnlyList<string>? configuredPaths = null,
        string? environmentPaths = null)
    {
        return PluginDiscovery.Discover(_root, configuredPaths ?? [], environmentPaths, _hostVersion);
    }

    private void AssertRoslynReferenceIsNewerThanTheHost(Version reference)
    {
        var pluginDirectory = CreateDropInPlugin("sample");
        WriteDepsJson(pluginDirectory, EntryFileName, roslyn: reference);

        var result = Discover();

        Assert.Empty(result.Plans);
        var skip = Assert.Single(result.Skipped);
        Assert.Contains($"reference 'Microsoft.CodeAnalysis' {reference}", skip.Reason, StringComparison.Ordinal);
        Assert.Contains("is newer than the loaded host assembly", skip.Reason, StringComparison.Ordinal);
    }

    /// <summary>Creates <c>{root}/plugins/{directoryName}</c> with a compatible manifest and deps.json.</summary>
    private string CreateDropInPlugin(string directoryName, string? id = null, string minHostVersion = "1.0.0")
    {
        var pluginDirectory = Path.Combine(_root, PluginDiscovery.DropInDirectoryName, directoryName);
        return CreatePlugin(pluginDirectory, id ?? directoryName, EntryFileName, minHostVersion);
    }

    private static string CreatePlugin(string pluginDirectory, string id, string entry, string minHostVersion)
    {
        Directory.CreateDirectory(pluginDirectory);
        WriteManifest(pluginDirectory, id, entry, minHostVersion);
        WriteDepsJson(pluginDirectory, entry);
        return pluginDirectory;
    }

    private static void WriteManifest(string pluginDirectory, string id, string entry, string minHostVersion)
    {
        File.WriteAllText(
            Path.Combine(pluginDirectory, PluginManifest.FileName),
            $$"""
            {
              "id": "{{id}}",
              "entry": "{{entry}}",
              "pluginType": "{{PluginType}}",
              "toolPrefix": "{{ToolPrefix}}",
              "minHostVersion": "{{minHostVersion}}"
            }
            """);
    }

    /// <summary>
    /// Writes the <c>deps.json</c> of a plugin in the shape a real build produces for the three gated
    /// contract assemblies: Roslyn under the library key <c>Microsoft.CodeAnalysis.Common</c> and the host
    /// product with a bare <c>RoslynMcpServer.dll</c> asset that records no version, so its version has to
    /// come from the library key. The defaults are the versions loaded in this process, so only the gate
    /// under test passes another one.
    /// </summary>
    private static void WriteDepsJson(
        string pluginDirectory,
        string entry,
        Version? modelContextProtocol = null,
        Version? hostProduct = null,
        Version? roslyn = null)
    {
        var mcpVersion = modelContextProtocol ?? _mcpHostVersion;
        var productVersion = hostProduct ?? _productHostVersion;
        var roslynVersion = roslyn ?? _roslynHostVersion;

        File.WriteAllText(
            Path.Combine(pluginDirectory, Path.GetFileNameWithoutExtension(entry) + ".deps.json"),
            $$"""
            {
              "targets": {
                ".NETCoreApp,Version=v10.0": {
                  "ModelContextProtocol/{{mcpVersion}}": {
                    "compile": {
                      "lib/net10.0/ModelContextProtocol.dll": { "assemblyVersion": "{{mcpVersion}}" }
                    }
                  },
                  "RoslynMcpServer/{{productVersion}}": {
                    "runtime": {
                      "RoslynMcpServer.dll": {}
                    }
                  },
                  "Microsoft.CodeAnalysis.Common/{{roslynVersion}}": {
                    "compile": {
                      "lib/net10.0/Microsoft.CodeAnalysis.dll": { "assemblyVersion": "{{roslynVersion}}" }
                    }
                  }
                }
              }
            }
            """);
    }

    /// <summary>
    /// Version of a host assembly loaded in this test process, read the same way discovery reads it. The
    /// expected reference versions follow the pinned packages instead of repeating them.
    /// </summary>
    private static Version LoadedHostVersion(string simpleName)
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
                           .FirstOrDefault(candidate => candidate.GetName().Name == simpleName)
                       ?? Assembly.Load(new AssemblyName(simpleName));

        return assembly.GetName().Version
               ?? throw new InvalidOperationException($"Host assembly '{simpleName}' reports no version.");
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "PluginDiscoveryTests-" + Guid.NewGuid().ToString("N"));
    private static readonly Version _hostVersion = new(1, 4, 17);
    private static readonly Version _mcpHostVersion = LoadedHostVersion("ModelContextProtocol");
    private static readonly Version _productHostVersion = LoadedHostVersion("RoslynMcpServer");
    private static readonly Version _roslynHostVersion = LoadedHostVersion("Microsoft.CodeAnalysis");
}
