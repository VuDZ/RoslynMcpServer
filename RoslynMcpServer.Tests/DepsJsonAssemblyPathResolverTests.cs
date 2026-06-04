using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class DepsJsonAssemblyPathResolverTests
{
    [Fact]
    public void TryResolveFromDepsFile_finds_webapi_dll_not_common()
    {
        var brqDeps = @"c:\Devel\BrqMover\src\BrqMover.Console\bin\Debug\net10.0\BrqMover.Console.deps.json";

        if (!File.Exists(brqDeps))
        {
            return;
        }

        var nugetRoot = NuGetPackagesRoot.Get();
        var ok = DepsJsonAssemblyPathResolver.TryResolveFromDepsFile(
            brqDeps,
            "Microsoft.VisualStudio.Services.WebApi.dll",
            nugetRoot,
            out var path,
            out _);

        Assert.True(ok, "expected WebApi.dll in BrqMover.Console.deps.json");
        Assert.NotNull(path);
        Assert.EndsWith("Microsoft.VisualStudio.Services.WebApi.dll", path, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Common.dll", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryResolveFromDepsFile_uses_exact_file_name_only()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "RoslynMcpDepsTests", Guid.NewGuid().ToString("N"));
        var nugetRoot = Path.Combine(tempRoot, "packages");
        var packageDir = Path.Combine(nugetRoot, "sample.pkg", "1.0.0", "lib", "netstandard2.0");
        Directory.CreateDirectory(packageDir);

        var webApiDll = Path.Combine(packageDir, "Microsoft.VisualStudio.Services.WebApi.dll");
        var commonDll = Path.Combine(packageDir, "Microsoft.VisualStudio.Services.Common.dll");
        File.WriteAllBytes(webApiDll, [0x4D, 0x5A]);
        File.WriteAllBytes(commonDll, [0x4D, 0x5A]);

        var depsPath = Path.Combine(tempRoot, "App.deps.json");
        File.WriteAllText(
            depsPath,
            """
            {
              "targets": {
                ".NETCoreApp,Version=v10.0": {
                  "sample.pkg/1.0.0": {
                    "runtime": {
                      "lib/netstandard2.0/Microsoft.VisualStudio.Services.Common.dll": {},
                      "lib/netstandard2.0/Microsoft.VisualStudio.Services.WebApi.dll": {}
                    }
                  }
                }
              },
              "libraries": {
                "sample.pkg/1.0.0": {
                  "type": "package",
                  "path": "sample.pkg/1.0.0"
                }
              }
            }
            """);

        try
        {
            var ok = DepsJsonAssemblyPathResolver.TryResolveFromDepsFile(
                depsPath,
                "Microsoft.VisualStudio.Services.WebApi.dll",
                nugetRoot,
                out var path,
                out _);

            Assert.True(ok);
            Assert.Equal(webApiDll, path);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
