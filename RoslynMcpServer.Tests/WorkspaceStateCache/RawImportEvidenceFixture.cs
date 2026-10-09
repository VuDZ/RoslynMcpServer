using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace RoslynMcpServer.Tests.WorkspaceStateCache;

/// <summary>Owns copies only; restore prepares the controlled input corpus and is not the evidence channel.</summary>
internal sealed class RawImportEvidenceFixture : IDisposable
{
    public string DirectoryPath { get; }

    public string ControlledProjectPath => Path.Combine(DirectoryPath, "controlled", "App", "App.csproj");

    public string RealProjectPath => Path.Combine(DirectoryPath, "real", "samples", "RoslynMcpPlugin", "RoslynMcpPlugin.csproj");

    public string ExpectedInventoryPath => Path.Combine(DirectoryPath, "controlled", "expected.json");

    public RawImportEvidenceFixture(string root)
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), "raw-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        var real = Path.Combine(DirectoryPath, "real");
        foreach (var directory in new[] { "Config", "Diagnostics", "Hosting", "Plugins", "Properties", "Services", "Tools", "samples/RoslynMcpPlugin" })
        {
            foreach (var path in Directory.GetFiles(Path.Combine(root, directory), "*", SearchOption.AllDirectories)
                .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)))
            {
                Copy(path, Path.Combine(real, Path.GetRelativePath(root, path)));
            }
        }
        foreach (var name in new[] { "RoslynMcpServer.csproj", "Directory.Build.props", "Program.cs" })
        {
            Copy(Path.Combine(root, name), Path.Combine(real, name));
        }
        foreach (var path in Directory.GetFiles(Path.Combine(root, "obj"), "*", SearchOption.TopDirectoryOnly))
        {
            Copy(path, Path.Combine(real, "obj", Path.GetFileName(path)));
        }
        foreach (var directory in new[] { "Debug", "Release" })
        {
            var source = Path.Combine(root, "obj", directory, "net10.0");
            // A clean CI checkout builds Release only. Copy existing generated inputs without requiring a prior Debug build.
            if (!Directory.Exists(source))
            {
                continue;
            }
            foreach (var path in Directory.GetFiles(source, "*.cs", SearchOption.TopDirectoryOnly))
            {
                Copy(path, Path.Combine(real, "obj", directory, "net10.0", Path.GetFileName(path)));
            }
        }
        _realHashes = Directory.GetFiles(real, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .ToDictionary(path => path, path => SHA256.HashData(File.ReadAllBytes(path)));
    }

    public async Task PrepareControlledAsync(CancellationToken cancellationToken)
    {
        var root = Path.Combine(DirectoryPath, "controlled");
        Write("NuGet.Config", "<configuration><packageSources><clear /></packageSources></configuration>");
        Write("Directory.Build.props", """
            <Project><PropertyGroup>
            <BaseIntermediateOutputPath>../intermediate/$(MSBuildProjectName)/</BaseIntermediateOutputPath>
            <MSBuildProjectExtensionsPath>$(BaseIntermediateOutputPath)</MSBuildProjectExtensionsPath>
            <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
            <NuGetLockFilePath>$(MSBuildProjectDirectory)/../locks/$(MSBuildProjectName).lock.json</NuGetLockFilePath>
            </PropertyGroup><Import Project="imports/shared.props" /></Project>
            """);
        Write("imports/shared.props", "<Project><Import Project=\"nested.props\" /></Project>");
        Write("imports/nested.props", "<Project><PropertyGroup><NestedWitness>true</NestedWitness></PropertyGroup></Project>");
        Write("imports/debug.targets", "<Project><PropertyGroup><ConditionalWitness>true</ConditionalWitness></PropertyGroup></Project>");
        var external = Path.Combine(DirectoryPath, "external", "external.targets");
        Directory.CreateDirectory(Path.GetDirectoryName(external)!);
        File.WriteAllText(external, "<Project><PropertyGroup><ExternalWitness>true</ExternalWitness></PropertyGroup></Project>");
        foreach (var name in new[] { "App", "Library" })
        {
            var reference = name == "App" ? "<ItemGroup><ProjectReference Include=\"../Library/Library.csproj\" /></ItemGroup>" : "";
            Write(name + "/" + name + ".csproj", $$"""
                <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                <Import Project="../imports/debug.targets" Condition="'$(Configuration)' == 'Debug'" />
                <Import Project="../imports/absent.targets" Condition="Exists('../imports/absent.targets')" />
                <Import Project="{{external}}" />{{reference}}</Project>
                """);
            Write(name + "/Class.cs", "public class " + name + "Control { }");
        }
        Directory.CreateDirectory(Path.Combine(root, "locks"));
        await RestoreAsync(ControlledProjectPath, cancellationToken);
        var expected = new
        {
            Consumers = new[] { ControlledProjectPath, Path.Combine(root, "Library", "Library.csproj") },
            Imports = new[] { "Directory.Build.props", "imports/shared.props", "imports/nested.props", "imports/debug.targets", external },
            KnownAbsent = new[] { "imports/absent.targets" },
            NuGetConfig = Path.Combine(root, "NuGet.Config"),
            Assets = new[] { "intermediate/App/project.assets.json", "intermediate/Library/project.assets.json" },
            Locks = new[] { "locks/App.lock.json", "locks/Library.lock.json" },
        };
        File.WriteAllText(Path.Combine(root, "expected.json"), JsonSerializer.Serialize(expected,
            new JsonSerializerOptions { WriteIndented = true }));

        void Write(string path, string text)
        {
            var full = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }
    }

    public void AssertRealBytesUnchanged(string outputDirectory)
    {
        foreach (var (path, hash) in _realHashes)
        {
            Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(path)));
        }
        File.WriteAllText(Path.Combine(outputDirectory, "real-copy-hashes.json"), JsonSerializer.Serialize(
            _realHashes.Select(pair => new { Path = Path.GetRelativePath(Path.Combine(DirectoryPath, "real"), pair.Key),
                Sha256 = Convert.ToHexString(pair.Value) }), new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);

    private void Copy(string source, string destination)
    {
        _copiedBytes += new FileInfo(source).Length;
        if (++_copiedFiles > 2000 || _copiedBytes > 128 * 1024 * 1024)
        {
            throw new IOException("The owned control copy exceeds the bounded file or byte budget.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination);
    }

    private static async Task RestoreAsync(string project, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(project)!,
        };
        foreach (var argument in new[] { "restore", project, "--nologo", "-p:NuGetAudit=false" })
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            throw;
        }
        Assert.True(process.ExitCode == 0, await output + "\n" + await error);
    }

    private readonly Dictionary<string, byte[]> _realHashes;
    private int _copiedFiles;
    private long _copiedBytes;
}
