using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.TestRunner;

public sealed class TestToolsDllBuildTests
{
    [Fact]
    public async Task Three_tools_reject_both_parameters_without_starting_a_process()
    {
        var runner = new RecordingRunner();
        var tools = Tools(runner);
        const string path = @"C:\missing\AppTests.csproj";

        var dotnet = await tools.RunDotNetTest(path, binariesPath: @"C:\bin", noBuild: false, buildPolicy: "auto");
        var specific = await tools.RunSpecificTest(path, className: "Sample", binariesPath: @"C:\bin", noBuild: true, buildPolicy: "never");
        var filter = await tools.RunTestByFilter(path, "FullyQualifiedName~Sample", binariesPath: @"C:\bin", noBuild: false, buildPolicy: "always");

        Assert.Contains("not both", dotnet, StringComparison.Ordinal);
        Assert.Contains("not both", specific, StringComparison.Ordinal);
        Assert.Contains("not both", filter, StringComparison.Ordinal);
        Assert.Empty(runner.Arguments);
    }

    [Fact]
    public async Task Three_tools_reject_build_policy_without_binaries_path()
    {
        var runner = new RecordingRunner();
        var tools = Tools(runner);
        const string path = @"C:\missing\AppTests.csproj";

        var dotnet = await tools.RunDotNetTest(path, buildPolicy: "always");
        var specific = await tools.RunSpecificTest(path, className: "Sample", buildPolicy: "auto");
        var filter = await tools.RunTestByFilter(path, "FullyQualifiedName~Sample", buildPolicy: "never");

        Assert.Contains("requires `binariesPath`", dotnet, StringComparison.Ordinal);
        Assert.Contains("requires `binariesPath`", specific, StringComparison.Ordinal);
        Assert.Contains("requires `binariesPath`", filter, StringComparison.Ordinal);
        Assert.Empty(runner.Arguments);
    }

    [Fact]
    public async Task Omitted_no_build_on_the_non_dll_route_is_false_false_and_true()
    {
        using var tree = new TempTree();
        var csproj = tree.Write("AppTests.csproj", "<Project />");
        var runner = new RecordingRunner();
        var tools = Tools(runner);

        await tools.RunDotNetTest(csproj, timeoutSeconds: 12);
        AssertBuildThenTest(runner, csproj);
        runner.Clear();

        await tools.RunSpecificTest(csproj, className: "Sample", timeoutSeconds: 12);
        AssertBuildThenTest(runner, csproj);
        runner.Clear();

        await tools.RunTestByFilter(csproj, "FullyQualifiedName~Sample", timeoutSeconds: 12);
        var only = Assert.Single(runner.Arguments);
        Assert.StartsWith("test ", only, StringComparison.Ordinal);
        Assert.Contains("--no-build", only, StringComparison.Ordinal);
        Assert.Contains(csproj, only, StringComparison.Ordinal);
        Assert.DoesNotContain("-t:", only, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("dotnet", false)]
    [InlineData("dotnet", true)]
    [InlineData("specific", false)]
    [InlineData("specific", true)]
    [InlineData("filter", false)]
    [InlineData("filter", true)]
    public async Task Successful_pre_test_build_only_shows_warnings_when_requested(string tool, bool includeWarnings)
    {
        using var tree = new TempTree();
        var csproj = tree.Write("AppTests.csproj", "<Project />");
        var runner = new RecordingRunner
        {
            BuildOutput = "App.cs(1,1): warning CS0168: Unused variable",
            TestOutput = "Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1",
        };
        var tools = Tools(runner);
        var report = tool switch
        {
            "dotnet" => await tools.RunDotNetTest(csproj, includeBuildWarnings: includeWarnings),
            "specific" => await tools.RunSpecificTest(csproj, className: "Sample", includeBuildWarnings: includeWarnings),
            _ => await tools.RunTestByFilter(csproj, "FullyQualifiedName~Sample", noBuild: false, includeBuildWarnings: includeWarnings),
        };

        Assert.Equal(2, runner.Arguments.Count);
        Assert.Equal(includeWarnings, report.Contains("CS0168", StringComparison.Ordinal));
        Assert.DoesNotContain("build-meta", report, StringComparison.Ordinal);
        Assert.DoesNotContain("test-meta", report, StringComparison.Ordinal);
        Assert.DoesNotContain("Execution context", report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_pre_test_build_keeps_errors_and_context(bool includeWarnings)
    {
        using var tree = new TempTree();
        var csproj = tree.Write("AppTests.csproj", "<Project />");
        var runner = new RecordingRunner
        {
            BuildExitCode = 1,
            BuildOutput = "warning CS0168: Unused variable\nerror CS1001: Identifier expected",
        };
        var report = await Tools(runner).RunDotNetTest(csproj, includeBuildWarnings: includeWarnings);

        Assert.Single(runner.Arguments);
        Assert.Contains("CS1001", report, StringComparison.Ordinal);
        Assert.Contains("### Execution context", report, StringComparison.Ordinal);
        Assert.Contains("build-meta", report, StringComparison.Ordinal);
        Assert.Equal(includeWarnings, report.Contains("CS0168", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Three_tools_share_one_session_ensure_and_a_second_auto_call_still_builds()
    {
        using var tree = new TempTree();
        using var workspace = new AdhocWorkspace();
        var csproj = tree.Write("AppTests.csproj", "<Project />");
        var sln = tree.Write("App.sln", SingleProjectSln);
        var bin = Path.Combine(tree.Root, "bin");
        Directory.CreateDirectory(bin);
        var dll = Path.Combine(bin, "AppTests.dll");
        File.WriteAllBytes(dll, new byte[] { 1 });
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "AppTests",
            "AppTests",
            LanguageNames.CSharp,
            filePath: csproj));
        var manager = SolutionManagerTestFactory.Create();
        Set(manager, "_loadedPath", sln);
        Set(manager, "_solution", solution);
        manager.GetType().GetProperty(nameof(SolutionManager.LoadedTargetFramework))!.SetValue(manager, "net10.0");
        manager.ApplySessionBuildArgs("-p:TreatWarningsAsErrors=false");
        Assert.False(manager.SessionBuildState.TryRecordInvocation("execute_dotnet_command"));

        var runner = new RecordingRunner { BuildOutput = "App.cs(1,1): warning CS0168: BUILD-ONLY-LOG", TestOutput = "Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1" };
        var tools = new TestTools(manager, NullLogger<TestTools>.Instance, runner.Run);
        var context = new BuildContext("Sit-Debug", "x64", "net10.0", "-p:TreatWarningsAsErrors=false", dll);

        var first = await tools.RunDotNetTest(
            csproj,
            timeoutSeconds: 12,
            configuration: "Sit-Debug",
            platform: "x64",
            binariesPath: bin);
        AssertSolutionBuildThenDllTest(runner, sln, dll);
        Assert.DoesNotContain("Execution context", first, StringComparison.Ordinal);
        Assert.DoesNotContain("build-meta", first, StringComparison.Ordinal);
        Assert.DoesNotContain("test-meta", first, StringComparison.Ordinal);
        Assert.DoesNotContain("BUILD-ONLY-LOG", first, StringComparison.Ordinal);
        var proof = manager.SessionBuildState.GetProof(projectId, context);
        Assert.NotNull(proof);
        Assert.Equal(context.OutputPath, InputPathCanon.TryCanonicalize(proof.OutputIdentity) ?? proof.OutputIdentity);
        Assert.False(string.Equals(proof.OutputIdentity, "execute_dotnet_command", StringComparison.Ordinal));
        Assert.NotEqual(BuildFreshnessState.Current, manager.SessionBuildState.GetState(projectId, context));

        runner.Clear();
        await tools.RunDotNetTest(csproj, timeoutSeconds: 12, configuration: "Sit-Debug", platform: "x64", binariesPath: bin);
        AssertSolutionBuildThenDllTest(runner, sln, dll);

        runner.Clear();
        var withWarnings = await tools.RunSpecificTest(csproj, className: "Sample", timeoutSeconds: 12, configuration: "Sit-Debug", platform: "x64", binariesPath: bin, includeBuildWarnings: true);
        Assert.Contains("CS0168", withWarnings, StringComparison.Ordinal);
        Assert.DoesNotContain("Execution context", withWarnings, StringComparison.Ordinal);
        AssertSolutionBuildThenDllTest(runner, sln, dll);

        runner.Clear();
        await tools.RunTestByFilter(csproj, "FullyQualifiedName~Sample", timeoutSeconds: 12, configuration: "Sit-Debug", platform: "x64", binariesPath: bin);
        AssertSolutionBuildThenDllTest(runner, sln, dll);
        Assert.NotEqual(BuildFreshnessState.Current, manager.SessionBuildState.GetState(projectId, context));
    }

    [Fact]
    public async Task Dll_build_failure_does_not_start_tests()
    {
        using var tree = new TempTree();
        using var workspace = new AdhocWorkspace();
        var csproj = tree.Write("AppTests.csproj", "<Project />");
        var sln = tree.Write("App.sln", SingleProjectSln);
        var bin = Path.Combine(tree.Root, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllBytes(Path.Combine(bin, "AppTests.dll"), new byte[] { 1 });
        var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Create(),
            "AppTests",
            "AppTests",
            LanguageNames.CSharp,
            filePath: csproj));
        var manager = SolutionManagerTestFactory.Create();
        Set(manager, "_loadedPath", sln);
        Set(manager, "_solution", solution);
        var runner = new RecordingRunner { BuildExitCode = 1, BuildOutput = "error CS1001: build failed" };
        var tools = new TestTools(manager, NullLogger<TestTools>.Instance, runner.Run);

        var result = await tools.RunDotNetTest(csproj, timeoutSeconds: 12, binariesPath: bin);

        var command = Assert.Single(runner.Arguments);
        Assert.StartsWith($"build \"{sln}\"", command, StringComparison.Ordinal);
        Assert.Contains("-t:\"AppTests\"", command, StringComparison.Ordinal);
        Assert.DoesNotContain($"build \"{csproj}\"", command, StringComparison.Ordinal);
        Assert.Contains("Tests were not started", result, StringComparison.Ordinal);
        Assert.DoesNotContain("## Test run", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_loaded_solution_does_not_start_a_process()
    {
        using var tree = new TempTree();
        var csproj = tree.Write("AppTests.csproj", "<Project />");
        var bin = Path.Combine(tree.Root, "bin");
        Directory.CreateDirectory(bin);
        var runner = new RecordingRunner();
        var tools = Tools(runner);

        var dotnet = await tools.RunDotNetTest(csproj, timeoutSeconds: 12, binariesPath: bin);
        var specific = await tools.RunSpecificTest(csproj, className: "Sample", timeoutSeconds: 12, binariesPath: bin);
        var filter = await tools.RunTestByFilter(csproj, "FullyQualifiedName~Sample", timeoutSeconds: 12, binariesPath: bin);

        Assert.Contains(".sln", dotnet, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".sln", specific, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".sln", filter, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(runner.Arguments);
    }

    private const string SingleProjectSln = """
        Microsoft Visual Studio Solution File, Format Version 12.00
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "AppTests", "AppTests.csproj", "{11111111-1111-1111-1111-111111111111}"
        EndProject
        Global
        EndGlobal
        """;

    private static TestTools Tools(RecordingRunner runner) =>
        new(SolutionManagerTestFactory.Create(), NullLogger<TestTools>.Instance, runner.Run);

    private static void AssertBuildThenTest(RecordingRunner runner, string csproj)
    {
        Assert.Equal(2, runner.Arguments.Count);
        Assert.Equal($"build \"{csproj}\"", runner.Arguments[0]);
        Assert.DoesNotContain("-t:", runner.Arguments[0], StringComparison.Ordinal);
        Assert.StartsWith("test ", runner.Arguments[1], StringComparison.Ordinal);
        Assert.Contains("--no-build", runner.Arguments[1], StringComparison.Ordinal);
        Assert.Contains(csproj, runner.Arguments[1], StringComparison.Ordinal);
    }

    private static void AssertSolutionBuildThenDllTest(RecordingRunner runner, string sln, string dll)
    {
        Assert.Equal(2, runner.Arguments.Count);
        Assert.Equal(
            $"build \"{sln}\" -t:\"AppTests\" -p:Configuration=\"Sit-Debug\" -p:Platform=\"x64\" -p:TargetFramework=\"net10.0\" -p:TreatWarningsAsErrors=false",
            runner.Arguments[0]);
        Assert.DoesNotContain("AppTests.csproj", runner.Arguments[0], StringComparison.Ordinal);
        Assert.StartsWith($"test \"{dll}\"", runner.Arguments[1], StringComparison.Ordinal);
        Assert.Contains("--no-build", runner.Arguments[1], StringComparison.Ordinal);
        Assert.DoesNotContain("AppTests.csproj", runner.Arguments[1], StringComparison.Ordinal);
        Assert.DoesNotContain("-t:", runner.Arguments[1], StringComparison.Ordinal);
    }

    private static void Set(SolutionManager manager, string fieldName, object value)
    {
        typeof(SolutionManager)
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, value);
    }

    private sealed class RecordingRunner
    {
        public List<string> Arguments { get; } = new();

        public int BuildExitCode { get; set; }

        public string BuildOutput { get; set; } = "build-log";

        public string TestOutput { get; set; } = "test-log";

        public void Clear() => Arguments.Clear();

        public Task<DotNetCliRunner.RunResult> Run(
            string arguments,
            string? workingDirectory,
            TimeSpan? timeout,
            int? previousExit,
            CancellationToken cancellationToken)
        {
            Arguments.Add(arguments);
            if (arguments.StartsWith("build ", StringComparison.Ordinal))
            {
                return Task.FromResult(new DotNetCliRunner.RunResult(
                    BuildExitCode,
                    BuildOutput,
                    "build-meta",
                    0,
                    0));
            }

            return Task.FromResult(new DotNetCliRunner.RunResult(0, TestOutput, "test-meta", 0, 0));
        }
    }

    private sealed class TempTree : IDisposable
    {
        public TempTree()
        {
            Root = Path.Combine(Path.GetTempPath(), "RoslynMcpDllEnsure", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Write(string name, string text)
        {
            var path = Path.Combine(Root, name);
            File.WriteAllText(path, text);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
