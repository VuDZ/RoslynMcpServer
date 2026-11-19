namespace TestImpactIlProbe;

/// <summary>An attribute on a method, kept to recognize test methods after all assemblies are read.</summary>
internal readonly record struct MethodAttribute(string Method, TypeName Attribute);
