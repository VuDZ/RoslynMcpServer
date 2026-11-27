using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using RoslynMcpServer.Hosting;

namespace RoslynMcpServer.Plugins;

/// <summary>
/// Registration surface handed to <see cref="IRoslynMcpPlugin.Register"/>. It writes into the host
/// <see cref="IServiceCollection"/> and the same SDK tool collection the built-in tools use.
/// </summary>
/// <remarks>
/// <para>
/// A refused name covers that name only: the other names of the same call keep registering, and the
/// reason is reported back. A tool host the host cannot build is different — it rejects the whole
/// plugin, so the container never holds a tool that would fail on the first agent call.
/// </para>
/// <para>
/// Reservations live outside the DI container keyed by the <see cref="IServiceCollection"/> instance, so
/// the same tool name registers again in another container of the same process.
/// </para>
/// </remarks>
public sealed class RoslynMcpPluginContext : IDisposable
{
    /// <summary>Host service collection the plugin registers its singletons into.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Plugin id this context was created for.</summary>
    public string PluginName { get; }

    /// <summary>Tool prefix every name registered through this context must start with.</summary>
    public string ToolPrefix { get; }

    /// <summary>Everything this context published or refused so far, in call order.</summary>
    public RoslynMcpPluginRegistration Registration
    {
        get
        {
            lock (_gate)
            {
                return _registration;
            }
        }
    }

    /// <summary>
    /// Creates the registration surface for one plugin of one service collection.
    /// </summary>
    /// <param name="services">Host collection the plugin registers into.</param>
    /// <param name="pluginName">Plugin id, used in skip reasons.</param>
    /// <param name="toolPrefix">Manifest tool prefix. Every tool name of this plugin must start with it.</param>
    /// <exception cref="ArgumentException"><paramref name="toolPrefix"/> is empty or whitespace.</exception>
    public RoslynMcpPluginContext(IServiceCollection services, string pluginName, string toolPrefix)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);

        // An empty prefix is a host error, not a broken plugin: the prefix comes from the manifest that
        // a later epoch validates, and it must fail before any method is walked.
        if (string.IsNullOrWhiteSpace(toolPrefix))
        {
            throw new ArgumentException(
                $"Plugin '{pluginName}' has an empty tool prefix. Refusing to register its tools.",
                nameof(toolPrefix));
        }

        Services = services;
        PluginName = pluginName;
        ToolPrefix = toolPrefix;
        _reservations = Reservations.GetValue(services, static _ => new ReservedToolNames());
    }

    /// <summary>
    /// Registers every public method of <typeparamref name="T"/> carrying <c>[McpServerTool]</c> as an MCP
    /// tool, and <typeparamref name="T"/> itself as a singleton the host constructs per invocation.
    /// </summary>
    /// <remarks>
    /// A name is refused, with the reason reported back, when it lacks the plugin tool prefix, when the
    /// built-in catalog already owns it, or when another registration of this service collection already
    /// took it. Such a refusal covers that name only: the other names of the same call still register. A
    /// tool host type that cannot be constructed is different — it throws
    /// <see cref="RoslynMcpPluginToolHostException"/> so the host rejects the whole plugin.
    /// </remarks>
    /// <returns>Names published by this call, with the refused methods and their reasons.</returns>
    /// <exception cref="RoslynMcpPluginToolHostException">A tool host type could not be constructed.</exception>
    public RoslynMcpPluginRegistration AddToolsFrom<T>()
        where T : class
    {
        var hostType = typeof(T);
        var accepted = new List<(string Name, MethodInfo Method)>();
        var skipped = new List<RoslynMcpPluginToolSkip>();

        foreach (var method in DiscoverToolMethods(hostType))
        {
            var name = ReadToolName(method);
            if (!name.StartsWith(ToolPrefix, StringComparison.Ordinal))
            {
                skipped.Add(Skip(name, method, $"tool name does not start with the plugin prefix '{ToolPrefix}'"));
                continue;
            }

            if (McpToolCatalog.All.Any(descriptor => descriptor.Name.Equals(name, StringComparison.Ordinal)))
            {
                skipped.Add(Skip(name, method, $"tool name '{name}' is reserved by the built-in catalog"));
                continue;
            }

            if (TryReserve(name, out var reason))
            {
                accepted.Add((name, method));
                continue;
            }

            skipped.Add(Skip(name, method, reason));
        }

        var registeredNames = new List<string>();
        foreach (var (name, method) in accepted)
        {
            var descriptor = new McpToolDescriptor
            {
                Name = name,
                HostType = hostType,
                Method = method,
                Group = string.Empty,
                InLiteCore = false,
                IsReadOnly = true,
                ExecutesProcess = false,
            };

            var factory = descriptor.CreateFactory();
            try
            {
                var validation = ValidationProvider();
                _ = factory(validation);

                // McpServerTool.Create stores the target factory instead of running it, so it alone would
                // not notice a tool host type the container cannot build. Constructing the host type is
                // what turns Create into a real check: a constructor that does not resolve rejects the
                // plugin while the host is still starting, not on the first agent call.
                if (!method.IsStatic)
                {
                    _ = ActivatorUtilities.CreateInstance(validation, hostType);
                }
            }
            catch (Exception ex)
            {
                // This name cannot be served and the plugin must not look half loaded, so the failure
                // leaves AddToolsFrom as an exception. The registrar is the only place that can undo the
                // descriptors and names the plugin published in earlier calls.
                ReleaseReservations(accepted.Select(entry => entry.Name));
                throw new RoslynMcpPluginToolHostException(DescribeCreationFailure(method, ex), ex);
            }

            var tool = ServiceDescriptor.Singleton(factory);
            Services.Add(tool);
            registeredNames.Add(name);
        }

        if (registeredNames.Count > 0 && !IsToolHostRegistered(hostType))
        {
            Services.AddSingleton(hostType);
        }

        lock (_gate)
        {
            _publishedNames.UnionWith(registeredNames);
        }

        return Record(new RoslynMcpPluginRegistration
        {
            RegisteredTypes = registeredNames.Count == 0 ? [] : [hostType],
            RegisteredNames = registeredNames,
            Skipped = skipped,
        });
    }

    /// <summary>
    /// Removes every descriptor added since <paramref name="baseline"/>, for a plugin that must be
    /// rejected as a whole. The baseline is the descriptor count taken before <c>Register</c> started, so
    /// the cut covers what the plugin added on its own — <c>Services.AddSingleton</c> calls included — and
    /// never touches a descriptor of the host or of an earlier plugin.
    /// </summary>
    internal void RemoveDescriptorsAddedSince(int baseline)
    {
        for (var index = Services.Count - 1; index >= baseline && index >= 0; index--)
        {
            Services.RemoveAt(index);
        }
    }

    /// <summary>Frees the names this plugin published, for a plugin that must be rejected as a whole.</summary>
    internal IReadOnlyList<string> ReleasePublishedNames()
    {
        string[] published;
        lock (_gate)
        {
            published = [.. _publishedNames];
            _publishedNames.Clear();
        }

        foreach (var name in published)
        {
            _reservations.Release(name, PluginName);
        }

        return published;
    }

    public void Dispose()
    {
        _validationProvider?.Dispose();
        _validationProvider = null;
    }

    private static IEnumerable<MethodInfo> DiscoverToolMethods(Type hostType) =>
        hostType
            .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName
                             && method.GetCustomAttribute<McpServerToolAttribute>() is not null);

    private static string ReadToolName(MethodInfo method)
    {
        var attribute = method.GetCustomAttribute<McpServerToolAttribute>()!;
        return string.IsNullOrWhiteSpace(attribute.Name) ? method.Name : attribute.Name;
    }

    private static RoslynMcpPluginToolSkip Skip(string toolName, MethodInfo method, string reason) =>
        new()
        {
            ToolName = toolName,
            MethodName = method.Name,
            Reason = reason,
        };

    private static string DescribeCreationFailure(MethodInfo method, Exception exception)
    {
        var message = exception.Message.Trim();
        var detail = message.Length == 0 ? $"failed with {exception.GetType().Name}" : $"failed: {message}";
        return $"method '{method.Name}' was refused because its tool host {detail}";
    }

    /// <summary>
    /// Provider used to construct a tool host type before the plugin is committed. It is built from the
    /// descriptors registered so far, so a plugin singleton added earlier in <c>Register</c> is visible.
    /// It is rebuilt when the plugin registered more services since the previous call, and it is thrown
    /// away afterwards: the tool the agent calls is always constructed from the host provider.
    /// </summary>
    private IServiceProvider ValidationProvider()
    {
        if (_validationProvider is not null && _validationProviderMark == Services.Count)
        {
            return _validationProvider;
        }

        _validationProvider?.Dispose();
        _validationProvider = Services.BuildServiceProvider();
        _validationProviderMark = Services.Count;
        return _validationProvider;
    }

    private bool TryReserve(string name, out string reason)
    {
        if (_reservations.TryReserve(name, PluginName))
        {
            reason = string.Empty;
            return true;
        }

        // The owner is named even when the owner is this plugin: repeating a name across two calls of
        // one plugin is still a second registration of that name.
        reason = $"tool name '{name}' is already registered in this service collection by plugin "
                 + $"'{_reservations.OwnerOf(name) ?? PluginName}'";
        return false;
    }

    private void ReleaseReservations(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            _reservations.Release(name, PluginName);
        }
    }

    private bool IsToolHostRegistered(Type hostType) =>
        Services.Any(descriptor => descriptor.ServiceType == hostType);

    private RoslynMcpPluginRegistration Record(RoslynMcpPluginRegistration report)
    {
        lock (_gate)
        {
            _registration = _registration.Merge(report);
        }

        return report;
    }

    private static readonly ConditionalWeakTable<IServiceCollection, ReservedToolNames> Reservations = new();

    private readonly Lock _gate = new();
    private readonly ReservedToolNames _reservations;
    private readonly HashSet<string> _publishedNames = new(StringComparer.Ordinal);
    private RoslynMcpPluginRegistration _registration = RoslynMcpPluginRegistration.Empty;
    private ServiceProvider? _validationProvider;
    private int _validationProviderMark = -1;

    /// <summary>
    /// Tool names already taken inside one service collection. The table is keyed by the collection
    /// instance, so the set never outlives the container and a second host of the same process starts
    /// with free names.
    /// </summary>
    private sealed class ReservedToolNames
    {
        public bool TryReserve(string name, string pluginName)
        {
            lock (_gate)
            {
                return _owners.TryAdd(name, pluginName);
            }
        }

        public string? OwnerOf(string name)
        {
            lock (_gate)
            {
                return _owners.GetValueOrDefault(name);
            }
        }

        public void Release(string name, string pluginName)
        {
            lock (_gate)
            {
                if (_owners.TryGetValue(name, out var owner) && owner.Equals(pluginName, StringComparison.Ordinal))
                {
                    _owners.Remove(name);
                }
            }
        }

        private readonly Dictionary<string, string> _owners = new(StringComparer.Ordinal);
        private readonly Lock _gate = new();
    }
}
