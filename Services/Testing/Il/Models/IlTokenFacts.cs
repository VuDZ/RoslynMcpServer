namespace RoslynMcpServer.Services.Testing.Il.Models;

/// <summary>What a metadata token used in a method body refers to. Decoded once per token and assembly.</summary>
internal sealed class IlTokenFacts
{
    public static IlTokenFacts Empty { get; } = new();

    /// <summary>The type itself for a type token, the declaring type for a member token.</summary>
    public IlTypeName Head { get; init; }

    /// <summary>The member name for a method or field token.</summary>
    public string? Member { get; init; }

    /// <summary>Every type the token mentions, including signature and generic argument types.</summary>
    public IlTypeName[] Types { get; init; } = [];

    /// <summary>Type arguments of a generic method instantiation.</summary>
    public IlTypeName[] TypeArguments { get; init; } = [];

    /// <summary>Assemblies a generic type in the token asks a framework to enumerate.</summary>
    public string[] Scans { get; init; } = [];
}
