namespace RoslynMcpServer.Plugins;

/// <summary>
/// Outcome of one <see cref="RoslynMcpPluginContext.AddToolsFrom{T}"/> call, and of the plugin
/// registration that contains it.
/// </summary>
/// <remarks>
/// <see cref="RegisteredNames"/> lists what the plugin published, including names of a call that
/// succeeded before a later call failed. A rejected plugin reports its reason under
/// <see cref="Failures"/>, and its names are free again in the container even though this report still
/// shows them.
/// </remarks>
public sealed class RoslynMcpPluginRegistration
{
    /// <summary>Report of a plugin that contributed nothing, skipped nothing and failed at nothing.</summary>
    public static RoslynMcpPluginRegistration Empty { get; } = new();

    /// <summary>Tool host types whose tools were published to the SDK collection.</summary>
    public IReadOnlyList<Type> RegisteredTypes { get; init; } = [];

    /// <summary>Tool names published to the SDK collection, in registration order.</summary>
    public IReadOnlyList<string> RegisteredNames { get; init; } = [];

    /// <summary>Methods whose name was refused while the rest of their call registered.</summary>
    public IReadOnlyList<RoslynMcpPluginToolSkip> Skipped { get; init; } = [];

    /// <summary>
    /// Plugin-level failures: an exception that escaped <c>Register</c>, or a tool host the host could
    /// not build. A plugin with a failure here is not in the container.
    /// </summary>
    public IReadOnlyList<string> Failures { get; init; } = [];

    /// <summary>True when this registration published no tool name.</summary>
    public bool IsEmpty => RegisteredNames.Count == 0;

    /// <summary>Combines this report with a later one, keeping the first occurrence order.</summary>
    public RoslynMcpPluginRegistration Merge(RoslynMcpPluginRegistration other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return new RoslynMcpPluginRegistration
        {
            RegisteredTypes = [.. RegisteredTypes, .. other.RegisteredTypes],
            RegisteredNames = [.. RegisteredNames, .. other.RegisteredNames],
            Skipped = [.. Skipped, .. other.Skipped],
            Failures = [.. Failures, .. other.Failures],
        };
    }
}
