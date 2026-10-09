using System.Collections;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Execution;
using Microsoft.Build.Logging;
using Microsoft.CodeAnalysis.MSBuild;

namespace RoslynMcpServer.Tests.WorkspaceStateCache;

/// <summary>Inspects the existing None channel without changing production provenance or cache eligibility.</summary>
internal static class RawImportEvidenceCapture
{
    public static async Task CaptureAsync(string projectPath, string outputDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        using var workspace = MSBuildWorkspace.Create();
        var logger = new BinaryLogger
        {
            Parameters = Path.Combine(outputDirectory, "capture.binlog") + ";ProjectImports=None",
            Verbosity = LoggerVerbosity.Normal,
        };
        var project = await workspace.OpenProjectAsync(projectPath, msbuildLogger: logger,
            cancellationToken: cancellationToken);
        var symbolFound = (await project.GetCompilationAsync(cancellationToken))?
            .GetTypeByMetadataName("RoslynMcpPlugin.SamplePlugin") is not null;
        await WriteAsync(Path.Combine(outputDirectory, "workspace.json"), new
        {
            Utc = DateTimeOffset.UtcNow,
            Pid = Environment.ProcessId,
            LoggerParameters = "ProjectImports=None",
            LoggerVerbosity = "Normal",
            MsBuildAssembly = typeof(BinaryLogger).Assembly.GetName().Version?.ToString(),
            MsBuildProduct = System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(BinaryLogger).Assembly.Location).ProductVersion,
            Roslyn = typeof(MSBuildWorkspace).Assembly.GetName().Version?.ToString(),
            SamplePluginSymbolFound = symbolFound,
            Projects = workspace.CurrentSolution.Projects.Select(item => new { item.Name, item.FilePath }),
            Diagnostics = workspace.Diagnostics.Select(item => new { Kind = item.Kind.ToString(), item.Message }),
        }, cancellationToken);
        var paths = Directory.GetFiles(outputDirectory, "*.binlog").Order().ToArray();
        if (paths.Length == 0 || paths.Sum(path => new FileInfo(path).Length) > 32 * 1024 * 1024)
        {
            throw new InvalidOperationException("The raw log inventory is absent or exceeds the bounded experiment budget.");
        }
        foreach (var path in paths)
        {
            await ReplayAsync(path, cancellationToken);
        }
    }

    private static async Task ReplayAsync(string path, CancellationToken cancellationToken)
    {
        var events = new List<object>();
        var counts = new SortedDictionary<string, int>();
        var replay = new BinaryLogReplayEventSource();
        replay.AnyEventRaised += (_, args) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var kind = args.GetType().Name;
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
            if (counts.Values.Sum() > 100000)
            {
                throw new InvalidOperationException("The raw event budget was exceeded.");
            }
            var context = args.BuildEventContext;
            object? values = args switch
            {
                ProjectImportedEventArgs import => new
                {
                    import.ProjectFile, import.ImportedProjectFile, import.UnexpandedProject,
                    import.ImportIgnored, import.Message,
                },
                ProjectEvaluationStartedEventArgs evaluation => new { evaluation.ProjectFile },
                ProjectEvaluationFinishedEventArgs evaluation => new
                {
                    evaluation.ProjectFile,
                    Properties = ReadProperties(evaluation.Properties),
                    Globals = ReadProperties(evaluation.GlobalProperties),
                },
                ProjectStartedEventArgs started => new
                {
                    started.ProjectFile, started.ProjectId,
                    PropertyEntryTypes = started.Properties?.Cast<object>().Select(value => value.GetType().FullName).Distinct(),
                    Properties = ReadProperties(started.Properties),
                    Globals = started.GlobalProperties?.Where(pair => _propertyNames.Contains(pair.Key))
                        .ToDictionary(pair => pair.Key, pair => pair.Value),
                },
                TaskParameterEventArgs parameter when _propertyNames.Contains(parameter.ItemType) => new
                {
                    parameter.ItemType, Kind = parameter.Kind.ToString(),
                    Items = parameter.Items?.Cast<object>().Select(item => item is ITaskItem taskItem ? taskItem.ItemSpec : item.ToString()),
                },
                _ => null,
            };
            if (values is not null)
            {
                events.Add(new
                {
                    Kind = kind,
                    Context = context is null ? null : new
                    {
                        context.NodeId, context.ProjectContextId, context.ProjectInstanceId,
                        context.EvaluationId, context.SubmissionId,
                    },
                    Values = values,
                });
            }
        };
        using var stream = File.OpenRead(path);
        replay.Replay(stream, cancellationToken);
        await WriteAsync(path + ".json", new { File = Path.GetFileName(path), Bytes = new FileInfo(path).Length, Counts = counts, Events = events },
            cancellationToken);
    }

    private static SortedDictionary<string, string> ReadProperties(IEnumerable? values)
    {
        var result = new SortedDictionary<string, string>();
        if (values is null)
        {
            return result;
        }
        foreach (var value in values)
        {
            if (value is DictionaryEntry entry && entry.Key is string name && _propertyNames.Contains(name))
            {
                result[name] = entry.Value?.ToString() ?? "";
            }
            else if (value is KeyValuePair<string, string> pair && _propertyNames.Contains(pair.Key))
            {
                result[pair.Key] = pair.Value;
            }
            else if (value is ProjectPropertyInstance property && _propertyNames.Contains(property.Name))
            {
                result[property.Name] = property.EvaluatedValue;
            }
        }
        return result;
    }

    private static Task WriteAsync(string path, object value, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

    // Arbitrary evaluated environment properties can contain secrets. Keep only path/instance facts needed by this spike.
    private static readonly HashSet<string> _propertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Configuration", "Platform", "TargetFramework", "TargetFrameworks", "MSBuildAllProjects", "MSBuildProjectFullPath",
        "MSBuildProjectExtensionsPath", "BaseIntermediateOutputPath", "IntermediateOutputPath", "ProjectAssetsFile",
        "NuGetPackageRoot", "NuGetProjectStyle", "RestoreConfigFile",
        "RestorePackagesWithLockFile", "NuGetLockFilePath", "RestoreLockedMode",
        "DirectoryBuildPropsPath", "DirectoryBuildTargetsPath", "MSBuildSDKsPath", "NETCoreSdkVersion", "MSBuildVersion",
    };
}
