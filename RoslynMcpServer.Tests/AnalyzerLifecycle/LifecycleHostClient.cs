using System.Diagnostics;
using System.Text;
using System.Text.Json;
using RoslynMcpServer.LifecycleTestHost;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tests.AnalyzerLifecycle;

internal sealed class LifecycleHostClient : IAsyncDisposable
{
    internal static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(45);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly Process _process;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly StringBuilder _stderr = new();

    private LifecycleHostClient(Process process)
    {
        _process = process;
        _process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                lock (_stderr)
                {
                    _stderr.AppendLine(e.Data);
                }
            }
        };
        _process.BeginErrorReadLine();
    }

    public string StderrSnapshot
    {
        get
        {
            lock (_stderr)
            {
                return _stderr.ToString();
            }
        }
    }

    public static string FindHostDll()
    {
        var name = "RoslynMcpServer.LifecycleTestHost.dll";
        var searchRoots = new List<string>();
        if (!string.IsNullOrEmpty(AppContext.BaseDirectory))
        {
            searchRoots.Add(AppContext.BaseDirectory);
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var hostBin = Path.Combine(dir.FullName, "RoslynMcpServer.LifecycleTestHost", "bin");
            if (Directory.Exists(hostBin))
            {
                searchRoots.Add(hostBin);
            }

            if (File.Exists(Path.Combine(dir.FullName, "RoslynMcpServer.csproj")))
            {
                break;
            }

            dir = dir.Parent;
        }

        foreach (var root in searchRoots)
        {
            var ranked = Directory.GetFiles(root, name, SearchOption.AllDirectories)
                .Where(IsUsableHostDll)
                .OrderBy(HostDllRank)
                .ThenByDescending(File.GetLastWriteTimeUtc)
                .ToList();
            if (ranked.Count > 0)
            {
                return ranked[0];
            }
        }

        throw new FileNotFoundException(
            "Lifecycle test host DLL was not found. Build RoslynMcpServer.LifecycleTestHost.",
            name);
    }

    private static bool IsUsableHostDll(string hostDll)
    {
        if (Environment.Is64BitProcess
            && hostDll.Contains($"{Path.DirectorySeparatorChar}win-x86{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static int HostDllRank(string hostDll)
    {
        var normalized = hostDll.Replace('/', Path.DirectorySeparatorChar);
        if (normalized.Contains($"{Path.DirectorySeparatorChar}win-x86{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (normalized.Contains($"{Path.DirectorySeparatorChar}win-x64{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 0;
    }

    public static LifecycleHostClient Start()
    {
        var hostDll = FindHostDll();
        var psi = new ProcessStartInfo
        {
            FileName = DotNetHostResolver.ResolveDotNetExecutable(),
            Arguments = "exec \"" + hostDll + "\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            WorkingDirectory = Path.GetDirectoryName(hostDll) ?? AppContext.BaseDirectory,
        };

        var process = new Process { StartInfo = psi };
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start LifecycleTestHost.");
        }

        return new LifecycleHostClient(process);
    }

    public async Task<HostResponse> SendAsync(HostCommand command, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_process.HasExited)
            {
                return new HostResponse
                {
                    Ok = false,
                    Error = "host-exited:" + _process.ExitCode + " stderr=" + StderrSnapshot,
                };
            }

            var line = JsonSerializer.Serialize(command, JsonOptions);
            await _process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ResponseTimeout);
            string? responseLine = null;
            try
            {
                while (true)
                {
                    var raw = await _process.StandardOutput.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
                    if (raw is null)
                    {
                        break;
                    }

                    var trimmed = raw.TrimStart();
                    if (trimmed.StartsWith('{'))
                    {
                        responseLine = trimmed;
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new HostResponse
                {
                    Ok = false,
                    Error = "host-response-timeout stderr=" + StderrSnapshot,
                };
            }

            if (responseLine is null)
            {
                return new HostResponse
                {
                    Ok = false,
                    Error = "host-stdout-closed stderr=" + StderrSnapshot,
                };
            }

            var response = JsonSerializer.Deserialize<HostResponse>(responseLine, JsonOptions);
            return response ?? new HostResponse { Ok = false, Error = "empty-response" };
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _ = _process.WaitForExit(2_000);
            }
        }
        catch
        {
        }
        finally
        {
            _process.Dispose();
            _gate.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
