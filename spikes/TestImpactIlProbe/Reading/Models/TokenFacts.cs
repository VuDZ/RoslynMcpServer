namespace TestImpactIlProbe;

/// <summary>What a metadata token used in a method body refers to. Decoded once per token and assembly.</summary>
internal sealed class TokenFacts
{
    public static TokenFacts Empty { get; } = new();

    /// <summary>The type itself for a type token, the declaring type for a member token.</summary>
    public TypeName Head { get; init; }

    /// <summary>The member name for a method or field token.</summary>
    public string? Member { get; init; }

    /// <summary>Every type the token mentions, including signature and generic argument types.</summary>
    public TypeName[] Types { get; init; } = [];

    /// <summary>Type arguments of a generic method instantiation.</summary>
    public TypeName[] TypeArguments { get; init; } = [];

    /// <summary>Assemblies a generic type in the token asks a framework to enumerate.</summary>
    public string[] Scans { get; init; } = [];
}
