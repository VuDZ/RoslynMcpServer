using RoslynMcpServer.Services.Testing.Il.Models;
using System.Reflection.PortableExecutable;
using System.Reflection.Metadata;

namespace RoslynMcpServer.Services.Testing.Il;

/// <summary>Computes fingerprints directly from the shared memory image without opening its path.</summary>
internal static class IlAssemblyFingerprints
{
    public static IlFingerprintSnapshot Compute(IlAssemblyImage image, IReadOnlySet<string> solutionAssemblies)
    {
        using var pe = new PEReader(image.Content);
        var metadata = pe.GetMetadataReader();
        if (!metadata.IsAssembly)
        {
            throw new BadImageFormatException("The image has no assembly manifest.");
        }

        return new IlFingerprintReader(pe, metadata, solutionAssemblies).Read();
    }
}
