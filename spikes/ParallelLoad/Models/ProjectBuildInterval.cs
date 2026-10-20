namespace ParallelLoad.Models;

internal sealed record ProjectBuildInterval(string FilePath, DateTime PreviousEnd, DateTime Start, DateTime End);
