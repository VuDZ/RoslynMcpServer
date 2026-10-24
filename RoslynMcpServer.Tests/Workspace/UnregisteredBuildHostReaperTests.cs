using System.Diagnostics;
using System.Globalization;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Services.Workspace;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

public sealed class UnregisteredBuildHostReaperTests
{
    private const int OwnProcessId = 10;

    private static readonly DateTime Started = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Local);

    [Fact]
    public void Select_terminates_an_unregistered_child_build_host()
    {
        var plan = Select(new Dictionary<int, BuildHostProcessObservation>
        {
            [30] = Host(30, parentId: OwnProcessId, Started.AddSeconds(1)),
        });

        Assert.Equal([30], plan.ProcessIds);
        Assert.Equal(0, plan.UnidentifiedChildren);
    }

    [Fact]
    public void Select_leaves_an_in_use_host()
    {
        var plan = Select(
            new Dictionary<int, BuildHostProcessObservation>
            {
                [30] = Host(30, parentId: OwnProcessId, Started.AddSeconds(1)),
            },
            inUse: new HashSet<int> { 30 });

        Assert.Empty(plan.ProcessIds);
        Assert.Equal(0, plan.UnidentifiedChildren);
    }

    [Fact]
    public void Select_leaves_a_host_of_another_process()
    {
        var plan = Select(new Dictionary<int, BuildHostProcessObservation>
        {
            [30] = Host(30, parentId: 99, Started.AddSeconds(1)),
            [99] = Observed(99, parentId: 100, "other", commandLine: "other", Started),
        });

        Assert.Empty(plan.ProcessIds);
    }

    [Fact]
    public void Select_leaves_a_child_dotnet_that_is_not_a_build_host()
    {
        var plan = Select(new Dictionary<int, BuildHostProcessObservation>
        {
            [30] = Observed(30, OwnProcessId, "dotnet", "dotnet build app.csproj", Started.AddSeconds(1)),
        });

        Assert.Empty(plan.ProcessIds);
        Assert.Equal(0, plan.UnidentifiedChildren);
    }

    [Fact]
    public void Select_counts_a_child_with_no_command_line_and_does_not_terminate_it()
    {
        var plan = Select(new Dictionary<int, BuildHostProcessObservation>
        {
            [30] = Observed(30, OwnProcessId, "dotnet", commandLine: null, Started.AddSeconds(1)),
        });

        Assert.Empty(plan.ProcessIds);
        Assert.Equal(1, plan.UnidentifiedChildren);
    }

    [Fact]
    public void Select_terminates_a_framework_host_by_process_name()
    {
        var plan = Select(new Dictionary<int, BuildHostProcessObservation>
        {
            [30] = Observed(
                30,
                OwnProcessId,
                UnregisteredBuildHostReaper.FrameworkHostProcessName,
                commandLine: null,
                Started.AddSeconds(1)),
        });

        Assert.Equal([30], plan.ProcessIds);
    }

    [Fact]
    public void Select_leaves_a_build_host_of_a_program_this_process_started()
    {
        // A tool such as run_dotnet_run starts a program that loads its own workspace; its host may be in use.
        var plan = Select(new Dictionary<int, BuildHostProcessObservation>
        {
            [20] = Observed(20, OwnProcessId, "dotnet", "dotnet run --project app.csproj", Started.AddSeconds(1)),
            [30] = Host(30, parentId: 20, Started.AddSeconds(2)),
        });

        Assert.Empty(plan.ProcessIds);
    }

    [Fact]
    public void Select_rejects_a_child_of_an_earlier_process_with_the_same_id()
    {
        var plan = Select(new Dictionary<int, BuildHostProcessObservation>
        {
            [30] = Host(30, parentId: OwnProcessId, Started.AddSeconds(-5)),
        });

        Assert.Empty(plan.ProcessIds);
    }

    [Fact]
    public void Select_accepts_a_child_start_inside_the_tolerance()
    {
        var plan = Select(
            new Dictionary<int, BuildHostProcessObservation>
            {
                [30] = Host(30, parentId: OwnProcessId, Started.AddMilliseconds(-50)),
            },
            tolerance: TimeSpan.FromMilliseconds(100));

        Assert.Equal([30], plan.ProcessIds);
    }

    [Fact]
    public void Select_leaves_a_host_that_has_exited_and_this_process()
    {
        var plan = Select(new Dictionary<int, BuildHostProcessObservation>
        {
            [OwnProcessId] = Host(OwnProcessId, parentId: 1, Started),
            [30] = Host(30, OwnProcessId, Started.AddSeconds(1)) with { HasExited = true },
        });

        Assert.Empty(plan.ProcessIds);
    }

    [Fact]
    public void Reap_with_no_matching_id_terminates_nothing()
    {
        var plan = UnregisteredBuildHostReaper.Reap(
            NullLogger.Instance,
            inUseProcessIds: null,
            limitToProcessIds: new HashSet<int> { -1 });

        Assert.Empty(plan.ProcessIds);
        Assert.Equal(0, plan.UnidentifiedChildren);
    }

    [Fact]
    public void Reap_kills_an_unregistered_child_build_host_and_leaves_another_child()
    {
        var hostDll = RequirePackagedBuildHost();
        using var bystander = StartBystander();
        using var host = StartBuildHost(hostDll);
        try
        {
            WaitUntilRunning(bystander);
            WaitUntilRunning(host);
            var onlyThese = new HashSet<int> { host.Id, bystander.Id };

            var spared = UnregisteredBuildHostReaper.Reap(
                NullLogger.Instance,
                inUseProcessIds: new HashSet<int> { host.Id },
                onlyThese);
            host.Refresh();
            bystander.Refresh();
            Assert.False(host.HasExited);
            Assert.False(bystander.HasExited);
            Assert.Empty(spared.ProcessIds);

            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            {
                var left = UnregisteredBuildHostReaper.Reap(
                    NullLogger.Instance,
                    inUseProcessIds: null,
                    onlyThese);
                host.Refresh();
                Assert.False(host.HasExited);
                Assert.Empty(left.ProcessIds);
                return;
            }

            var killed = UnregisteredBuildHostReaper.Reap(
                NullLogger.Instance,
                inUseProcessIds: null,
                onlyThese);
            WaitUntilExited(host);
            bystander.Refresh();
            Assert.False(bystander.HasExited);
            Assert.Contains(host.Id, killed.ProcessIds);
        }
        finally
        {
            KillIfAlive(host);
            KillIfAlive(bystander);
        }
    }

    private static UnregisteredBuildHostReapPlan Select(
        Dictionary<int, BuildHostProcessObservation> processes,
        IReadOnlySet<int>? inUse = null,
        TimeSpan? tolerance = null)
        => UnregisteredBuildHostReaper.Select(
            processes,
            inUse ?? new HashSet<int>(),
            OwnProcessId,
            Started,
            tolerance ?? TimeSpan.Zero);

    private static BuildHostProcessObservation Host(int id, int? parentId, DateTime startTime)
        => Observed(
            id,
            parentId,
            "dotnet",
            "dotnet " + UnregisteredBuildHostReaper.NetCoreHostAssembly,
            startTime);

    private static BuildHostProcessObservation Observed(
        int id,
        int? parentId,
        string name,
        string? commandLine,
        DateTime startTime)
        => new(id, parentId, startTime, name, commandLine, HasExited: false);

    private static string RequirePackagedBuildHost()
    {
        var assemblyPath = typeof(MSBuildWorkspace).Assembly.Location;
        var directory = Path.GetDirectoryName(assemblyPath)
            ?? throw new InvalidOperationException($"Assembly location has no directory: {assemblyPath}");
        var fromLayout = Path.GetFullPath(Path.Combine(
            directory,
            "..",
            "..",
            "contentFiles",
            "any",
            "any",
            "BuildHost-netcore",
            UnregisteredBuildHostReaper.NetCoreHostAssembly));
        if (File.Exists(fromLayout))
        {
            return fromLayout;
        }

        var version = typeof(MSBuildWorkspace).Assembly.GetName().Version
            ?? throw new InvalidOperationException("MSBuild workspace assembly has no version.");
        var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(packages))
        {
            packages = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages");
        }

        var fromCache = Path.Combine(
            packages,
            "microsoft.codeanalysis.workspaces.msbuild",
            $"{version.Major}.{version.Minor}.{version.Build}",
            "contentFiles",
            "any",
            "any",
            "BuildHost-netcore",
            UnregisteredBuildHostReaper.NetCoreHostAssembly);
        if (File.Exists(fromCache))
        {
            return fromCache;
        }

        throw new InvalidOperationException(
            $"BuildHost dll was not found next to '{assemblyPath}' or at '{fromCache}'.");
    }

    private static Process StartBuildHost(string hostDll)
    {
        var start = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["DOTNET_ROLL_FORWARD_TO_PRERELEASE"] = "1";
        start.ArgumentList.Add("--roll-forward");
        start.ArgumentList.Add("LatestMajor");
        start.ArgumentList.Add(hostDll);
        start.ArgumentList.Add(Guid.NewGuid().ToString("N"));
        start.ArgumentList.Add(CultureInfo.CurrentUICulture.Name);
        var process = Process.Start(start) ?? throw new InvalidOperationException("BuildHost did not start.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static Process StartBystander()
    {
        ProcessStartInfo start;
        if (OperatingSystem.IsWindows())
        {
            start = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add("ping");
            start.ArgumentList.Add("-n");
            start.ArgumentList.Add("30");
            start.ArgumentList.Add("127.0.0.1");
        }
        else
        {
            start = new ProcessStartInfo
            {
                FileName = "sleep",
                UseShellExecute = false,
            };
            start.ArgumentList.Add("30");
        }

        return Process.Start(start) ?? throw new InvalidOperationException("Bystander process did not start.");
    }

    private static void WaitUntilRunning(Process process)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            process.Refresh();
            if (!process.HasExited)
            {
                return;
            }

            Thread.Sleep(50);
        }

        throw new InvalidOperationException($"Process {process.Id} exited with code {process.ExitCode}.");
    }

    private static void WaitUntilExited(Process process)
    {
        if (!process.WaitForExit(15_000))
        {
            throw new InvalidOperationException($"Process {process.Id} was still running after reap.");
        }
    }

    private static void KillIfAlive(Process process)
    {
        try
        {
            process.Refresh();
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }
}
