using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services;

/// <summary>
/// Minimal, public-API-only <see cref="IAnalyzerAssemblyLoader"/> for analyzer/generator DLLs that
/// <see cref="AnalyzerReferenceShadowCopier"/> has already copied into a private scratch directory.
/// Roslyn's own non-locking loader (<c>Microsoft.CodeAnalysis.AnalyzerAssemblyLoader.CreateNonLockingLoader</c>)
/// is <c>internal</c> and not usable from a normal NuGet consumer, so this type implements the small public
/// contract itself. Because callers only ever pass paths under a scratch directory (never the analyzer
/// project's real build output), a plain <see cref="Assembly.LoadFrom(string)"/> is safe here — the file this
/// process ends up locking is the private copy, not the one a later <c>dotnet build</c> needs to overwrite.
/// </summary>
public sealed class InProcessAnalyzerAssemblyLoader : IAnalyzerAssemblyLoader
{
    private readonly ConcurrentDictionary<string, Assembly> _loadedByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _dependencyDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _resolveHandlerGate = new();
    private bool _resolveHandlerRegistered;

    public Assembly LoadFromPath(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        if (!Path.IsPathRooted(fullPath))
        {
            throw new ArgumentException("Path must be absolute.", nameof(fullPath));
        }

        return _loadedByPath.GetOrAdd(fullPath, LoadCore);
    }

    public void AddDependencyLocation(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            _dependencyDirectories.TryAdd(directory, 0);
        }
    }

    private Assembly LoadCore(string fullPath)
    {
        EnsureDependencyResolveHandlerRegistered();
        return Assembly.LoadFrom(fullPath);
    }

    /// <summary>
    /// Best-effort same-directory probing for analyzer dependencies that are not already loadable from the
    /// default load context (e.g. a helper library shipped alongside the analyzer DLL). NuGet-provided BCL/Roslyn
    /// dependencies normally resolve without this; this only covers extra private dependencies.
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

            AppDomain.CurrentDomain.AssemblyResolve += ResolveDependencyFromKnownDirectories;
            _resolveHandlerRegistered = true;
        }
    }

    private Assembly? ResolveDependencyFromKnownDirectories(object? sender, ResolveEventArgs args)
    {
        var simpleName = new AssemblyName(args.Name).Name;
        if (string.IsNullOrEmpty(simpleName))
        {
            return null;
        }

        foreach (var directory in _dependencyDirectories.Keys)
        {
            var candidate = Path.Combine(directory, simpleName + ".dll");
            if (File.Exists(candidate))
            {
                return _loadedByPath.GetOrAdd(candidate, Assembly.LoadFrom);
            }
        }

        return null;
    }
}
