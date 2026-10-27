namespace BuildHostFork.Trace;

/// <summary>A project entry of the solution file and whether the workspace holds a project with that path.</summary>
internal sealed record SolutionEntry(string Path, string Kind, bool Loaded);
