using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Xunit;

namespace RoslynMcpServer.Tests.TestRunner;

public sealed class TestDllEnsureSolutionFixtureTests
{
    [Fact(Timeout = 180000)]
    public async Task Solution_target_build_produces_the_dll_and_tests_use_no_build()
    {
        var root = Path.Combine(Path.GetTempPath(), "RoslynMcpDllEnsure", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var libDir = Path.Combine(root, "Lib");
            var appDir = Path.Combine(root, "AppTests");
            Directory.CreateDirectory(libDir);
            Directory.CreateDirectory(appDir);
            await File.WriteAllTextAsync(Path.Combine(libDir, "Lib.csproj"), LibProject);
            await File.WriteAllTextAsync(Path.Combine(libDir, "Marker.cs"), LibSource);
            await File.WriteAllTextAsync(Path.Combine(appDir, "AppTests.csproj"), AppProject);
            await File.WriteAllTextAsync(Path.Combine(appDir, "Program.cs"), AppSource);
            var solution = Path.Combine(root, "App.sln");
            await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.props"), "<Project />");
            await File.WriteAllTextAsync(solution, SolutionText);

            var dll = Path.Combine(appDir, "bin", "Debug", "net10.0", "AppTests.dll");
            var libraryDll = Path.Combine(libDir, "bin", "Debug", "net10.0", "Lib.dll");
            var appId = ProjectId.CreateNewId();
            var libraryId = ProjectId.CreateNewId();
            var context = new BuildContext("Debug", null, "net10.0", null, dll);
            var state = new SessionBuildState();
            var ensure = new TestDllEnsure(state, Run);
            var request = Request(root, solution, dll, appId, libraryId, context);

            var validation = Stopwatch.StartNew();
            state.Observe(request.PullSnapshot());
            var before = state.EvaluateReuse(appId, context);
            validation.Stop();

            var firstClock = Stopwatch.StartNew();
            var first = await ensure.ExecuteAsync(request);
            firstClock.Stop();

            var unchanged = Stopwatch.StartNew();
            var decision = state.EvaluateReuse(appId, context);
            unchanged.Stop();

            var secondClock = Stopwatch.StartNew();
            var second = await ensure.ExecuteAsync(request);
            secondClock.Stop();

            Assert.True(
                first.TestsStarted,
                first.Error
                + Environment.NewLine
                + first.BuildArguments
                + Environment.NewLine
                + first.BuildRun?.CombinedOutput);
            Assert.True(File.Exists(dll));
            Assert.True(File.Exists(libraryDll));
            Assert.Contains($"build \"{solution}\"", first.BuildArguments, StringComparison.Ordinal);
            Assert.Contains("-t:\"AppTests\"", first.BuildArguments, StringComparison.Ordinal);
            Assert.DoesNotContain("AppTests.csproj", first.BuildArguments, StringComparison.Ordinal);
            Assert.Contains("--no-build", first.TestArguments, StringComparison.Ordinal);
            Assert.Contains(dll, first.TestArguments, StringComparison.Ordinal);
            Assert.DoesNotContain("-t:", first.TestArguments, StringComparison.Ordinal);
            Assert.NotNull(first.TestRun);
            Assert.False(before.SkipAllowed);
            Assert.False(decision.SkipAllowed);
            Assert.NotEqual(BuildFreshnessState.Current, decision.State);
            Assert.Contains(BuildReuseBlocker.InputContentHashAbsent, decision.Blockers);
            Assert.True(second.TestsStarted, second.Error);
            Assert.Contains($"build \"{solution}\"", second.BuildArguments, StringComparison.Ordinal);
            Assert.Contains("--no-build", second.TestArguments, StringComparison.Ordinal);
            Assert.NotEqual(TestDllEnsure.ReasonConfirmedReuse, second.Reason);
            Assert.Null(state.GetProof(libraryId, context));
            Assert.NotEqual(BuildFreshnessState.Current, state.GetState(libraryId, context));
            Assert.False(state.TryRecordInvocation("execute_dotnet_command"));

            var timing = "EPOCH3_TIMING"
                + " runtime=" + RuntimeInformation.FrameworkDescription
                + " validation_ms=" + validation.ElapsedMilliseconds
                + " unchanged_ms=" + unchanged.ElapsedMilliseconds
                + " first_ms=" + firstClock.ElapsedMilliseconds
                + " second_ms=" + secondClock.ElapsedMilliseconds
                + " reason=" + second.Reason
                + " skip=" + decision.SkipAllowed
                + Environment.NewLine
                + first.BuildRun?.RunMetadata;
            Console.WriteLine(timing);
            await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "RoslynMcpEpoch3Timing.txt"), timing);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (IOException)
            {
                // A build process can keep a file open for a moment after it exits.
            }
        }
    }

    private static TestDllEnsureRequest Request(
        string root,
        string solution,
        string dll,
        ProjectId appId,
        ProjectId libraryId,
        BuildContext context)
    {
        var snapshot = Snapshot(root, appId, libraryId);
        return new TestDllEnsureRequest
        {
            ProjectId = appId,
            Context = context,
            Policy = TestBuildPolicy.Auto,
            PullSnapshot = () => snapshot,
            SolutionPath = solution,
            ProjectNeedle = "AppTests",
            AssemblyPath = dll,
            AssemblyExists = File.Exists,
            BuildWorkingDirectory = root,
            TestWorkingDirectory = root,
            Timeout = TimeSpan.FromSeconds(120),
        };
    }

    private static WorkspaceInputSnapshot Snapshot(string root, ProjectId appId, ProjectId libraryId)
    {
        var appDirectory = Path.Combine(root, "AppTests");
        var libraryDirectory = Path.Combine(root, "Lib");
        var app = new LoadedProjectInstance(
            appId,
            Path.Combine(appDirectory, "AppTests.csproj"),
            "AppTests",
            "AppTests",
            new[] { libraryId },
            new[] { "net10.0" });
        var library = new LoadedProjectInstance(
            libraryId,
            Path.Combine(libraryDirectory, "Lib.csproj"),
            "Lib",
            "Lib",
            Array.Empty<ProjectId>(),
            new[] { "net10.0" });
        var paths = new[]
        {
            new InputPathSnapshot(
                Path.Combine(appDirectory, "Program.cs"),
                1,
                0,
                true,
                false,
                new[] { appId },
                new[] { InputRole.UserInput },
                Array.Empty<ProjectId>()),
            new InputPathSnapshot(
                Path.Combine(libraryDirectory, "Marker.cs"),
                1,
                0,
                true,
                false,
                new[] { libraryId },
                new[] { InputRole.UserInput },
                Array.Empty<ProjectId>()),
        };
        var regions = new[]
        {
            new MembershipRegion(appDirectory, true, appId, "project-directory", true),
            new MembershipRegion(libraryDirectory, true, libraryId, "project-directory", true),
        };
        return new WorkspaceInputSnapshot(
            Guid.NewGuid(),
            1,
            1,
            new[] { app.FilePath!, library.FilePath! },
            new[] { app, library },
            regions,
            paths,
            InputCoverage.Unknown,
            new[] { new InputCoverageGap(InputCoverageReason.LoadIncomplete, InputCoverageScope.WholeGraph) },
            "fixture",
            false);
    }

    private static Task<DotNetCliRunner.RunResult> Run(
        string arguments,
        string? workingDirectory,
        TimeSpan? timeout,
        int? previousExit,
        CancellationToken cancellationToken)
    {
        return DotNetCliRunner.RunWithMetadataAsync(arguments, workingDirectory, cancellationToken, timeout);
    }

    private const string LibProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
        </Project>
        """;

    private const string AppProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="..\Lib\Lib.csproj" />
          </ItemGroup>
        </Project>
        """;

    private const string LibSource = """
        namespace Lib;

        public static class Marker
        {
            public static int Value => 7;
        }
        """;

    private const string AppSource = """
        namespace AppTests;

        public static class Program
        {
            public static int Main() => Lib.Marker.Value;
        }
        """;

    private const string SolutionText = """
        Microsoft Visual Studio Solution File, Format Version 12.00
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Lib", "Lib\Lib.csproj", "{11111111-1111-1111-1111-111111111111}"
        EndProject
        Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "AppTests", "AppTests\AppTests.csproj", "{22222222-2222-2222-2222-222222222222}"
        EndProject
        Global
        	GlobalSection(SolutionConfigurationPlatforms) = preSolution
        		Debug|Any CPU = Debug|Any CPU
        	EndGlobalSection
        	GlobalSection(ProjectConfigurationPlatforms) = postSolution
        		{11111111-1111-1111-1111-111111111111}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
        		{11111111-1111-1111-1111-111111111111}.Debug|Any CPU.Build.0 = Debug|Any CPU
        		{22222222-2222-2222-2222-222222222222}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
        		{22222222-2222-2222-2222-222222222222}.Debug|Any CPU.Build.0 = Debug|Any CPU
        	EndGlobalSection
        EndGlobal
        """;
}
