using System.Diagnostics;
using System.Text;
using RoslynMcpServer.Services;

namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// Parent side of the deadlock witness: starts the host in a separate process, waits for its readiness
/// signal with a bound of its own, then bounds the operation. When the bound expires the parent kills
/// the whole process tree and waits for the exit, because a synchronous hang cannot be undone from the
/// inside: a timer started next to the call neither completes the operation nor stops the hung thread.
/// </summary>
internal sealed class DeadlockWitnessClient : IDisposable
{
    /// <summary>Bound for starting the host. Generous: a loaded CI machine may need seconds to start a runtime.</summary>
    public static readonly TimeSpan HostStartTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Bound for waiting for the killed process to disappear.</summary>
    public static readonly TimeSpan KillWaitTimeout = TimeSpan.FromSeconds(15);

    public int HostProcessId => _process.Id;

    public bool HasExited => _process.HasExited;

    public int? ExitCode => _process.HasExited ? _process.ExitCode : null;

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

    private const string HostFileName = "DeadlockWitnessHost.dll";

    private readonly Process _process;
    private readonly StringBuilder _stderr = new();
    private bool _disposed;

    private DeadlockWitnessClient(Process process)
    {
        _process = process;
        _process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                lock (_stderr)
                {
                    _stderr.AppendLine(args.Data);
                }
            }
        };
        _process.BeginErrorReadLine();
    }

    /// <summary>Path of the host assembly next to the test assembly; the project reference copies it there.</summary>
    public static string HostPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, HostFileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Deadlock witness host was not found next to the test assembly. Build the test project so its project reference is built too.",
                path);
        }

        return path;
    }

    /// <summary>Starts the host and waits for its readiness line; the host start has its own bound.</summary>
    public static async Task<DeadlockWitnessClient> StartAsync(CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = DotNetHostResolver.ResolveDotNetExecutable(),
            Arguments = "exec \"" + HostPath() + "\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            WorkingDirectory = AppContext.BaseDirectory,
        };

        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start the deadlock witness host.");
        }

        var client = new DeadlockWitnessClient(process);
        try
        {
            await client.WaitForReadinessAsync(cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Sends one command and returns what happened within <paramref name="operationTimeout"/>. On expiry
    /// the process tree is killed and awaited before the outcome is returned, so the caller can rely on
    /// the host being gone.
    /// </summary>
    public async Task<WitnessOutcome> RunAsync(
        string assemblyPath,
        string typeName,
        string methodName,
        TimeSpan operationTimeout,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(operationTimeout);
        try
        {
            await _process.StandardInput.WriteLineAsync($"{assemblyPath}|{typeName}|{methodName}".AsMemory(), timeout.Token)
                .ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(timeout.Token).ConfigureAwait(false);

            var line = await _process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            started.Stop();
            if (line is null)
            {
                return new WitnessOutcome(WitnessStatus.Failed, string.Empty, "host-stdout-closed stderr=" + StderrSnapshot, started.Elapsed);
            }

            if (line.StartsWith("result:", StringComparison.Ordinal))
            {
                return new WitnessOutcome(WitnessStatus.Completed, line["result:".Length..], string.Empty, started.Elapsed);
            }

            return new WitnessOutcome(WitnessStatus.Failed, string.Empty, line, started.Elapsed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            KillProcessTree();
            started.Stop();
            return new WitnessOutcome(WitnessStatus.TimedOut, string.Empty, StderrSnapshot, started.Elapsed);
        }
    }

    /// <summary>Waits for a self-exit after the operation; returns false when the bound expires.</summary>
    public async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await _process.WaitForExitAsync(cancellation.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Kills the whole process tree and waits for the exit; a no-op when the host already exited.</summary>
    public void KillProcessTree()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit((int)KillWaitTimeout.TotalMilliseconds);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the check and the kill.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        KillProcessTree();
        _process.Dispose();
    }

    private async Task WaitForReadinessAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(HostStartTimeout);
        string? line;
        try
        {
            line = await _process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            KillProcessTree();
            throw new TimeoutException(
                $"the witness host did not report readiness within {HostStartTimeout.TotalSeconds:F0} s; stderr: {StderrSnapshot}");
        }

        var expected = $"ready pid={_process.Id}";
        if (!string.Equals(line, expected, StringComparison.Ordinal))
        {
            KillProcessTree();
            throw new InvalidOperationException($"unexpected witness readiness `{line ?? "<eof>"}`, expected `{expected}`; stderr: {StderrSnapshot}");
        }
    }
}
