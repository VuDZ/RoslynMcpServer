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

    [Theory]
    [InlineData("Spectre.Console", "Spectre.Console")]
    [InlineData("Spectre.Console.dll", "Spectre.Console")]
    [InlineData("Spectre.Console.DLL", "Spectre.Console")]
    [InlineData("Newtonsoft.Json", "Newtonsoft.Json")]
    [InlineData("System", "System")]
    [InlineData("  Microsoft.TeamFoundation.Client  ", "Microsoft.TeamFoundation.Client")]
    [InlineData("MyTool.exe", "MyTool")]
    public void NormalizeAssemblySimpleName_keeps_dotted_simple_names(string input, string expected)
    {
        Assert.Equal(expected, AssemblyReferenceResolver.NormalizeAssemblySimpleName(input));
    }

    [Fact]
    public void NormalizeAssemblySimpleName_strips_dll_from_file_path()
    {
        var path = Path.Combine("lib", "net8.0", "Spectre.Console.dll");
        Assert.Equal("Spectre.Console", AssemblyReferenceResolver.NormalizeAssemblySimpleName(path));
    }
}
