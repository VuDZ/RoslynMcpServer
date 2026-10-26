using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace BuildHostFork.Rpc;

/// <summary>A sequential client for the upstream line-delimited JSON protocol, without loading host internals.</summary>
internal sealed class BuildHostClient : IAsyncDisposable
{
    private BuildHostClient(Process process, NamedPipeClientStream pipe, Task<string> stdout, Task<string> stderr)
    {
        _process = process;
        _pipe = pipe;
        _reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
        _writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        _stdout = stdout;
        _stderr = stderr;
    }

    public static async Task<BuildHostClient> StartAsync(string hostPath)
    {
        var pipeName = "buildhost-contract-" + Guid.NewGuid().ToString("N");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // Match Roslyn's BuildHostProcessManager so the host can load the solution's SDK.
        start.Environment["DOTNET_ROLL_FORWARD_TO_PRERELEASE"] = "1";
        start.Environment.Remove("MSBUILD_EXE_PATH");
        start.ArgumentList.Add("--roll-forward");
        start.ArgumentList.Add("LatestMajor");
        start.ArgumentList.Add(hostPath);
        start.ArgumentList.Add(pipeName);
        start.ArgumentList.Add("en-US");
        var process = Process.Start(start) ?? throw new InvalidOperationException("BuildHost did not start.");
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await pipe.ConnectAsync(30000).ConfigureAwait(false);
            return new BuildHostClient(process, pipe, stdout, stderr);
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
            await stdout.ConfigureAwait(false);
            await stderr.ConfigureAwait(false);
            pipe.Dispose();
            process.Dispose();
            throw;
        }
    }

    public async Task<JsonElement> CallAsync(int targetObject, string method, params object?[] parameters)
    {
        var id = ++_requestId;
        var request = JsonSerializer.Serialize(new { id, targetObject, method, parameters });
        using var timeout = new CancellationTokenSource(method == "ShutdownAsync"
            ? TimeSpan.FromSeconds(10) : TimeSpan.FromMinutes(5));
        await _writer.WriteLineAsync(request.AsMemory(), timeout.Token).ConfigureAwait(false);
        var line = await _reader.ReadLineAsync(timeout.Token).ConfigureAwait(false)
            ?? throw new IOException("BuildHost closed its RPC channel.");
        using var response = JsonDocument.Parse(line);
        var root = response.RootElement;
        if (root.GetProperty("id").GetInt32() != id)
        {
            throw new InvalidDataException("Unexpected RPC response ID.");
        }

        if (root.TryGetProperty("exceptionMessage", out var error))
        {
            throw new InvalidOperationException($"BuildHost {method}: {error.GetString()}");
        }

        return root.TryGetProperty("value", out var value) ? value.Clone() : default;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_pipe.IsConnected && !_process.HasExited)
            {
                await CallAsync(0, "ShutdownAsync").ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                _writer.Dispose();
            }
            finally
            {
                _reader.Dispose();
                _pipe.Dispose();
                await FinishProcessAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task FinishProcessAsync()
    {
        try
        {
            try
            {
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().ConfigureAwait(false);
                throw;
            }

            var errors = await _stderr.ConfigureAwait(false);
            await _stdout.ConfigureAwait(false);
            if (_process.ExitCode != 0)
            {
                throw new InvalidOperationException($"BuildHost exited with code {_process.ExitCode}: {errors}");
            }
        }
        finally
        {
            _process.Dispose();
        }
    }

    private readonly Process _process;
    private readonly NamedPipeClientStream _pipe;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly Task<string> _stdout;
    private readonly Task<string> _stderr;
    private int _requestId;
}
