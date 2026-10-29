using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PipeRelay.Preflight.Models;

namespace PipeRelay.Preflight;

/// <summary>
/// Measures the identity costs of one server file set for task-06: a full content manifest, a stat scan,
/// copy with hashing, verification of the copy and reuse of an already written manifest.
/// </summary>
internal static class IdentityBench
{
    /// <summary>
    /// <c>identity-bench &lt;source&gt; &lt;scratch&gt; &lt;output.json&gt; &lt;runs&gt; [excluded top-level directory...]</c>.
    /// A new owned subdirectory of scratch receives staging copies and is deleted afterwards.
    /// Source and scratch must be disjoint; an existing output is never overwritten.
    /// </summary>
    public static int Run(string[] args)
    {
        if (args.Length < 5 || !int.TryParse(args[4], out var runs) || runs <= 0)
        {
            throw new ArgumentException("Expected source, scratch parent, new output path and a positive run count.");
        }

        var source = Path.GetFullPath(args[1]);
        var scratchParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[2]));
        var output = Path.GetFullPath(args[3]);
        ValidatePaths(source, scratchParent, output);
        var excluded = args.Skip(5).ToHashSet(StringComparer.Ordinal);
        var files = ListFiles(source, excluded);
        if (!files.Any(file => file.EndsWith(".dll", StringComparison.Ordinal) && new FileInfo(Path.Combine(source, file)).Length > 0))
        {
            throw new ArgumentException("The source file set must contain a non-empty DLL for the stale-stat probe.");
        }

        Directory.CreateDirectory(scratchParent);
        var scratch = Path.Combine(scratchParent, "identity-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var manifestPath = Path.Combine(scratch, "manifest.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(Hash(source, files)));
            var samples = new List<IdentitySample>();
            var cacheModes = OperatingSystem.IsLinux() ? new[] { "cold-advised", "warm" } : new[] { "warm" };
            foreach (var cache in cacheModes)
            {
                for (var run = 1; run <= runs; run++)
                {
                    samples.Add(Measure(source, scratch, files, manifestPath, cache, run));
                }
            }

            var report = new IdentityReport(
                source, excluded.Order(StringComparer.Ordinal).ToArray(), files.Count,
                files.Sum(file => new FileInfo(Path.Combine(source, file)).Length),
                RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription,
                GCSettings.IsServerGC ? "server" : "workstation", Environment.ProcessorCount,
                OperatingSystem.IsLinux()
                    ? "posix_fadvise(DONTNEED), return checked; advisory only, residency not verified"
                    : "unavailable: warm samples only",
                samples, ProbeStaleStat(source, scratch, files));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using var reportStream = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
            JsonSerializer.Serialize(reportStream, report, new JsonSerializerOptions { WriteIndented = true });
            return 0;
        }
        finally
        {
            if (Path.GetDirectoryName(scratch) != scratchParent
                || (File.GetAttributes(scratch) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("Refusing to clean an unowned benchmark directory.");
            }

            Directory.Delete(scratch, recursive: true);
        }
    }

    private static void ValidatePaths(string source, string scratchParent, string output)
    {
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException("The benchmark source directory does not exist.");
        }

        if (IsWithin(scratchParent, source) || IsWithin(source, scratchParent))
        {
            throw new ArgumentException("Source and scratch parent directories must not overlap.");
        }

        if (IsWithin(output, source) || File.Exists(output) || Directory.Exists(output))
        {
            throw new ArgumentException("The output must be a new file outside the source directory.");
        }
    }

    private static bool IsWithin(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative == "." || (!Path.IsPathRooted(relative)
            && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static List<string> ListFiles(string source, HashSet<string> excluded) => Directory
        .EnumerateFiles(source, "*", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(source, path))
        .Where(path => !excluded.Contains(path.Split(Path.DirectorySeparatorChar)[0]))
        .Order(StringComparer.Ordinal)
        .ToList();

    private static IdentitySample Measure(string source, string scratch, List<string> files, string manifestPath,
        string cache, int run)
    {
        if (cache == "cold-advised")
        {
            Evict(source, files);
        }

        var stat = Time(() => StatStamp(source, files));
        if (cache == "cold-advised")
        {
            Evict(source, files);
        }

        var manifest = Time(() => Hash(source, files));
        if (cache == "cold-advised")
        {
            Evict(source, files);
        }

        var staging = Path.Combine(scratch, $"staging-{cache}-{run}");
        var copy = Time(() => CopyWithHash(source, staging, files));
        var verify = Time(() => Hash(staging, files).Digest == copy.Result.Digest);
        if (!verify.Result || copy.Result.Digest != manifest.Result.Digest)
        {
            throw new InvalidDataException("The staged copy does not match the source manifest.");
        }

        Directory.Delete(staging, recursive: true);
        var reuse = Time(() => ReuseManifest(source, manifestPath));
        if (!reuse.Result)
        {
            throw new InvalidDataException("The stored manifest no longer matches the stat stamps.");
        }

        return new IdentitySample(cache, run, stat.Milliseconds, manifest.Milliseconds, copy.Milliseconds,
            verify.Milliseconds, reuse.Milliseconds);
    }

    private static Manifest Hash(string root, List<string> files)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var entries = new List<ManifestEntry>(files.Count);
        foreach (var file in files)
        {
            var path = Path.Combine(root, file);
            byte[] hash;
            using (var stream = File.OpenRead(path))
            {
                hash = SHA256.HashData(stream);
            }

            var info = new FileInfo(path);
            entries.Add(new ManifestEntry(file, info.Length, info.LastWriteTimeUtc.Ticks, Convert.ToHexString(hash)));
            AppendEntry(digest, file, hash);
        }

        return new Manifest(Convert.ToHexString(digest.GetHashAndReset()), entries);
    }

    private static Manifest CopyWithHash(string source, string staging, List<string> files)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var entries = new List<ManifestEntry>(files.Count);
        var buffer = new byte[1 << 20];
        foreach (var file in files)
        {
            var target = Path.Combine(staging, file);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var fileHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (var input = File.OpenRead(Path.Combine(source, file)))
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
            {
                int read;
                while ((read = input.Read(buffer)) > 0)
                {
                    fileHash.AppendData(buffer, 0, read);
                    output.Write(buffer, 0, read);
                }

                // Include the cost of flushing file contents; this benchmark does not publish an image.
                output.Flush(flushToDisk: true);
            }

            var hash = fileHash.GetHashAndReset();
            entries.Add(new ManifestEntry(file, new FileInfo(target).Length, 0, Convert.ToHexString(hash)));
            AppendEntry(digest, file, hash);
        }

        return new Manifest(Convert.ToHexString(digest.GetHashAndReset()), entries);
    }

    private static bool ReuseManifest(string source, string manifestPath)
    {
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath))!;
        return manifest.Entries.All(entry =>
        {
            var info = new FileInfo(Path.Combine(source, entry.Path));
            return info.Exists && info.Length == entry.Length && info.LastWriteTimeUtc.Ticks == entry.LastWriteTicks;
        });
    }

    private static long StatStamp(string root, List<string> files)
    {
        var sum = 0L;
        foreach (var file in files)
        {
            var info = new FileInfo(Path.Combine(root, file));
            sum += info.Length ^ info.LastWriteTimeUtc.Ticks;
        }

        return sum;
    }

    /// <summary>
    /// Changes one byte of a staged copy and restores its modification time: the stat stamp stays equal,
    /// only the content hash notices the change.
    /// </summary>
    private static StaleStatProbe ProbeStaleStat(string source, string scratch, List<string> files)
    {
        var file = files.First(name => name.EndsWith(".dll", StringComparison.Ordinal)
            && new FileInfo(Path.Combine(source, name)).Length > 0);
        var copy = Path.Combine(scratch, "stale-" + Path.GetFileName(file));
        File.Copy(Path.Combine(source, file), copy);
        var before = new FileInfo(copy);
        var stamp = (before.Length, before.LastWriteTimeUtc);
        var hash = SHA256.HashData(File.ReadAllBytes(copy));
        using (var stream = new FileStream(copy, FileMode.Open, FileAccess.ReadWrite))
        {
            stream.Position = stream.Length / 2;
            var value = stream.ReadByte();
            stream.Position--;
            stream.WriteByte((byte)(value ^ 0xFF));
        }

        File.SetLastWriteTimeUtc(copy, stamp.LastWriteTimeUtc);
        var after = new FileInfo(copy);
        return new StaleStatProbe(file, (after.Length, after.LastWriteTimeUtc) == stamp,
            SHA256.HashData(File.ReadAllBytes(copy)).SequenceEqual(hash));
    }

    private static void AppendEntry(IncrementalHash digest, string file, byte[] hash)
    {
        digest.AppendData(Encoding.UTF8.GetBytes(file.Replace('\\', '/') + "\0"));
        digest.AppendData(hash);
    }

    /// <summary>
    /// Requests eviction of clean pages without root. Successful advice does not prove a cold cache.
    /// </summary>
    private static void Evict(string root, List<string> files)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        foreach (var file in files)
        {
            using var stream = File.OpenRead(Path.Combine(root, file));
            var result = PosixFadvise(stream.SafeFileHandle.DangerousGetHandle().ToInt32(), 0, 0, FadviseDontNeed);
            if (result != 0)
            {
                throw new IOException($"posix_fadvise(DONTNEED) failed with error {result}.");
            }
        }
    }

    private static (T Result, double Milliseconds) Time<T>(Func<T> action)
    {
        var watch = Stopwatch.StartNew();
        var result = action();
        return (result, watch.Elapsed.TotalMilliseconds);
    }

    [DllImport("libc", EntryPoint = "posix_fadvise")]
    private static extern int PosixFadvise(int fd, long offset, long length, int advice);

    private const int FadviseDontNeed = 4;
}
