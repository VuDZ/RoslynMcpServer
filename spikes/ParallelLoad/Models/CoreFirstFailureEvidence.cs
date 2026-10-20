namespace ParallelLoad.Models;

internal sealed record CoreFirstFailureEvidence(string Error, ProcessMetrics Process, string? SamplerFinalizationError);
