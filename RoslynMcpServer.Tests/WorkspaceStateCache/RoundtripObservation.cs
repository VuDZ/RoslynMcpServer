using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services.Workspace.StateCache;
using RoslynMcpServer.Services.Workspace.StateCache.Models;
using Xunit;

namespace RoslynMcpServer.Tests.WorkspaceStateCache;

internal static class RoundtripObservation
{
    public static async Task<string> ReadAsync(Solution solution, CancellationToken cancellationToken)
    {
        var projects = new List<object>();
        foreach (var project in solution.Projects.OrderBy(project => project.FilePath, StringComparer.Ordinal))
        {
            var documents = new List<object>();
            foreach (var (document, kind) in project.Documents.Select(document => ((TextDocument)document, "source"))
                .Concat(project.AdditionalDocuments.Select(document => (document, "additional")))
                .Concat(project.AnalyzerConfigDocuments.Select(document => ((TextDocument)document, "analyzer-config")))
                .OrderBy(pair => pair.Item2).ThenBy(pair => pair.Item1.FilePath, StringComparer.Ordinal))
            {
                var text = await document.GetTextAsync(cancellationToken);
                documents.Add(new
                {
                    Kind = kind, document.FilePath, document.Name, Folders = document.Folders.ToArray(),
                    SourceKind = (document as Document)?.SourceCodeKind.ToString(),
                    TextHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text.ToString()))),
                    CodePage = text.Encoding?.CodePage, Preamble = text.Encoding?.GetPreamble(),
                    ChecksumAlgorithm = text.ChecksumAlgorithm.ToString(), Checksum = text.GetChecksum().ToArray(),
                });
            }
            var options = Assert.IsType<CSharpCompilationOptions>(project.CompilationOptions);
            projects.Add(new
            {
                project.FilePath, project.Name, project.AssemblyName, project.Language, project.OutputFilePath,
                project.OutputRefFilePath, project.DefaultNamespace,
                project.CompilationOutputInfo.AssemblyPath, project.CompilationOutputInfo.GeneratedFilesOutputDirectory,
                Parse = SnapshotOptionFacts.Parse(Assert.IsType<CSharpParseOptions>(project.ParseOptions)),
                // The public equality witness normalizes only the three declared host-owned identities.
                Options = SnapshotOptionFacts.Compilation(project, options.WithSyntaxTreeOptionsProvider(null),
                    SnapshotSigningProfile.IndependentPolicy),
                Documents = documents,
                Edges = project.ProjectReferences.Select(reference => new
                {
                    Target = solution.GetProject(reference.ProjectId)!.FilePath,
                    Aliases = reference.Aliases.ToArray(), reference.EmbedInteropTypes,
                }).OrderBy(reference => reference.Target, StringComparer.Ordinal).ToArray(),
                Metadata = project.MetadataReferences.Cast<PortableExecutableReference>().Select(reference => new
                {
                    reference.FilePath, Kind = reference.Properties.Kind.ToString(),
                    Aliases = reference.Properties.Aliases.ToArray(), reference.Properties.EmbedInteropTypes,
                }).OrderBy(reference => reference.FilePath, StringComparer.Ordinal).ToArray(),
                Analyzers = project.AnalyzerReferences.Select(reference => reference.FullPath).Order(StringComparer.Ordinal).ToArray(),
            });
        }
        return JsonSerializer.Serialize(projects);
    }

    public static void AssertBindings(WorkspaceSnapshot snapshot, IsolatedHydratedSession session)
    {
        Assert.Equal(snapshot.InputPaths.Length, session.InputMap.Count);
        foreach (var input in snapshot.InputPaths)
        {
            var actual = session.InputMap[input.Path];
            Assert.Equal(input.Occurrences.Length, actual.Length);
            for (var index = 0; index < actual.Length; index++)
            {
                var expected = input.Occurrences[index];
                var occurrence = actual[index];
                Assert.Equal(session.Projects[expected.Owner], occurrence.Owner);
                Assert.Equal(expected.Role, occurrence.Role);
                Assert.Equal(expected.RoleSource, occurrence.RoleSource);
                Assert.Equal(expected.ProducerSource, occurrence.ProducerSource);
                Assert.Equal(expected.Producer is null ? null : session.Projects[expected.Producer], occurrence.Producer);
                Assert.Equal(File.Exists(input.Path), occurrence.Present);
                if (expected.DocumentKind is not null)
                {
                    var project = session.Solution.GetProject(occurrence.Owner)!;
                    var documents = expected.DocumentKind switch
                    {
                        "source" => project.Documents.Cast<TextDocument>(),
                        "additional" => project.AdditionalDocuments,
                        _ => project.AnalyzerConfigDocuments,
                    };
                    Assert.Equal(input.Path, Assert.Single(documents, document => document.Id == occurrence.DocumentId).FilePath);
                }
                else
                {
                    Assert.Null(occurrence.DocumentId);
                }
            }
        }
    }

    internal static async Task CaptureAsync(string path, string directory, CancellationToken cancellationToken)
    {
        MsBuildBootstrapper.Register();
        using var workspace = new AdhocWorkspace();
        var descriptors = await new MSBuildProjectLoader(workspace).LoadProjectInfoAsync(
            path, cancellationToken: cancellationToken);
        workspace.AddProjects(descriptors);
        var capture = await OrdinaryWorkspaceCapture.CaptureAsync(workspace.CurrentSolution, descriptors,
            path, Properties(), originalAnalyzerPathsProven: true, cancellationToken);
        Assert.Null(capture.UnsupportedReason);
        var snapshot = Assert.IsType<WorkspaceSnapshot>(capture.Snapshot);
        Assert.Equal(Properties(), snapshot.Properties);
        Assert.All(snapshot.Projects, project =>
        {
            Assert.Equal("Debug", project.EvaluatedProperties.Values.Configuration);
            Assert.Equal("net10.0", project.EvaluatedProperties.Values.TargetFramework);
            Assert.Empty(project.EvaluatedProperties.UnknownProperties);
        });
        await File.WriteAllBytesAsync(Path.Combine(directory, "snapshot.bin"),
            WorkspaceSnapshotCodec.Encode(snapshot), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "ordinary.json"),
            await ReadAsync(workspace.CurrentSolution, cancellationToken), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "ordinary.ids"),
            string.Join("\n", workspace.CurrentSolution.ProjectIds.Select(id => id.Id)
                .Concat(workspace.CurrentSolution.Projects.SelectMany(AllDocuments).Select(document => document.Id.Id))),
            cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "parent.pid"), Environment.ProcessId.ToString(), cancellationToken);
    }

    internal static SnapshotLoadProperties Properties() => new(new(null, null, null), new(null, null, null));

    internal static IEnumerable<TextDocument> AllDocuments(Project project) =>
        project.Documents.Cast<TextDocument>().Concat(project.AdditionalDocuments).Concat(project.AnalyzerConfigDocuments);
}
