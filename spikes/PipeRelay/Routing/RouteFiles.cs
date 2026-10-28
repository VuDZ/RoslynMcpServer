using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PipeRelay.Routing.Models;

namespace PipeRelay.Routing;

internal sealed class RouteFiles
{
    public string DirectoryPath { get; }
    public string Key { get; }
    public string PipeName { get; }

    public RouteFiles(RouteOptions options)
    {
        _descriptor = options.Descriptor;
        // Root is an explicit spike fixture directory, never Path.GetTempPath(). Production discovery is task-01.
        var root = Path.GetFullPath(options.Root);
        Directory.CreateDirectory(root);
        if (!OperatingSystem.IsWindows())
        {
            if (new DirectoryInfo(root).LinkTarget is not null)
            {
                throw new IOException("The rendezvous directory must not be a symbolic link.");
            }

            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        Key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[]
        {
            Environment.UserName, BuildIdentity.Value, root, options.Context, options.Descriptor,
        })))).ToLowerInvariant();
        DirectoryPath = Path.Combine(root, Key[..20]);
        Directory.CreateDirectory(DirectoryPath);
        PipeName = OperatingSystem.IsWindows() ? "pr4-" + Key : Path.Combine(DirectoryPath, "pipe");
        if (!OperatingSystem.IsWindows() && Encoding.UTF8.GetByteCount(PipeName) > 103)
        {
            throw new IOException("Unix socket path exceeds the portable 103-byte limit.");
        }
    }

    public string PathFor(string name) => Path.Combine(DirectoryPath, name);

    public FileStream? TryLock(string name)
    {
        if (OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("This spike uses FileStream.Lock, unsupported on macOS.");
        }

        var stream = new FileStream(PathFor(name), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        try
        {
            stream.Lock(0, 1);
            return stream;
        }
        catch (IOException)
        {
            stream.Dispose();
            return null;
        }
    }

    public void Event(string name, Guid generation, object? data = null)
    {
        lock (_eventGate)
        {
            File.AppendAllText(PathFor($"events-{Environment.ProcessId}.jsonl"), JsonSerializer.Serialize(new
            {
                utc = DateTimeOffset.UtcNow, pid = Environment.ProcessId,
                uptimeMs = Environment.TickCount64,
                key = Key, descriptor = _descriptor,
                ticks = Checks.ProcessIdentity.Stamp(Environment.ProcessId),
                name, generation, data,
            }) + "\n");
        }
    }

    public static async Task Barrier(string? path, CancellationToken cancellationToken)
    {
        while (path is not null && !File.Exists(path))
        {
            await Task.Delay(20, cancellationToken);
        }
    }

    private readonly object _eventGate = new();
    private readonly string _descriptor;
}
