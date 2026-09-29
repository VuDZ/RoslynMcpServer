using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using RoslynMcpServer.Hosting;
using RoslynMcpServer.Plugins;
using Xunit;

namespace RoslynMcpServer.Tests.Plugins;

/// <summary>
/// Shape of the sample plugin colleagues copy: its build output carries the entry assembly, the private
/// managed dependency, the <c>deps.json</c> and the manifest that discovery turns into a load plan, while
/// the host assembly and the host's publish closure stay free of the sample.
/// </summary>
/// <remarks>
/// The output is located from the layout of this test run rather than from a build started here: the test
/// project references the sample project, so the enclosing build produced both in one configuration. The
/// host assembly read below is the one that same build wrote, which is what a sample source accidentally
/// compiled into the host project would change. Nothing in this class loads the sample assembly — the
/// entry file is only read as bytes.
/// </remarks>
public sealed class SamplePluginShapeTests
{
    private const string SampleProjectName = "RoslynMcpPlugin";
    private const string SampleEntryFileName = SampleProjectName + ".dll";
    private const string SampleDepsFileName = SampleProjectName + ".deps.json";
    private const string SamplePluginTypeName = "RoslynMcpPlugin.SamplePlugin";
    private const string SamplePluginId = "sample";
    private const string SampleToolPrefix = "sample_";
    private const string SampleToolName = "sample_loaded_workspace";
    private const string HostAssemblyFileName = "RoslynMcpServer.dll";
    private const string HostDepsFileName = "RoslynMcpServer.deps.json";
    private const string ModelContextProtocolFileName = "ModelContextProtocol.dll";
    private const string PrivateDependencyFileName = "Newtonsoft.Json.dll";
    private const string PrivateDependencyAssemblyName = "Newtonsoft.Json";
    private const string SolutionFileName = "RoslynMcpServer.sln";
    private const string TargetFrameworkDirectoryName = "net10.0";
    private const string PublishDirectoryName = "publish";

    /// <summary>
    /// Runtime identifiers of the host project. A publish of it is made per identifier, so each of these
    /// directories is a published host that has to be free of the private dependency of the sample.
    /// </summary>
    private static readonly string[] HostRuntimeIdentifiers =
    [
        "win-x64",
        "win-arm64",
        "osx-arm64",
        "linux-x64",
        "linux-arm64",
        "linux-musl-x64",
    ];

    [Fact]
    public void Sample_output_holds_the_entry_assembly_its_deps_json_and_the_manifest()
    {
        var outputDirectory = ExistingSampleOutputDirectory();

        AssertFileExists(outputDirectory, SampleEntryFileName);
        AssertFileExists(outputDirectory, SampleDepsFileName);
        AssertFileExists(outputDirectory, PluginManifest.FileName);
    }

    [Fact]
    public void Sample_output_holds_the_private_dependency_and_no_host_contract_assembly()
    {
        var outputDirectory = ExistingSampleOutputDirectory();

        // The plugin carries its private managed dependency itself, because a host shares only the
        // contract it defines with a plugin. The fact under test is the copy in the plugin directory.
        AssertFileExists(outputDirectory, PrivateDependencyFileName);

        // What must never travel with the plugin is the contract itself: those assemblies have to be the
        // ones the running host already loaded.
        AssertFileAbsent(outputDirectory, HostAssemblyFileName);
        AssertFileAbsent(outputDirectory, ModelContextProtocolFileName);

        // The test project builds the sample without taking its output assembly, so the plugin does not
        // join the test process either.
        AssertFileAbsent(AppContext.BaseDirectory, SampleEntryFileName);
    }

    /// <summary>
    /// No published host of the tested configuration carries the private managed dependency of the sample,
    /// neither as a file in the publish directory nor as an asset of a library in the deps.json of the
    /// published host.
    /// </summary>
    /// <remarks>
    /// The sample calls that dependency, so a host that published it too would serve the call from its own
    /// closure and the sample would stop showing a plugin that resolves a private package of its own. A
    /// publish may or may not have been made on the machine running the tests: with none, this pass has no
    /// artifact to read and asserts nothing, and the check happens on every identifier that was published
    /// once one exists — a later publish under another identifier cannot be masked by an earlier one.
    /// </remarks>
    [Fact]
    public void No_published_host_carries_the_private_dependency_of_the_sample()
    {
        var publishDirectories = HostPublishDirectories();
        if (publishDirectories.Count == 0)
        {
            return;
        }

        // Every located directory is a published host this pass reads, so the file it reads has to be
        // there: a path that stopped matching the artifact would otherwise turn the checks below into a
        // comparison against nothing.
        foreach (var publishDirectory in publishDirectories)
        {
            AssertFileExists(publishDirectory, HostDepsFileName);
            AssertFileAbsent(publishDirectory, PrivateDependencyFileName);
        }

        // The deps.json of a published host is the list of libraries that publish resolves, so no library
        // in any of them may carry the private dependency as a compile or runtime asset. The library key is
        // not read: the same file can name the package as a dependency of another library, which is not a
        // copy of it.
        Assert.DoesNotContain(
            publishDirectories.SelectMany(PublishLibraries),
            library => CarriesAssemblyAsset(library, PrivateDependencyFileName));
    }

    [Fact]
    public void Sample_entry_assembly_references_the_private_dependency()
    {
        var entryPath = Path.Combine(ExistingSampleOutputDirectory(), SampleEntryFileName);

        var references = AssemblyReferences(entryPath);

        // A package reference alone does not make the assembly a dependency the plugin needs at run time.
        // The reference recorded by the compiler is what ties the DLL in the output to a real call.
        Assert.Contains(PrivateDependencyAssemblyName, references);
    }

    [Fact]
    public void Sample_manifest_of_the_output_is_read_as_the_sample_plan()
    {
        var outputDirectory = ExistingSampleOutputDirectory();
        var manifestPath = Path.Combine(outputDirectory, PluginManifest.FileName);
        Assert.True(
            PluginManifest.TryRead(manifestPath, out var manifest, out var error),
            $"'{manifestPath}' could not be read: {error}");

        Assert.Equal(SamplePluginId, manifest.Id);
        Assert.Equal(SampleEntryFileName, manifest.Entry);
        Assert.Equal(SamplePluginTypeName, manifest.PluginType);
        Assert.Equal(SampleToolPrefix, manifest.ToolPrefix);

        // The delivered manifest states the assembly version the plugin was compiled against, so the value
        // of the source file is replaced by the build and never reaches the output.
        var hostVersion = LoadedHostAssemblyVersion();
        Assert.Equal(hostVersion, manifest.MinHostVersion);

        var sourceManifestPath = Path.Combine(
            RepositoryRoot(),
            "samples",
            SampleProjectName,
            PluginManifest.FileName);
        Assert.True(
            PluginManifest.TryRead(sourceManifestPath, out var sourceManifest, out var sourceError),
            $"'{sourceManifestPath}' could not be read: {sourceError}");

        Assert.Equal(new Version(1, 0), sourceManifest.MinHostVersion);
        Assert.NotEqual(sourceManifest.MinHostVersion, manifest.MinHostVersion);

        // The environment variable is deliberately not read: a value of the machine running the tests must
        // not add plans to this pass.
        var discovery = PluginDiscovery.Discover(outputDirectory, [outputDirectory], environmentPaths: null, hostVersion);

        Assert.Empty(discovery.Skipped);
        var plan = Assert.Single(discovery.Plans);
        Assert.Equal(SamplePluginId, plan.Id);
        Assert.Equal(SampleToolPrefix, plan.ToolPrefix);
        Assert.Equal(SamplePluginTypeName, plan.PluginType);
        Assert.True(File.Exists(plan.EntryPath), $"the plan entry '{plan.EntryPath}' does not exist");
    }

    [Fact]
    public void Sample_type_and_tool_name_stay_out_of_the_host_assembly()
    {
        // Reading the catalog builds and validates it, so a sample tool that had reached the host assembly
        // without a catalog row would fail this pass instead of being listed.
        var catalog = McpToolCatalog.All;
        var hostAssembly = typeof(McpToolCatalog).Assembly;

        // The assembly under test is the one the build of this test run wrote, so a rebuild that compiled a
        // sample source into the host project leaves its type here instead of passing unnoticed.
        Assert.DoesNotContain(catalog, descriptor => descriptor.Name == SampleToolName);
        Assert.DoesNotContain(McpToolCatalog.HostTypes, type => type.Namespace == SampleProjectName);
        Assert.Null(hostAssembly.GetType(SamplePluginTypeName, throwOnError: false));
        Assert.DoesNotContain(hostAssembly.GetTypes(), type => type.Namespace == SampleProjectName);
    }

    /// <summary>
    /// Build output of the sample project for the configuration of this test run. The project reference
    /// builds the sample in that same configuration, and the default output layout of a class library adds
    /// no other directory between the configuration and the target framework.
    /// </summary>
    private static string ExistingSampleOutputDirectory()
    {
        var configuration = BuildConfigurationName();

        var outputDirectory = Path.Combine(
            RepositoryRoot(),
            "samples",
            SampleProjectName,
            "bin",
            configuration,
            TargetFrameworkDirectoryName);

        Assert.True(
            Directory.Exists(outputDirectory),
            $"the sample plugin output '{outputDirectory}' does not exist");

        return outputDirectory;
    }

    /// <summary>
    /// Configuration of the build this test run came from: the directory named directly under the
    /// <c>bin</c> of the repository.
    /// </summary>
    /// <remarks>
    /// The directory above the test output is not the configuration: the host project declares runtime
    /// identifiers, so a test output can be laid out as <c>bin/Release/net10.0/win-x64</c> and the
    /// directory above it is then the target framework. Reading the first name after <c>bin</c> returns
    /// the configuration for both layouts, which is what the sample output and the published host of this
    /// run are addressed with.
    /// </remarks>
    private static string BuildConfigurationName()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (directory.Parent?.Name.Equals("bin", StringComparison.OrdinalIgnoreCase) == true)
            {
                return directory.Name;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"no 'bin' directory was found above '{AppContext.BaseDirectory}'");
    }

    /// <summary>
    /// Publish directories of the host for the configuration of this test run, in the order of
    /// <see cref="HostRuntimeIdentifiers"/>, or an empty list when nothing of that configuration was
    /// published.
    /// </summary>
    /// <remarks>
    /// The published host is located by its documented path and not by searching for a
    /// <c>RoslynMcpServer.deps.json</c> anywhere under <c>bin</c>: a plain build output is not a publish,
    /// and a search by depth reaches that earlier artifact first. The self-contained publish is made per
    /// runtime identifier, so every identifier the host declares is looked up and all the directories that
    /// exist are returned.
    /// </remarks>
    private static IReadOnlyList<string> HostPublishDirectories()
    {
        var frameworkDirectory = Path.Combine(
            RepositoryRoot(),
            "bin",
            BuildConfigurationName(),
            TargetFrameworkDirectoryName);

        return
        [
            .. HostRuntimeIdentifiers
                .Select(runtimeIdentifier => Path.Combine(frameworkDirectory, runtimeIdentifier, PublishDirectoryName))
                .Where(Directory.Exists),
        ];
    }

    /// <summary>
    /// Library records of the <c>deps.json</c> a publish directory delivers, across every target it lists.
    /// </summary>
    private static IEnumerable<JsonElement> PublishLibraries(string publishDirectory)
    {
        using var hostDeps = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(publishDirectory, HostDepsFileName)));

        return
        [
            .. hostDeps.RootElement
                .GetProperty("targets")
                .EnumerateObject()
                .SelectMany(target => target.Value.EnumerateObject())
                .Select(library => library.Value.Clone()),
        ];
    }

    /// <summary>
    /// True when one deps.json library record lists <paramref name="assetFileName"/> under the compile or
    /// runtime assets, so the library is a copy of that assembly rather than a mention of its name.
    /// </summary>
    private static bool CarriesAssemblyAsset(JsonElement library, string assetFileName)
    {
        if (library.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // A deps.json is third-party input, so a record of another shape is not an asset of this assembly
        // rather than a failed test.
        return HasAsset(library, "compile", assetFileName) || HasAsset(library, "runtime", assetFileName);
    }

    /// <summary>True when one asset section of a deps.json library record lists that assembly file.</summary>
    private static bool HasAsset(JsonElement library, string sectionName, string assetFileName)
    {
        if (!library.TryGetProperty(sectionName, out var section) || section.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // Asset keys are paths such as lib/net10.0/Newtonsoft.Json.dll, so the file name decides.
        return section.EnumerateObject().Any(asset =>
            Path.GetFileName(asset.Name).Equals(assetFileName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Repository root: the nearest directory above the test output holding the solution file. The sample
    /// project is a sibling of this test project, so it is only reachable through that root.
    /// </summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"'{SolutionFileName}' was not found above '{AppContext.BaseDirectory}'");
    }

    /// <summary>
    /// Assembly version of the host assembly this test process loaded. The sample compiles against the same
    /// project, so this is the version its manifest has to state.
    /// </summary>
    private static Version LoadedHostAssemblyVersion() =>
        typeof(McpToolCatalog).Assembly.GetName().Version
        ?? throw new InvalidOperationException($"the host assembly '{HostAssemblyFileName}' reports no version");

    /// <summary>
    /// Simple names of the assemblies a file references, read from its metadata. The file is never loaded
    /// into this process.
    /// </summary>
    private static string[] AssemblyReferences(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        MetadataReader metadataReader = peReader.GetMetadataReader();

        return
        [
            .. metadataReader.AssemblyReferences.Select(handle =>
                metadataReader.GetString(metadataReader.GetAssemblyReference(handle).Name)),
        ];
    }

    /// <summary>Asserts that a directory holds a file, naming the file that is missing.</summary>
    private static void AssertFileExists(string directory, string fileName) =>
        Assert.True(
            File.Exists(Path.Combine(directory, fileName)),
            $"'{fileName}' is not in '{directory}'");

    /// <summary>Asserts that a directory does not hold a file, naming the file that must not be there.</summary>
    private static void AssertFileAbsent(string directory, string fileName) =>
        Assert.False(
            File.Exists(Path.Combine(directory, fileName)),
            $"'{fileName}' must not be in '{directory}'");
}
