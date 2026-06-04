using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class AssemblyReferenceResolverTests
{
    [Fact]
    public void Resolve_uses_assemblyPath_when_file_exists()
    {
        var dll = Path.Combine(Path.GetTempPath(), "RoslynMcpTests", Guid.NewGuid().ToString("N"), "Fake.Assembly.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
        File.WriteAllBytes(dll, [0x4D, 0x5A]);

        try
        {
            var result = AssemblyReferenceResolver.Resolve(solution: null, assemblyName: null, assemblyPath: dll);
            Assert.True(result.Success);
            Assert.Equal(Path.GetFullPath(dll), result.DllPath);
        }
        finally
        {
            if (File.Exists(dll))
            {
                File.Delete(dll);
            }
        }
    }

    [Fact]
    public void Resolve_requires_name_or_path()
    {
        var result = AssemblyReferenceResolver.Resolve(null, null, null);
        Assert.False(result.Success);
        Assert.Contains("assemblyName", result.ErrorMessage!, StringComparison.Ordinal);
    }

}
