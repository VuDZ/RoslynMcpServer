namespace RoslynMcpServer.Services.Workspace.Models;

/// <summary>
/// The optimizations of the forked BuildHost for the loads of this process, as <see cref="BuildHostOptimizations"/>
/// resolved them from the environment and RoslynMcp.jsonc.
/// </summary>
/// <param name="ReferencesFirst">Part A: the BuildHost builds the projects a project references before it.</param>
/// <param name="EvaluateAheadWorkers">Part B: worker threads that evaluate ahead; 0 when part B is off.</param>
/// <param name="SharedSdkCache">Candidate C: the evaluations of one project share one SDK resolution cache.</param>
internal sealed record BuildHostOptimizationSettings(bool ReferencesFirst, int EvaluateAheadWorkers, bool SharedSdkCache);
