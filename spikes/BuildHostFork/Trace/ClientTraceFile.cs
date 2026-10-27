using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Build.Construction;
using Microsoft.Build.Exceptions;
using Microsoft.CodeAnalysis;

namespace BuildHostFork.Trace;

/// <summary>
/// Writes and reads <c>client.json</c>, the client half of a traced load, next to the build hosts' trace files.
/// </summary>
internal static class ClientTraceFile
{
    /// <summary>The variable the fork BuildHost reads; the client writes its half into the same directory.</summary>
    public const string DirectoryVariable = "ROSLYN_BUILDHOST_TRACE_DIRECTORY";

    public const string FileName = "client.json";

    /// <summary>The trace directory of this process, or null when tracing is off.</summary>
    public static string? TraceDirectory => Environment.GetEnvironmentVariable(DirectoryVariable) is { Length: > 0 } directory
        ? Path.GetFullPath(directory)
        : null;

    // MSBuild types must not be loaded before MSBuildLocator registration; this method is JIT-compiled after it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Write(
        string directory,
        string solutionPath,
        (long Start, long End) load,
        IReadOnlyList<ClientOperation> operations,
        Solution solution,
        int workspaceFailures)
    {
        var loadedPaths = solution.Projects
            .Select(project => project.FilePath)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var trace = new ClientTrace(
            solutionPath,
            Stopwatch.Frequency,
            load.Start,
            load.End,
            operations,
            ReadEntries(solutionPath, loadedPaths),
            solution.ProjectIds.Count,
            solution.Projects.Sum(project => project.DocumentIds.Count),
            workspaceFailures);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, FileName), JsonSerializer.Serialize(trace, _options));
    }

    public static ClientTrace Read(string directory)
        => JsonSerializer.Deserialize<ClientTrace>(File.ReadAllText(Path.Combine(directory, FileName)), _options)
            ?? throw new InvalidDataException($"{FileName} in {directory} is empty.");

    // Solution folders are not projects; every other entry stays, including ones the workspace could not load.
    private static IReadOnlyList<SolutionEntry> ReadEntries(string solutionPath, HashSet<string> loadedPaths)
    {
        try
        {
            return SolutionFile.Parse(solutionPath).ProjectsInOrder
                .Where(project => project.ProjectType != SolutionProjectType.SolutionFolder)
                .Select(project => new SolutionEntry(
                    Path.GetFullPath(project.AbsolutePath),
                    project.ProjectType.ToString(),
                    loadedPaths.Contains(Path.GetFullPath(project.AbsolutePath))))
                .ToArray();
        }
        catch (Exception ex) when (ex is InvalidProjectFileException or IOException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Solution entries were not read: {ex.Message}");
            return [];
        }
    }

    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true };
}
