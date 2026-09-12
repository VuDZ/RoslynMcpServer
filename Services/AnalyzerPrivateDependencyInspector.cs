using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace RoslynMcpServer.Services;

internal sealed record AnalyzerDependencyInspection(
    AssemblyName? Identity,
    IReadOnlyList<AssemblyName> References,
    IReadOnlyList<AssemblyName> PrivateReferences,
    string? FailureReason);

/// <summary>
/// Reads AssemblyRef metadata from a generator DLL. Private references are those
/// not on <see cref="AnalyzerHostContractCatalog"/>. This is a refusal detector
/// for main-only support, not a production discovery source for a dependency set.
/// </summary>
internal static class AnalyzerPrivateDependencyInspector
{
    /// <summary>Test counter: metadata inspection calls. Ordinary publication must not increment this.</summary>
    internal static int InspectCount;

    public static AnalyzerDependencyInspection Inspect(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        InspectCount++;

        if (!File.Exists(assemblyPath))
        {
            return new AnalyzerDependencyInspection(
                null,
                Array.Empty<AssemblyName>(),
                Array.Empty<AssemblyName>(),
                "source-missing");
        }

        AssemblyName identity;
        try
        {
            identity = AssemblyName.GetAssemblyName(assemblyPath);
        }
        catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or ArgumentException)
        {
            return new AnalyzerDependencyInspection(
                null,
                Array.Empty<AssemblyName>(),
                Array.Empty<AssemblyName>(),
                "identity-unreadable:" + ex.GetType().Name);
        }

        IReadOnlyList<AssemblyName> references;
        try
        {
            references = ReadAssemblyReferences(assemblyPath);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or IOException)
        {
            return new AnalyzerDependencyInspection(
                identity,
                Array.Empty<AssemblyName>(),
                Array.Empty<AssemblyName>(),
                "metadata-unreadable:" + ex.GetType().Name);
        }

        var privateRefs = references
            .Where(r =>
                !string.IsNullOrEmpty(r.Name)
                && !AnalyzerHostContractCatalog.IsSharedHostContract(r)
                && !string.Equals(r.Name, identity.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new AnalyzerDependencyInspection(identity, references, privateRefs, FailureReason: null);
    }

    internal static IReadOnlyList<AssemblyName> ReadAssemblyReferences(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata)
        {
            throw new BadImageFormatException("Assembly has no metadata.", assemblyPath);
        }

        var reader = pe.GetMetadataReader();
        var result = new List<AssemblyName>(reader.AssemblyReferences.Count);
        foreach (var handle in reader.AssemblyReferences)
        {
            var reference = reader.GetAssemblyReference(handle);
            var name = new AssemblyName
            {
                Name = reader.GetString(reference.Name),
                Version = reference.Version,
                CultureName = reference.Culture.IsNil ? null : reader.GetString(reference.Culture),
            };
            if (!reference.PublicKeyOrToken.IsNil)
            {
                var token = reader.GetBlobBytes(reference.PublicKeyOrToken);
                if (token is { Length: > 0 })
                {
                    name.SetPublicKeyToken(token);
                }
            }

            result.Add(name);
        }

        return result;
    }
}
