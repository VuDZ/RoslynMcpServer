using System.Xml;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Execution;

namespace BuildOrderProbe;

/// <summary>
/// One project collection and one build manager session set up like the 5.9.0 BuildHost's ProjectBuildManager:
/// its global properties, no loggers on the collection, its way of evaluating a project, design-time builds of Compile, CoreCompile and
/// DesignTimeMarkupCompilation, and for a multi-targeting project the host's switching of the global TargetFramework
/// on the shared Project, one build per target framework and a restore at the end.
/// </summary>
/// <remarks>
/// Evaluation may run on several threads, one project each; builds and the TargetFramework switches of a Project
/// must stay on one thread at a time, as in the host.
/// </remarks>
internal sealed class HostSession : IDisposable
{
    public HostSession(string solutionPath)
    {
        _collection = CreateCollection(solutionPath);
        BuildManager.DefaultBuildManager.BeginBuild(new BuildParameters(_collection)
        {
            Loggers = [_counter],
            EnableNodeReuse = false,
        });
    }

    /// <summary>Evaluations the build manager ran inside design-time builds; root evaluations are not counted.</summary>
    public int EvaluationsDuringBuilds => _counter.Count;

    /// <summary>A project collection with the host's global properties for the solution and no loggers.</summary>
    public static ProjectCollection CreateCollection(string solutionPath)
    {
        // The client passes SolutionDir with a trailing separator; imports such as $(SolutionDir)x.props rely on it.
        var solutionDirectory = Path.GetDirectoryName(solutionPath)!;
        var properties = new Dictionary<string, string>(_hostProperties, StringComparer.OrdinalIgnoreCase)
        {
            ["SolutionDir"] = Path.EndsInDirectorySeparator(solutionDirectory)
                ? solutionDirectory
                : solutionDirectory + Path.DirectorySeparatorChar,
        };
        return new ProjectCollection(properties, loggers: [], ToolsetDefinitionLocations.Default);
    }

    /// <summary>The root evaluation of a project, or null when MSBuild cannot load it.</summary>
    public Project? Evaluate(string path)
        => Evaluate(_collection, path);

    /// <summary>
    /// The root evaluation of a project in <paramref name="collection"/> the way ProjectBuildManager.LoadProjectCore
    /// makes it, or null when MSBuild cannot load it.
    /// </summary>
    /// <remarks>
    /// Not <see cref="ProjectCollection.LoadProject(string)"/>: that holds the collection's write lock for the whole
    /// evaluation, so evaluations in one collection run one at a time, while the host's path takes the lock only to
    /// register the project. The host also reads the file itself and uses its own load settings.
    /// </remarks>
    public static Project? Evaluate(ProjectCollection collection, string path)
    {
        try
        {
            var loaded = collection.GetLoadedProjects(path);
            if (loaded.Count > 0)
            {
                return loaded.First();
            }

            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var xmlReader = XmlReader.Create(stream, _xmlReaderSettings);
            var xml = ProjectRootElement.Create(xmlReader, collection);
            xml.FullPath = path;
            return new Project(xml, globalProperties: null, toolsVersion: null, collection, _hostLoadSettings);
        }
        catch (Exception e) when (e is InvalidProjectFileException or XmlException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The root evaluation through <see cref="ProjectCollection.LoadProject(string)"/>, which the probe used before;
    /// kept to compare the two paths. Null when MSBuild cannot load the project.
    /// </summary>
    public static Project? EvaluateWithLoadProject(ProjectCollection collection, string path)
    {
        try
        {
            return collection.LoadProject(path);
        }
        catch (InvalidProjectFileException)
        {
            return null;
        }
    }

    /// <summary>Design-time builds of a project, one per target framework, as (target framework, fingerprint).</summary>
    public IReadOnlyList<(string? TargetFramework, string Fingerprint)> Build(Project project)
    {
        var targetFramework = project.GetPropertyValue("TargetFramework");
        var targetFrameworks = project.GetPropertyValue("TargetFrameworks");
        if (!string.IsNullOrEmpty(targetFramework) || string.IsNullOrEmpty(targetFrameworks))
        {
            return [(targetFramework.Length == 0 ? null : targetFramework, BuildInstance(project))];
        }

        // The host splits without trimming and restores the initial global value afterwards; the same here.
        project.GlobalProperties.TryGetValue("TargetFramework", out var initial);
        var results = new List<(string?, string)>();
        foreach (var framework in targetFrameworks.Split(';'))
        {
            project.SetGlobalProperty("TargetFramework", framework);
            project.ReevaluateIfNecessary();
            results.Add((framework, BuildInstance(project)));
        }

        if (initial is null)
        {
            project.RemoveGlobalProperty("TargetFramework");
        }
        else
        {
            project.SetGlobalProperty("TargetFramework", initial);
        }

        project.ReevaluateIfNecessary();
        return results;
    }

    public void Dispose()
    {
        BuildManager.DefaultBuildManager.EndBuild();
        _collection.Dispose();
    }

    private static string BuildInstance(Project project)
    {
        var instance = project.CreateProjectInstance();
        if (!instance.Targets.ContainsKey("Compile") || !instance.Targets.ContainsKey("CoreCompile"))
        {
            return "no Compile or CoreCompile target";
        }

        var targets = new List<string> { "Compile", "CoreCompile" };
        if (instance.Targets.ContainsKey("DesignTimeMarkupCompilation"))
        {
            targets.Add("DesignTimeMarkupCompilation");
        }

        var request = new BuildRequestData(instance, [.. targets], hostServices: null, BuildRequestDataFlags.ProvideProjectStateAfterBuild);
        var result = BuildManager.DefaultBuildManager.BuildRequest(request);
        return BuildFingerprint.Of(result.OverallResult, result.ProjectStateAfterBuild ?? instance);
    }

    // ProjectBuildManager.s_defaultGlobalProperties of the .NET host in 5.9.0.
    private static readonly Dictionary<string, string> _hostProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DesignTimeBuild"] = "True",
        ["NonExistentFile"] = "__NonExistentSubDir__\\__NonExistentFile__",
        ["BuildProjectReferences"] = "False",
        ["BuildingProject"] = "False",
        ["ProvideCommandLineArgs"] = "True",
        ["SkipCompilerExecution"] = "True",
        ["ContinueOnError"] = "ErrorAndContinue",
        ["ShouldUnsetParentConfigurationAndPlatform"] = "False",
    };

    // ProjectBuildManager.s_xmlReaderSettings and the load settings of its LoadProjectCore in 5.9.0.
    private static readonly XmlReaderSettings _xmlReaderSettings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };

    private static readonly ProjectLoadSettings _hostLoadSettings = ProjectLoadSettings.RejectCircularImports
        | ProjectLoadSettings.IgnoreEmptyImports
        | ProjectLoadSettings.IgnoreMissingImports
        | ProjectLoadSettings.IgnoreInvalidImports
        | ProjectLoadSettings.DoNotEvaluateElementsWithFalseCondition
        | ProjectLoadSettings.FailOnUnresolvedSdk;

    private readonly ProjectCollection _collection;
    private readonly EvaluationCounter _counter = new();
}
