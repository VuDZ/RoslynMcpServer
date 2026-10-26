using System.Diagnostics;
using BuildOrderProbe.Models;
using Microsoft.Build.Evaluation;

namespace BuildOrderProbe;

/// <summary>
/// Reproduces the late evaluation that GetAllGlobs performs without RecordEvaluatedItemElements.
/// A build changes a condition's input without making the evaluated Project dirty.
/// </summary>
internal static class RecordItemElementsProbe
{
    public static RecordItemElementsRun Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RoslynBuildHostGlobsCheck-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "before"));
        Directory.CreateDirectory(Path.Combine(directory, "after"));
        File.WriteAllText(Path.Combine(directory, "before", "Before.cs"), "class Before { }");
        File.WriteAllText(Path.Combine(directory, "after", "After.cs"), "class After { }");
        var path = Path.Combine(directory, "Probe.csproj");
        File.WriteAllText(path, """
            <Project>
              <ItemGroup>
                <Compile Include="before/**/*.cs" />
                <Compile Include="after/**/*.cs" Condition="Exists('marker.txt')" />
              </ItemGroup>
              <Target Name="CoreCompile" />
              <Target Name="Compile" DependsOnTargets="CoreCompile">
                <WriteLinesToFile File="$(MSBuildProjectDirectory)/marker.txt" Lines="ready" Overwrite="true" />
              </Target>
            </Project>
            """);
        var original = Read(path, recordElements: false);
        var recorded = Read(path, recordElements: true);
        if (!original.Documents.SequenceEqual(recorded.Documents)
            || original.Globs.Length != 2
            || recorded.Globs.Length != 1
            || original.DirtyAfterBuild
            || recorded.DirtyAfterBuild)
        {
            throw new InvalidOperationException("The fixture did not reproduce the expected glob difference.");
        }

        return new RecordItemElementsRun(
            FileVersionInfo.GetVersionInfo(typeof(Project).Assembly.Location).FileVersion,
            original.Documents,
            original.Globs,
            recorded.Globs,
            original.DirtyAfterBuild,
            recorded.DirtyAfterBuild);
    }

    private static (string[] Documents, string[] Globs, bool DirtyAfterBuild) Read(string path, bool recordElements)
    {
        File.Delete(Path.Combine(Path.GetDirectoryName(path)!, "marker.txt"));
        using var collection = new ProjectCollection();
        var settings = recordElements ? ProjectLoadSettings.RecordEvaluatedItemElements : ProjectLoadSettings.Default;
        var project = new Project(path, new Dictionary<string, string> { ["DesignTimeBuild"] = "true" }, null, collection, settings);
        var instance = project.CreateProjectInstance();
        if (!instance.Build("Compile", []))
        {
            throw new InvalidOperationException("The fixture build failed.");
        }

        var dirty = project.IsDirty;
        var documents = instance.GetItems("Compile").Select(item => item.EvaluatedInclude).ToArray();
        var globs = project.GetAllGlobs().SelectMany(glob => glob.IncludeGlobs).Order(StringComparer.Ordinal).ToArray();
        return (documents, globs, dirty);
    }
}
