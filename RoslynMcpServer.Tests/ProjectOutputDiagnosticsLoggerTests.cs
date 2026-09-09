using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public class ProjectOutputDiagnosticsLoggerTests
{
    [Fact]
    public void Collect_reports_existing_output_file_and_generated_directory()
    {
        using var fixture = new ProjectFixture();
        var existingOutputFile = fixture.CreateFile("Existing.dll");
        var existingGeneratedDir = fixture.CreateDirectory("generated");

        var solution = fixture.AddProject(
            name: "Existing",
            assemblyName: "Existing",
            outputFilePath: existingOutputFile,
            generatedFilesOutputDirectory: existingGeneratedDir,
            analyzerReferences: Array.Empty<AnalyzerReference>());

        var project = solution.Projects.Single(p => p.Name == "Existing");
        var results = ProjectOutputDiagnosticsLogger.Collect(project.Solution);
        var diagnostic = results.Single(d => d.ProjectName == "Existing");

        Assert.Equal(existingOutputFile, diagnostic.OutputFilePath);
        Assert.True(diagnostic.OutputFileExists);
        Assert.NotNull(diagnostic.OutputFileLastWriteTimeUtc);
        Assert.Equal(existingGeneratedDir, diagnostic.GeneratedFilesOutputDirectory);
        Assert.True(diagnostic.GeneratedFilesOutputDirectoryExists);
        Assert.Empty(diagnostic.AnalyzerReferences);
    }

    [Fact]
    public void Collect_reports_missing_output_file_and_missing_generated_directory()
    {
        using var fixture = new ProjectFixture();
        var missingOutputFile = Path.Combine(fixture.RootDirectory, "artifacts", "Missing", "Debug", "Missing.dll");
        var missingGeneratedDir = Path.Combine(fixture.RootDirectory, "artifacts", "Missing", "Debug", "generated");

        var solution = fixture.AddProject(
            name: "Missing",
            assemblyName: "Missing",
            outputFilePath: missingOutputFile,
            generatedFilesOutputDirectory: missingGeneratedDir,
            analyzerReferences: Array.Empty<AnalyzerReference>());

        var project = solution.Projects.Single(p => p.Name == "Missing");
        var diagnostic = ProjectOutputDiagnosticsLogger.Collect(project.Solution).Single(d => d.ProjectName == "Missing");

        Assert.Equal(missingOutputFile, diagnostic.OutputFilePath);
        Assert.False(diagnostic.OutputFileExists);
        Assert.Null(diagnostic.OutputFileLastWriteTimeUtc);
        Assert.False(diagnostic.GeneratedFilesOutputDirectoryExists);
    }

    [Fact]
    public void Collect_reports_analyzer_reference_existence_per_reference()
    {
        using var fixture = new ProjectFixture();
        var existingAnalyzerDll = fixture.CreateFile("Analyzer.dll");
        var missingAnalyzerDll = Path.Combine(fixture.RootDirectory, "artifacts", "Analyzer", "Debug", "Analyzer.dll");

        var analyzerReferences = new AnalyzerReference[]
        {
            new FakeAnalyzerReference("Analyzer (present)", existingAnalyzerDll),
            new FakeAnalyzerReference("Analyzer (stale)", missingAnalyzerDll),
        };

        var solution = fixture.AddProject(
            name: "Consumer",
            assemblyName: "Consumer",
            outputFilePath: fixture.CreateFile("Consumer.dll"),
            generatedFilesOutputDirectory: null,
            analyzerReferences: analyzerReferences);

        var project = solution.Projects.Single(p => p.Name == "Consumer");
        var diagnostic = ProjectOutputDiagnosticsLogger.Collect(project.Solution).Single(d => d.ProjectName == "Consumer");

        Assert.Equal(2, diagnostic.AnalyzerReferences.Count);

        var present = diagnostic.AnalyzerReferences.Single(a => a.Display == "Analyzer (present)");
        Assert.True(present.Exists);
        Assert.Equal(existingAnalyzerDll, present.FullPath);
        Assert.NotNull(present.LastWriteTimeUtc);

        var stale = diagnostic.AnalyzerReferences.Single(a => a.Display == "Analyzer (stale)");
        Assert.False(stale.Exists);
        Assert.Equal(missingAnalyzerDll, stale.FullPath);
        Assert.Null(stale.LastWriteTimeUtc);
    }

    [Fact]
    public void Log_writes_one_information_line_per_project_and_per_analyzer_reference()
    {
        using var fixture = new ProjectFixture();
        var analyzerReferences = new AnalyzerReference[]
        {
            new FakeAnalyzerReference("Analyzer (present)", fixture.CreateFile("Analyzer.dll")),
            new FakeAnalyzerReference("Analyzer (stale)", Path.Combine(fixture.RootDirectory, "missing.dll")),
        };

        var solution = fixture.AddProject(
            name: "Consumer",
            assemblyName: "Consumer",
            outputFilePath: fixture.CreateFile("Consumer.dll"),
            generatedFilesOutputDirectory: fixture.CreateDirectory("generated"),
            analyzerReferences: analyzerReferences);

        var logger = new CapturingLogger();
        ProjectOutputDiagnosticsLogger.Log(solution, logger);

        // One line for the project itself, plus one line per analyzer reference.
        Assert.Contains(logger.Messages, m => m.Contains("project=Consumer", StringComparison.Ordinal) && m.Contains("outputFileExists=True", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, m => m.Contains("Analyzer (present)", StringComparison.Ordinal) && m.Contains("exists=True", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, m => m.Contains("Analyzer (stale)", StringComparison.Ordinal) && m.Contains("exists=False", StringComparison.Ordinal));
        Assert.Equal(3, logger.Messages.Count);
    }

    private sealed class FakeAnalyzerReference : AnalyzerReference
    {
        public FakeAnalyzerReference(string display, string? fullPath)
        {
            Display = display;
            FullPath = fullPath;
        }

        public override string Display { get; }

        public override string? FullPath { get; }

        public override object Id => Display;

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages() => ImmutableArray<DiagnosticAnalyzer>.Empty;

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language) => ImmutableArray<DiagnosticAnalyzer>.Empty;
    }

    /// <summary>Minimal <see cref="ILogger"/> capturing formatted messages for assertion without a mocking framework.</summary>
    private sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    /// <summary>Builds an <see cref="AdhocWorkspace"/>-backed project with a temp directory for file-existence fixtures.</summary>
    private sealed class ProjectFixture : IDisposable
    {
        private readonly AdhocWorkspace _workspace = new();

        public ProjectFixture()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "RoslynMcpServer.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootDirectory);
        }

        public string RootDirectory { get; }

        public string CreateFile(string relativeName)
        {
            var path = Path.Combine(RootDirectory, relativeName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[] { 0 });
            return path;
        }

        public string CreateDirectory(string relativeName)
        {
            var path = Path.Combine(RootDirectory, relativeName);
            Directory.CreateDirectory(path);
            return path;
        }

        public Solution AddProject(
            string name,
            string assemblyName,
            string? outputFilePath,
            string? generatedFilesOutputDirectory,
            IReadOnlyList<AnalyzerReference> analyzerReferences)
        {
            var projectId = ProjectId.CreateNewId(debugName: name);
            var compilationOutputInfo = new CompilationOutputInfo()
                .WithAssemblyPath(outputFilePath)
                .WithGeneratedFilesOutputDirectory(generatedFilesOutputDirectory);

            var projectInfo = ProjectInfo.Create(
                    projectId,
                    VersionStamp.Create(),
                    name,
                    assemblyName,
                    LanguageNames.CSharp,
                    filePath: null,
                    outputFilePath: outputFilePath)
                .WithCompilationOutputInfo(compilationOutputInfo)
                .WithAnalyzerReferences(analyzerReferences);

            return _workspace.CurrentSolution.AddProject(projectInfo);
        }

        public void Dispose()
        {
            _workspace.Dispose();
            try
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup; leftover temp files do not affect other tests.
            }
        }
    }
}
