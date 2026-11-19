using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace RoslynMcpServer.Tests.Testing.Il;

/// <summary>
/// Looks into a compiled fixture, so a test can state what the compiler emitted for a case instead of
/// assuming it.
/// </summary>
internal static class IlFixtureInspector
{
    /// <summary>The IL bytes of a method of a top-level type.</summary>
    public static byte[] MethodBody(string assemblyPath, string typeName, string methodName)
    {
        using var image = new PEReader(File.OpenRead(assemblyPath));
        var metadata = image.GetMetadataReader();
        foreach (var typeHandle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(typeHandle);
            if (metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name) != typeName)
            {
                continue;
            }

            foreach (var methodHandle in type.GetMethods())
            {
                var method = metadata.GetMethodDefinition(methodHandle);
                if (metadata.GetString(method.Name) == methodName)
                {
                    return image.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
                }
            }
        }

        throw new InvalidOperationException($"{typeName}.{methodName} is not in {assemblyPath}.");
    }
}
