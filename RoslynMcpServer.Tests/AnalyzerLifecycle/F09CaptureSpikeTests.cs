using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Formatting;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.MSBuild;
using RoslynMcpServer.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

[Collection("AnalyzerLifecycle")]
[Trait("Category", "AnalyzerLifecycle")]
[Trait("Category", "F09CaptureSpike")]
public sealed class F09CaptureSpikeTests
{
    private static readonly object BootstrapGate = new();
    private static bool s_bootstrapped;
    private readonly ITestOutputHelper _output;

    public F09CaptureSpikeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [AnalyzerLifecycleFact]
    public async Task BuildHost_binlog_preserves_provenance_and_exact_project_joins()
    {
        EnsureMsBuildRegistered();
        using var fixture = GeneratorConsumerFixture.Create(
            OutputPathMode.RedirectedMissingAnalyzerPath,
            missingForeignPath: false);

        await BuildFixtureAsync(fixture.SolutionPath);
        AddMissingSameNameAnalyzer(fixture.ConsumerProjectPath);
        AddReleaseConfiguration(fixture.SolutionPath);
        var outputsBefore = SnapshotBuildOutputs(fixture.Root);

        var debugSolution = await CaptureAsync(
            fixture.SolutionPath,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Configuration"] = "Debug",
                ["Platform"] = "AnyCPU",
            });
        var releaseSolution = await CaptureAsync(
            fixture.SolutionPath,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Configuration"] = "Release",
                ["Platform"] = "AnyCPU",
            });
        var projectOpen = await CaptureAsync(
            fixture.ConsumerProjectPath,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Configuration"] = "Debug",
                ["Platform"] = "AnyCPU",
            });

        Dump("solution-debug", debugSolution);
        Dump("solution-release", releaseSolution);
        Dump("project-debug", projectOpen);
        AssertCaptureHasExactJoin(debugSolution, fixture, "Debug");
        AssertCaptureHasExactJoin(releaseSolution, fixture, "Release");
        AssertCaptureHasExactJoin(projectOpen, fixture, "Debug");
        AssertExplicitAnalyzerRemainsUnconfirmed(debugSolution, fixture);
        AssertBuildOutputsUnchanged(outputsBefore, SnapshotBuildOutputs(fixture.Root));

    }

    [AnalyzerLifecycleFact]
    public async Task BuildHost_binlog_distinguishes_two_inner_tfms_and_foreign_same_name()
    {
        EnsureMsBuildRegistered();
        using var fixture = GeneratorConsumerFixture.Create(
            OutputPathMode.SdkDefaultCorrectPath,
            extraConsumers: 1,
            missingForeignPath: false);

        await BuildFixtureAsync(fixture.SolutionPath);
        MakeGeneratorMultiTargeted(fixture.GeneratorProjectPath);
        var net10Consumer = fixture.ExtraConsumerSourcePaths.Single();
        var net10Directory = Path.GetDirectoryName(net10Consumer)!;
        var net10Project = Path.Combine(net10Directory, Path.GetFileName(net10Directory) + ".csproj");
        SetTargetFramework(net10Project, "net10.0");
        await BuildFixtureAsync(fixture.GeneratorProjectPath);
        await BuildFixtureAsync(net10Project);
        AddMissingSameNameAnalyzer(fixture.ConsumerProjectPath);
        var outputsBefore = SnapshotBuildOutputs(fixture.Root);

        var capture = await CaptureAsync(
            fixture.SolutionPath,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Configuration"] = "Debug",
                ["Platform"] = "AnyCPU",
            });

        var produced = capture.AnalyzerItems
            .Where(item => PathEquals(item.SourceProjectFile, fixture.GeneratorProjectPath))
            .ToList();
        Assert.Contains(produced, item => string.Equals(item.NearestTargetFramework, "netstandard2.0", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(produced, item => string.Equals(item.NearestTargetFramework, "net10.0", StringComparison.OrdinalIgnoreCase));

        var joins = produced.Select(item => Join(capture.Solution, capture.ProjectContexts, item)).ToList();
        Assert.All(joins, join =>
        {
            Assert.Single(join.ConsumerCandidates);
            Assert.Single(join.SourceCandidates);
        });
        Assert.Equal(2, joins.Select(join => join.SourceCandidates.Single().Id).Distinct().Count());
        AssertExplicitAnalyzerRemainsUnconfirmed(capture, fixture);
        AssertBuildOutputsUnchanged(outputsBefore, SnapshotBuildOutputs(fixture.Root));

        Dump("solution-multi-tfm", capture);
    }

    [AnalyzerLifecycleFact]
    public async Task Replay_failure_modes_are_fail_closed_and_temp_files_are_deleted()
    {
        EnsureMsBuildRegistered();
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await BuildFixtureAsync(fixture.SolutionPath);

        var capture = await CaptureAsync(fixture.SolutionPath, properties: null, keepBinlogs: true);
        try
        {
            var valid = Assert.Single(capture.BinlogPaths);
            var missing = valid + ".missing";
            Assert.Throws<FileNotFoundException>(() => ReplayOne(missing, CancellationToken.None));

            var corrupt = Path.Combine(capture.TempDirectory, "corrupt.binlog");
            File.WriteAllBytes(corrupt, [0x01, 0x02, 0x03, 0x04]);
            Assert.ThrowsAny<Exception>(() => ReplayOne(corrupt, CancellationToken.None));

            var partial = Path.Combine(capture.TempDirectory, "partial.binlog");
            var bytes = File.ReadAllBytes(valid);
            File.WriteAllBytes(partial, bytes[..Math.Max(1, bytes.Length / 2)]);
            Assert.ThrowsAny<Exception>(() => ReplayOne(partial, CancellationToken.None));

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.Throws<OperationCanceledException>(() => ReplayOne(valid, cancelled.Token));

            var mixed = ReplayAll([valid, corrupt], CancellationToken.None);
            Assert.Equal(CaptureStatus.Incomplete, mixed.Status);
            Assert.NotEmpty(mixed.Items);
        }
        finally
        {
            await DeleteDirectoryWithRetryAsync(capture.TempDirectory);
        }

        Assert.False(Directory.Exists(capture.TempDirectory));
    }

    [AnalyzerLifecycleFact]
    public async Task Capture_records_fixture_and_real_solution_cost()
    {
        EnsureMsBuildRegistered();
        using var fixture = GeneratorConsumerFixture.Create(OutputPathMode.SdkDefaultCorrectPath);
        await BuildFixtureAsync(fixture.SolutionPath);

        _ = await OpenWithoutCaptureAsync(fixture.SolutionPath);
        var repositorySolution = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "RoslynMcpServer.sln"));
        Assert.True(File.Exists(repositorySolution), repositorySolution);
        _ = await OpenWithoutCaptureAsync(repositorySolution);

        var fixtureSamples = await MeasureAsync(fixture.SolutionPath, count: 3);
        var repositorySamples = await MeasureAsync(repositorySolution, count: 3);
        var fixtureCapture = fixtureSamples.Captures[^1];
        var repositoryCapture = repositorySamples.Captures[^1];
        var msbuildAssembly = Assembly.Load(new AssemblyName("Microsoft.Build"));

        var lines = new[]
        {
            $"msbuild.runtime.version={msbuildAssembly.GetName().Version}",
            $"msbuild.runtime.path={msbuildAssembly.Location}",
            $"fixture.baseline.samples.ms={Format(fixtureSamples.Baselines.Select(value => value.TotalMilliseconds))}",
            $"fixture.capture-open.samples.ms={Format(fixtureSamples.Captures.Select(value => value.OpenDuration.TotalMilliseconds))}",
            $"fixture.replay.samples.ms={Format(fixtureSamples.Captures.Select(value => value.ReplayDuration.TotalMilliseconds))}",
            $"fixture.additional.median.ms={Median(fixtureSamples.AdditionalMilliseconds):F1}",
            $"fixture.binlog.files={fixtureCapture.BinlogPaths.Count}",
            $"fixture.binlog.bytes={fixtureCapture.TotalBytes}",
            $"fixture.project-contexts={fixtureCapture.ProjectContexts.Count}",
            $"fixture.analyzer-items={fixtureCapture.AnalyzerItems.Count}",
            $"repository.baseline.samples.ms={Format(repositorySamples.Baselines.Select(value => value.TotalMilliseconds))}",
            $"repository.capture-open.samples.ms={Format(repositorySamples.Captures.Select(value => value.OpenDuration.TotalMilliseconds))}",
            $"repository.replay.samples.ms={Format(repositorySamples.Captures.Select(value => value.ReplayDuration.TotalMilliseconds))}",
            $"repository.additional.median.ms={Median(repositorySamples.AdditionalMilliseconds):F1}",
            $"repository.binlog.files={repositoryCapture.BinlogPaths.Count}",
            $"repository.binlog.bytes={repositoryCapture.TotalBytes}",
            $"repository.project-contexts={repositoryCapture.ProjectContexts.Count}",
            $"repository.analyzer-items={repositoryCapture.AnalyzerItems.Count}",
        };
        foreach (var line in lines)
        {
            _output.WriteLine(line);
        }
    }

    private static async Task<MetricSamples> MeasureAsync(string path, int count)
    {
        var baselines = new List<TimeSpan>(count);
        var captures = new List<CaptureRun>(count);
        var additional = new List<double>(count);
        for (var index = 0; index < count; index++)
        {
            TimeSpan baseline;
            CaptureRun capture;
            if (index % 2 == 0)
            {
                baseline = await OpenWithoutCaptureAsync(path);
                capture = await CaptureAsync(path, properties: null);
            }
            else
            {
                capture = await CaptureAsync(path, properties: null);
                baseline = await OpenWithoutCaptureAsync(path);
            }

            baselines.Add(baseline);
            captures.Add(capture);
            additional.Add((capture.OpenDuration + capture.ReplayDuration - baseline).TotalMilliseconds);
        }

        return new MetricSamples(baselines, captures, additional);
    }

    private static string Format(IEnumerable<double> values) =>
        string.Join(",", values.Select(value => value.ToString("F1", CultureInfo.InvariantCulture)));

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.Order().ToArray();
        return ordered[ordered.Length / 2];
    }

    private static void EnsureMsBuildRegistered()
    {
        lock (BootstrapGate)
        {
            if (s_bootstrapped)
            {
                return;
            }

            MsBuildBootstrapper.Register();
            _ = typeof(CSharpFormattingOptions).Assembly.FullName;
            _ = Assembly.Load(new AssemblyName("Microsoft.CodeAnalysis.Features"));
            _ = Assembly.Load(new AssemblyName("Microsoft.CodeAnalysis.CSharp.Features"));
            s_bootstrapped = true;
        }
    }

    private static async Task BuildFixtureAsync(string path)
    {
        await using var host = LifecycleHostClient.Start();
        await Epoch1HostOps.BuildAsync(host, path);
    }

    private static async Task<CaptureRun> CaptureAsync(
        string path,
        IReadOnlyDictionary<string, string>? properties,
        bool keepBinlogs = false)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "RoslynMcpServer.F09", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var requestedPath = Path.Combine(tempDirectory, "capture.binlog");
        var logger = new BinaryLogger
        {
            Parameters = requestedPath + ";ProjectImports=None",
            Verbosity = LoggerVerbosity.Diagnostic,
        };
        var host = CreateMefHost();
        using var workspace = properties is null || properties.Count == 0
            ? MSBuildWorkspace.Create(host)
            : MSBuildWorkspace.Create(
                new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase),
                host);

        try
        {
            var openTimer = Stopwatch.StartNew();
            if (string.Equals(Path.GetExtension(path), ".csproj", StringComparison.OrdinalIgnoreCase))
            {
                _ = await workspace.OpenProjectAsync(path, msbuildLogger: logger);
            }
            else
            {
                _ = await workspace.OpenSolutionAsync(path, msbuildLogger: logger);
            }

            openTimer.Stop();
            var binlogs = Directory.EnumerateFiles(tempDirectory, "*.binlog", SearchOption.TopDirectoryOnly)
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Assert.NotEmpty(binlogs);

            var replayTimer = Stopwatch.StartNew();
            var replay = ReplayAll(binlogs, CancellationToken.None);
            replayTimer.Stop();
            Assert.True(
                replay.Status == CaptureStatus.Complete,
                $"Replay status: {replay.Status}{Environment.NewLine}{string.Join(Environment.NewLine, replay.Errors)}");

            var copiedSolution = workspace.CurrentSolution;
            var totalBytes = binlogs.Sum(file => new FileInfo(file).Length);
            var result = new CaptureRun(
                copiedSolution,
                replay.Contexts,
                replay.Items,
                tempDirectory,
                binlogs,
                totalBytes,
                openTimer.Elapsed,
                replayTimer.Elapsed);
            if (!keepBinlogs)
            {
                DeleteDirectoryBestEffort(tempDirectory);
            }

            return result;
        }
        catch
        {
            DeleteDirectoryBestEffort(tempDirectory);
            throw;
        }
    }

    private static async Task<TimeSpan> OpenWithoutCaptureAsync(string path)
    {
        using var workspace = MSBuildWorkspace.Create(CreateMefHost());
        var timer = Stopwatch.StartNew();
        if (string.Equals(Path.GetExtension(path), ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            _ = await workspace.OpenProjectAsync(path);
        }
        else
        {
            _ = await workspace.OpenSolutionAsync(path);
        }

        timer.Stop();
        return timer.Elapsed;
    }

    private static MefHostServices CreateMefHost() =>
        MefHostServices.Create([
            .. MefHostServices.DefaultAssemblies,
            typeof(CSharpFormattingOptions).Assembly,
            Assembly.Load(new AssemblyName("Microsoft.CodeAnalysis.Features")),
            Assembly.Load(new AssemblyName("Microsoft.CodeAnalysis.CSharp.Features")),
        ]);

    private static ReplayOutcome ReplayAll(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var contexts = new List<CapturedProjectContext>();
        var items = new List<CapturedAnalyzerItem>();
        var errors = new List<string>();
        var failures = 0;
        foreach (var path in paths)
        {
            try
            {
                var one = ReplayOne(path, cancellationToken);
                contexts.AddRange(one.Contexts);
                items.AddRange(one.Items);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                failures++;
                errors.Add($"{path}: {ex}");
            }
        }

        return new ReplayOutcome(
            failures == 0 ? CaptureStatus.Complete : failures == paths.Count ? CaptureStatus.Failed : CaptureStatus.Incomplete,
            contexts,
            items,
            errors);
    }

    private static ReplayOutcome ReplayOne(string path, CancellationToken cancellationToken)
    {
        var contexts = new List<CapturedProjectContext>();
        var items = new List<CapturedAnalyzerItem>();
        var replay = new BinaryLogReplayEventSource();
        replay.ProjectStarted += (_, args) =>
        {
            if (args.BuildEventContext is null || string.IsNullOrWhiteSpace(args.ProjectFile))
            {
                return;
            }

            contexts.Add(new CapturedProjectContext(
                path,
                ProjectContextKey.From(args.BuildEventContext),
                args.ParentProjectBuildEventContext is null ? null : EventContextKey.From(args.ParentProjectBuildEventContext),
                Path.GetFullPath(args.ProjectFile),
                Copy(args.GlobalProperties),
                ReadProperties(args.Properties)));
        };
        replay.AnyEventRaised += (_, args) =>
        {
            if (args is not TaskParameterEventArgs
                {
                    Kind: TaskParameterMessageKind.TaskOutput,
                    ItemType: "Analyzer",
                    BuildEventContext: not null,
                } taskOutput)
            {
                return;
            }

            foreach (var item in taskOutput.Items.OfType<ITaskItem>())
            {
                var context = taskOutput.BuildEventContext!;
                var metadata = item.MetadataNames
                    .Cast<string>()
                    .ToDictionary(
                        name => name,
                        item.GetMetadata,
                        StringComparer.OrdinalIgnoreCase);
                items.Add(new CapturedAnalyzerItem(
                    path,
                    ProjectContextKey.From(context),
                    EventContextKey.From(context),
                    Path.GetFullPath(item.ItemSpec),
                    Get(metadata, "MSBuildSourceProjectFile"),
                    Get(metadata, "NearestTargetFramework"),
                    Get(metadata, "SetTargetFramework"),
                    metadata));
            }
        };

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        replay.Replay(stream, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new ReplayOutcome(CaptureStatus.Complete, contexts, items, Array.Empty<string>());
    }

    private static void AssertCaptureHasExactJoin(CaptureRun capture, GeneratorConsumerFixture fixture, string configuration)
    {
        var produced = capture.AnalyzerItems
            .Where(item => PathEquals(item.SourceProjectFile, fixture.GeneratorProjectPath))
            .ToList();
        Assert.NotEmpty(produced);
        Assert.Contains(produced, item => item.Identity.Contains(configuration, StringComparison.OrdinalIgnoreCase));

        foreach (var item in produced)
        {
            Assert.Contains(
                capture.ProjectContexts,
                context => string.Equals(context.BinlogPath, item.BinlogPath, StringComparison.OrdinalIgnoreCase)
                    && context.ParentContext == item.TaskContext
                    && PathEquals(context.ProjectFile, item.SourceProjectFile)
                    && string.Equals(
                        Get(context.GlobalProperties, "Configuration"),
                        configuration,
                        StringComparison.OrdinalIgnoreCase));
            var join = Join(capture.Solution, capture.ProjectContexts, item);
            Assert.Single(join.ConsumerCandidates);
            Assert.Single(join.SourceCandidates);
        }
    }

    private static void AssertExplicitAnalyzerRemainsUnconfirmed(CaptureRun capture, GeneratorConsumerFixture fixture)
    {
        var missingPath = fixture.MissingForeignPath ?? Path.Combine(fixture.Root, "missing", "Generator.dll");
        Assert.Contains(
            capture.Solution.Projects
                .Where(project => PathEquals(project.FilePath, fixture.ConsumerProjectPath))
                .SelectMany(project => project.AnalyzerReferences),
            reference => PathEquals(reference.FullPath, missingPath)
                || (reference.Display?.Contains(
                    Path.Combine("missing", "Generator.dll"),
                    StringComparison.OrdinalIgnoreCase) ?? false));
        Assert.DoesNotContain(
            capture.AnalyzerItems,
            item => PathEquals(item.Identity, missingPath)
                && !string.IsNullOrWhiteSpace(item.SourceProjectFile));
    }

    private static JoinResult Join(
        Solution solution,
        IReadOnlyList<CapturedProjectContext> contexts,
        CapturedAnalyzerItem item)
    {
        var consumerContext = contexts.Single(context =>
            string.Equals(context.BinlogPath, item.BinlogPath, StringComparison.OrdinalIgnoreCase)
            && context.Context == item.ConsumerContext);
        var consumers = solution.Projects
            .Where(project => PathEquals(project.FilePath, consumerContext.ProjectFile)
                && project.AnalyzerReferences.Any(reference => PathEquals(reference.FullPath, item.Identity)))
            .ToList();

        var nestedContexts = contexts
            .Where(context => string.Equals(context.BinlogPath, item.BinlogPath, StringComparison.OrdinalIgnoreCase)
                && context.ParentContext == item.TaskContext
                && PathEquals(context.ProjectFile, item.SourceProjectFile))
            .ToList();
        var sources = solution.Projects
            .Where(project => PathEquals(project.FilePath, item.SourceProjectFile)
                && (PathEquals(item.Identity, project.OutputFilePath)
                    || nestedContexts.Any(context => ContextMatchesProject(context, project))))
            .ToList();
        return new JoinResult(consumers, sources);
    }

    private static bool ContextMatchesProject(CapturedProjectContext context, Project project)
    {
        var targetPath = ResolveProjectValue(context.ProjectFile, Get(context.Properties, "TargetPath"));
        var intermediateAssembly = ResolveProjectValue(context.ProjectFile, Get(context.Properties, "IntermediateAssembly"));
        return (!string.IsNullOrWhiteSpace(targetPath) && PathEquals(targetPath, project.OutputFilePath))
            || (!string.IsNullOrWhiteSpace(intermediateAssembly)
                && PathEquals(intermediateAssembly, project.CompilationOutputInfo.AssemblyPath));
    }

    private static string? ResolveProjectValue(string projectFile, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Path.IsPathRooted(value)
            ? Path.GetFullPath(value)
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectFile)!, value));
    }

    private void Dump(string label, CaptureRun capture)
    {
        _output.WriteLine(
            "{0}: openMs={1:F1} replayMs={2:F1} files={3} bytes={4} projects={5} analyzerItems={6}",
            label,
            capture.OpenDuration.TotalMilliseconds,
            capture.ReplayDuration.TotalMilliseconds,
            capture.BinlogPaths.Count,
            capture.TotalBytes,
            capture.ProjectContexts.Count,
            capture.AnalyzerItems.Count);
        foreach (var item in capture.AnalyzerItems)
        {
            var nested = capture.ProjectContexts.Where(context =>
                string.Equals(context.BinlogPath, item.BinlogPath, StringComparison.OrdinalIgnoreCase)
                && context.ParentContext == item.TaskContext
                && PathEquals(context.ProjectFile, item.SourceProjectFile));
            _output.WriteLine(
                "  item={0}; source={1}; nearest={2}; setTfm={3}",
                item.Identity,
                item.SourceProjectFile ?? "(empty)",
                item.NearestTargetFramework ?? "(empty)",
                item.SetTargetFramework ?? "(empty)");
            foreach (var context in nested)
            {
                _output.WriteLine(
                    "    nested cfg={0}; tfm={1}; target={2}; intermediate={3}",
                    Get(context.GlobalProperties, "Configuration") ?? "(empty)",
                    Get(context.GlobalProperties, "TargetFramework") ?? "(empty)",
                    Get(context.Properties, "TargetPath") ?? "(empty)",
                    Get(context.Properties, "IntermediateAssembly") ?? "(empty)");
            }
        }
        foreach (var project in capture.Solution.Projects)
        {
            _output.WriteLine(
                "  loaded={0}; file={1}; output={2}; compilation={3}",
                project.Name,
                project.FilePath,
                project.OutputFilePath,
                project.CompilationOutputInfo.AssemblyPath);
        }
    }

    private static void AddMissingSameNameAnalyzer(string projectPath)
    {
        var text = File.ReadAllText(projectPath);
        text = text.Replace(
            "</Project>",
            """
              <ItemGroup>
                <Analyzer Include="..\missing\Generator.dll" />
              </ItemGroup>
            </Project>
            """,
            StringComparison.Ordinal);
        File.WriteAllText(projectPath, text);
    }

    private static void MakeGeneratorMultiTargeted(string projectPath)
    {
        var text = File.ReadAllText(projectPath)
            .Replace(
                "<TargetFramework>netstandard2.0</TargetFramework>",
                "<TargetFrameworks>netstandard2.0;net10.0</TargetFrameworks>",
                StringComparison.Ordinal);
        File.WriteAllText(projectPath, text);
    }

    private static void SetTargetFramework(string projectPath, string targetFramework)
    {
        var text = File.ReadAllText(projectPath)
            .Replace(
                "<TargetFramework>netstandard2.0</TargetFramework>",
                $"<TargetFramework>{targetFramework}</TargetFramework>",
                StringComparison.Ordinal);
        File.WriteAllText(projectPath, text);
    }

    private static void AddReleaseConfiguration(string solutionPath)
    {
        var text = File.ReadAllText(solutionPath);
        text = text.Replace(
            "\t\tDebug|Any CPU = Debug|Any CPU",
            "\t\tDebug|Any CPU = Debug|Any CPU\r\n\t\tRelease|Any CPU = Release|Any CPU",
            StringComparison.Ordinal);
        text = text.Replace(
            "\t\t{id}.Debug|Any CPU.Build.0 = Debug|Any CPU",
            "\t\t{id}.Debug|Any CPU.Build.0 = Debug|Any CPU",
            StringComparison.Ordinal);
        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.None).ToList();
        var additions = lines
            .Where(line => line.Contains(".Debug|Any CPU.ActiveCfg = Debug|Any CPU", StringComparison.Ordinal))
            .SelectMany(line =>
            {
                var prefix = line[..line.IndexOf(".Debug|", StringComparison.Ordinal)];
                return new[]
                {
                    prefix + ".Release|Any CPU.ActiveCfg = Release|Any CPU",
                    prefix + ".Release|Any CPU.Build.0 = Release|Any CPU",
                };
            })
            .ToList();
        var end = lines.FindLastIndex(line => line.Contains("EndGlobalSection", StringComparison.Ordinal));
        lines.InsertRange(end, additions);
        File.WriteAllText(solutionPath, string.Join(Environment.NewLine, lines));
    }

    private static IReadOnlyDictionary<string, FileStamp> SnapshotBuildOutputs(string root)
    {
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || path.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => Path.GetExtension(path) is ".dll" or ".pdb" or ".exe")
            .ToDictionary(
                Path.GetFullPath,
                path => new FileStamp(
                    File.GetLastWriteTimeUtc(path),
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))),
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    private static void AssertBuildOutputsUnchanged(
        IReadOnlyDictionary<string, FileStamp> before,
        IReadOnlyDictionary<string, FileStamp> after)
    {
        var changed = before.Keys
            .Union(after.Keys, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .Where(path => !before.TryGetValue(path, out var oldStamp)
                || !after.TryGetValue(path, out var newStamp)
                || oldStamp != newStamp)
            .ToList();
        Assert.True(
            changed.Count == 0,
            "Design-time capture changed build outputs:" + Environment.NewLine + string.Join(Environment.NewLine, changed));
    }

    private static Dictionary<string, string> Copy(IDictionary<string, string>? source) =>
        source is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(source, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> ReadProperties(IEnumerable? properties)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (properties is null)
        {
            return result;
        }

        foreach (var property in properties)
        {
            if (property is DictionaryEntry entry && entry.Key is string name)
            {
                result[name] = entry.Value?.ToString() ?? string.Empty;
                continue;
            }

            var type = property?.GetType();
            var reflectedName = type?.GetProperty("Name")?.GetValue(property)?.ToString()
                ?? type?.GetProperty("Key")?.GetValue(property)?.ToString();
            var reflectedValue = type?.GetProperty("EvaluatedValue")?.GetValue(property)?.ToString()
                ?? type?.GetProperty("Value")?.GetValue(property)?.ToString();
            if (!string.IsNullOrWhiteSpace(reflectedName))
            {
                result[reflectedName] = reflectedValue ?? string.Empty;
            }
        }

        return result;
    }

    private static string? Get(IReadOnlyDictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static bool PathEquals(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private static async Task DeleteDirectoryWithRetryAsync(string path)
    {
        for (var attempt = 0; attempt < 20 && Directory.Exists(path); attempt++)
        {
            DeleteDirectoryBestEffort(path);
            if (Directory.Exists(path))
            {
                await Task.Delay(50);
            }
        }
    }

    private sealed record CaptureRun(
        Solution Solution,
        IReadOnlyList<CapturedProjectContext> ProjectContexts,
        IReadOnlyList<CapturedAnalyzerItem> AnalyzerItems,
        string TempDirectory,
        IReadOnlyList<string> BinlogPaths,
        long TotalBytes,
        TimeSpan OpenDuration,
        TimeSpan ReplayDuration);

    private sealed record MetricSamples(
        IReadOnlyList<TimeSpan> Baselines,
        IReadOnlyList<CaptureRun> Captures,
        IReadOnlyList<double> AdditionalMilliseconds);

    private sealed record ReplayOutcome(
        CaptureStatus Status,
        IReadOnlyList<CapturedProjectContext> Contexts,
        IReadOnlyList<CapturedAnalyzerItem> Items,
        IReadOnlyList<string> Errors);

    private sealed record CapturedProjectContext(
        string BinlogPath,
        ProjectContextKey Context,
        EventContextKey? ParentContext,
        string ProjectFile,
        IReadOnlyDictionary<string, string> GlobalProperties,
        IReadOnlyDictionary<string, string> Properties);

    private sealed record CapturedAnalyzerItem(
        string BinlogPath,
        ProjectContextKey ConsumerContext,
        EventContextKey TaskContext,
        string Identity,
        string? SourceProjectFile,
        string? NearestTargetFramework,
        string? SetTargetFramework,
        IReadOnlyDictionary<string, string> Metadata);

    private sealed record JoinResult(
        IReadOnlyList<Project> ConsumerCandidates,
        IReadOnlyList<Project> SourceCandidates);

    private readonly record struct ProjectContextKey(
        int SubmissionId,
        int NodeId,
        int ProjectInstanceId,
        int ProjectContextId)
    {
        public static ProjectContextKey From(BuildEventContext context) =>
            new(context.SubmissionId, context.NodeId, context.ProjectInstanceId, context.ProjectContextId);
    }

    private readonly record struct EventContextKey(
        int SubmissionId,
        int NodeId,
        int ProjectInstanceId,
        int ProjectContextId,
        int TargetId,
        int TaskId)
    {
        public static EventContextKey From(BuildEventContext context) =>
            new(
                context.SubmissionId,
                context.NodeId,
                context.ProjectInstanceId,
                context.ProjectContextId,
                context.TargetId,
                context.TaskId);
    }

    private readonly record struct FileStamp(DateTime LastWriteUtc, string Sha256);

    private enum CaptureStatus
    {
        Complete,
        Incomplete,
        Failed,
    }
}
