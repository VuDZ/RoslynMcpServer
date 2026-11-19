namespace TestImpactIlProbe;

/// <summary>One step of a path from a test class to a type.</summary>
internal readonly record struct ReachStep(int Node, ReachReason Reason);
