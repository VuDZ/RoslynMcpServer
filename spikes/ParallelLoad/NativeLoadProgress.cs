using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.MSBuild;

namespace ParallelLoad;

internal sealed class NativeLoadProgress(ConcurrentQueue<string> observations) : IProgress<ProjectLoadProgress>
{
    public void Report(ProjectLoadProgress value)
    {
        if (Path.GetExtension(value.FilePath).Equals(".vcxproj", StringComparison.OrdinalIgnoreCase))
        {
            observations.Enqueue(System.Text.Json.JsonSerializer.Serialize(new
            {
                value.FilePath, Operation = value.Operation.ToString(), ElapsedMs = value.ElapsedTime.TotalMilliseconds,
            }));
        }
    }
}
