using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// The declared analysis scope: which files are compiled, with which references, language version and
/// configuration, and which conditional branches are therefore active. The scope is part of the
/// verdict: a file outside it is not covered, and <see cref="SourceSetAnalysis"/> reports conditional
/// directives (inactive branches) instead of silently analyzing them.
/// </summary>
internal sealed class SourceSetScope
{
    /// <summary>Human-readable scope statement, repeated in every diagnostic of the analysis.</summary>
    public string Description { get; }

    public string AssemblyName { get; }

    public IReadOnlyList<SourceFile> Files { get; }

    public IReadOnlyList<MetadataReference> References { get; }

    /// <summary>Conditional compilation symbols that are defined for this scope.</summary>
    public IReadOnlyList<string> PreprocessorSymbols { get; }

    public LanguageVersion LanguageVersion { get; }

    public OutputKind OutputKind { get; }

    private SourceSetScope(
        string description,
        string assemblyName,
        IReadOnlyList<SourceFile> files,
        IReadOnlyList<MetadataReference> references,
        IReadOnlyList<string> preprocessorSymbols,
        LanguageVersion languageVersion,
        OutputKind outputKind)
    {
        Description = description;
        AssemblyName = assemblyName;
        Files = files;
        References = references;
        PreprocessorSymbols = preprocessorSymbols;
        LanguageVersion = languageVersion;
        OutputKind = outputKind;
    }

    /// <summary>
    /// Scope for in-memory examples: the framework references of the test process, the Debug symbol set
    /// and a library output kind. Used by the counterexample corpus of the checks.
    /// </summary>
    public static SourceSetScope Demo(string description, IEnumerable<SourceFile> files)
    {
        return new SourceSetScope(
            description,
            "SourceStructureDemo",
            files.ToList(),
            MetadataReferenceSets.Runtime(),
            DemoPreprocessorSymbols,
            LanguageVersion.Latest,
            OutputKind.DynamicallyLinkedLibrary);
    }

    /// <summary>
    /// Scope of the production sources of <paramref name="repositoryRoot"/>: the same file set MSBuild
    /// globs for <c>RoslynMcpServer.csproj</c> (solution test and host projects excluded), compiled
    /// against the runtime references plus the compile-only assets of the restore. Global usings are
    /// supplied as a synthetic file, because the compiler normally gets them from the SDK build.
    /// </summary>
    public static SourceSetScope ProductionRepository(string repositoryRoot)
    {
        var files = ProductionSourcePaths(repositoryRoot)
            .Select(SourceFile.ReadFromDisk)
            .ToList();
        files.Add(SourceFile.Create(GlobalUsingsPath, string.Join('\n', ImplicitGlobalUsings)));

        return new SourceSetScope(
            $"production sources of RoslynMcpServer.csproj under the working copy at {repositoryRoot} "
            + $"(Debug; symbols: {string.Join(", ", DemoPreprocessorSymbols)})",
            "RoslynMcpServerSources",
            files,
            MetadataReferenceSets.Production(repositoryRoot),
            DemoPreprocessorSymbols,
            LanguageVersion.Latest,
            OutputKind.ConsoleApplication);
    }

    /// <summary>Returns a copy of this scope with an extra file, used to widen an example with a neighbor.</summary>
    public SourceSetScope WithFile(SourceFile file)
    {
        var files = Files.ToList();
        files.Add(file);
        return new SourceSetScope(Description, AssemblyName, files, References, PreprocessorSymbols, LanguageVersion, OutputKind);
    }

    /// <summary>
    /// Files of the production compilation: every <c>*.cs</c> under the project directory except build
    /// output and the sibling projects that <c>RoslynMcpServer.csproj</c> removes from its own compile set.
    /// Enumerating the tree, rather than listing directories, keeps a new production folder inside the scope.
    /// </summary>
    private static IEnumerable<string> ProductionSourcePaths(string repositoryRoot)
    {
        foreach (var path in Directory.EnumerateFiles(repositoryRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(repositoryRoot, path);
            var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Take(segments.Length - 1).Any(IsExcludedSegment))
            {
                continue;
            }

            yield return path;
        }
    }

    /// <summary>Build output, hidden tooling folders and the sibling projects outside the production compile set.</summary>
    private static bool IsExcludedSegment(string segment) =>
        segment.StartsWith('.')
        || string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase)
        || string.Equals(segment, ExcludedTestProjectDirectory, StringComparison.OrdinalIgnoreCase)
        || string.Equals(segment, ExcludedHostProjectDirectory, StringComparison.OrdinalIgnoreCase);

    private const string GlobalUsingsPath = "obj/RoslynMcpServer.GlobalUsings.g.cs";
    private const string ExcludedTestProjectDirectory = "RoslynMcpServer.Tests";
    private const string ExcludedHostProjectDirectory = "RoslynMcpServer.LifecycleTestHost";

    /// <summary>
    /// Implicit usings the SDK generates for this project (<c>ImplicitUsings</c> is enabled). The
    /// compilation is verified by a zero-error gate, so a missing entry shows up as a diagnostic
    /// instead of a silently unresolved type.
    /// </summary>
    private static readonly string[] ImplicitGlobalUsings =
    {
        "global using global::System;",
        "global using global::System.Collections.Generic;",
        "global using global::System.IO;",
        "global using global::System.Linq;",
        "global using global::System.Net.Http;",
        "global using global::System.Threading;",
        "global using global::System.Threading.Tasks;",
    };

    private static readonly string[] DemoPreprocessorSymbols =
    {
        "DEBUG",
        "TRACE",
        "NET10_0",
        "NET10_0_OR_GREATER",
        "NETCOREAPP",
    };
}
