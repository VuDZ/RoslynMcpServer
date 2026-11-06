using System.Security;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynMcpServer.Services.Testing.Models;

namespace RoslynMcpServer.Tests.Testing;

internal sealed class ProjectImpactFixture : IDisposable
{
    internal string Root { get; }
    internal string SolutionPath { get; }
    internal string AppDirectory { get; }
    internal string DataPath { get; }
    internal string ObjInputPath { get; }
    internal string SharedPath { get; }
    internal string SpecsPath { get; }
    internal ProjectId Library { get; } = ProjectId.CreateNewId();
    internal ProjectId App { get; } = ProjectId.CreateNewId();
    internal ProjectId Specs { get; } = ProjectId.CreateNewId();
    internal ProjectId Isolated { get; } = ProjectId.CreateNewId();
    internal Solution Solution { get; private set; }

    internal ProjectImpactFixture(bool insideArtifacts = false, bool solutionInBuild = false)
    {
        _temporaryRoot = Path.Combine(Path.GetTempPath(), "RoslynMcpImpact-" + Guid.NewGuid().ToString("N"));
        Root = insideArtifacts ? Path.Combine(_temporaryRoot, "artifacts", "repo") : _temporaryRoot;
        SolutionPath = Path.Combine(Root, solutionInBuild ? "build" : "", "Repo.slnx");
        AppDirectory = Path.Combine(Root, "Library", "App");
        DataPath = Path.Combine(Root, "Library", "data.json");
        ObjInputPath = Path.Combine(Root, "Library", "obj", "manual.json");
        SharedPath = Path.Combine(Root, "linked", "Shared.cs");
        SpecsPath = Path.Combine(Root, "Specs", "Cases.cs");
        Solution = _workspace.CurrentSolution;
        AddProject(Library, "Library", Path.Combine(Root, "Library"));
        AddProject(App, "App", AppDirectory);
        AddProject(Specs, "Specs", Path.Combine(Root, "Specs"));
        AddProject(Isolated, "Isolated", Path.Combine(Root, "Isolated"));
        Solution = Solution.AddProjectReference(App, new ProjectReference(Library))
            .AddProjectReference(Specs, new ProjectReference(App));
        AddSource(Specs, SpecsPath, "class FactAttribute : System.Attribute {} class Cases { [Fact] void Case() {} }");
        AddSource(Isolated, Path.Combine(Root, "Isolated", "Cases.cs"),
            "class TestAttribute : System.Attribute {} class Cases { [Test] void Case() {} }");
        AddSource(Library, SharedPath, "public class Shared {}");
        AddSource(Isolated, SharedPath, "public class Shared {}");
        AddAdditional(Library, DataPath);
        AddAdditional(Library, ObjInputPath);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        Directory.Delete(_temporaryRoot, recursive: true);
    }

    internal Task<ProjectImpactResult> MapAsync(params string[] paths) => MapWithStateAsync(false, paths);

    internal Task<ProjectImpactResult> MapWithStateAsync(bool stale, params string[] paths) =>
        MapWithStateAsync(stale, stale, paths);

    internal Task<ProjectImpactResult> MapWithStateAsync(bool stale, bool graphFileStale, params string[] paths) =>
        ProjectImpactMapper.MapAsync(Solution, BuildMap(), paths, SolutionPath, stale, graphFileStale, CancellationToken.None);

    internal WorkspaceInputMap BuildMap() =>
        WorkspaceInputMapBuilder.Build(Solution, Guid.NewGuid(), SolutionPath, loadGraphComplete: true);

    internal void AddAdditional(ProjectId project, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}");
        Solution = Solution.AddAdditionalDocument(
            DocumentId.CreateNewId(project), Path.GetFileName(path), "{}", filePath: path);
    }

    internal void ReplaceSpecs(string source)
    {
        var document = Solution.GetProject(Specs)!.Documents.Single();
        Solution = Solution.WithDocumentText(document.Id, SourceText.From(source));
    }

    internal void AddProjectReference(ProjectId project, ProjectId dependency) =>
        Solution = Solution.AddProjectReference(project, new ProjectReference(dependency));

    internal void ReplaceSource(ProjectId project, string path, string source)
    {
        var document = Solution.GetDocumentIdsWithFilePath(path).FirstOrDefault(id => id.ProjectId == project);
        if (document is null)
        {
            AddSource(project, path, source);
        }
        else
        {
            File.WriteAllText(path, source);
            Solution = Solution.WithDocumentText(document, SourceText.From(source));
        }
    }

    internal void WriteBuildProjects()
    {
        foreach (var project in Solution.Projects)
        {
            var sources = project.Documents.Select(document =>
                "<Compile Include=\"" + SecurityElement.Escape(document.FilePath!) + "\" />");
            var additional = project.AdditionalDocuments.Select(document =>
                "<AdditionalFiles Include=\"" + SecurityElement.Escape(document.FilePath!) + "\" />");
            var references = project.ProjectReferences.Select(reference =>
                "<ProjectReference Include=\"" + SecurityElement.Escape(Solution.GetProject(reference.ProjectId)!.FilePath!) + "\" />");
            File.WriteAllText(project.FilePath!, $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                  </PropertyGroup>
                  <ItemGroup>{{string.Join("", sources.Concat(additional).Concat(references))}}</ItemGroup>
                </Project>
                """);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(SolutionPath)!);
        File.WriteAllText(SolutionPath, "<Solution>" + string.Join("", Solution.Projects.Select(project =>
            "<Project Path=\"" + SecurityElement.Escape(Path.GetRelativePath(Path.GetDirectoryName(SolutionPath)!, project.FilePath!)) + "\" />")) + "</Solution>");
    }

    private void AddProject(ProjectId id, string name, string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name + ".csproj");
        File.WriteAllText(path, "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        Solution = Solution.AddProject(ProjectInfo.Create(
            id, VersionStamp.Create(), name, name, LanguageNames.CSharp, filePath: path))
            .AddMetadataReference(id, MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
    }

    private void AddSource(ProjectId project, string path, string source)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
        Solution = Solution.AddDocument(DocumentId.CreateNewId(project), Path.GetFileName(path),
            SourceText.From(source), filePath: path);
    }

    private readonly AdhocWorkspace _workspace = new();
    private readonly string _temporaryRoot;
}
