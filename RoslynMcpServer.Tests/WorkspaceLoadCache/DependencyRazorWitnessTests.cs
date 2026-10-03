using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using RoslynMcpServer.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

/// <summary>
/// Independently witnesses SDK Razor output and an input outside the selected profile regions.
/// This is a dependency-closure counterexample, not approval of a cache or analyzer overlay.
/// </summary>
public sealed class DependencyRazorWitnessTests
{
    public DependencyRazorWitnessTests(ITestOutputHelper output)
    {
        MsBuildBootstrapper.Register();
        _output = output;
    }

    [Fact]
    public async Task Scoped_css_changes_fresh_Razor_output_without_changing_selected_source_regions()
    {
        var fixture = new Fixture();
        _output.WriteLine($"Retained Razor witness evidence: {fixture.Root}");
        try
        {
            await fixture.CreateAsync();
            await fixture.RunAsync("sdk-version", "--version");
            Assert.Equal("10.0.300", File.ReadAllText(fixture.Artifact("sdk-version.stdout.txt")).Trim());
            await fixture.RunAsync("sdk-info", "--info");
            await fixture.RunAsync("sdk-list", "--list-sdks");
            await fixture.RunAsync("restore", "restore", fixture.ProjectPath, "--configfile", fixture.PathFor("NuGet.config"));
            var originalManifest = fixture.SelectedRegionManifest();
            await fixture.SaveAsync("selected-regions-before.json", originalManifest);
            await fixture.BuildAsync("baseline");
            var baseline = await CaptureAsync(fixture, "baseline");
            AssertWitness(baseline);
            var knownInputs = fixture.KnownInputManifest();
            await fixture.SaveAsync("known-inputs-before-css.json", knownInputs);

            await File.WriteAllTextAsync(fixture.PathFor("App/Marker.razor.css"), "h1 { color: crimson; }\n");
            var preOpenInputs = fixture.KnownInputManifest();
            await fixture.SaveAsync("known-inputs-after-css-before-build-or-open.json", preOpenInputs);
            Assert.Equal(knownInputs, preOpenInputs);
            var mutatedManifest = fixture.SelectedRegionManifest();
            await fixture.SaveAsync("selected-regions-after.json", mutatedManifest);
            Assert.Equal(originalManifest, mutatedManifest);
            await fixture.BuildAsync("scoped-css");
            var scoped = await CaptureAsync(fixture, "scoped-css");
            AssertWitness(scoped);
            Assert.Equal(baseline.Generated.Keys, scoped.Generated.Keys);
            Assert.False(baseline.Generated.SequenceEqual(scoped.Generated),
                "Adding scoped CSS must change full Razor generated output to establish this counterexample.");
            Assert.Contains(scoped.Generated.Values, text => text.Contains("<h1 b-", StringComparison.Ordinal));
            await fixture.SaveAsync("comparison.json", new
            {
                Outcome = "passed",
                Category = "dependency-closure-counterexample",
                SelectedRegionsUnchanged = true,
                PreviouslyExistingFixtureInputsUnchangedBeforeBuildOrOpen = true,
                ChangedGeneratedIdentities = baseline.Generated.Keys
                    .Where(key => baseline.Generated[key] != scoped.Generated[key]).ToArray(),
                BaselineMarker = baseline.Marker,
                ScopedMarker = scoped.Marker
            });

            await fixture.RestoreAsync();
            await fixture.RunAsync("restored-restore", "restore", fixture.ProjectPath,
                "--configfile", fixture.PathFor("NuGet.config"));
            await fixture.BuildAsync("restored");
            var restored = await CaptureAsync(fixture, "restored");
            AssertWitness(restored);
            Assert.Equal(baseline.Generated, restored.Generated);
        }
        catch (Exception exception)
        {
            await fixture.SaveAsync("failure.json", new { Outcome = "failed", Detail = exception.ToString() });
            throw;
        }
        finally
        {
            await fixture.RestoreAsync();
            await fixture.SaveAsync("restoration.json", new { OriginalBytesVerified = true, ScopedCssAbsent = true });
            await fixture.SaveArtifactHashesAsync();
        }
    }

    private static void AssertWitness(Observation observation)
    {
        Assert.NotEmpty(observation.Generated);
        Assert.Equal("E0RazorMarker", observation.Marker);
        Assert.Contains(observation.AdditionalInputs, path => path.EndsWith("Marker.razor", StringComparison.Ordinal));
        Assert.Contains(observation.ConfigInputs, path => path.EndsWith(".editorconfig", StringComparison.Ordinal));
        Assert.Contains(observation.Diagnostics, diagnostic => diagnostic.StartsWith("CS0219|Error|", StringComparison.Ordinal));
        Assert.Contains(observation.Diagnostics, diagnostic => diagnostic.StartsWith("CS0542|Error|", StringComparison.Ordinal));
    }

    // Registration happens in the constructor, before the JIT touches Microsoft.Build types here.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<Observation> CaptureAsync(Fixture fixture, string stage)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>
        {
            ["Configuration"] = "Debug",
            ["TargetFramework"] = "net10.0"
        });
        var project = await workspace.OpenProjectAsync(fixture.ProjectPath, cancellationToken: timeout.Token);
        await fixture.SaveAsync(stage + ".workspace-diagnostics.json",
            workspace.Diagnostics.Select(diagnostic => new { diagnostic.Kind, diagnostic.Message }).ToArray());
        Assert.DoesNotContain(workspace.Diagnostics, diagnostic => diagnostic.Kind == WorkspaceDiagnosticKind.Failure);
        var compilation = await project.GetCompilationAsync(timeout.Token);
        Assert.NotNull(compilation);
        var generated = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var identities = new List<object>();
        foreach (var document in await project.GetSourceGeneratedDocumentsAsync(timeout.Token))
        {
            var text = (await document.GetTextAsync(timeout.Token)).ToString();
            var identity = $"App/App.csproj|net10.0|{document.FilePath}|{document.Name}";
            generated.Add(identity, text);
            identities.Add(new { Identity = identity, document.Name, document.FilePath, Sha256 = Hash(Encoding.UTF8.GetBytes(text)) });
        }

        var markerType = compilation.GetTypeByMetadataName("E0Razor.Marker");
        var marker = markerType?.GetMembers("Marker").OfType<IFieldSymbol>().SingleOrDefault()?.ConstantValue as string;
        var observation = new Observation(
            generated,
            marker,
            project.AdditionalDocuments.Select(document => document.FilePath!).Order(StringComparer.Ordinal).ToArray(),
            project.AnalyzerConfigDocuments.Select(document => document.FilePath!).Order(StringComparer.Ordinal).ToArray(),
            compilation.GetDiagnostics(timeout.Token).Select(diagnostic =>
                $"{diagnostic.Id}|{diagnostic.Severity}|{diagnostic.GetMessage(CultureInfo.InvariantCulture)}")
                .Order(StringComparer.Ordinal).ToArray());
        await fixture.SaveAsync(stage + ".observation.json", observation);
        await fixture.SaveAsync(stage + ".generated-identities.json", identities);
        await fixture.SaveAsync(stage + ".analyzers.json", project.AnalyzerReferences.Select(reference => new
        {
            reference.FullPath,
            Assembly = AssemblyName.GetAssemblyName(reference.FullPath!).FullName,
            Sha256 = Hash(File.ReadAllBytes(reference.FullPath!))
        }).ToArray());
        await fixture.SaveAsync(stage + ".runtime.json", new
        {
            Roslyn = typeof(Microsoft.CodeAnalysis.Workspace).Assembly.FullName,
            RoslynPath = typeof(Microsoft.CodeAnalysis.Workspace).Assembly.Location,
            MsBuild = typeof(Microsoft.Build.Evaluation.Project).Assembly.FullName,
            MsBuildPath = typeof(Microsoft.Build.Evaluation.Project).Assembly.Location,
            ProcessPath = Environment.ProcessPath,
            Runtime = Environment.Version.ToString(),
            OperatingSystem = Environment.OSVersion.ToString()
        });
        foreach (var config in project.AnalyzerConfigDocuments)
        {
            var name = Path.GetFileName(config.FilePath);
            await File.WriteAllTextAsync(fixture.Artifact(stage + "." + name + ".txt"),
                (await config.GetTextAsync(timeout.Token)).ToString());
        }
        return observation;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed record Observation(
        SortedDictionary<string, string> Generated,
        string? Marker,
        string[] AdditionalInputs,
        string[] ConfigInputs,
        string[] Diagnostics);

    private sealed class Fixture
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "RoslynMcp-E0-Razor", Guid.NewGuid().ToString("N"));
        public string ProjectPath => PathFor("App/App.csproj");

        public string PathFor(string relativePath)
        {
            var path = Path.GetFullPath(Path.Combine(Root, relativePath));
            if (!path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The fixture path must stay inside its owned root.", nameof(relativePath));
            }
            return path;
        }

        public string Artifact(string name) => PathFor("evidence/" + name);

        public async Task CreateAsync()
        {
            Directory.CreateDirectory(PathFor("App"));
            Directory.CreateDirectory(PathFor("evidence"));
            foreach (var (path, text) in _originalFiles)
            {
                await File.WriteAllBytesAsync(PathFor(path), Encoding.UTF8.GetBytes(text));
            }
            await SaveAsync("original-inputs.json", _originalFiles.ToDictionary(
                pair => pair.Key,
                pair => new { Text = pair.Value, Sha256 = Hash(Encoding.UTF8.GetBytes(pair.Value)) }));
        }

        public string[] SelectedRegionManifest() => Directory.EnumerateFiles(PathFor("App"), "*", SearchOption.AllDirectories)
            .Select(path => (Path: path, Relative: Path.GetRelativePath(PathFor("App"), path).Replace('\\', '/')))
            .Where(entry => !entry.Relative.StartsWith("obj/", StringComparison.Ordinal)
                && !entry.Relative.StartsWith("bin/", StringComparison.Ordinal))
            .Where(entry => entry.Relative.EndsWith(".cs", StringComparison.Ordinal)
                || entry.Relative.EndsWith(".razor", StringComparison.Ordinal)
                || entry.Relative.EndsWith(".cshtml", StringComparison.Ordinal)
                || entry.Relative.StartsWith("wwwroot/", StringComparison.Ordinal))
            .Select(entry => entry.Relative + "|" + Hash(File.ReadAllBytes(entry.Path)))
            .Order(StringComparer.Ordinal).ToArray();

        public Task SaveAsync(string name, object value) => File.WriteAllTextAsync(Artifact(name),
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));

        public string[] KnownInputManifest() => Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(PathFor("evidence") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(path, PathFor("App/Marker.razor.css"), StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(Root, path).Replace('\\', '/') + "|" + Hash(File.ReadAllBytes(path)))
            .Order(StringComparer.Ordinal).ToArray();

        // Ordinary build suppresses the deliberately promoted diagnostic; the fresh workspace does not.
        public Task BuildAsync(string stage) => RunCommandAsync(stage + "-build", "CS0542",
            ["build", ProjectPath, "--no-restore", "-c", "Debug", "-p:NoWarn=CS0219",
                "-bl:" + Artifact(stage + ".build.binlog") + ";ProjectImports=None"]);

        public Task RunAsync(string stage, params string[] arguments) => RunCommandAsync(stage, null, arguments);

        public async Task RestoreAsync()
        {
            File.Delete(PathFor("App/Marker.razor.css"));
            foreach (var relativePath in new[] { "App/bin", "App/obj" })
            {
                var path = PathFor(relativePath);
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            foreach (var (path, text) in _originalFiles)
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                await File.WriteAllBytesAsync(PathFor(path), bytes);
                Assert.Equal(bytes, await File.ReadAllBytesAsync(PathFor(path)));
            }
            Assert.False(File.Exists(PathFor("App/Marker.razor.css")));
            Assert.Equal(_originalFiles.Keys.Order(StringComparer.Ordinal), Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(Root, path).Replace('\\', '/'))
                .Where(path => !path.StartsWith("evidence/", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        }

        public Task SaveArtifactHashesAsync() => SaveAsync("artifact-hashes.json",
            Directory.EnumerateFiles(PathFor("evidence"), "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith("artifact-hashes.json", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToDictionary(path => Path.GetRelativePath(Root, path).Replace('\\', '/'), path => Hash(File.ReadAllBytes(path))));

        private async Task RunCommandAsync(string stage, string? expectedFailure, string[] arguments)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }
            // Binary logs can include environment properties. Only infrastructure variables enter this owned build.
            start.Environment.Clear();
            foreach (var name in new[]
            {
                "PATH", "SystemRoot", "WINDIR", "TEMP", "TMP", "USERPROFILE", "APPDATA", "LOCALAPPDATA",
                "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "ProgramData", "ALLUSERSPROFILE",
                "DOTNET_ROOT", "NUGET_PACKAGES", "HOME", "PROCESSOR_ARCHITECTURE"
            })
            {
                if (Environment.GetEnvironmentVariable(name) is { } value)
                {
                    start.Environment[name] = value;
                }
            }
            start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw;
            }
            finally
            {
                await File.WriteAllTextAsync(Artifact(stage + ".stdout.txt"), await output);
                await File.WriteAllTextAsync(Artifact(stage + ".stderr.txt"), await error);
                await SaveAsync(stage + ".command.json", new { start.FileName, Arguments = arguments, start.WorkingDirectory, process.ExitCode });
            }
            if (expectedFailure is null)
            {
                Assert.True(process.ExitCode == 0, $"dotnet stage {stage} failed; evidence: {Root}");
            }
            else
            {
                Assert.NotEqual(0, process.ExitCode);
                Assert.Contains("error " + expectedFailure + ":", await output);
                await SaveAsync(stage + ".expected-failure.json", new { Outcome = "failed-positive-fixture", Diagnostic = expectedFailure });
            }
        }

        private static readonly SortedDictionary<string, string> _originalFiles = new(StringComparer.Ordinal)
        {
            ["global.json"] = "{\"sdk\":{\"version\":\"10.0.300\",\"rollForward\":\"disable\"}}\n",
            ["NuGet.config"] = "<configuration><packageSources><clear /></packageSources></configuration>\n",
            ["App/App.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk.Razor">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <LangVersion>14</LangVersion>
                    <RootNamespace>E0Razor</RootNamespace>
                    <EnableDefaultRazorGenerateItems>true</EnableDefaultRazorGenerateItems>
                  </PropertyGroup>
                  <ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /></ItemGroup>
                </Project>
                """,
            ["App/Marker.razor"] = "<h1>Witness</h1>\n@code { public const string Marker = \"E0RazorMarker\"; }\n",
            ["App/Use.cs"] = "namespace E0Razor; public static class Use { public static string Read() { int unused = 0; return Marker.Marker; } }\n",
            ["App/.editorconfig"] = "root = true\n[*.cs]\ndotnet_diagnostic.CS0219.severity = error\n"
        };
    }

    private readonly ITestOutputHelper _output;
}
