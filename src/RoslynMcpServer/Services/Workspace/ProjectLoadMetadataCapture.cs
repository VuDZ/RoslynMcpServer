using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using RoslynMcpServer.Services.Workspace.Models;

namespace RoslynMcpServer.Services.Workspace;

internal sealed class ProjectLoadMetadataCapture : IProgress<ProjectLoadProgress>
{
    internal const string BinlogName = "roslyn-mcp-workspace.binlog";

    public void Report(ProjectLoadProgress progress)
    {
        if (progress.Operation != ProjectLoadOperation.Resolve
            || InputPathCanon.TryCanonicalize(progress.FilePath) is not { } path)
        {
            return;
        }

        if (!_frameworksByPath.TryGetValue(path, out var frameworks))
        {
            frameworks = [];
            _frameworksByPath.Add(path, frameworks);
        }

        frameworks.Add(progress.TargetFramework);
    }

    internal ImmutableDictionary<ProjectId, ProjectLoadMetadata> Bind(
        Solution solution,
        string captureDirectory,
        CancellationToken cancellationToken)
    {
        var outputTypes = ReadProjectOutputTypes(captureDirectory, cancellationToken)
            .ToLookup(entry => entry.ProjectFile, InputPathCanon.Comparer);
        var metadata = ImmutableDictionary.CreateBuilder<ProjectId, ProjectLoadMetadata>();
        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (InputPathCanon.TryCanonicalize(project.FilePath) is not { } path
                || !_frameworksByPath.TryGetValue(path, out var frameworks))
            {
                continue;
            }

            // Roslyn's MSBuildProjectLoader uses the project file stem and adds "(<TFM>)" when
            // one file yields multiple projects. File path alone can select the wrong instance.
            // See docs/architecture-review/findings.md#ar-09.
            var projectStem = Path.GetFileNameWithoutExtension(path);
            var matches = frameworks
                .Where(framework =>
                {
                    var name = frameworks.Count > 1 && !string.IsNullOrWhiteSpace(framework)
                        ? $"{projectStem}({framework})"
                        : projectStem;
                    return StringComparer.Ordinal.Equals(project.Name, name);
                })
                .Take(2)
                .ToArray();
            if (matches.Length == 1)
            {
                var framework = matches[0];
                var entries = outputTypes[path]
                    .Where(entry => StringComparer.Ordinal.Equals(framework, entry.TargetFramework))
                    .ToArray();
                var outputs = entries.Select(entry => entry.OutputType).Distinct(StringComparer.Ordinal).Take(2).ToArray();
                var codePages = entries.Select(entry => entry.CodePage).Distinct().Take(2).ToArray();
                metadata.Add(project.Id, new ProjectLoadMetadata(framework, outputs.Length == 1 ? outputs[0] : null)
                {
                    CodePage = codePages.Length switch
                    {
                        0 => ReadLoaderCodePage(project),
                        1 => codePages[0],
                        _ => null,
                    },
                });
            }
        }

        return metadata.ToImmutable();
    }

    private static int? ReadLoaderCodePage(Project project)
    {
        // BuildHost-net472 has no project sidecar. Roslyn creates every source loader with the
        // same project encoding before detecting a file's BOM. See docs/project-review-2026-11-25/task-36-p1-framework-bom-free-writes-hi.md.
        // The pinned workspace does not expose the original loader on Document's public API.
        if (project.Documents.FirstOrDefault() is not { } document
            || _documentState?.GetValue(document) is not { } state
            || _textSource?.GetValue(state) is not { } source
            || _textLoader?.GetValue(source) is not FileTextLoader loader)
        {
            return null;
        }

        return loader.DefaultEncoding?.CodePage ?? 0;
    }

    private static IEnumerable<OutputTypeEntry> ReadProjectOutputTypes(
        string captureDirectory,
        CancellationToken cancellationToken)
    {
        // Roslyn's compilation options can contain a default target kind. Use only the values
        // emitted by the BuildHost for the ProjectInstances that it returned.
        foreach (var path in GetOutputTypeLogPaths(captureDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var entry in ReadOutputTypeLog(path))
            {
                if (entry is not null && InputPathCanon.TryCanonicalize(entry.ProjectFile) is { } projectFile)
                {
                    var outputType = string.IsNullOrWhiteSpace(entry.OutputType)
                        || entry.OutputType.Contains('\r')
                        || entry.OutputType.Contains('\n')
                            ? null
                            : entry.OutputType;
                    yield return entry with { ProjectFile = projectFile, OutputType = outputType };
                }
            }
        }
    }

    private static string[] GetOutputTypeLogPaths(string directory)
    {
        try
        {
            return Directory.GetFiles(directory, "roslyn-mcp-workspace*.binlog.projects.json");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static OutputTypeEntry?[] ReadOutputTypeLog(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<OutputTypeEntry?[]>(stream) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private sealed record OutputTypeEntry(string ProjectFile, string? TargetFramework, string? OutputType, int? CodePage);

    private readonly Dictionary<string, List<string?>> _frameworksByPath = new(InputPathCanon.Comparer);
    private static readonly PropertyInfo? _documentState =
        typeof(TextDocument).GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? _textSource = _documentState?.PropertyType.GetField("TextAndVersionSource");
    private static readonly PropertyInfo? _textLoader = _textSource?.FieldType.GetProperty("TextLoader");
}
