using System.Diagnostics.CodeAnalysis;

namespace RoslynMcpServer.Plugins;

/// <summary>
/// Copies the directory of every plan that must not be loaded where it lies into a directory of its own
/// under the temporary path, so the build output of a plugin can be replaced while the host keeps running.
/// </summary>
/// <remarks>
/// <para>
/// A plan without the shadow-copy flag is returned unchanged, and no directory is created for it. A plan
/// that is copied keeps its original directory in <see cref="PluginLoadPlan.Directory"/> and gets an entry
/// path inside the copy, so the caller holds both locations: the directory the plugin was found in and the
/// file the host will load.
/// </para>
/// <para>
/// The files the host already provides are left out of the copy, because the plugin has to bind to the host
/// copies of those types. Everything else in the tree travels with the plugin. Nothing here opens an
/// assembly, resolves a native library, or removes an old copy.
/// </para>
/// </remarks>
public static class PluginShadowCopy
{
    /// <summary>Directory under <see cref="Path.GetTempPath"/> holding the plugin copies of this host.</summary>
    public const string TempRootDirectoryName = "RoslynMcpServer";

    /// <summary>Directory under <see cref="TempRootDirectoryName"/> whose subdirectories are the copies.</summary>
    public const string TempPluginsDirectoryName = "plugins";

    private const string AssemblyFileExtension = ".dll";
    private const string SymbolFileExtension = ".pdb";
    private const string HostAssemblyName = "RoslynMcpServer";
    private const string McpAssemblyName = "ModelContextProtocol";
    private const string RoslynAssemblyPrefix = "Microsoft.CodeAnalysis";
    private const string ExtensionsAssemblyPrefix = "Microsoft.Extensions.";

    /// <summary>
    /// Copies every plan that needs a shadow copy and returns the plans with their entry path inside the
    /// copies.
    /// </summary>
    /// <param name="plans">Plans of one pass, in order.</param>
    /// <param name="copyRootDirectory">
    /// Root of the per-run copies. <see langword="null"/> or whitespace selects
    /// <c>{Path.GetTempPath()}/{TempRootDirectoryName}/{TempPluginsDirectoryName}</c>, the layout the host
    /// uses; a caller that passes a root, such as a test that watches the copies, gets that root instead.
    /// </param>
    /// <returns>
    /// The plans in input order, each with its entry path inside its own copy when it needed one, and one
    /// skip per plan whose copy failed.
    /// </returns>
    /// <remarks>
    /// A file-system problem of one plan becomes a skip of that plan and never an exception out of this
    /// method, so the remaining plans are still copied. Nothing is deleted here, and a failed copy may leave
    /// a partial directory behind.
    /// </remarks>
    public static PluginShadowCopyResult Copy(IReadOnlyList<PluginLoadPlan> plans, string? copyRootDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(plans);

        var copyRoot = ResolveCopyRoot(copyRootDirectory);
        var copied = new List<PluginLoadPlan>(plans.Count);
        var skipped = new List<PluginDiscoverySkip>();

        foreach (var plan in plans)
        {
            if (!plan.UsesShadowCopy)
            {
                copied.Add(plan);
                continue;
            }

            if (TryCopyPlan(plan, copyRoot, out var copiedPlan, out var reason))
            {
                copied.Add(copiedPlan);
            }
            else
            {
                skipped.Add(Skip(plan, reason));
            }
        }

        return new PluginShadowCopyResult
        {
            Plans = [.. copied],
            Skipped = [.. skipped],
        };
    }

    /// <summary>Root of the per-run copies: the root of the caller, or the default under the temporary path.</summary>
    private static string ResolveCopyRoot(string? copyRootDirectory) =>
        string.IsNullOrWhiteSpace(copyRootDirectory)
            ? Path.Combine(Path.GetTempPath(), TempRootDirectoryName, TempPluginsDirectoryName)
            : copyRootDirectory;

    /// <summary>
    /// Copies the plugin directory of one plan into a fresh directory under <paramref name="copyRoot"/> and
    /// repoints the entry path of that plan at the copy of the same file name.
    /// </summary>
    /// <param name="plan">Plan whose directory is copied; the plan itself keeps its original directory.</param>
    /// <param name="copyRoot">Root of the copies; the directory of this plan is a new child of it.</param>
    /// <param name="copiedPlan">Plan to load, with its entry path inside the copy.</param>
    /// <param name="reason">Concrete cause of the failure; always set when this method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the copy holds the entry file and the plan is loadable from it.</returns>
    private static bool TryCopyPlan(
        PluginLoadPlan plan,
        string copyRoot,
        [NotNullWhen(true)] out PluginLoadPlan? copiedPlan,
        [NotNullWhen(false)] out string? reason)
    {
        copiedPlan = null;

        string copyDirectory;
        try
        {
            copyDirectory = Path.Combine(copyRoot, Guid.NewGuid().ToString("N"));
            CopyTree(plan.Directory, copyDirectory);
        }
        catch (Exception ex) when (IsFileSystemFailure(ex))
        {
            reason = $"plugin directory '{plan.Directory}' could not be copied under '{copyRoot}': {ex.Message}";
            return false;
        }

        var entryFileName = Path.GetFileName(plan.EntryPath);
        var copiedEntryPath = Path.Combine(copyDirectory, entryFileName);
        if (!File.Exists(copiedEntryPath))
        {
            // The entry file is what the host opens, so a copy without it is useless even though the rest of
            // the tree was copied: the plan is skipped instead of pointing at a file that is not there.
            reason = $"copy '{copyDirectory}' of plugin directory '{plan.Directory}' has no entry file '{entryFileName}'";
            return false;
        }

        copiedPlan = plan with { EntryPath = copiedEntryPath };
        reason = null;
        return true;
    }

    /// <summary>One failed plan as a skip: the source is the original directory, not the partial copy.</summary>
    private static PluginDiscoverySkip Skip(PluginLoadPlan plan, string reason) =>
        new()
        {
            SourcePath = plan.Directory,
            PluginId = plan.Id,
            Reason = reason,
        };

    /// <summary>
    /// Copies the tree of <paramref name="sourceDirectory"/> into <paramref name="destinationDirectory"/>,
    /// leaving out the files the host already provides.
    /// </summary>
    /// <param name="sourceDirectory">Directory of the plugin, as discovery found it.</param>
    /// <param name="destinationDirectory">Directory of the copy, created together with its parents.</param>
    /// <remarks>
    /// A file-system failure throws from here and is turned into a skip of this plan by
    /// <see cref="TryCopyPlan"/>, which owns the error handling.
    /// </remarks>
    private static void CopyTree(string sourceDirectory, string destinationDirectory)
    {
        RefuseReparsePoint(sourceDirectory);
        Directory.CreateDirectory(destinationDirectory);

        foreach (var sourcePath in Directory.EnumerateFileSystemEntries(sourceDirectory))
        {
            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));

            if (Directory.Exists(sourcePath))
            {
                CopyTree(sourcePath, destinationPath);
                continue;
            }

            RefuseReparsePoint(sourcePath);
            if (!IsHostProvidedFile(Path.GetFileName(sourcePath)))
            {
                File.Copy(sourcePath, destinationPath, overwrite: true);
            }
        }
    }

    /// <summary>Fails the copy of this plan when <paramref name="path"/> is a link or another reparse point.</summary>
    /// <remarks>
    /// A link is refused instead of followed, because it can name content of any size outside the plugin
    /// directory: following it would put foreign files into the temporary copy without saying so, and
    /// skipping it would leave a hole in a copy the plugin is then loaded from. The whole plan therefore
    /// fails, and this refusal covers the directory the copy starts from as well as every entry below it.
    /// </remarks>
    private static void RefuseReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"'{path}' is a symbolic link or another reparse point, and links are never copied");
        }
    }

    /// <summary>
    /// True for a file the host already provides and which must not be copied: the plugin has to bind to the
    /// host copy of those types, so a second copy beside it would load a second set of MCP or Roslyn types
    /// and the types shared with the host would stop matching.
    /// </summary>
    /// <param name="fileName">Simple file name inside the plugin tree.</param>
    /// <remarks>
    /// The decision uses the simple assembly name, so the <c>.pdb</c> of a skipped assembly is skipped with
    /// it, while every other file, including a private dependency of the plugin, is copied.
    /// </remarks>
    private static bool IsHostProvidedFile(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (!extension.Equals(AssemblyFileExtension, StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(SymbolFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var assemblyName = Path.GetFileNameWithoutExtension(fileName);
        return assemblyName.Equals(HostAssemblyName, StringComparison.OrdinalIgnoreCase)
            || IsMcpAssembly(assemblyName)
            || assemblyName.StartsWith(RoslynAssemblyPrefix, StringComparison.OrdinalIgnoreCase)
            || assemblyName.StartsWith(ExtensionsAssemblyPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True for the MCP assembly itself and for its satellite assemblies.</summary>
    private static bool IsMcpAssembly(string assemblyName) =>
        assemblyName.Equals(McpAssemblyName, StringComparison.OrdinalIgnoreCase)
        || assemblyName.StartsWith(McpAssemblyName + ".", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True for the failures a copy of one plugin can hit: a missing, locked or unreadable file, a path the
    /// file system rejects, or an operation it does not support.
    /// </summary>
    /// <remarks>
    /// The list is closed on purpose: a failure outside it is a defect of this code rather than a property of
    /// the plugin, and reporting it as a skip would hide it.
    /// </remarks>
    private static bool IsFileSystemFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}

/// <summary>
/// Result of one shadow-copy pass: the plans to load, in input order, and the plans whose copy failed.
/// </summary>
/// <remarks>
/// A plan that needed a copy keeps its original directory in <see cref="PluginLoadPlan.Directory"/> while its
/// entry path points into the copy, so the directory the plugin was found in and the file the host loads are
/// both known to the caller.
/// </remarks>
public sealed record PluginShadowCopyResult
{
    /// <summary>Plans to load, in input order, each one loadable as it is.</summary>
    public IReadOnlyList<PluginLoadPlan> Plans { get; init; } = [];

    /// <summary>Plans whose copy failed, each with its concrete reason; one skip never ends the pass.</summary>
    public IReadOnlyList<PluginDiscoverySkip> Skipped { get; init; } = [];
}
