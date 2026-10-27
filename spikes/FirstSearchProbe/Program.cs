using System.Diagnostics;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;

internal static class Program
{
    private static async Task Main()
    {
        await MeasureAsync("warmup", 1, 0);
        var samples = new[]
        {
            await MeasureAsync("control", 1, 0),
            await MeasureAsync("one_unreadable_document", 1, 1),
            await MeasureAsync("eight_unreadable_documents", 1, 8),
            await MeasureAsync("three_projects_one_unreadable_each", 3, 1),
        };
        foreach (var sample in samples)
        {
            if (sample.Matches != sample.Projects || sample.LoadAttempts != sample.UnreadableDocuments * 6)
            {
                throw new InvalidOperationException("Unexpected result or retry count: " + sample.Name);
            }
        }
        var missingFile = await MeasureMissingFileAsync();
        Console.WriteLine(JsonSerializer.Serialize(new { samples, missingFile }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<object> MeasureMissingFileAsync()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cs");
        var solution = workspace.CurrentSolution
            .AddProject(projectId, "MissingFile", "MissingFile", LanguageNames.CSharp)
            .AddDocument(
                DocumentId.CreateNewId(projectId),
                "MainViewModel.cs",
                SourceText.From("public class MainViewModel { }"))
            .AddDocument(DocumentInfo.Create(
                documentId,
                "Missing.cs",
                loader: new FileTextLoader(missingPath, null),
                filePath: missingPath));
        var project = solution.GetProject(projectId)!;
        var timer = Stopwatch.StartNew();
        var matches = (await SymbolFinder.FindSourceDeclarationsAsync(
            project,
            "MainViewModel",
            ignoreCase: true,
            SymbolFilter.TypeAndMember)).Count();
        var searchMs = timer.Elapsed.TotalMilliseconds;
        var tree = await solution.GetDocument(documentId)!.GetSyntaxTreeAsync();
        var treeDiagnostics = tree!.GetDiagnostics().Select(diagnostic => diagnostic.Id).ToArray();
        var compilation = await project.GetCompilationAsync();
        var parseDiagnostics = compilation!.GetParseDiagnostics().Select(diagnostic => diagnostic.Id).ToArray();
        if (matches != 1 || File.Exists(missingPath))
        {
            throw new InvalidOperationException("Unexpected missing file probe result.");
        }
        return new { searchMs, matches, treeDiagnostics, parseDiagnostics };
    }

    private static async Task<Sample> MeasureAsync(string name, int projects, int unreadablePerProject)
    {
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var loaders = new List<UnreadableLoader>();
        for (var projectIndex = 0; projectIndex < projects; projectIndex++)
        {
            var projectId = ProjectId.CreateNewId();
            solution = solution.AddProject(projectId, "Probe" + projectIndex, "Probe" + projectIndex, LanguageNames.CSharp)
                .AddDocument(
                    DocumentId.CreateNewId(projectId),
                    "MainViewModel.cs",
                    SourceText.From("public class MainViewModel { }"));
            for (var documentIndex = 0; documentIndex < unreadablePerProject; documentIndex++)
            {
                var loader = new UnreadableLoader();
                loaders.Add(loader);
                solution = solution.AddDocument(DocumentInfo.Create(
                    DocumentId.CreateNewId(projectId),
                    "Unreadable" + documentIndex + ".cs",
                    loader: loader));
            }
        }
        var timer = Stopwatch.StartNew();
        var matches = 0;
        foreach (var project in solution.Projects)
        {
            var symbols = await SymbolFinder.FindSourceDeclarationsAsync(
                project,
                "MainViewModel",
                ignoreCase: true,
                SymbolFilter.TypeAndMember);
            matches += symbols.Count();
        }
        var coldMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        foreach (var project in solution.Projects)
        {
            _ = await SymbolFinder.FindSourceDeclarationsAsync(project, "MainViewModel", true, SymbolFilter.TypeAndMember);
        }
        return new Sample(
            name,
            projects,
            loaders.Count,
            loaders.Sum(loader => loader.Attempts),
            matches,
            coldMs,
            timer.Elapsed.TotalMilliseconds);
    }

    private sealed record Sample(
        string Name,
        int Projects,
        int UnreadableDocuments,
        int LoadAttempts,
        int Matches,
        double ColdMs,
        double WarmMs);

    private sealed class UnreadableLoader : TextLoader
    {
        public int Attempts => _attempts;

        public override Task<TextAndVersion> LoadTextAndVersionAsync(LoadTextOptions options, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempts);
            throw new IOException("Synthetic unreadable source document.");
        }

        private int _attempts;
    }
}
