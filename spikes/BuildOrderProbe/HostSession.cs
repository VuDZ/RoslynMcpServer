using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Execution;

namespace BuildOrderProbe;

/// <summary>
/// One project collection and one build manager session set up like the 5.9.0 BuildHost's ProjectBuildManager:
/// its global properties, no loggers on the collection, design-time builds of Compile, CoreCompile and
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
        // The client passes SolutionDir with a trailing separator; imports such as $(SolutionDir)x.props rely on it.
        var solutionDirectory = Path.GetDirectoryName(solutionPath)!;
        var properties = new Dictionary<string, string>(_hostProperties, StringComparer.OrdinalIgnoreCase)
        {
            ["SolutionDir"] = Path.EndsInDirectorySeparator(solutionDirectory)
                ? solutionDirectory
                : solutionDirectory + Path.DirectorySeparatorChar,
        };
        _collection = new ProjectCollection(properties, loggers: [], ToolsetDefinitionLocations.Default);
        BuildManager.DefaultBuildManager.BeginBuild(new BuildParameters(_collection)
        {
            Loggers = [_counter],
            EnableNodeReuse = false,
        });
    }

    /// <summary>Evaluations the build manager ran inside design-time builds; root evaluations are not counted.</summary>
    public int EvaluationsDuringBuilds => _counter.Count;

    /// <summary>The root evaluation of a project, or null when MSBuild cannot load it.</summary>
    public Project? Evaluate(string path)
    {
        try
        {
            return _collection.LoadProject(path);
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

    private readonly ProjectCollection _collection;
    private readonly EvaluationCounter _counter = new();
}
