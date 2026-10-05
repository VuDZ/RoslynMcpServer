using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using RoslynMcpServer.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

public sealed class DependencyEvidenceRunnerTests
{
    public DependencyEvidenceRunnerTests(ITestOutputHelper output)
    {
        MsBuildBootstrapper.Register();
        _output = output;
    }

    [Theory]
    [InlineData("standard")]
    [InlineData("absent")]
    [InlineData("custom-target")]
    [InlineData("linked")]
    [InlineData("explicit-analyzer")]
    [InlineData("web")]
    [InlineData("external-asset")]
    [InlineData("escaping-glob")]
    [InlineData("multi-target")]
    [InlineData("empty")]
    [InlineData("missing-reference")]
    [InlineData("non-sdk")]
    public async Task Real_build_and_dtb_observations_do_not_authorize_unproven_closure(string scenario)
    {
        var fixture = await CreateFixtureAsync(scenario);
        _output.WriteLine("Retained private raw evidence: " + fixture.Artifacts);
        var restore = await DependencyEvidenceRunner.RunAsync(fixture.Root, fixture.Artifacts, "restore",
            ["restore", fixture.Project, "--configfile", Path.Combine(fixture.Root, "NuGet.Config"), "-v:minimal"], default);
        var runner = new DependencyEvidenceRunner();
        var evidence = await runner.InspectAsync(fixture.Request, fixture.Artifacts, default);
        Assert.False(evidence.OverlayReady);
        Assert.False(evidence.AnalyzerInventoryComplete);
        Assert.Equal(scenario is "escaping-glob" or "non-sdk" ? "unsupported" : "unknown", evidence.Disposition);
        Assert.Contains("restore-resolution-closure-unproven", evidence.Reasons);
        if (scenario is not ("multi-target" or "missing-reference" or "non-sdk"))
        {
            Assert.True(restore.ExitCode == 0, "Fixture restore failed: " + restore.StandardOutput + restore.StandardError);
            Assert.DoesNotContain(evidence.Reasons, reason => reason.StartsWith("build-or-dtb-failed:", StringComparison.Ordinal));
        }
        if (scenario == "standard")
        {
            Assert.Contains(evidence.Entries, entry => entry.Category == "import"
                && entry.PathOrRegion.EndsWith("Directory.Build.props", StringComparison.Ordinal));
            Assert.Contains(evidence.Entries, entry => entry.Category == "compile"
                && entry.PathOrRegion.EndsWith("App.AssemblyInfo.cs", StringComparison.Ordinal));
            Assert.Contains(evidence.Entries, entry => entry.Category == "analyzer-config"
                && entry.PathOrRegion.EndsWith("GeneratedMSBuildEditorConfig.editorconfig", StringComparison.Ordinal));
            Assert.DoesNotContain(evidence.Entries, entry => entry.Category == "compile"
                && entry.PathOrRegion.Contains("secret", StringComparison.Ordinal));
            foreach (var entry in evidence.Entries.Where(entry => entry.PathOrRegion.StartsWith(
                    fixture.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .Where(entry => entry.Category == "analyzer-config"
                || entry.PathOrRegion.EndsWith("App.AssemblyInfo.cs", StringComparison.Ordinal)
                || entry.PathOrRegion.EndsWith("Directory.Build.props", StringComparison.Ordinal)).DistinctBy(entry => entry.PathOrRegion))
            {
                Assert.StartsWith(fixture.Root + Path.DirectorySeparatorChar, Path.GetFullPath(entry.PathOrRegion));
                var bytes = await File.ReadAllBytesAsync(entry.PathOrRegion);
                Assert.Empty(await DependencyEvidenceRunner.RecheckObservedFilesAsync([entry], default));
                await File.WriteAllBytesAsync(entry.PathOrRegion, bytes.Concat(new byte[] { 32 }).ToArray());
                Assert.Contains(await DependencyEvidenceRunner.RecheckObservedFilesAsync([entry], default), failure => failure.StartsWith("content-changed:", StringComparison.Ordinal));
                await File.WriteAllBytesAsync(entry.PathOrRegion, bytes);
                Assert.Equal(bytes, await File.ReadAllBytesAsync(entry.PathOrRegion));
                File.Delete(entry.PathOrRegion);
                Assert.Contains(await DependencyEvidenceRunner.RecheckObservedFilesAsync([entry], default), failure => failure.StartsWith("unreadable:", StringComparison.Ordinal));
                await File.WriteAllBytesAsync(entry.PathOrRegion, bytes);
                Assert.Empty(await DependencyEvidenceRunner.RecheckObservedFilesAsync([entry], default));
            }
            var emptyRegion = Assert.Single(evidence.Regions.Where(region => region.Role == "additional"));
            Assert.Empty(emptyRegion.Members);
            var missingDirectory = Path.Combine(fixture.Root, "App", "missing-dir");
            Directory.CreateDirectory(missingDirectory);
            var appeared = Path.Combine(missingDirectory, "new.txt");
            await File.WriteAllTextAsync(appeared, "new wildcard member");
            var changedEvidence = await runner.InspectAsync(fixture.Request,
                Path.Combine(fixture.Artifacts, "region-mutation"), default);
            var changedRegion = Assert.Single(changedEvidence.Regions.Where(region => region.Role == "additional"));
            Assert.NotEqual(emptyRegion.ManifestSha256, changedRegion.ManifestSha256);
            Assert.Equal("missing-dir/new.txt", Assert.Single(changedRegion.Members).RelativePath);
            File.Delete(appeared);
            Assert.False(File.Exists(appeared));
        }
        if (scenario == "absent")
        {
            var absent = Assert.Single(evidence.Entries.Where(entry => entry.Category == "known-absent"
                && entry.PathOrRegion.EndsWith("Local.props", StringComparison.Ordinal)));
            Assert.Equal(Path.Combine(fixture.Root, "App", "Local.props"), absent.PathOrRegion);
            Assert.False(File.Exists(absent.PathOrRegion));
            Assert.Empty(await DependencyEvidenceRunner.RecheckObservedFilesAsync([absent], default));
            var before = await File.ReadAllBytesAsync(fixture.Project);
            await File.WriteAllTextAsync(absent.PathOrRegion, "<Project><PropertyGroup><DefineConstants>APPEARED</DefineConstants></PropertyGroup></Project>");
            Assert.True(File.Exists(absent.PathOrRegion));
            Assert.Contains("known-absent-appeared:" + absent.PathOrRegion,
                await DependencyEvidenceRunner.RecheckObservedFilesAsync([absent], default));
            Assert.Equal(before, await File.ReadAllBytesAsync(fixture.Project));
            File.Delete(absent.PathOrRegion);
            Assert.False(File.Exists(absent.PathOrRegion));
        }
        if (scenario == "custom-target")
        {
            Assert.Contains("custom-target", evidence.Reasons);
            Assert.True(File.Exists(Path.Combine(fixture.Root, "App", "target-observation.txt")));
        }
        if (scenario == "linked")
        {
            var shared = Path.Combine(fixture.Root, "shared", "Shared.cs");
            Assert.Equal(2, evidence.Entries.Where(entry => entry.Category == "compile" && entry.PathOrRegion == shared)
                .Select(entry => entry.Owner).Distinct().Count());
        }
        if (scenario == "explicit-analyzer")
        {
            Assert.Contains("compiler-analyzer-missing-taskoutput", evidence.Reasons);
            var analyzer = Assert.Single(evidence.Entries.Where(entry => entry.Category == "analyzer"
                && entry.PathOrRegion.EndsWith("Explicit.dll", StringComparison.Ordinal)));
            Assert.Equal(Path.Combine(fixture.Root, "external", "Explicit.dll"), analyzer.PathOrRegion);
            var bytes = await File.ReadAllBytesAsync(analyzer.PathOrRegion);
            Assert.Empty(await DependencyEvidenceRunner.RecheckObservedFilesAsync([analyzer], default));
            var changed = (byte[])bytes.Clone();
            changed[^1] ^= 1;
            await File.WriteAllBytesAsync(analyzer.PathOrRegion, changed);
            Assert.Contains("content-changed:" + analyzer.PathOrRegion,
                await DependencyEvidenceRunner.RecheckObservedFilesAsync([analyzer], default));
            await File.WriteAllBytesAsync(analyzer.PathOrRegion, bytes);
            using (var held = new FileStream(analyzer.PathOrRegion, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Contains("unreadable:" + analyzer.PathOrRegion,
                    await DependencyEvidenceRunner.RecheckObservedFilesAsync([analyzer], default));
            }
            Assert.Empty(await DependencyEvidenceRunner.RecheckObservedFilesAsync([analyzer], default));
        }
        if (scenario == "external-asset")
        {
            Assert.Contains("external-static-asset", evidence.Reasons);
        }
        if (scenario == "web")
        {
            var region = Assert.Single(evidence.Regions.Where(region => region.Role == "web-asset"));
            Assert.Contains(region.Members, member => member.RelativePath == "wwwroot/.well-known/e0.txt");
        }
        using var workspace = new AdhocWorkspace();
        var snapshot = new SemanticSnapshot("e0-semantic-1", fixture.Request, [], []);
        Assert.Null(await runner.RevalidateOverlayAsync(snapshot, evidence, Guid.NewGuid(), workspace.CurrentSolution, default));
        await SaveManifestAsync(fixture.Root, fixture.Artifacts);
    }

    [Fact]
    public async Task Pinned_sdk_and_pack_reresolution_reads_current_installation_without_loading_analyzers()
    {
        var fixture = await CreateFixtureAsync("resolution");
        var runtimeSdk = Path.GetDirectoryName(typeof(Microsoft.Build.Evaluation.Project).Assembly.Location)!;
        var dotnetRoot = Directory.GetParent(runtimeSdk)!.Parent!.FullName;
        // The testhost's registered MSBuild SDK can differ from the fixture's exact SDK pin.
        // Resolve the fixture through the CLI independently of the in-process assembly location.
        // https://github.com/VuDZ/RoslynMcpServer/actions/runs/37329352099
        var sdkVersion = await DependencyEvidenceRunner.RunAsync(
            fixture.Root, fixture.Artifacts, "sdk-version", ["--version"], default);
        Assert.True(sdkVersion.ExitCode == 0,
            "Fixture SDK resolution failed: " + sdkVersion.StandardOutput + sdkVersion.StandardError);
        var expectedSdk = Path.GetFullPath(Path.Combine(dotnetRoot, "sdk", sdkVersion.StandardOutput.Trim()));
        var first = ResolvePinnedPack(fixture.Root, dotnetRoot);
        Assert.Equal(expectedSdk, first.Sdk);
        _output.WriteLine("Registered MSBuild SDK: " + runtimeSdk + "; fixture SDK: " + first.Sdk);
        Assert.True(Directory.Exists(first.Pack));
        var manifest = Path.Combine(first.Sdk, "Microsoft.NETCoreSdk.BundledVersions.props");
        var identityBefore = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(manifest)));
        var second = ResolvePinnedPack(fixture.Root, dotnetRoot);
        Assert.Equal(first, second);
        Assert.Equal(identityBefore, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(manifest))));
        await DependencyEvidenceRunner.WriteJsonAsync(Path.Combine(fixture.Artifacts, "independent-resolution.json"),
            new { first.Sdk, first.Pack, Manifest = manifest, Sha256 = identityBefore,
                Scope = "Exact pin, default installed targeting pack only; overrides and dynamic dependencies remain unproved." }, default);
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "global.json"), "{\"sdk\":{\"version\":\"0.0.0\",\"rollForward\":\"disable\"}}");
        Assert.Throws<DirectoryNotFoundException>(() => ResolvePinnedPack(fixture.Root, dotnetRoot));
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "global.json"), "{\"sdk\":{\"version\":\"10.0.300\",\"rollForward\":\"disable\"}}");
        Assert.Equal(first, ResolvePinnedPack(fixture.Root, dotnetRoot));
        _output.WriteLine("Retained independent resolution evidence: " + fixture.Artifacts);
    }

    [Fact]
    public async Task Foreign_directory_is_rejected_before_any_process_or_write()
    {
        var root = Path.Combine(Path.GetTempPath(), "dependency-spike-unowned-" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "App.csproj");
        var request = new ExperimentRequest(project, null, null, null, [project], "full", false);
        await Assert.ThrowsAsync<ArgumentException>(() => new DependencyEvidenceRunner().InspectAsync(request, root, default));
        Assert.False(Directory.Exists(root));
    }

    private static (string Sdk, string Pack) ResolvePinnedPack(string fixtureRoot, string dotnetRoot)
    {
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtureRoot, "global.json")));
        var sdk = document.RootElement.GetProperty("sdk");
        if (sdk.GetProperty("rollForward").GetString() != "disable")
        {
            throw new InvalidDataException("The experiment requires exact SDK pinning.");
        }
        var version = sdk.GetProperty("version").GetString()!;
        var path = Path.GetFullPath(Path.Combine(dotnetRoot, "sdk", version));
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException("The pinned SDK is unavailable.");
        }
        var bundled = XDocument.Load(Path.Combine(path, "Microsoft.NETCoreSdk.BundledVersions.props"));
        var reference = bundled.Descendants("KnownFrameworkReference").Single(element =>
            element.Attribute("Include")?.Value == "Microsoft.NETCore.App"
            && element.Attribute("TargetFramework")?.Value == "net10.0");
        var pack = Path.Combine(dotnetRoot, "packs", reference.Attribute("TargetingPackName")!.Value,
            reference.Attribute("TargetingPackVersion")!.Value);
        if (!Directory.Exists(pack))
        {
            throw new DirectoryNotFoundException("The independently selected targeting pack is unavailable.");
        }
        return (path, pack);
    }

    private static async Task<Fixture> CreateFixtureAsync(string scenario)
    {
        var root = Path.Combine(Path.GetTempPath(), "roslyn-dependency-spike", scenario + "-" + Guid.NewGuid().ToString("N"));
        var app = Path.Combine(root, "App");
        var artifacts = Path.Combine(root, "artifacts");
        Directory.CreateDirectory(app);
        Directory.CreateDirectory(artifacts);
        await File.WriteAllTextAsync(Path.Combine(root, ".dependency-spike-owned"), "sdk-project-v1/e0-1");
        await File.WriteAllTextAsync(Path.Combine(root, "global.json"), "{\"sdk\":{\"version\":\"10.0.300\",\"rollForward\":\"disable\"}}");
        await File.WriteAllTextAsync(Path.Combine(root, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
        await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.props"), "<Project><PropertyGroup><DefineConstants>POSITIVE_IMPORT</DefineConstants></PropertyGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.targets"), "<Project><PropertyGroup><E0Positive>1</E0Positive></PropertyGroup></Project>");
        var sdk = scenario is "web" or "external-asset" ? "Microsoft.NET.Sdk.Web" : "Microsoft.NET.Sdk";
        var properties = scenario == "multi-target"
            ? "<TargetFrameworks>net9.0;net10.0</TargetFrameworks>"
            : "<TargetFramework>net10.0</TargetFramework>";
        properties += "<Nullable>enable</Nullable><LangVersion>14</LangVersion>";
        if (scenario == "empty")
        {
            properties += "<GenerateAssemblyInfo>false</GenerateAssemblyInfo><GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>";
        }
        var extra = scenario switch
        {
            "absent" => "<Import Project=\"Local.props\" Condition=\"Exists('Local.props')\" />",
            "standard" => "<ItemGroup><Compile Remove=\"secret/**/*.cs\"/><AdditionalFiles Include=\"missing-dir/**/*.txt\"/></ItemGroup>",
            "custom-target" => "<Target Name=\"ReadHiddenInput\" BeforeTargets=\"CoreCompile\"><Exec Command=\"cmd /c type &amp;quot;$(MSBuildProjectDirectory)\\..\\hidden-input.txt&amp;quot; &amp;gt; &amp;quot;$(MSBuildProjectDirectory)\\target-observation.txt&amp;quot;\"/></Target>",
            "linked" => "<Import Project=\"../common.props\"/><ItemGroup><Compile Include=\"../shared/Shared.cs\" Link=\"Shared.cs\"/><ProjectReference Include=\"../Library/Library.csproj\"/></ItemGroup>",
            "external-asset" => "<ItemGroup><Content Include=\"../external/asset.txt\" Link=\"wwwroot/asset.txt\" CopyToOutputDirectory=\"PreserveNewest\"/></ItemGroup>",
            "escaping-glob" => "<ItemGroup><Compile Include=\"../external/**/*.cs\"/></ItemGroup>",
            "missing-reference" => "<ItemGroup><ProjectReference Include=\"../Missing/Missing.csproj\"/></ItemGroup>",
            _ => "",
        };
        if (scenario == "explicit-analyzer")
        {
            var sdkPath = Path.GetDirectoryName(typeof(Microsoft.Build.Evaluation.Project).Assembly.Location)!;
            var analyzer = Path.Combine(sdkPath, "Sdks", "Microsoft.NET.Sdk", "analyzers", "Microsoft.CodeAnalysis.NetAnalyzers.dll");
            Directory.CreateDirectory(Path.Combine(root, "external"));
            File.Copy(analyzer, Path.Combine(root, "external", "Explicit.dll"));
            extra = "<ItemGroup><Analyzer Include=\"../external/Explicit.dll\"/></ItemGroup>";
        }
        var project = Path.Combine(app, "App.csproj");
        var projectText = $"<Project Sdk=\"{sdk}\"><PropertyGroup>{properties}</PropertyGroup>{extra}</Project>";
        if (scenario == "non-sdk")
        {
            projectText = "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>";
        }
        await File.WriteAllTextAsync(project, projectText, new UTF8Encoding(false));
        if (scenario != "empty")
        {
            await File.WriteAllTextAsync(Path.Combine(app, "Value.cs"), sdk.EndsWith("Web", StringComparison.Ordinal)
                ? "public static class Program { public static void Main() { } }"
                : "public static class Value { public const string Text = \"Привет Ω\"; }");
        }
        Directory.CreateDirectory(Path.Combine(app, "secret"));
        if (scenario == "standard")
        {
            await File.WriteAllTextAsync(Path.Combine(app, "secret", "Excluded.cs"), "This is deliberately invalid C sharp.");
        }
        if (scenario == "custom-target")
        {
            await File.WriteAllTextAsync(Path.Combine(root, "hidden-input.txt"), "hidden target input");
            // XML attribute encoding must preserve the shell's redirection and quoted paths.
            await File.WriteAllTextAsync(project, projectText.Replace("&amp;quot;", "&quot;", StringComparison.Ordinal)
                .Replace("&amp;gt;", "&gt;", StringComparison.Ordinal));
        }
        var roots = ImmutableArray.CreateBuilder<string>();
        roots.Add(project);
        if (scenario == "linked")
        {
            Directory.CreateDirectory(Path.Combine(root, "shared"));
            Directory.CreateDirectory(Path.Combine(root, "Library"));
            await File.WriteAllTextAsync(Path.Combine(root, "shared", "Shared.cs"), "namespace Shared; public static class Link { public const int Number = 42; }");
            await File.WriteAllTextAsync(Path.Combine(root, "common.props"), "<Project><PropertyGroup><DefineConstants>LINKED_IMPORT</DefineConstants></PropertyGroup></Project>");
            var library = Path.Combine(root, "Library", "Library.csproj");
            await File.WriteAllTextAsync(library, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><Import Project=\"../common.props\"/><ItemGroup><Compile Include=\"../shared/Shared.cs\" Link=\"Shared.cs\"/></ItemGroup></Project>");
            roots.Add(library);
        }
        if (scenario is "web" or "external-asset")
        {
            Directory.CreateDirectory(Path.Combine(app, "wwwroot", ".well-known"));
            Directory.CreateDirectory(Path.Combine(root, "external"));
            await File.WriteAllTextAsync(Path.Combine(app, "wwwroot", "index.txt"), "local asset");
            await File.WriteAllTextAsync(Path.Combine(app, "wwwroot", ".well-known", "e0.txt"), "hidden asset");
            await File.WriteAllTextAsync(Path.Combine(root, "external", "asset.txt"), "external asset");
        }
        await SaveManifestAsync(root, artifacts, "fixture-before.json");
        var request = new ExperimentRequest(project, "Debug", "AnyCPU", null, roots.ToImmutable(), "full", false);
        return new Fixture(root, project, artifacts, request);
    }

    private static Task SaveManifestAsync(string root, string artifacts, string name = "artifacts-manifest.json")
    {
        var paths = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(name, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();
        var manifest = paths.Select(path => new
        {
            Path = Path.GetRelativePath(root, path),
            Length = new FileInfo(path).Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
        }).ToArray();
        return DependencyEvidenceRunner.WriteJsonAsync(Path.Combine(artifacts, name), manifest, default);
    }

    private sealed record Fixture(string Root, string Project, string Artifacts, ExperimentRequest Request);

    private readonly ITestOutputHelper _output;
}
