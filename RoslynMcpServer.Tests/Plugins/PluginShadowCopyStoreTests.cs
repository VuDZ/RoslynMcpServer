using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using RoslynMcpServer.Plugins;
using Xunit;

namespace RoslynMcpServer.Tests.Plugins;

public sealed class PluginShadowCopyStoreTests : IDisposable
{
    public PluginShadowCopyStoreTests()
    {
        // A redirected TEMP may be on a volume writable by other accounts. Use a new fixture inside the
        // Windows profile so the production ancestry check is exercised without sweeping the profile itself.
        if (OperatingSystem.IsWindows())
        {
            _temporary = new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
                .CreateSubdirectory(FixturePrefix + Guid.NewGuid().ToString("N"));
        }
        else if (OperatingSystem.IsLinux())
        {
            _temporary = CreateLinuxFixture();
        }
        else
        {
            _temporary = Directory.CreateTempSubdirectory(FixturePrefix);
        }
    }

    public void Dispose() => _temporary.Delete(recursive: true);

    [Fact]
    public void Startup_reclaims_exited_owners_and_keeps_live_legacy_and_unknown_copies()
    {
        if (!SupportsManagedCopies)
        {
            Assert.Null(PluginShadowCopyStore.Prepare(_temporary.FullName, needsCopies: true));
            return;
        }

        var live = CreateLiveSession();
        var exitedPid = GetExitedProcessId();
        var expired = UseOwnerRecord(live, exitedPid);
        var reusedPid = UseOwnerRecord(live, Environment.ProcessId);
        var otherScope = UseOwnerRecord(live, exitedPid, scope: new string('0', 64));
        var invalid = UseOwnerRecord(live, exitedPid);
        File.AppendAllText(OwnerPath(invalid), new string('0', 110));
        var incomplete = UseOwnerRecord(live, exitedPid);
        File.WriteAllText(OwnerPath(incomplete), incomplete.Name);
        var nulPid = UseOwnerRecord(live, exitedPid);
        File.WriteAllText(OwnerPath(nulPid), File.ReadAllText(OwnerPath(nulPid))[..^1] + "\0\n");
        var unterminatedPid = UseOwnerRecord(live, exitedPid);
        File.WriteAllText(OwnerPath(unterminatedPid), File.ReadAllText(OwnerPath(unterminatedPid))[..^1]);
        var legacy = _temporary.CreateSubdirectory(Path.Combine(
            PluginShadowCopy.TempRootDirectoryName, PluginShadowCopy.TempPluginsDirectoryName, Guid.NewGuid().ToString("N")));
        var start = OperatingSystem.IsWindows() ? CommandPrompt("/d", "/c", "set", "/p", "wait=")
            : new ProcessStartInfo("/bin/cat") { UseShellExecute = false };
        start.RedirectStandardInput = true;
        using var livePeer = Process.Start(start);
        Assert.NotNull(livePeer);

        try
        {
            var peer = UseOwnerRecord(live, livePeer.Id);
            PluginShadowCopy.CopyForStartup([], _temporary.FullName);

            Assert.False(livePeer.HasExited);
            Assert.False(Directory.Exists(expired.FullName));
            Assert.All(
                new[] { live, peer, reusedPid, otherScope, invalid, incomplete, nulPid, unterminatedPid, legacy },
                directory => Assert.True(Directory.Exists(directory.FullName), $"Expected to keep {directory.Name}."));
        }
        finally
        {
            livePeer.StandardInput.Close();
            if (!livePeer.WaitForExit(10_000))
            {
                livePeer.Kill();
                livePeer.WaitForExit();
            }
        }
    }

    [Fact]
    public void Startup_falls_back_from_untrusted_temporary_ancestry()
    {
        var temporary = _temporary.CreateSubdirectory("unsafe-temp");
        if (OperatingSystem.IsWindows())
        {
            var acl = temporary.GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Delete, AccessControlType.Allow));
            temporary.SetAccessControl(acl);
        }
        else if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(temporary.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherWrite);
        }

        var state = Path.Combine(_temporary.FullName, "new-state");
        var legacy = Path.Combine(temporary.FullName, PluginShadowCopy.TempRootDirectoryName);
        var source = _temporary.CreateSubdirectory("source");
        var entry = Path.Combine(source.FullName, "sample.dll");
        File.WriteAllText(entry, "Plugin test file");
        var plan = CreatePlan(source.FullName, entry);

        var legacyReport = PluginShadowCopy.CopyForStartup([plan], temporary.FullName);
        var stateReport = PluginShadowCopy.CopyForStartup([plan], temporary.FullName, state);

        Assert.StartsWith(legacy + Path.DirectorySeparatorChar, Assert.Single(legacyReport.Plans).EntryPath);
        var stateCopy = Assert.Single(stateReport.Plans).EntryPath;
        var expectedRoot = OperatingSystem.IsWindows() ? state : legacy;
        Assert.StartsWith(expectedRoot + Path.DirectorySeparatorChar, stateCopy);
        Assert.Equal("Plugin test file", File.ReadAllText(stateCopy));
        Assert.True(File.Exists(Assert.Single(legacyReport.Plans).EntryPath));
    }

    [Fact]
    public async Task Concurrent_startup_passes_leave_both_current_copies_usable()
    {
        if (!SupportsManagedCopies)
        {
            Assert.Null(PluginShadowCopyStore.Prepare(_temporary.FullName, needsCopies: true));
            return;
        }

        var live = CreateLiveSession();
        var expired = UseOwnerRecord(live, GetExitedProcessId());
        var plugin = _temporary.CreateSubdirectory("plugin-source");
        var entryPath = Path.Combine(plugin.FullName, "sample.dll");
        File.WriteAllText(entryPath, "Plugin test file");
        var plan = CreatePlan(plugin.FullName, entryPath);

        var reports = await Task.WhenAll(
            Task.Run(() => PluginShadowCopy.CopyForStartup([plan], _temporary.FullName)),
            Task.Run(() => PluginShadowCopy.CopyForStartup([plan], _temporary.FullName)));

        Assert.False(Directory.Exists(expired.FullName));
        var plans = reports.Select(report => Assert.Single(report.Plans)).ToArray();
        Assert.NotEqual(plans[0].EntryPath, plans[1].EntryPath);
        Assert.All(plans, copy =>
        {
            Assert.Equal("Plugin test file", File.ReadAllText(copy.EntryPath));
            Assert.Equal(plugin.FullName, copy.Directory);
            Assert.Equal(live.Parent!.FullName, new FileInfo(copy.EntryPath).Directory!.Parent!.Parent!.Parent!.FullName);
        });
    }

    [Fact]
    public void Startup_keeps_linked_candidates_and_payloads()
    {
        if (!SupportsManagedCopies)
        {
            Assert.Null(PluginShadowCopyStore.Prepare(_temporary.FullName, needsCopies: true));
            return;
        }

        var live = CreateLiveSession();
        var exitedPid = GetExitedProcessId();
        var nestedLink = UseOwnerRecord(live, exitedPid);
        var outside = _temporary.CreateSubdirectory("outside-managed-root");
        var sentinel = Path.Combine(outside.FullName, "sentinel.txt");
        File.WriteAllText(sentinel, "Keep this");
        var payloadAlias = Path.Combine(nestedLink.FullName, "copies", "linked");
        var candidateAlias = Path.Combine(live.Parent!.FullName, Guid.NewGuid().ToString("N"));
        WriteOwner(Path.Combine(outside.FullName, "owner"), Path.GetFileName(candidateAlias), live, exitedPid);

        try
        {
            CreateDirectoryLink(payloadAlias, outside.FullName);
            CreateDirectoryLink(candidateAlias, outside.FullName);

            PluginShadowCopy.CopyForStartup([], _temporary.FullName);

            Assert.True(Directory.Exists(payloadAlias));
            Assert.True(Directory.Exists(candidateAlias));
            Assert.Equal("Keep this", File.ReadAllText(sentinel));
        }
        finally
        {
            // Unlink the fixture junctions before recursively deleting their parent on Windows.
            foreach (var alias in new[] { candidateAlias, payloadAlias })
            {
                if (Directory.Exists(alias))
                {
                    Directory.Delete(alias);
                }
            }
        }
    }

    [Fact]
    public void Unreadable_owner_record_does_not_stop_sweeping_other_copies()
    {
        if (!SupportsManagedCopies)
        {
            Assert.Null(PluginShadowCopyStore.Prepare(_temporary.FullName, needsCopies: true));
            return;
        }

        var live = CreateLiveSession();
        var exitedPid = GetExitedProcessId();
        var unreachable = UseOwnerRecord(live, exitedPid);
        var expired = UseOwnerRecord(live, exitedPid);

        using (new FileStream(OwnerPath(unreachable), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            PluginShadowCopy.CopyForStartup([], _temporary.FullName);
            Assert.True(Directory.Exists(unreachable.FullName));
            Assert.False(Directory.Exists(expired.FullName));
        }

        PluginShadowCopy.CopyForStartup([], _temporary.FullName);
        Assert.False(Directory.Exists(unreachable.FullName));
    }

    private DirectoryInfo CreateLiveSession()
    {
        var copyPath = PluginShadowCopyStore.Prepare(_temporary.FullName, needsCopies: true);
        Assert.NotNull(copyPath);
        var directory = Directory.CreateDirectory(copyPath);
        File.WriteAllText(Path.Combine(directory.FullName, "copy.dll"), "Fixture copy");
        return directory.Parent!;
    }

    private static DirectoryInfo UseOwnerRecord(DirectoryInfo live, int pid, string? scope = null)
    {
        var name = Guid.NewGuid().ToString("N");
        var directory = live.Parent!.CreateSubdirectory(name);
        File.WriteAllText(Path.Combine(directory.CreateSubdirectory("copies").FullName, "stale.dll"), "Fixture copy");
        WriteOwner(OwnerPath(directory), name, live, pid, scope);
        return directory;
    }

    private static void WriteOwner(string path, string name, DirectoryInfo live, int pid, string? scope = null)
    {
        var current = File.ReadAllLines(OwnerPath(live));
        File.WriteAllText(path, FormattableString.Invariant($"{name}\n{scope ?? current[1]}\n{pid}\n"));
    }

    private static string OwnerPath(DirectoryInfo directory) => Path.Combine(directory.FullName, "owner");

    private static int GetExitedProcessId()
    {
        var start = OperatingSystem.IsWindows() ? CommandPrompt("/d", "/c", "exit", "0")
            : new ProcessStartInfo("/bin/true") { UseShellExecute = false };
        using var child = Process.Start(start);
        Assert.NotNull(child);
        var pid = child.Id;
        Assert.True(child.WaitForExit(10_000), "The temporary fixture process did not exit.");
        Assert.Equal(0, child.ExitCode);
        return pid;
    }

    private static void CreateDirectoryLink(string path, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(path, target);
            return;
        }

        using var command = Process.Start(CommandPrompt("/d", "/c", "mklink", "/j", path, target));
        Assert.NotNull(command);
        Assert.True(command.WaitForExit(10_000), "The junction creation command did not exit.");
        Assert.Equal(0, command.ExitCode);
    }

    private static ProcessStartInfo CommandPrompt(params string[] arguments)
    {
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }

    private static PluginLoadPlan CreatePlan(string directory, string entryPath) =>
        new()
        {
            Id = "sample",
            Directory = directory,
            EntryPath = entryPath,
            PluginType = "Sample.Plugin",
            ToolPrefix = "sample_",
            UsesShadowCopy = true
        };

    private const string FixturePrefix = "RoslynMcpPluginCopiesTests-";

    [SupportedOSPlatform("linux")]
    private static DirectoryInfo CreateLinuxFixture()
    {
        // Production rejects a group- or other-writable ancestor without the sticky bit. A TMPDIR created
        // under umask 002 is such an ancestor, and every managed-copy test would then see the legacy fallback.
        // The sticky /tmp still passes, and the fixture holds only a few small files.
        var baseDirectory = new[] { Path.GetTempPath(), "/tmp" }.FirstOrDefault(HasProtectedLinuxAncestry)
            ?? throw new InvalidOperationException(
                "Neither TMPDIR nor /tmp has ancestors that only their owner can modify; managed plugin copies cannot be tested.");
        var path = Path.Combine(baseDirectory, FixturePrefix + Guid.NewGuid().ToString("N"));
        return Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static bool HasProtectedLinuxAncestry(string path)
    {
        const UnixFileMode sharedWrite = UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
        {
            if (!directory.Exists || directory.LinkTarget is not null)
            {
                return false;
            }

            var mode = directory.UnixFileMode;
            if ((mode & sharedWrite) != 0 && (mode & UnixFileMode.StickyBit) == 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool SupportsManagedCopies => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    private readonly DirectoryInfo _temporary;
}
