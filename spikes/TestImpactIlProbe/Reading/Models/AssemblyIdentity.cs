namespace TestImpactIlProbe;

/// <summary>What a cheap first look at a file tells: enough to drop copies and assemblies without sources.</summary>
internal sealed record AssemblyIdentity(string Path, string Name, Guid Mvid, AssemblyOrigin Origin);
