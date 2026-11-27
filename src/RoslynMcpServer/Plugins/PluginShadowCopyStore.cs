using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace RoslynMcpServer.Plugins;

/// <summary>Owns temporary startup copies whose process can be checked on the same host.</summary>
internal static class PluginShadowCopyStore
{
    private const string RootPrefix = "RoslynMcpServer-plugin-copies-v1-";
    private const string OwnerFileName = "owner";
    private const string CopiesDirectoryName = "copies";
    private const int MaxOwnerBytes = 109;
    private const uint LinuxStatFields = 0x100B;
    private const int NoSuchFile = 2;
    private const int NoSuchProcess = 3;
    private const UnixFileMode PrivateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    internal static string? Prepare(string baseDirectory, bool needsCopies, bool createBaseDirectory = false)
    {
        if (!Path.IsPathFullyQualified(baseDirectory))
        {
            return null;
        }

        try
        {
            var location = new DirectoryInfo(baseDirectory);
            var scope = GetScope(location);
            if (scope is null)
            {
                return null;
            }

            var root = new DirectoryInfo(Path.Combine(location.FullName, RootPrefix + scope.UserKey));
            if (!needsCopies && !root.Exists)
            {
                return null;
            }

            if (createBaseDirectory && needsCopies && !location.Exists && location.Parent is { } parent
                && HasSafeAncestors(parent, scope))
            {
                // Only create the state directory itself, under an existing protected parent.
                location.Create();
                location.Refresh();
            }

            if (!HasSafeAncestors(location, scope))
            {
                return null;
            }

            if (needsCopies && !root.Exists)
            {
                CreatePrivateRoot(root, scope);
            }

            if (!IsPrivateRoot(root, location, scope))
            {
                return null;
            }

            Sweep(root, scope);
            return needsCopies ? StartCopy(root, scope.Context) : null;
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            return null;
        }
    }

    private static OwnerScope? GetScope(DirectoryInfo location)
    {
        if (OperatingSystem.IsWindows())
        {
            // A shared drive can host a different machine's process table.
            if (!IsWindowsVolumeRoot(location.Root.FullName))
            {
                return null;
            }

            using var identity = WindowsIdentity.GetCurrent();
            var user = identity.User;
            var machine = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null) as string;
            return user is not null && Guid.TryParse(machine, out var machineId) && machineId != Guid.Empty
                ? new OwnerScope(Hash(user.Value)[..32], Hash($"windows:{machineId:N}:{Environment.MachineName}:{user.Value}"), Sid: user)
                : null;
        }

        if (OperatingSystem.IsLinux() && CanProbeLinuxProcesses()
            && Guid.TryParse(File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim(), out var boot)
            && boot != Guid.Empty
            && new FileInfo("/proc/self/ns/pid").LinkTarget is { } pidNamespace
            && pidNamespace.StartsWith("pid:[", StringComparison.Ordinal))
        {
            var uid = GetEffectiveUserId();
            return new OwnerScope(
                uid.ToString(CultureInfo.InvariantCulture),
                Hash(FormattableString.Invariant($"linux:{boot:N}:{pidNamespace}:{uid}")),
                Uid: uid);
        }

        // shortcut: no sweep without a process scope, extend when the platform can prove one.
        return null;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsWindowsVolumeRoot(string root)
    {
        if (root.Length != 3 || new DriveInfo(root).DriveType != DriveType.Fixed)
        {
            return false;
        }

        // A SUBST drive can hide unsafe ancestors. QueryDosDevice puts the current mapping first.
        const string volumePrefix = @"\Device\HarddiskVolume";
        var target = new StringBuilder(512);
        if (QueryDosDevice(root[..2], target, (uint)target.Capacity) == 0)
        {
            return false;
        }

        var mapping = target.ToString();
        return mapping.StartsWith(volumePrefix, StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(mapping.AsSpan(volumePrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    private static bool CanProbeLinuxProcesses()
    {
        var pid = Environment.ProcessId;
        var pidText = pid.ToString(CultureInfo.InvariantCulture);
        const string field = "NStgid:";
        // An ancestor's procfs shows additional PID namespace IDs, even if a number coincides.
        return File.ReadLines("/proc/self/status").Any(line =>
                line.StartsWith(field, StringComparison.Ordinal) && line.AsSpan(field.Length).Trim().SequenceEqual(pidText))
            && ProbeProcess(pid, 0) == 0
            && ReadLinuxEntry("/proc") is { Mount: > 0 } proc
            && ReadLinuxEntry(ProcPath(pid))?.Mount == proc.Mount
            && HasVisibleProcMount(proc.Mount);
    }

    private static bool HasVisibleProcMount(ulong mount)
    {
        // hidepid can report ENOENT for a live peer. Check the active mount, not a shadowed /proc entry.
        var prefix = mount.ToString(CultureInfo.InvariantCulture) + " ";
        var line = File.ReadLines("/proc/self/mountinfo").FirstOrDefault(entry => entry.StartsWith(prefix, StringComparison.Ordinal));
        if (line is null)
        {
            return false;
        }

        var fields = line.Split(' ');
        if (fields.Length < 10 || fields[3] != "/" || fields[4] != "/proc")
        {
            return false;
        }

        var separator = Array.IndexOf(fields, "-", 6);
        return separator >= 6 && fields.Length == separator + 4 && fields[separator + 1] == "proc"
            && !fields[^1].Split(',').Any(option => option.StartsWith("hidepid", StringComparison.Ordinal)
                && option is not ("hidepid=0" or "hidepid=off"));
    }

    private static bool HasSafeAncestors(DirectoryInfo directory, OwnerScope scope)
    {
        for (var ancestor = directory; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (!IsPlainDirectory(ancestor))
            {
                return false;
            }

            if (OperatingSystem.IsWindows()
                ? !HasTrustedWindowsControl(ancestor, scope.Sid!)
                : !IsSafeLinuxAncestor(ancestor, scope.Uid))
            {
                return false;
            }
        }

        return true;
    }

    [SupportedOSPlatform("windows")]
    private static bool HasTrustedWindowsControl(DirectoryInfo directory, SecurityIdentifier user)
    {
        var security = directory.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
        var owner = security.GetOwner(typeof(SecurityIdentifier));
        if (owner is not SecurityIdentifier ownerSid || !IsTrustedWindowsSid(ownerSid, user) || !HasWindowsDacl(security))
        {
            return false;
        }

        var rights = FileSystemRights.Delete | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership
            | FileSystemRights.WriteAttributes | FileSystemRights.DeleteSubdirectoriesAndFiles;
        return security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .All(rule => rule.AccessControlType != AccessControlType.Allow
                || (rule.PropagationFlags & PropagationFlags.InheritOnly) != 0
                || (rule.FileSystemRights & rights) == 0
                || rule.IdentityReference is SecurityIdentifier sid && IsTrustedWindowsSid(sid, user));
    }

    [SupportedOSPlatform("windows")]
    private static bool IsTrustedWindowsSid(SecurityIdentifier sid, SecurityIdentifier user) =>
        sid.Equals(user)
        || sid.IsWellKnown(WellKnownSidType.LocalSystemSid)
        || sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)
        || sid.Value == "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"; // TrustedInstaller

    [SupportedOSPlatform("windows")]
    private static bool HasWindowsDacl(DirectorySecurity security) =>
        new RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(), 0).DiscretionaryAcl is not null;

    private static bool IsSafeLinuxAncestor(DirectoryInfo directory, uint uid)
    {
        return ReadLinuxEntry(directory.FullName) is { } entry
            && (entry.Uid == uid || entry.Uid == 0)
            && (((UnixFileMode)entry.Mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) == 0
                || ((UnixFileMode)entry.Mode & UnixFileMode.StickyBit) != 0);
    }

    private static void CreatePrivateRoot(DirectoryInfo root, OwnerScope scope)
    {
        if (OperatingSystem.IsWindows())
        {
            var security = new DirectorySecurity();
            security.SetOwner(scope.Sid!);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(
                scope.Sid!,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            root.Create(security);
        }
        else if (OperatingSystem.IsLinux())
        {
            Directory.CreateDirectory(root.FullName, PrivateMode);
        }
    }

    private static bool IsPrivateRoot(DirectoryInfo root, DirectoryInfo location, OwnerScope scope)
    {
        root.Refresh();
        if (!IsPlainDirectory(root))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            var security = root.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            if (!Equals(security.GetOwner(typeof(SecurityIdentifier)), scope.Sid)
                || !security.AreAccessRulesProtected || !HasWindowsDacl(security))
            {
                return false;
            }

            foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType == AccessControlType.Allow && !Equals(rule.IdentityReference, scope.Sid))
                {
                    return false;
                }
            }

            return true;
        }

        return ReadLinuxEntry(root.FullName) is { } entry && entry.Uid == scope.Uid
            && entry.Mount != 0 && ReadLinuxEntry(location.FullName)?.Mount == entry.Mount
            && ((UnixFileMode)entry.Mode & (UnixFileMode)0xFFF) == PrivateMode;
    }

    private static bool IsPlainDirectory(DirectoryInfo directory) =>
        directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) == 0;

    private static string StartCopy(DirectoryInfo root, string context)
    {
        var name = Guid.NewGuid().ToString("N");
        var session = root.CreateSubdirectory(name);
        using var owner = new FileStream(Path.Combine(session.FullName, OwnerFileName), FileMode.CreateNew, FileAccess.Write);
        owner.Write(Encoding.ASCII.GetBytes(FormattableString.Invariant($"{name}\n{context}\n{Environment.ProcessId}\n")));
        // Keep the server's marker out of the tree copied from the development plugin.
        return Path.Combine(session.FullName, CopiesDirectoryName);
    }

    private static void Sweep(DirectoryInfo root, OwnerScope scope)
    {
        var mount = OperatingSystem.IsLinux() ? ReadLinuxEntry(root.FullName)?.Mount ?? 0 : 0;
        foreach (var candidate in root.EnumerateDirectories())
        {
            try
            {
                if (Guid.TryParseExact(candidate.Name, "N", out var id)
                    && candidate.Name == id.ToString("N")
                    && IsPlainDirectory(candidate)
                    && IsConfinedEntry(candidate.FullName, mount)
                    && ReadOwner(candidate, scope.Context, mount) is { } owner
                    && HasExited(owner))
                {
                    DeleteCopies(candidate, mount);
                }
            }
            catch (Exception ex) when (IsUnavailable(ex))
            {
                // A running peer can also be sweeping; a transient cleanup failure must not reject a plugin.
            }
        }
    }

    private static void DeleteCopies(DirectoryInfo candidate, ulong mount)
    {
        List<FileSystemInfo>? copies = null;
        foreach (var entry in candidate.EnumerateFileSystemInfos())
        {
            if (entry.Name == OwnerFileName)
            {
                continue;
            }

            if (entry.Name != CopiesDirectoryName || entry is not DirectoryInfo directory
                || !IsPlainDirectory(directory) || !IsConfinedEntry(directory.FullName, mount)
                || CollectOwnedTree(directory, mount) is not { } ownedTree)
            {
                return;
            }

            copies = ownedTree;
        }

        // Keep the owner record if the payload is currently inaccessible so startup can retry later.
        if (copies is not null)
        {
            for (var i = copies.Count - 1; i >= 0; i--)
            {
                copies[i].Delete();
            }
        }

        File.Delete(Path.Combine(candidate.FullName, OwnerFileName));
        candidate.Delete();
    }

    private static int? ReadOwner(DirectoryInfo candidate, string context, ulong mount)
    {
        var marker = new FileInfo(Path.Combine(candidate.FullName, OwnerFileName));
        if (!marker.Exists
            || (marker.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0
            || OperatingSystem.IsLinux()
                && (ReadLinuxEntry(marker.FullName) is not { } entry || entry.Mount != mount || (entry.Mode & 0xF000) != 0x8000))
        {
            return null;
        }

        using var owner = new FileStream(marker.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> data = stackalloc byte[MaxOwnerBytes + 1];
        var length = owner.ReadAtLeast(data, data.Length, throwOnEndOfStream: false);
        if (length > MaxOwnerBytes)
        {
            return null;
        }

        var lines = Encoding.ASCII.GetString(data[..length]).Split('\n');
        return lines.Length == 4 && lines[0] == candidate.Name && lines[1] == context && lines[3].Length == 0
            && int.TryParse(lines[2], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
            && pid > 0 && pid != Environment.ProcessId
            && lines[2] == pid.ToString(CultureInfo.InvariantCulture)
            ? pid
            : null;
    }

    private static bool HasExited(int pid)
    {
        if (OperatingSystem.IsLinux())
        {
            // A filter can return ESRCH without running kill. Confirm absence independently in procfs.
            return ProbeProcess(pid, 0) == -1 && Marshal.GetLastPInvokeError() == NoSuchProcess
                && ReadLinuxStat(ProcPath(pid), out _) == -1 && Marshal.GetLastPInvokeError() == NoSuchFile;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static List<FileSystemInfo>? CollectOwnedTree(DirectoryInfo candidate, ulong mount)
    {
        var directories = new Stack<DirectoryInfo>();
        directories.Push(candidate);
        var entries = new List<FileSystemInfo>();
        while (directories.TryPop(out var directory))
        {
            entries.Add(directory);
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0 || !IsConfinedEntry(entry.FullName, mount))
                {
                    return null;
                }

                if (entry is DirectoryInfo child)
                {
                    directories.Push(child);
                }
                else
                {
                    entries.Add(entry);
                }
            }
        }

        return entries;
    }

    private static bool IsConfinedEntry(string path, ulong mount) =>
        !OperatingSystem.IsLinux() || mount != 0 && ReadLinuxEntry(path)?.Mount == mount;

    private static LinuxEntry? ReadLinuxEntry(string path)
    {
        // Type, mode, owner and mount ID using the fixed-width Linux statx ABI.
        return ReadLinuxStat(path, out var entry) == 0 && (entry.Mask & LinuxStatFields) == LinuxStatFields
            ? entry
            : null;
    }

    private static int ReadLinuxStat(string path, out LinuxEntry entry) =>
        ReadStatx(-100, path, 0x100, LinuxStatFields, out entry);

    private static string ProcPath(int pid) => FormattableString.Invariant($"/proc/{pid}");

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool IsUnavailable(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException
            or SecurityException or Win32Exception or EntryPointNotFoundException or DllNotFoundException;

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int ProbeProcess(int pid, int signal);

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int ReadStatx(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, out LinuxEntry entry);

    [DllImport("kernel32.dll", EntryPoint = "QueryDosDeviceW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint QueryDosDevice(string name, StringBuilder targetPath, uint capacity);

    private sealed record OwnerScope(string UserKey, string Context, uint Uid = 0, SecurityIdentifier? Sid = null);

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxEntry
    {
        [FieldOffset(0)]
        public uint Mask;

        [FieldOffset(20)]
        public uint Uid;

        [FieldOffset(28)]
        public ushort Mode;

        [FieldOffset(144)]
        public ulong Mount;
    }
}
