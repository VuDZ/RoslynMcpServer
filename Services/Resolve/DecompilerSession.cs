using ICSharpCode.Decompiler.CSharp;

namespace RoslynMcpServer.Services.Resolve;

/// <summary>A decompiler for one assembly together with the references its resolver could not find.</summary>
public sealed class DecompilerSession
{
    public CSharpDecompiler Decompiler { get; }

    /// <summary>
    /// References shown as unknown types in the output, as <c>Name Version</c>. Read it after decompiling:
    /// the type system may resolve further references while decompiling.
    /// </summary>
    public IReadOnlyList<string> UnresolvedReferences => _resolver.UnresolvedReferences;

    internal DecompilerSession(CSharpDecompiler decompiler, NuGetFallbackAssemblyResolver resolver)
    {
        Decompiler = decompiler;
        _resolver = resolver;
    }

    private readonly NuGetFallbackAssemblyResolver _resolver;
}
