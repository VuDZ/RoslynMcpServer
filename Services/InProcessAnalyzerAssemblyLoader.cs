using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services;

/// <summary>
/// Process-lifetime <see cref="IAnalyzerAssemblyLoader"/> owned by <see cref="SolutionManager"/>.
/// Loads only paths the caller already prepared (shadow generations), never the analyzer
/// project's real build output. Same-identity bytes cannot be replaced in-process
/// (<see cref="AnalyzerLoaderContract.SupportedRefreshMode"/>). Private helper resolution
/// is instrumented and refused — first-match simple-name probing is not supported.
/// Workspace clear does not unload assemblies or detach the resolve handler.
/// </summary>
public sealed class InProcessAnalyzerAssemblyLoader : IAnalyzerAssemblyLoader
{
    private readonly ConcurrentDictionary<string, Assembly> _loadedByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Assembly> _loadedByIdentity = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<DependencyLocationRecord> _dependencyLocations = new();
    private readonly ConcurrentQueue<DependencyResolveAttempt> _resolveAttempts = new();
    private readonly object _resolveHandlerGate = new();
    private bool _resolveHandlerRegistered;

    public Assembly LoadFromPath(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        if (!Path.IsPathRooted(fullPath))
        {
            throw new ArgumentException("Path must be absolute.", nameof(fullPath));
        }

        var normalized = Path.GetFullPath(fullPath);
        if (_loadedByPath.TryGetValue(normalized, out var existing))
        {
            return existing;
        }

        if (TryGetIdentityCollision(normalized, out var collision))
        {
            throw new AnalyzerIdentityCollisionException(
                AnalyzerLoaderContract.IdentityCollisionReason,
                normalized,
                collision.Location,
                collision.Identity);
        }

        return _loadedByPath.GetOrAdd(normalized, LoadCore);
    }

    /// <summary>
    /// Snapshot of assemblies this loader has loaded in the current process. Test/host observation only.
    /// </summary>
    internal IReadOnlyList<LoadedAnalyzerAssembly> SnapshotLoadedAssemblies()
    {
        return _loadedByPath
            .Select(kv => new LoadedAnalyzerAssembly(
                kv.Key,
                kv.Value.GetName().FullName ?? kv.Value.FullName ?? kv.Value.GetName().Name ?? kv.Key,
                kv.Value.Location))
            .ToList();
    }

    internal IReadOnlyList<DependencyLocationRecord> SnapshotDependencyLocations() => _dependencyLocations.ToArray();

    internal IReadOnlyList<DependencyResolveAttempt> SnapshotResolveAttempts() => _resolveAttempts.ToArray();

    internal bool ResolveHandlerRegistered
    {
        get
        {
            lock (_resolveHandlerGate)
            {
                return _resolveHandlerRegistered;
            }
        }
    }

    internal readonly record struct LoadedAnalyzerAssembly(string RequestedPath, string Identity, string Location);

    internal readonly record struct DependencyLocationRecord(
        string FullPath,
        string? Directory,
        DateTimeOffset RecordedAtUtc);

    internal readonly record struct DependencyResolveAttempt(
        string RequestedName,
        string? RequestingAssemblyLocation,
        string? RequestingAssemblyIdentity,
        string? SelectedPath,
        string Outcome);

    public void AddDependencyLocation(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return;
        }

        string? directory = null;
        try
        {
            directory = Path.GetDirectoryName(Path.GetFullPath(fullPath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            directory = Path.GetDirectoryName(fullPath);
        }

        _dependencyLocations.Enqueue(new DependencyLocationRecord(
            fullPath,
            directory,
            DateTimeOffset.UtcNow));
    }

    internal bool TryGetIdentityCollision(string assemblyPath, out LoadedAnalyzerAssembly loaded)
    {
        loaded = default;
        AssemblyName identity;
        try
        {
            identity = AssemblyName.GetAssemblyName(assemblyPath);
        }
        catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or ArgumentException)
        {
            return false;
        }

        var key = IdentityKey(identity);
        if (string.IsNullOrEmpty(key) || !_loadedByIdentity.TryGetValue(key, out var assembly))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(assembly.Location) && PathsEqual(assembly.Location, assemblyPath))
        {
            return false;
        }

        loaded = new LoadedAnalyzerAssembly(
            assembly.Location,
            assembly.GetName().FullName ?? assembly.FullName ?? key,
            assembly.Location);
        return true;
    }

    private Assembly LoadCore(string fullPath)
    {
        EnsureDependencyResolveHandlerRegistered();
        if (TryGetIdentityCollision(fullPath, out var collision))
        {
            throw new AnalyzerIdentityCollisionException(
                AnalyzerLoaderContract.IdentityCollisionReason,
                fullPath,
                collision.Location,
                collision.Identity);
        }

        Assembly assembly;
        try
        {
            assembly = Assembly.LoadFrom(fullPath);
        }
        catch (FileLoadException)
        {
            if (TryGetIdentityCollision(fullPath, out var loaded)
                || AnalyzerExecutionGate.TryGetProcessIdentityCollision(fullPath, out loaded))
            {
                throw new AnalyzerIdentityCollisionException(
                    AnalyzerLoaderContract.IdentityCollisionReason,
                    fullPath,
                    loaded.Location,
                    loaded.Identity);
            }

            throw;
        }

        var key = IdentityKey(assembly.GetName());
        if (!string.IsNullOrEmpty(key))
        {
            _loadedByIdentity.TryAdd(key, assembly);
        }

        return assembly;
    }

    /// <summary>
    /// Records resolve requests. Does not bind the first matching simple name from an
    /// unordered process-global directory set — that is explicitly unsupported.
    /// </summary>
    private void EnsureDependencyResolveHandlerRegistered()
    {
        if (_resolveHandlerRegistered)
        {
            return;
        }

        lock (_resolveHandlerGate)
        {
            if (_resolveHandlerRegistered)
            {
                return;
            }

            AppDomain.CurrentDomain.AssemblyResolve += RecordUnsupportedDependencyResolve;
            _resolveHandlerRegistered = true;
        }
    }

    private Assembly? RecordUnsupportedDependencyResolve(object? sender, ResolveEventArgs args)
    {
        var requested = args.Name;
        var requesting = args.RequestingAssembly;
        string? selected = null;
        var outcome = "unsupported-private-dependency";

        if (AnalyzerHostContractCatalog.IsSharedHostContract(new AssemblyName(requested).Name))
        {
            outcome = "host-contract-not-probed";
        }
        else
        {
            var simpleName = new AssemblyName(requested).Name;
            if (!string.IsNullOrEmpty(simpleName) && requesting?.Location is { Length: > 0 } requesterPath)
            {
                var alongside = Path.Combine(Path.GetDirectoryName(requesterPath) ?? string.Empty, simpleName + ".dll");
                if (File.Exists(alongside))
                {
                    selected = alongside;
                    outcome = AnalyzerHostContractCatalog.IsSharedHostContract(simpleName)
                        ? "rejected-generation-private-contract-copy"
                        : "rejected-private-helper";
                }
            }
        }

        _resolveAttempts.Enqueue(new DependencyResolveAttempt(
            requested,
            requesting?.Location,
            requesting?.GetName().FullName,
            selected,
            outcome));
        return null;
    }

    private static string? IdentityKey(AssemblyName name)
    {
        if (string.IsNullOrEmpty(name.Name))
        {
            return null;
        }

        return name.Name + "," + (name.Version?.ToString() ?? "0.0.0.0");
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(left, right, comparison);
        }
    }
}

internal sealed class AnalyzerIdentityCollisionException : InvalidOperationException
{
    public AnalyzerIdentityCollisionException(
        string message,
        string requestedPath,
        string loadedPath,
        string identity)
        : base(message)
    {
        RequestedPath = requestedPath;
        LoadedPath = loadedPath;
        Identity = identity;
    }

    public string RequestedPath { get; }

    public string LoadedPath { get; }

    public string Identity { get; }
}
