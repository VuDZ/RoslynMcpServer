using RoslynMcpServer.Plugins;
using Xunit;

namespace RoslynMcpServer.Tests.Plugins;

/// <summary>
/// Shadow-copy seam: a plan that must not be loaded where it lies gets a directory of its own under the
/// temporary path, and the plan is then repointed at the same-named entry file inside that copy while its
/// directory property keeps the original location. The fixture entry file holds arbitrary bytes: it is not
/// an assembly and is never loaded.
/// </summary>
[Collection(nameof(PluginShadowCopyTests))]
public sealed class PluginShadowCopyTests : IDisposable
{
    private const string PluginType = "Sample.SamplePlugin";
    private const string ToolPrefix = "sample_";
    private const string EntryFileName = "sample.dll";
    private const string SecondEntryFileName = "second.dll";
    private const string ForeignAssemblyFileName = "Their.Heuristic.dll";
    private const string ForeignSymbolFileName = "Their.Heuristic.pdb";

    public PluginShadowCopyTests()
    {
        Directory.CreateDirectory(_root);
        _copyRoot = Path.Combine(_root, "copies");
        Directory.CreateDirectory(_copyRoot);
    }

    public void Dispose()
    {
        try
        {
            foreach (var copyDirectory in _createdCopyDirectories)
            {
                if (Directory.Exists(copyDirectory))
                {
                    Directory.Delete(copyDirectory, recursive: true);
                }
            }

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
    public void Plan_with_shadow_copy_points_into_a_new_directory_holding_the_whole_tree()
    {
        var pluginDirectory = CreatePluginDirectory("sample", EntryFileName);
        var plan = CreatePlan("sample", pluginDirectory, Path.Combine(pluginDirectory, EntryFileName));

        // No root is passed, so this call also pins the default root of the host.
        var result = CopyToDefaultRoot([plan]);

        Assert.Empty(result.Skipped);
        var copiedPlan = Assert.Single(result.Plans);
        Assert.Equal(pluginDirectory, copiedPlan.Directory);

        var copyDirectory = CopyDirectoryOf(copiedPlan);
        Assert.NotEqual(pluginDirectory, copyDirectory);
        Assert.Equal(EntryFileName, Path.GetFileName(copiedPlan.EntryPath));

        // The copy lives under the temporary path of this run, never inside the plugin directory and never
        // next to the host executable.
        Assert.StartsWith(Path.GetTempPath(), copiedPlan.EntryPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            Path.Combine(Path.GetTempPath(), PluginShadowCopy.TempRootDirectoryName, PluginShadowCopy.TempPluginsDirectoryName),
            Path.GetDirectoryName(copyDirectory));

        // The original directory is still there with its own bytes: the copy is an addition, not a move.
        Assert.True(Directory.Exists(pluginDirectory), "the original plugin directory was removed");
        Assert.Equal(_entryBytes, File.ReadAllBytes(Path.Combine(pluginDirectory, EntryFileName)));

        Assert.True(File.Exists(Path.Combine(copyDirectory, PluginManifest.FileName)), "plugin.json was not copied");
        Assert.Equal(_entryBytes, File.ReadAllBytes(Path.Combine(copyDirectory, EntryFileName)));
        Assert.Equal(_nativeBytes, File.ReadAllBytes(NativeFilePath(copyDirectory)));
        Assert.True(File.Exists(Path.Combine(copyDirectory, DepsFileName(EntryFileName))), "the deps.json of the plugin was not copied");
    }

    [Fact]
    public void Host_assemblies_and_their_pdb_files_are_left_out_of_the_copy()
    {
        var pluginDirectory = CreatePluginDirectory("sample", EntryFileName);
        var plan = CreatePlan("sample", pluginDirectory, Path.Combine(pluginDirectory, EntryFileName));

        var result = Copy([plan]);

        var copiedPlan = Assert.Single(result.Plans);
        var copiedNames = Directory
            .EnumerateFiles(CopyDirectoryOf(copiedPlan), "*", SearchOption.AllDirectories)
            .Select(path => Path.GetFileName(path))
            .ToArray();

        foreach (var hostAssemblyFileName in _hostAssemblyFileNames)
        {
            Assert.True(
                File.Exists(Path.Combine(pluginDirectory, hostAssemblyFileName)),
                $"{hostAssemblyFileName} was not planted in the fixture");

            Assert.DoesNotContain(hostAssemblyFileName, copiedNames);
            Assert.DoesNotContain(Path.ChangeExtension(hostAssemblyFileName, ".pdb"), copiedNames);
        }

        Assert.Contains(ForeignAssemblyFileName, copiedNames);

        // A symbol file is skipped by the assembly name behind it, not by its extension, so the symbol file
        // of the plugin's own assembly travels with it.
        Assert.Contains(ForeignSymbolFileName, copiedNames);
    }

    [Fact]
    public void Original_entry_file_can_be_overwritten_after_the_copy()
    {
        var pluginDirectory = CreatePluginDirectory("sample", EntryFileName);
        var entryPath = Path.Combine(pluginDirectory, EntryFileName);
        var plan = CreatePlan("sample", pluginDirectory, entryPath);

        var result = Copy([plan]);

        var copiedPlan = Assert.Single(result.Plans);
        Assert.NotEqual(entryPath, copiedPlan.EntryPath);

        // The next build of a plugin replaces its output while the host keeps running, so writing the
        // original file has to succeed and must not reach the copy.
        byte[] replacementBytes = [0x4D, 0x5A, 0x0A, 0x0B, 0x0C];
        File.WriteAllBytes(entryPath, replacementBytes);

        // This proves that the copy is a file of its own. It is not evidence that a loaded
        // AssemblyLoadContext holds the copy path: that binding is a later step.
        Assert.Equal(replacementBytes, File.ReadAllBytes(entryPath));
        Assert.Equal(_entryBytes, File.ReadAllBytes(copiedPlan.EntryPath));
    }

    [Fact]
    public void Two_plans_get_two_different_copy_directories()
    {
        var firstDirectory = CreatePluginDirectory("first", EntryFileName);
        var secondDirectory = CreatePluginDirectory("second", SecondEntryFileName);
        var plans = new[]
        {
            CreatePlan("first", firstDirectory, Path.Combine(firstDirectory, EntryFileName)),
            CreatePlan("second", secondDirectory, Path.Combine(secondDirectory, SecondEntryFileName)),
        };

        var result = Copy(plans);

        Assert.Empty(result.Skipped);
        Assert.Equal(["first", "second"], result.Plans.Select(plan => plan.Id).ToArray());
        Assert.Equal([firstDirectory, secondDirectory], result.Plans.Select(plan => plan.Directory).ToArray());

        // Each plan keeps its own entry file name inside a copy of its own under the root of this test.
        Assert.Equal(EntryFileName, Path.GetFileName(result.Plans[0].EntryPath));
        Assert.Equal(SecondEntryFileName, Path.GetFileName(result.Plans[1].EntryPath));
        Assert.NotEqual(CopyDirectoryOf(result.Plans[0]), CopyDirectoryOf(result.Plans[1]));
        Assert.All(result.Plans, plan => Assert.Equal(_copyRoot, Path.GetDirectoryName(CopyDirectoryOf(plan))));
        Assert.All(result.Plans, plan => Assert.True(File.Exists(plan.EntryPath)));
    }

    [Fact]
    public void Plan_without_shadow_copy_keeps_its_entry_path_and_creates_no_directory()
    {
        var pluginDirectory = CreatePluginDirectory("sample", EntryFileName);
        var entryPath = Path.Combine(pluginDirectory, EntryFileName);
        var plan = CreatePlan("sample", pluginDirectory, entryPath, usesShadowCopy: false);

        // No root is passed on purpose: a plan that stays where it lies must not gain a copy under the
        // default root either. Every directory this code creates is a child of the default copy root below
        // the temporary path, so an unchanged set of its children is the proof. The set is compared instead
        // of emptiness because a shared temporary path can hold copies of other runs.
        var before = DefaultCopyRootChildren();
        var result = PluginShadowCopy.Copy([plan]);
        var after = DefaultCopyRootChildren();

        Assert.Empty(result.Skipped);
        var returnedPlan = Assert.Single(result.Plans);
        Assert.Equal(plan, returnedPlan);
        Assert.False(returnedPlan.UsesShadowCopy);
        Assert.Equal(entryPath, returnedPlan.EntryPath);
        Assert.Equal(before, after);
        Assert.True(File.Exists(entryPath));
    }

    [Fact]
    public void Missing_entry_file_of_one_plan_does_not_cancel_the_copy_of_the_next()
    {
        var brokenDirectory = CreatePluginDirectory("sample", EntryFileName);
        var healthyDirectory = CreatePluginDirectory("second", SecondEntryFileName);
        var healthyEntryPath = Path.Combine(healthyDirectory, SecondEntryFileName);

        // The plan is built from a name discovery accepted, not from a file that still exists: nothing is
        // opened while planning. The healthy plan comes second, so a pass that gives up on the first
        // failure cannot produce the copy of the second one.
        var plans = new[]
        {
            CreatePlan("sample", brokenDirectory, Path.Combine(brokenDirectory, "gone.dll")),
            CreatePlan("second", healthyDirectory, healthyEntryPath),
        };

        var result = Copy(plans);

        var copiedPlan = Assert.Single(result.Plans);
        Assert.Equal("second", copiedPlan.Id);
        Assert.Equal(healthyDirectory, copiedPlan.Directory);
        Assert.NotEqual(healthyEntryPath, copiedPlan.EntryPath);
        Assert.Equal(_entryBytes, File.ReadAllBytes(copiedPlan.EntryPath));
        Assert.Equal(_copyRoot, Path.GetDirectoryName(CopyDirectoryOf(copiedPlan)));
        Assert.DoesNotContain(result.Plans, plan => plan.Id == "sample");

        var skip = Assert.Single(result.Skipped);
        Assert.Equal("sample", skip.PluginId);
        Assert.Equal(brokenDirectory, skip.SourcePath);
        Assert.Contains(brokenDirectory, skip.Reason, StringComparison.Ordinal);
        Assert.Contains("has no entry file", skip.Reason, StringComparison.Ordinal);
    }

    /// <summary>Copies with the copy root of this test and remembers the created directories for cleanup.</summary>
    private PluginShadowCopyResult Copy(IReadOnlyList<PluginLoadPlan> plans)
    {
        var result = PluginShadowCopy.Copy(plans, _copyRoot);
        RememberCopyDirectories(result);
        return result;
    }

    /// <summary>
    /// Copies with the default root under <see cref="Path.GetTempPath"/>, which is what the host does and
    /// what a caller can only observe through the result, and remembers the created directories.
    /// </summary>
    private PluginShadowCopyResult CopyToDefaultRoot(IReadOnlyList<PluginLoadPlan> plans)
    {
        var result = PluginShadowCopy.Copy(plans);
        RememberCopyDirectories(result);
        return result;
    }

    private void RememberCopyDirectories(PluginShadowCopyResult result)
    {
        foreach (var plan in result.Plans)
        {
            if (plan.UsesShadowCopy)
            {
                _createdCopyDirectories.Add(CopyDirectoryOf(plan));
            }
        }
    }

    /// <summary>Directory the plan was repointed at, or an empty string when its path names no directory.</summary>
    private static string CopyDirectoryOf(PluginLoadPlan plan) => Path.GetDirectoryName(plan.EntryPath) ?? string.Empty;

    /// <summary>
    /// Creates <c>{root}/{directoryName}</c> as a plugin directory: a hand-written manifest, an entry file of
    /// arbitrary bytes, a native file under <c>runtimes/win</c>, the assemblies the host already provides
    /// with their symbol files, and one foreign assembly of the plugin with its symbol file.
    /// </summary>
    private string CreatePluginDirectory(string directoryName, string entryFileName)
    {
        var pluginDirectory = Path.Combine(_root, directoryName);
        Directory.CreateDirectory(pluginDirectory);

        var nativeFilePath = NativeFilePath(pluginDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(nativeFilePath) ?? pluginDirectory);

        File.WriteAllText(Path.Combine(pluginDirectory, PluginManifest.FileName), Manifest(directoryName, entryFileName));
        File.WriteAllBytes(Path.Combine(pluginDirectory, entryFileName), _entryBytes);
        File.WriteAllText(Path.Combine(pluginDirectory, DepsFileName(entryFileName)), """{"targets":{}}""");
        File.WriteAllBytes(nativeFilePath, _nativeBytes);

        foreach (var hostAssemblyFileName in _hostAssemblyFileNames)
        {
            File.WriteAllBytes(Path.Combine(pluginDirectory, hostAssemblyFileName), _assemblyBytes);
            File.WriteAllBytes(Path.Combine(pluginDirectory, Path.ChangeExtension(hostAssemblyFileName, ".pdb")), _assemblyBytes);
        }

        File.WriteAllBytes(Path.Combine(pluginDirectory, ForeignAssemblyFileName), _assemblyBytes);
        File.WriteAllBytes(Path.Combine(pluginDirectory, ForeignSymbolFileName), _assemblyBytes);

        return pluginDirectory;
    }

    private static string Manifest(string pluginId, string entryFileName) =>
        $$"""
        {
          "id": "{{pluginId}}",
          "entry": "{{entryFileName}}",
          "pluginType": "{{PluginType}}",
          "toolPrefix": "{{ToolPrefix}}",
          "minHostVersion": "1.0.0"
        }
        """;

    /// <summary>Builds a plan directly: the shadow-copy pass reads no manifest and no deps.json.</summary>
    private static PluginLoadPlan CreatePlan(
        string pluginId,
        string pluginDirectory,
        string entryPath,
        bool usesShadowCopy = true) =>
        new()
        {
            Id = pluginId,
            Directory = pluginDirectory,
            EntryPath = entryPath,
            PluginType = PluginType,
            ToolPrefix = ToolPrefix,
            UsesShadowCopy = usesShadowCopy,
        };

    private static string NativeFilePath(string pluginDirectory) =>
        Path.Combine(pluginDirectory, "runtimes", "win", "native.bin");

    private static string DepsFileName(string entryFileName) =>
        Path.GetFileNameWithoutExtension(entryFileName) + ".deps.json";

    /// <summary>
    /// Immediate subdirectory names of the default copy root under <see cref="Path.GetTempPath"/>. Every
    /// directory the copy pass creates is a child of that root, so an unchanged set proves that a call
    /// created none; the root is shared with other runs and is not expected to be empty.
    /// </summary>
    private static string[] DefaultCopyRootChildren()
    {
        var defaultCopyRoot = Path.Combine(
            Path.GetTempPath(),
            PluginShadowCopy.TempRootDirectoryName,
            PluginShadowCopy.TempPluginsDirectoryName);

        return ImmediateSubdirectoryNames(defaultCopyRoot);
    }

    /// <summary>Immediate subdirectory names of one directory, sorted, or nothing when it does not exist.</summary>
    private static string[] ImmediateSubdirectoryNames(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return [.. Directory.EnumerateDirectories(directory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "PluginShadowCopyTests-" + Guid.NewGuid().ToString("N"));
    private readonly string _copyRoot;
    private readonly List<string> _createdCopyDirectories = [];

    private static readonly string[] _hostAssemblyFileNames =
    [
        "RoslynMcpServer.dll",
        "Microsoft.CodeAnalysis.CSharp.dll",
        "ModelContextProtocol.Core.dll",
        "Microsoft.Extensions.Logging.dll",
    ];

    private static readonly byte[] _entryBytes = [0x4D, 0x5A, 0x10, 0x20, 0x30, 0x40];
    private static readonly byte[] _nativeBytes = [0x7F, 0x45, 0x4C, 0x46, 0x01, 0x02];
    private static readonly byte[] _assemblyBytes = [0x4D, 0x5A, 0x90, 0x00];
}
