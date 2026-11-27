using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace RoslynMcpServer.Plugins;

/// <summary>
/// Loads the entry assembly of every plan into a load context of its own, creates the one type the plan
/// names and calls <see cref="RoslynMcpPluginRegistrar"/> so the plugin writes its tools into the host
/// service collection.
/// </summary>
/// <remarks>
/// <para>
/// The loader runs before <c>IHost.Build</c>, so the service collection is the only host object a plugin
/// reaches. The tool host types it registers are created from the built provider, exactly like the built-in
/// tool host types, so a plugin tool and a built-in tool see one <c>SolutionManager</c> instance and one
/// loaded solution.
/// </para>
/// <para>
/// One plan is one outcome: a plugin that cannot be loaded is skipped with its reason and the remaining
/// plans of the same call are still loaded. Nothing here throws for the entry file, the dependency graph,
/// the plugin type or the plugin code. Every load context stays in the process; unloading is out of scope.
/// </para>
/// </remarks>
public static class PluginAssemblyLoad
{
    private const string HostAssemblyName = "RoslynMcpServer";
    private const string McpAssemblyName = "ModelContextProtocol";
    private const string RoslynAssemblyPrefix = "Microsoft.CodeAnalysis";
    private const string ExtensionsAssemblyPrefix = "Microsoft.Extensions.";
    private const string DepsJsonExtension = ".deps.json";

    /// <summary>
    /// Loads every plan against <paramref name="services"/> and reports one outcome per plan.
    /// </summary>
    /// <param name="plans">Plans of one discovery pass, each with the entry path it must be loaded from.</param>
    /// <param name="services">Host collection after <c>AddRoslynMcpServerTools</c> and before <c>Build</c>.</param>
    /// <returns>
    /// One outcome per plan, in input order. A skipped plan reports no tool name and a reason; a loaded plan
    /// reports the names it published and never a reason.
    /// </returns>
    /// <remarks>
    /// A plan that cannot be loaded leaves the collection as it was before that plan: the registrar rolls
    /// back what the plugin had added and frees the tool names it had taken, so a later plan of the same
    /// call can take those names.
    /// </remarks>
    public static IReadOnlyList<PluginLoadOutcome> Load(IReadOnlyList<PluginLoadPlan> plans, IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(services);

        var outcomes = new List<PluginLoadOutcome>(plans.Count);
        foreach (var plan in plans)
        {
            outcomes.Add(LoadPlan(plan, services));
        }

        return [.. outcomes];
    }

    /// <summary>
    /// Loads one plan, or returns the outcome that names why this plan has no plugin.
    /// </summary>
    /// <remarks>
    /// Everything below the plan is third-party: the bytes of the entry file, its dependency graph, its
    /// types, its constructors and its <c>Register</c>. The catch is therefore not restricted to a list of
    /// exception types — a failure of that input is a skip of this plan, never an exception that ends the
    /// pass and leaves the remaining plugins unloaded. The checks that can name their own cause return
    /// before this boundary.
    /// </remarks>
    private static PluginLoadOutcome LoadPlan(PluginLoadPlan plan, IServiceCollection services)
    {
        try
        {
            return LoadOrSkip(plan, services);
        }
        catch (Exception ex)
        {
            return Skipped(plan, $"plugin '{plan.Id}' could not be loaded from '{plan.EntryPath}': {Message(ex)}");
        }
    }

    private static PluginLoadOutcome LoadOrSkip(PluginLoadPlan plan, IServiceCollection services)
    {
        PluginLoadContext context;
        try
        {
            context = new PluginLoadContext(plan);
        }
        catch (Exception ex)
        {
            // The resolver reads the deps.json next to the entry. A plugin without a readable graph cannot
            // have its own dependencies resolved at all, so this plan is refused instead of loaded against
            // the assemblies of the host.
            return Skipped(plan, $"the dependency graph of '{plan.EntryPath}' could not be read: {Message(ex)}");
        }

        Assembly entry;
        try
        {
            entry = context.LoadFromAssemblyPath(plan.EntryPath);
        }
        catch (Exception ex)
        {
            return Skipped(plan, $"entry assembly '{plan.EntryPath}' could not be loaded: {Message(ex)}");
        }

        if (!TryResolveReferences(entry, context, out var referenceFailure))
        {
            return Skipped(plan, referenceFailure);
        }

        if (!TryCreatePlugin(plan, entry, out var plugin, out var creationFailure))
        {
            return Skipped(plan, creationFailure);
        }

        // The registrar owns everything that can fail from here: it cuts off the descriptors the plugin added,
        // frees the names it had taken, keeps the process alive and writes the failure to stderr. The loader
        // turns that report into a skip of this plan.
        var registration = RoslynMcpPluginRegistrar.Register(services, plan.Id, plan.ToolPrefix, plugin);
        if (registration.Failures.Count > 0)
        {
            return Skipped(plan, registration.Failures[0], registration);
        }

        return new PluginLoadOutcome
        {
            Id = plan.Id,
            EntryPath = plan.EntryPath,
            IsLoaded = true,
            ToolNames = registration.RegisteredNames,
            SkipReason = null,
            Registration = registration,
        };
    }

    /// <summary>
    /// Creates the one type the plan names, or reports why this entry has no plugin of that plan.
    /// </summary>
    /// <param name="plan">Plan whose type name and id are the expectations for this entry.</param>
    /// <param name="entry">Entry assembly, already loaded from the plan path.</param>
    /// <param name="plugin">Created plugin, or <see langword="null"/> when this method returns <see langword="false"/>.</param>
    /// <param name="failure">Reason the entry produced no plugin; always set when this method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the type exists, implements the interface and reports the plan id.</returns>
    /// <remarks>
    /// The plugin type is asked for by name only: the loader never walks the types of an entry to find a
    /// candidate, and a type that only resembles a plugin is not one. The name of the instance is compared
    /// ordinally with the plan id before <c>Register</c> runs, because the id is what the manifest and the
    /// drop-in directory already agreed on.
    /// </remarks>
    private static bool TryCreatePlugin(
        PluginLoadPlan plan,
        Assembly entry,
        [NotNullWhen(true)] out IRoslynMcpPlugin? plugin,
        [NotNullWhen(false)] out string? failure)
    {
        plugin = null;

        var type = entry.GetType(plan.PluginType, throwOnError: false);
        if (type is null)
        {
            failure = $"plugin type '{plan.PluginType}' was not found in '{plan.EntryPath}'";
            return false;
        }

        if (!typeof(IRoslynMcpPlugin).IsAssignableFrom(type))
        {
            failure = $"plugin type '{plan.PluginType}' does not implement {nameof(IRoslynMcpPlugin)}";
            return false;
        }

        if (type.GetConstructor(Type.EmptyTypes) is null)
        {
            failure = $"plugin type '{plan.PluginType}' cannot be created: plugin type constructor must be parameterless";
            return false;
        }

        // The instance is created before Build, so a plugin constructor cannot take a service: a plugin gets
        // its dependencies in the constructors of its tool host types, which the container builds after Build
        // exactly like the tool host types of the built-in tools.
        try
        {
            plugin = (IRoslynMcpPlugin)Activator.CreateInstance(type)!;
        }
        catch (Exception ex)
        {
            failure = $"plugin type '{plan.PluginType}' threw while it was created: {Message(ex)}";
            return false;
        }

        if (!string.Equals(plugin.Name, plan.Id, StringComparison.Ordinal))
        {
            failure = $"plugin type '{plan.PluginType}' reports the name '{plugin.Name}', which is not the plan id '{plan.Id}'";
            plugin = null;
            return false;
        }

        failure = null;
        return true;
    }

    /// <summary>
    /// Resolves every assembly reference of the entry through the same three stages a call inside the plugin
    /// would use, and reports the first reference no stage can serve.
    /// </summary>
    /// <remarks>
    /// The runtime binds a reference when the member that needs it is first compiled, so a plugin with a
    /// missing dependency would load, register its tools and fail on the agent call that reaches that member.
    /// Walking the references moves that failure to load time, where the host can refuse the plugin while it
    /// starts. The walk adds no directory scan of its own: it asks the resolver of this plugin, which reads
    /// the graph the plugin was built with.
    /// </remarks>
    private static bool TryResolveReferences(
        Assembly entry,
        PluginLoadContext context,
        [NotNullWhen(false)] out string? failure)
    {
        foreach (var reference in entry.GetReferencedAssemblies())
        {
            if (context.TryResolve(reference, out _, out var reason))
            {
                continue;
            }

            failure = $"entry assembly '{entry.GetName().Name}' references '{reference.Name}': {reason}";
            return false;
        }

        failure = null;
        return true;
    }

    /// <summary>
    /// Simple names of the assemblies the plugin's own <c>deps.json</c> records as runtime assets, which are
    /// the assemblies that have to travel in the plugin directory.
    /// </summary>
    /// <remarks>
    /// The dependency resolver answers with a file only when that file exists, so it cannot tell a dependency
    /// the plugin declares from a name the plugin never carried. This set supplies that difference: a declared
    /// dependency with no file refuses the plugin instead of binding a host assembly of the same name. The
    /// file is third-party input, so an unreadable or differently shaped graph does not throw and does not
    /// refuse the plugin by itself — the set stays empty and the default context decides.
    /// </remarks>
    private static HashSet<string> ReadPrivateAssemblyNames(string entryPath)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var depsPath = Path.Combine(
            Path.GetDirectoryName(entryPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(entryPath) + DepsJsonExtension);

        if (!File.Exists(depsPath))
        {
            return names;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllBytes(depsPath));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return names;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("targets", out var targets)
                || targets.ValueKind != JsonValueKind.Object)
            {
                return names;
            }

            // One plugin is built for one target framework, but reading every target keeps the reader working
            // for a graph that lists more than one.
            foreach (var target in targets.EnumerateObject())
            {
                if (target.Value.ValueKind == JsonValueKind.Object)
                {
                    AddRuntimeAssetsOfTarget(target.Value, names);
                }
            }
        }

        return names;
    }

    /// <summary>Adds the assembly simple names of the runtime assets of one <c>deps.json</c> target.</summary>
    private static void AddRuntimeAssetsOfTarget(JsonElement target, HashSet<string> names)
    {
        foreach (var library in target.EnumerateObject())
        {
            if (library.Value.ValueKind != JsonValueKind.Object
                || !library.Value.TryGetProperty("runtime", out var runtime)
                || runtime.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            // A runtime asset is a managed dependency the build copies next to the entry, so its file name is
            // the simple name the runtime will ask for.
            foreach (var asset in runtime.EnumerateObject())
            {
                var assemblyName = Path.GetFileNameWithoutExtension(asset.Name);
                if (assemblyName.Length > 0)
                {
                    names.Add(assemblyName);
                }
            }
        }
    }

    /// <summary>True for the four families of assemblies the host shares with a plugin.</summary>
    private static bool IsHostContractAssembly(string simpleName) =>
        simpleName.Equals(HostAssemblyName, StringComparison.Ordinal)
        || simpleName.Equals(McpAssemblyName, StringComparison.Ordinal)
        || simpleName.StartsWith(McpAssemblyName + ".", StringComparison.Ordinal)
        || simpleName.StartsWith(RoslynAssemblyPrefix, StringComparison.Ordinal)
        || simpleName.StartsWith(ExtensionsAssemblyPrefix, StringComparison.Ordinal);

    /// <summary>An outcome that publishes no tool name and carries the reason this plan produced no plugin.</summary>
    private static PluginLoadOutcome Skipped(
        PluginLoadPlan plan,
        string reason,
        RoslynMcpPluginRegistration? registration = null) =>
        new()
        {
            Id = plan.Id,
            EntryPath = plan.EntryPath,
            IsLoaded = false,
            ToolNames = [],
            SkipReason = reason,
            Registration = registration ?? RoslynMcpPluginRegistration.Empty,
        };

    /// <summary>Message of a failure for a skip reason, or its type name when it carries no message.</summary>
    /// <remarks>
    /// A constructor that throws arrives wrapped in <see cref="TargetInvocationException"/>, whose own message
    /// says nothing about the plugin; the reason reports the exception the plugin code threw instead.
    /// </remarks>
    private static string Message(Exception exception)
    {
        var failure = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
        var message = failure.Message.Trim();
        return message.Length == 0 ? failure.GetType().Name : message;
    }

    /// <summary>
    /// Load context of one plugin: the plugin directory for its own dependencies, the default context for the
    /// host contract assemblies and for the framework.
    /// </summary>
    /// <remarks>
    /// The context is not collectible, because v1 never unloads a plugin; the process exits with the plugins
    /// it started with. There is no <c>LoadUnmanagedDll</c> override, so a file under <c>runtimes/</c> is
    /// copied with the plugin and a P/Invoke into it is not promised.
    /// </remarks>
    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        public PluginLoadContext(PluginLoadPlan plan)
            : base($"RoslynMcpPlugin:{plan.Id}")
        {
            _entryPath = plan.EntryPath;
            _resolver = new AssemblyDependencyResolver(plan.EntryPath);
            _privateAssemblyNames = ReadPrivateAssemblyNames(plan.EntryPath);
        }

        /// <summary>
        /// Resolves one assembly name in the order this loader defines and reports why it could not be served.
        /// </summary>
        /// <param name="assemblyName">Name the runtime or the reference walk asks for.</param>
        /// <param name="assembly">Resolved assembly, or <see langword="null"/> when no stage served it.</param>
        /// <param name="failure">Reason no stage served the name; always set when this method returns <see langword="false"/>.</param>
        /// <returns><see langword="true"/> when one of the three stages produced an assembly.</returns>
        public bool TryResolve(
            AssemblyName assemblyName,
            [NotNullWhen(true)] out Assembly? assembly,
            [NotNullWhen(false)] out string? failure)
        {
            assembly = null;
            var simpleName = assemblyName.Name ?? string.Empty;

            if (IsHostContractAssembly(simpleName))
            {
                return TryLoadHostContract(assemblyName, simpleName, out assembly, out failure);
            }

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            if (path is not null)
            {
                return TryLoadPluginDependency(path, simpleName, out assembly, out failure);
            }

            // A dependency the plugin's deps.json records as its own runtime asset has to come from the plugin
            // directory. The host may hold an assembly of that simple name, and handing that copy over would
            // hide the missing file behind a host library of another version.
            if (_privateAssemblyNames.Contains(simpleName))
            {
                failure = $"'{simpleName}' is a dependency the plugin records in its deps.json, but no file for it lies next to '{_entryPath}'";
                return false;
            }

            return TryLoadFramework(assemblyName, simpleName, out assembly, out failure);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // The runtime asks here for a reference that is first used at run time, including one no metadata
            // reference of the entry names. A miss stays a miss: the requesting call reports it. A reference
            // the loader can see is refused by the reference walk with a reason of its own.
            return TryResolve(assemblyName, out var assembly, out _) ? assembly : null;
        }

        /// <summary>First stage: an assembly the host shares with every plugin, from the default context.</summary>
        /// <remarks>
        /// The plugin compiled against the assemblies of this host, so it has to bind those instances: a second
        /// copy of RoslynMcpServer or ModelContextProtocol would make every shared type two types and the
        /// container would stop matching them. A file of the same name in the plugin directory is never opened
        /// here, which is also why the shadow copy leaves those files out.
        /// </remarks>
        private static bool TryLoadHostContract(
            AssemblyName assemblyName,
            string simpleName,
            out Assembly? assembly,
            out string? failure)
        {
            if (TryLoadFromDefaultContext(assemblyName, out assembly, out failure))
            {
                return true;
            }

            failure = $"the host contract assembly '{simpleName}' is not loaded and could not be loaded: {failure}";
            return false;
        }

        /// <summary>Second stage: a file of this plugin, from the dependency graph of its own deps.json.</summary>
        private bool TryLoadPluginDependency(
            string path,
            string simpleName,
            out Assembly? assembly,
            out string? failure)
        {
            try
            {
                assembly = LoadFromAssemblyPath(path);
                failure = null;
                return true;
            }
            catch (Exception ex)
            {
                assembly = null;
                failure = $"the plugin dependency '{simpleName}' at '{path}' could not be loaded: {Message(ex)}";
                return false;
            }
        }

        /// <summary>Third stage: the framework, and whatever else the default context of this process serves.</summary>
        private static bool TryLoadFramework(
            AssemblyName assemblyName,
            string simpleName,
            out Assembly? assembly,
            out string? failure)
        {
            if (TryLoadFromDefaultContext(assemblyName, out assembly, out failure))
            {
                return true;
            }

            failure = $"'{simpleName}' is neither a host contract assembly nor a plugin dependency, and the loaded framework does not provide it: {failure}";
            return false;
        }

        /// <summary>
        /// Assembly of that name from the default context: the instance this process already loaded when there
        /// is one, otherwise a load by name.
        /// </summary>
        /// <remarks>
        /// The already loaded instance is asked first so a shared type stays one type even when the request
        /// carries another version than the loaded assembly.
        /// </remarks>
        private static bool TryLoadFromDefaultContext(
            AssemblyName assemblyName,
            [NotNullWhen(true)] out Assembly? assembly,
            [NotNullWhen(false)] out string? failure)
        {
            assembly = FindLoaded(assemblyName.Name);
            if (assembly is not null)
            {
                failure = null;
                return true;
            }

            try
            {
                assembly = Default.LoadFromAssemblyName(assemblyName);
                failure = null;
                return true;
            }
            catch (Exception ex)
            {
                assembly = null;
                failure = Message(ex);
                return false;
            }
        }

        private static Assembly? FindLoaded(string? simpleName) =>
            simpleName is null
                ? null
                : Default.Assemblies.FirstOrDefault(
                    candidate => candidate.GetName().Name?.Equals(simpleName, StringComparison.Ordinal) == true);

        private readonly string _entryPath;
        private readonly AssemblyDependencyResolver _resolver;
        private readonly HashSet<string> _privateAssemblyNames;
    }
}
