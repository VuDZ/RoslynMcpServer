using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ParallelLoad;

internal static class SolutionObservation
{
    public static SortedDictionary<string, SortedDictionary<string, string>> Capture(Solution solution)
    {
        var projects = solution.Projects.ToDictionary(project => project.Id, project => ProjectMerger.Key(project.FilePath, project.Name));
        var result = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var project in solution.Projects)
        {
            var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
            Add(fields, "Language", project.Language);
            Add(fields, "AssemblyName", project.AssemblyName);
            Add(fields, "OutputFilePath", project.OutputFilePath);
            Add(fields, "OutputRefFilePath", project.OutputRefFilePath);
            Add(fields, "CompilationOutputInfo.AssemblyPath", project.CompilationOutputInfo.AssemblyPath);
            Add(fields, "DefaultNamespace", project.DefaultNamespace);
            AddDocuments(fields, "Documents", project.Documents.Select(document => document.FilePath));
            AddDocuments(fields, "AdditionalDocuments", project.AdditionalDocuments.Select(document => document.FilePath));
            AddDocuments(fields, "AnalyzerConfigDocuments", project.AnalyzerConfigDocuments.Select(document => document.FilePath));
            Add(fields, "MetadataReferences", project.MetadataReferences.Select(reference => JsonSerializer.Serialize(new
            {
                Path = (reference as PortableExecutableReference)?.FilePath ?? reference.Display,
                Aliases = reference.Properties.Aliases.ToArray(),
                reference.Properties.EmbedInteropTypes,
                reference.Properties.Kind,
            })).Order(StringComparer.Ordinal).ToArray());
            Add(fields, "AnalyzerReferences", project.AnalyzerReferences.Select(reference => reference.FullPath).Order(StringComparer.Ordinal).ToArray());
            Add(fields, "ProjectReferences", project.ProjectReferences.Select(reference => JsonSerializer.Serialize(new
            {
                Target = projects[reference.ProjectId],
                Aliases = reference.Aliases.ToArray(),
                reference.EmbedInteropTypes,
            })).Order(StringComparer.Ordinal).ToArray());
            AddOptions(fields, project);
            if (!result.TryAdd(projects[project.Id], fields))
            {
                throw new InvalidOperationException($"Duplicate project key: {projects[project.Id]}");
            }
        }

        return result;
    }

    private static void AddOptions(SortedDictionary<string, string> fields, Project project)
    {
        var compilation = project.CompilationOptions;
        Add(fields, "CompilationOptions.OutputKind", compilation?.OutputKind);
        Add(fields, "CompilationOptions.Platform", compilation?.Platform);
        Add(fields, "CompilationOptions.OptimizationLevel", compilation?.OptimizationLevel);
        Add(fields, "CompilationOptions.CheckOverflow", compilation?.CheckOverflow);
        Add(fields, "CompilationOptions.SpecificDiagnosticOptions", compilation?.SpecificDiagnosticOptions.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray());
        Add(fields, "CompilationOptions.NullableContextOptions", (compilation as CSharpCompilationOptions)?.NullableContextOptions);
        Add(fields, "CompilationOptions.AllowUnsafe", (compilation as CSharpCompilationOptions)?.AllowUnsafe);
        Add(fields, "ParseOptions.LanguageVersion", (project.ParseOptions as CSharpParseOptions)?.LanguageVersion);
        Add(fields, "ParseOptions.PreprocessorSymbolNames", project.ParseOptions?.PreprocessorSymbolNames.ToArray());
        Add(fields, "ParseOptions.DocumentationMode", project.ParseOptions?.DocumentationMode);
        Add(fields, "ParseOptions.Features", project.ParseOptions?.Features.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray());
    }

    private static void AddDocuments(SortedDictionary<string, string> fields, string category, IEnumerable<string?> paths)
    {
        var ordered = paths.ToArray();
        Add(fields, category + ".Ordered", ordered);
        Add(fields, category + ".Set", ordered.Distinct().Order(StringComparer.Ordinal).ToArray());
    }

    private static void Add<T>(SortedDictionary<string, string> fields, string category, T value) =>
        fields.Add(category, JsonSerializer.Serialize(value));
}
