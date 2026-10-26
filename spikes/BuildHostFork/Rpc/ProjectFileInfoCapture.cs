using System.Text.Json;
using BuildHostFork.Rpc.Models;
using Microsoft.Build.Construction;

namespace BuildHostFork.Rpc;

/// <summary>Captures the whole netcore host response; workspace load/search remains a separate check.</summary>
internal static class ProjectFileInfoCapture
{
    public static async Task RunAsync(string hostPath, string solutionPath, string outputPath)
    {
        var capture = await CaptureAsync(hostPath, solutionPath).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(capture, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Captured full ProjectFileInfo responses of {capture.Projects.Length} projects; host exited.");
    }

    private static async Task<RpcCapture> CaptureAsync(string hostPath, string solutionPath)
    {
        await using var client = await BuildHostClient.StartAsync(hostPath).ConfigureAwait(false);
        var directory = Path.GetDirectoryName(solutionPath)!;
        var properties = Program.SolutionProperties(solutionPath);
        properties["SolutionDir"] = Path.EndsInDirectorySeparator(directory)
            ? directory
            : directory + Path.DirectorySeparatorChar;
        await client.CallAsync(0, "ConfigureGlobalState", new[] { "C#" }, properties, null, null).ConfigureAwait(false);
        var msbuild = await client.CallAsync(0, "FindBestMSBuild", solutionPath).ConfigureAwait(false);
        var usable = await client.CallAsync(0, "HasUsableMSBuild", solutionPath).ConfigureAwait(false);
        if (!usable.GetBoolean())
        {
            throw new InvalidOperationException("No usable MSBuild for the RPC capture.");
        }

        var pending = new Queue<string>(SolutionFile.Parse(solutionPath).ProjectsInOrder
            .Select(project => project.AbsolutePath).Where(IsSupported));
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var projects = new List<RpcProject>();
        while (pending.TryDequeue(out var path))
        {
            path = Path.GetFullPath(path);
            if (!seen.Add(path))
            {
                continue;
            }

            var language = Path.GetExtension(path).Equals(".vbproj", StringComparison.OrdinalIgnoreCase)
                ? "Visual Basic" : "C#";
            var target = await client.CallAsync(0, "LoadProjectFileAsync", path, language).ConfigureAwait(false);
            var infos = await client.CallAsync(target.GetInt32(), "GetProjectFileInfosAsync").ConfigureAwait(false);
            var diagnostics = await client.CallAsync(target.GetInt32(), "GetDiagnosticLogItems").ConfigureAwait(false);
            if (infos.ValueKind != JsonValueKind.Array || infos.GetArrayLength() == 0
                || diagnostics.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException($"Incomplete RPC response for {path}.");
            }

            projects.Add(new RpcProject(path, infos, diagnostics));
            foreach (var info in infos.EnumerateArray())
            {
                foreach (var reference in info.GetProperty("projectReferences").EnumerateArray())
                {
                    var referencePath = Path.GetFullPath(reference.GetProperty("path").GetString()!, Path.GetDirectoryName(path)!);
                    if (IsSupported(referencePath))
                    {
                        pending.Enqueue(referencePath);
                    }
                }
            }
        }

        if (projects.Count == 0)
        {
            throw new InvalidDataException("No C# or VB projects were captured.");
        }

        return new RpcCapture(hostPath, msbuild, [.. projects]);
    }

    private static bool IsSupported(string path)
        => Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path).Equals(".vbproj", StringComparison.OrdinalIgnoreCase);
}
