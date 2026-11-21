using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Tests.Support;
using RoslynMcpServer.Tests.WorkspaceStateCache.Models;
using Xunit;

namespace RoslynMcpServer.Tests.WorkspaceStateCache;

public sealed class SupplementalManifestTests
{
    public SupplementalManifestTests() => MsBuildBootstrapper.Register();

    [Fact]
    public async Task Public_evaluation_improves_source_inventory_but_does_not_prove_consumed_generation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var root = FindRoot();
        var output = Path.Combine(root, "TestResults", "task-00.1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        using var fixture = new RawImportEvidenceFixture(root);
        await fixture.PrepareControlledAsync(timeout.Token);
        File.Copy(fixture.ExpectedInventoryPath, Path.Combine(output, "expected.json"));
        await ProbeAsync(
            fixture.ControlledProjectPath,
            [fixture.ControlledProjectPath, Path.Combine(fixture.DirectoryPath, "controlled", "Library", "Library.csproj")],
            Path.Combine(output, "controlled.json"),
            timeout.Token);
        var realPaths = new[] { fixture.RealProjectPath, Path.Combine(fixture.DirectoryPath, "real", "RoslynMcpServer.csproj") };
        await ProbeAsync(fixture.RealProjectPath, realPaths, Path.Combine(output, "real.json"), timeout.Token);
        fixture.AssertRealBytesUnchanged(output);
        var evaluations = SupplementalManifestCollector.Collect(
            [fixture.ControlledProjectPath, Path.Combine(fixture.DirectoryPath, "controlled", "Library", "Library.csproj")]);
        AssertControlledInventory(evaluations, fixture);
        Assert.Throws<InvalidDataException>(() => SupplementalManifestCollector.Collect(realPaths, pathBudget: 0));
        await ProbeExplicitAndCustomInputsAsync(fixture, timeout.Token);
        var controls = await ProbeRetentionAndAbaAsync(fixture, timeout.Token);
        File.WriteAllText(Path.Combine(output, "controls.json"), JsonSerializer.Serialize(controls, _jsonOptions));
        File.WriteAllText(Path.Combine(output, "owned-root.txt"), fixture.DirectoryPath);
    }

    private static async Task ProbeAsync(
        string rootPath,
        string[] expectedProjects,
        string outputPath,
        CancellationToken cancellationToken)
    {
        using var first = MSBuildWorkspace.Create();
        var ordinaryTimer = Stopwatch.StartNew();
        var project = await first.OpenProjectAsync(rootPath, cancellationToken: cancellationToken);
        var before = await ObserveAsync(first, cancellationToken);
        ordinaryTimer.Stop();
        Assert.Empty(first.Diagnostics);
        Assert.Equal(expectedProjects.Order(), first.CurrentSolution.Projects.Select(item => item.FilePath!).Order());
        var requiredSymbol = rootPath.Contains("RoslynMcpPlugin") ? "RoslynMcpPlugin.SamplePlugin" : "AppControl";
        Assert.NotNull((await project.GetCompilationAsync(cancellationToken))!.GetTypeByMetadataName(requiredSymbol));

        var collectionTimer = Stopwatch.StartNew();
        var candidates = SupplementalManifestCollector.Collect(expectedProjects);
        collectionTimer.Stop();
        Assert.Equal(2, candidates.Length);
        Assert.Equal(2, candidates.Select(item => item.EvaluationId).Distinct().Count());
        foreach (var candidate in candidates)
        {
            Assert.Equal("Debug", candidate.Properties["Configuration"]);
            Assert.Equal("AnyCPU", candidate.Properties["Platform"]);
            Assert.Equal("net10.0", candidate.Properties["TargetFramework"]);
            Assert.Contains(candidate.Imports, path => Path.GetFileName(path) == "Directory.Build.props");
            Assert.Contains(candidate.Imports, path => Path.GetFileName(path) == "Microsoft.NET.Sdk.targets");
            Assert.True(File.Exists(candidate.Properties["ProjectAssetsFile"]));
        }
        using var second = MSBuildWorkspace.Create();
        await second.OpenProjectAsync(rootPath, cancellationToken: cancellationToken);
        var after = await ObserveAsync(second, cancellationToken);
        Assert.Empty(second.Diagnostics);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
        var comparisons = candidates.Select(candidate =>
        {
            var ordinary = first.CurrentSolution.Projects.Single(item => item.FilePath == candidate.ProjectPath);
            var ordinaryPaths = ordinary.Documents.Select(document => document.FilePath!).ToHashSet();
            var ordinaryDefines = ((CSharpParseOptions)ordinary.ParseOptions!).PreprocessorSymbolNames.ToHashSet();
            return new
            {
                candidate.ProjectPath, candidate.EvaluationId,
                CandidateOnlyCompile = candidate.Compile.Except(ordinaryPaths).ToArray(),
                OrdinaryOnlyCompile = ordinaryPaths.Except(candidate.Compile).Order().ToArray(),
                CandidateOnlyDefines = candidate.Properties["DefineConstants"].Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Except(ordinaryDefines).ToArray(),
                OrdinaryReferenceCount = ordinary.MetadataReferences.Count,
                CandidateReferenceCount = candidate.ReferencePaths.Count,
            };
        }).ToArray();
        var evidence = new
        {
            Utc = DateTimeOffset.UtcNow, Pid = Environment.ProcessId,
            MsBuildProduct = FileVersionInfo.GetVersionInfo(typeof(Microsoft.Build.Evaluation.Project).Assembly.Location).ProductVersion,
            Roslyn = typeof(MSBuildWorkspace).Assembly.GetName().Version!.ToString(),
            OrdinaryMilliseconds = ordinaryTimer.Elapsed.TotalMilliseconds,
            SupplementalMilliseconds = collectionTimer.Elapsed.TotalMilliseconds,
            RequestedGlobals = new Dictionary<string, string>(), Candidates = candidates,
            Ordinary = before, Comparisons = comparisons, RequiredSymbol = requiredSymbol, SymbolFound = true,
            RepeatedOrdinaryEqual = true, UsableCapture = false,
            Refusal = new[] { "remote-instance-context-unproven", "current-restore-applicability-unproven",
                "namespace-and-generated-input-generation-unproven" },
        };
        File.WriteAllText(outputPath, JsonSerializer.Serialize(evidence, _jsonOptions));
    }

    private static void AssertControlledInventory(SupplementalEvaluation[] evaluations, RawImportEvidenceFixture fixture)
    {
        using var expected = JsonDocument.Parse(File.ReadAllText(fixture.ExpectedInventoryPath));
        var controlled = Path.Combine(fixture.DirectoryPath, "controlled");
        foreach (var candidate in evaluations)
        {
            foreach (var import in expected.RootElement.GetProperty("Imports").EnumerateArray())
            {
                Assert.Contains(Path.GetFullPath(import.GetString()!, controlled), candidate.Imports);
            }
            var name = Path.GetFileNameWithoutExtension(candidate.ProjectPath);
            Assert.Equal(
                Path.Combine(controlled, "intermediate", name, "project.assets.json"),
                Path.GetFullPath(candidate.Properties["ProjectAssetsFile"]));
            var lockPath = Path.GetFullPath(candidate.Properties["NuGetLockFilePath"]);
            Assert.Equal(Path.Combine(controlled, "locks", name + ".lock.json"), lockPath);
            Assert.True(File.Exists(lockPath));
            Assert.Contains(Path.Combine(controlled, "NuGet.Config"), candidate.RestoredConfigPaths);
            Assert.DoesNotContain(Path.Combine(controlled, "imports", "absent.targets"), candidate.Imports);
        }
    }

    private static void AssertMutationRejected(Action mutate)
    {
        var error = Record.Exception(mutate);
        Assert.True(
            error is IOException or UnauthorizedAccessException,
            "Holding the file did not reject the mutation: " + (error?.GetType().FullName ?? "no exception"));
    }

    private static async Task<object> ProbeRetentionAndAbaAsync(
        RawImportEvidenceFixture fixture,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(fixture.DirectoryPath, "controlled");
        var propsPath = Path.Combine(root, "Directory.Build.props");
        var original = File.ReadAllText(propsPath);
        // An open handle blocks writers on Windows only. Permissions reject the same two
        // mutations on Unix without taking write access away from the child directories.
        using (FileChangeBlocker.BlockWrite(propsPath))
        using (FileChangeBlocker.BlockDelete(propsPath))
        {
            AssertMutationRejected(() => File.WriteAllText(propsPath, original));
            AssertMutationRejected(() => File.Delete(propsPath));
            // Holding known files does not hold the namespace. A conditional input can appear and affect a new load.
            var absentPath = Path.Combine(root, "imports", "absent.targets");
            File.WriteAllText(absentPath, "<Project><PropertyGroup><DefineConstants>$(DefineConstants);NEW_INPUT</DefineConstants></PropertyGroup></Project>");
            using var workspace = MSBuildWorkspace.Create();
            var project = await workspace.OpenProjectAsync(fixture.ControlledProjectPath, cancellationToken: cancellationToken);
            Assert.Contains("NEW_INPUT", ((CSharpParseOptions)project.ParseOptions!).PreprocessorSymbolNames);
            Assert.Empty(workspace.Diagnostics);
            File.Delete(absentPath);
            var configPath = Path.Combine(root, "App", "NuGet.Config");
            File.WriteAllText(configPath, "<configuration />");
            Assert.DoesNotContain(configPath, SupplementalManifestCollector.Collect([fixture.ControlledProjectPath])[0].RestoredConfigPaths);
            var memberPath = Path.Combine(root, "App", "NewMember.cs");
            File.WriteAllText(memberPath, "public class NewMember { }");
            var editorConfig = Path.Combine(root, "App", ".editorconfig");
            File.WriteAllText(editorConfig, "root = true\n[*.cs]\nindent_size = 4\n");
            using var changedMembership = MSBuildWorkspace.Create();
            var changedProject = await changedMembership.OpenProjectAsync(fixture.ControlledProjectPath, cancellationToken: cancellationToken);
            Assert.Contains(changedProject.Documents, document => document.FilePath == memberPath);
            Assert.Contains(changedProject.AnalyzerConfigDocuments, document => document.FilePath == editorConfig);
            File.Delete(memberPath);
            File.Delete(editorConfig);
            File.Delete(configPath);
        }
        var hashBefore = SHA256.HashData(File.ReadAllBytes(propsPath));
        var changed = original.Replace(
            "</Project>",
            "<PropertyGroup><DefineConstants>$(DefineConstants);CONSUMED_B</DefineConstants></PropertyGroup></Project>");
        File.WriteAllText(propsPath, changed);
        using (var workspace = MSBuildWorkspace.Create())
        {
            var project = await workspace.OpenProjectAsync(fixture.ControlledProjectPath, cancellationToken: cancellationToken);
            Assert.Contains("CONSUMED_B", ((CSharpParseOptions)project.ParseOptions!).PreprocessorSymbolNames);
            // Restore A after B was consumed, before the first post-load hash. Equal hashes would authorize a false capture.
            File.WriteAllText(propsPath, original);
            Assert.Equal(hashBefore, SHA256.HashData(File.ReadAllBytes(propsPath)));
        }
        using (var workspace = MSBuildWorkspace.Create())
        {
            var project = await workspace.OpenProjectAsync(fixture.ControlledProjectPath, cancellationToken: cancellationToken);
            Assert.DoesNotContain("CONSUMED_B", ((CSharpParseOptions)project.ParseOptions!).PreprocessorSymbolNames);
            File.WriteAllText(propsPath, changed);
            Assert.NotEqual(hashBefore, SHA256.HashData(File.ReadAllBytes(propsPath)));
            File.WriteAllText(propsPath, original);
            Assert.Equal(hashBefore, SHA256.HashData(File.ReadAllBytes(propsPath)));
        }
        var generatedPath = Path.Combine(root, "intermediate", "App", "Debug", "net10.0", "App.AssemblyInfo.cs");
        Assert.True(File.Exists(generatedPath));
        using (FileChangeBlocker.BlockWrite(generatedPath))
        {
            AssertMutationRejected(() => File.WriteAllText(generatedPath, "// Changed generated input."));
        }
        var configs = Path.Combine(root, "NuGet.Config");
        File.Delete(configs);
        Assert.False(File.Exists(configs));
        Assert.Contains(configs, SupplementalManifestCollector.Collect([fixture.ControlledProjectPath])[0].RestoredConfigPaths);
        var assets = Path.Combine(root, "intermediate", "App", "project.assets.json");
        File.Delete(assets);
        Assert.Throws<IOException>(() => SupplementalManifestCollector.Collect([fixture.ControlledProjectPath]));
        return new
        {
            ExistingWriteAndDeleteBlocked = true, NewConditionalInputConsumedWhilePropsHeld = true,
            NewNamespaceEntriesPossible = true, ConsumedBWithEqualAHashes = true, PostConsumptionAbaHashesEqual = true,
            GeneratedWritesConflictWithRetention = true, DeletedConfigStillInAssets = true,
            MissingAssetsRefused = true, ZeroPathBudgetRefused = true, UsableCapture = false,
            NewConfigMissingFromRestoredInventory = true, NewSourceAndEditorConfigConsumed = true,
            ExplicitObjAndExternalCompilePreserved = true, CustomTargetInputNotInEvaluationInventory = true,
        };
    }

    private static async Task ProbeExplicitAndCustomInputsAsync(
        RawImportEvidenceFixture fixture,
        CancellationToken cancellationToken)
    {
        var projectPath = fixture.ControlledProjectPath;
        var original = File.ReadAllText(projectPath);
        var external = Path.Combine(fixture.DirectoryPath, "external", "Explicit.cs");
        var obj = Path.Combine(Path.GetDirectoryName(projectPath)!, "obj", "Explicit.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(obj)!);
        File.WriteAllText(external, "public class ExternalExplicit { }");
        File.WriteAllText(obj, "public class ObjExplicit { }");
        var customPath = Path.Combine(fixture.DirectoryPath, "external", "custom.txt");
        File.WriteAllText(customPath, "CUSTOM_INPUT");
        var customProject = original.Replace(
            "</Project>",
            $$"""
            <ItemGroup><Compile Include="{{external}}" /><Compile Include="{{obj}}" /></ItemGroup>
            <Target Name="ReadCustomInput" BeforeTargets="CoreCompile">
            <ReadLinesFromFile File="{{customPath}}"><Output TaskParameter="Lines" ItemName="CustomDefines" /></ReadLinesFromFile>
            <PropertyGroup><DefineConstants>$(DefineConstants);@(CustomDefines)</DefineConstants></PropertyGroup>
            </Target></Project>
            """);
        File.WriteAllText(projectPath, customProject);
        try
        {
            var candidate = SupplementalManifestCollector.Collect([projectPath])[0];
            Assert.Contains(external, candidate.Compile);
            Assert.Contains(obj, candidate.Compile);
            Assert.DoesNotContain(customPath, candidate.Imports.Concat(candidate.Compile));
            using var workspace = MSBuildWorkspace.Create();
            var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken);
            Assert.Contains("CUSTOM_INPUT", ((CSharpParseOptions)project.ParseOptions!).PreprocessorSymbolNames);
            Assert.DoesNotContain("CUSTOM_INPUT", candidate.Properties["DefineConstants"].Split(';'));
            Assert.Contains(project.Documents, document => document.FilePath == external);
            Assert.Contains(project.Documents, document => document.FilePath == obj);
        }
        finally
        {
            File.WriteAllText(projectPath, original);
        }
    }

    private static async Task<object[]> ObserveAsync(MSBuildWorkspace workspace, CancellationToken cancellationToken)
    {
        var result = new List<object>();
        foreach (var project in workspace.CurrentSolution.Projects.OrderBy(item => item.FilePath))
        {
            var parse = (CSharpParseOptions)project.ParseOptions!;
            var options = (CSharpCompilationOptions)project.CompilationOptions!;
            var documents = new List<object>();
            foreach (var document in project.Documents.OrderBy(item => item.FilePath))
            {
                var text = await document.GetTextAsync(cancellationToken);
                documents.Add(new { document.FilePath, Hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text.ToString()))) });
            }
            result.Add(new
            {
                project.FilePath, project.AssemblyName, Documents = documents,
                Additional = project.AdditionalDocuments.Select(item => item.FilePath).Order().ToArray(),
                Configs = project.AnalyzerConfigDocuments.Select(item => item.FilePath).Order().ToArray(),
                Defines = parse.PreprocessorSymbolNames.Order().ToArray(), LanguageVersion = parse.LanguageVersion.ToString(),
                options.AllowUnsafe, options.Deterministic, Nullable = options.NullableContextOptions.ToString(),
                Output = options.OutputKind.ToString(), Optimization = options.OptimizationLevel.ToString(), Platform = options.Platform.ToString(),
                References = project.MetadataReferences.Select(reference => new
                {
                    reference.Display, Aliases = reference.Properties.Aliases.ToArray(), reference.Properties.EmbedInteropTypes,
                }).OrderBy(item => item.Display).ToArray(),
                ProjectReferences = project.ProjectReferences.Select(reference => new
                {
                    Path = workspace.CurrentSolution.GetProject(reference.ProjectId)!.FilePath,
                    Aliases = reference.Aliases.ToArray(), reference.EmbedInteropTypes,
                }).OrderBy(item => item.Path).ToArray(),
            });
        }
        return result.ToArray();
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RoslynMcpServer.csproj")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
}
