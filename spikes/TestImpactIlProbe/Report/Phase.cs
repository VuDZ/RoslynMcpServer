namespace TestImpactIlProbe;

/// <summary>One stage of a run: how long it took and the process memory after it.</summary>
internal sealed record Phase(string Name, long Ms, MemorySnapshot Memory);
