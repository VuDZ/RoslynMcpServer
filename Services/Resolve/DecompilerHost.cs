using System.Text;
using System.Text.RegularExpressions;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;

namespace RoslynMcpServer.Services.Resolve;

/// <summary>Creates <see cref="DecompilerSession"/> instances with NuGet-aware assembly resolution.</summary>
public static class DecompilerHost
{
    private static readonly Regex ResolutionAssemblyName = new(
        @"Failed to resolve assembly:\s*'([^']+)'",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static DecompilerSession Create(string dllPath)
    {
        // Dependencies are best-effort. Strict resolution assumed every assembly reference ships with its
        // package, but some do not: Microsoft.CodeAnalysis.Workspaces references SQLitePCLRaw for an optional
        // storage service without a NuGet dependency on it, so no method of that assembly could be decompiled.
        // NuGetFallbackAssemblyResolver returns null for such a reference and records it; the type system
        // accepts null in either mode, and the setting below only keeps a resolver exception from being fatal.
        var settings = new DecompilerSettings { ThrowOnAssemblyResolveErrors = false };
        var module = new PEFile(dllPath);
        var targetFramework = module.DetectTargetFrameworkId();
        if (string.IsNullOrWhiteSpace(targetFramework))
        {
            targetFramework = GuessTargetFrameworkFromPath(dllPath) ?? "netstandard2.0";
        }

        // The inner resolver still throws: the NuGet fallback runs on its ResolutionException and records
        // the reference only when the fallback fails too.
        var inner = new UniversalAssemblyResolver(
            dllPath,
            throwOnError: true,
            targetFramework);

        foreach (var directory in CollectSearchDirectories(dllPath, targetFramework))
        {
            inner.AddSearchDirectory(directory);
        }

        var nugetRoot = GetNuGetPackagesRoot();
        inner.AddSearchDirectory(nugetRoot);
        var resolver = new NuGetFallbackAssemblyResolver(inner, nugetRoot);

        return new DecompilerSession(new CSharpDecompiler(module, resolver, settings), resolver);
    }

    /// <summary>
    /// A one-line note naming unresolved references, capped at <see cref="MaxListedUnresolvedReferences"/>
    /// names; empty when every reference was resolved.
    /// </summary>
    public static string FormatUnresolvedReferencesNote(IReadOnlyList<string> unresolvedReferences)
    {
        if (unresolvedReferences.Count == 0)
        {
            return string.Empty;
        }

        var listed = string.Join(", ", unresolvedReferences.Take(MaxListedUnresolvedReferences));
        var more = unresolvedReferences.Count > MaxListedUnresolvedReferences
            ? $" and {unresolvedReferences.Count - MaxListedUnresolvedReferences} more"
            : string.Empty;
        return $"// Unresolved assembly references, their types are shown as unknown: {listed}{more}.";
    }

    private const int MaxListedUnresolvedReferences = 10;

    public static string GetAssemblyLabel(string? assemblyName, string dllPath) =>
        string.IsNullOrWhiteSpace(assemblyName)
            ? Path.GetFileName(dllPath)
            : assemblyName;

    public static string FormatResolveError(Exception ex, string dllPath, string? targetFramework = null)
    {
        if (ex is not ResolutionException)
        {
            return ex.Message;
        }

        var sb = new StringBuilder();
        sb.Append($"Failed to resolve a dependency while decompiling `{Path.GetFileName(dllPath)}`. ");
        sb.Append(ex.Message.Trim());

        if (!string.IsNullOrWhiteSpace(targetFramework))
        {
            sb.Append($" Detected TFM: `{targetFramework}`.");
        }

        var match = ResolutionAssemblyName.Match(ex.Message);
        if (match.Success)
        {
            var assemblyRef = match.Groups[1].Value;
            var simpleName = assemblyRef.Split(',')[0].Trim();
            var fallback = NuGetFallbackAssemblyResolver.TryFindAssemblyDll(simpleName);
            sb.Append(fallback is not null
                ? $" NuGet fallback found: `{fallback}`."
                : $" NuGet fallback for `{simpleName}`: not found under `{GetNuGetPackagesRoot()}`.");
            if (simpleName.Contains("Version=0.0.0.0", StringComparison.OrdinalIgnoreCase)
                || assemblyRef.Contains("Version=0.0.0.0", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(" Hint: facade reference 0.0.0.0 — ensure the matching runtime pack exists in NuGet cache.");
            }
        }

        return sb.ToString();
    }

    private static IEnumerable<string> CollectSearchDirectories(string dllPath, string targetFramework)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return;
            }

            var full = Path.GetFullPath(path);
            if (seen.Add(full))
            {
                list.Add(full);
            }
        }

        var assemblyDir = Path.GetDirectoryName(dllPath);
        Add(assemblyDir);

        var current = assemblyDir;
        for (var depth = 0; depth < 8 && !string.IsNullOrEmpty(current); depth++)
        {
            Add(current);
            var libDir = Path.Combine(current, "lib");
            if (Directory.Exists(libDir))
            {
                foreach (var tfmDir in Directory.EnumerateDirectories(libDir))
                {
                    var tfmName = Path.GetFileName(tfmDir);
                    if (ShouldIncludeLibTfm(tfmName, targetFramework))
                    {
                        Add(tfmDir);
                    }
                }
            }

            current = Directory.GetParent(current)?.FullName;
        }

        foreach (var dotnetDir in CollectDotNetRuntimeSearchDirectories(targetFramework))
        {
            Add(dotnetDir);
        }

        return list;
    }

    private static bool ShouldIncludeLibTfm(string tfmFolderName, string targetFramework)
    {
        if (string.Equals(tfmFolderName, targetFramework, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (targetFramework.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)
            && tfmFolderName.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return tfmFolderName.StartsWith("netstandard2", StringComparison.OrdinalIgnoreCase)
               && !tfmFolderName.StartsWith("net472", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> CollectDotNetRuntimeSearchDirectories(string targetFramework)
    {
        var dotnetExe = DotNetHostResolver.ResolveDotNetExecutable();
        var dotnetRoot = Directory.GetParent(dotnetExe)?.FullName;
        if (string.IsNullOrEmpty(dotnetRoot) || !Directory.Exists(dotnetRoot))
        {
            yield break;
        }

        var sharedRoot = Path.Combine(dotnetRoot, "shared", "Microsoft.NETCore.App");
        if (Directory.Exists(sharedRoot))
        {
            var latest = Directory.GetDirectories(sharedRoot)
                .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (latest is not null)
            {
                yield return latest;
            }
        }

        var refPackRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
        if (!Directory.Exists(refPackRoot))
        {
            yield break;
        }

        var refVersion = Directory.GetDirectories(refPackRoot)
            .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (refVersion is null)
        {
            yield break;
        }

        foreach (var tfm in new[] { targetFramework, "netstandard2.0", "netstandard2.1", "net10.0" })
        {
            var refDir = Path.Combine(refVersion, "ref", tfm);
            if (Directory.Exists(refDir))
            {
                yield return refDir;
            }
        }
    }

    private static string GetNuGetPackagesRoot()
    {
        var fromEnv = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
        {
            return fromEnv.Trim();
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
    }

    private static string? GuessTargetFrameworkFromPath(string dllPath)
    {
        var path = dllPath.Replace('\\', '/');
        string[] candidates =
        [
            "net10.0", "net9.0", "net8.0", "net7.0", "net6.0", "net5.0",
            "netstandard2.1", "netstandard2.0", "net472", "net462", "net461"
        ];

        foreach (var tfm in candidates)
        {
            if (path.Contains($"/{tfm}/", StringComparison.OrdinalIgnoreCase))
            {
                return tfm;
            }
        }

        return null;
    }
}
