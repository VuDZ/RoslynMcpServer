namespace RoslynMcpServer.Services.Testing.Il.Models;

/// <summary>Why an assembly file gave no facts.</summary>
internal enum IlAssemblyFailureKind
{
    /// <summary>The file or its directory does not exist.</summary>
    Missing,

    /// <summary>The file exists and could not be opened or read.</summary>
    Inaccessible,

    /// <summary>The content is not a managed assembly: damaged, cut short, native, or a module without a manifest.</summary>
    NotAnAssembly,
}
