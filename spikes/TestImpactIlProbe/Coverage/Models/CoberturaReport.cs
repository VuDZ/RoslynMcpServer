namespace TestImpactIlProbe;

/// <summary>
/// Source files of one Cobertura report. A file name is kept as the report writes it: absolute, or relative
/// to one of <paramref name="Sources"/>. The value says whether any line of the file was executed.
/// </summary>
internal sealed record CoberturaReport(IReadOnlyList<string> Sources, IReadOnlyDictionary<string, bool> Files);
