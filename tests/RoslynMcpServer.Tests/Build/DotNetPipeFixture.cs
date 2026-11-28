using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcpServer.Tests.SourceStructure;
using Xunit;

namespace RoslynMcpServer.Tests.Build;

internal sealed class DotNetPipeFixture : IAsyncDisposable
{
    private const string Program = """
        using System;
        using System.Diagnostics;
        using System.IO;
        using System.Reflection;
        using System.Threading;

        if (args.Length == 0)
        {
            Console.WriteLine("parent-stdout");
            Console.Error.WriteLine("parent-stderr");
            return 17;
        }

        if (args[0] == "output")
        {
            var repetitions = int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
            var noise = new string('x', 1024);
            Console.WriteLine("stdout-head");
            Console.Error.WriteLine("stderr-head");
            for (var index = 0; index < repetitions; index++)
            {
                Console.WriteLine("Warning state from ordinary narration " + noise);
                Console.Error.WriteLine("Error checking from ordinary narration " + noise);
            }

            Console.WriteLine("fixture.cs(3,4): warning CS0168: warning-in-middle password=private-warning");
            Console.WriteLine("Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2");
            Console.Error.WriteLine("fixture.cs(5,6): error CS1002: error-in-middle");
            Console.Error.WriteLine("Failed!  - Failed: 1, Passed: 3, Skipped: 0, Total: 4");
            Console.Error.Write("token='");
            for (var index = 0; index < repetitions; index++)
            {
                Console.WriteLine(noise);
                Console.Error.Write(noise);
            }

            Console.Error.WriteLine("private-oversized-secret'");
            Console.WriteLine("stdout-tail");
            Console.Error.WriteLine("stderr-tail Bearer private-tail");
            return 17;
        }

        if (args[0] == "cross-pipe")
        {
            var capStdout = args[1] == "1";
            var capStderr = args[2] == "1";
            var quoted = args[3] == "1";
            var noise = new string('x', 1024);

            Console.WriteLine("stdout-anchor");
            if (capStdout)
            {
                for (var index = 0; index < 300; index++)
                {
                    Console.WriteLine(noise);
                }
            }

            Console.Write(quoted ? "password='private-left" : "password=");
            Console.Error.WriteLine("private-right");
            if (capStderr)
            {
                for (var index = 0; index < 170; index++)
                {
                    Console.Error.WriteLine(noise);
                }
            }

            if (quoted)
            {
                Console.Error.WriteLine("fixture.cs(1,1): error CS9999: private-quoted-diagnostic");
                Console.Error.WriteLine("private-middle'");
            }

            if (capStderr)
            {
                for (var index = 0; index < 170; index++)
                {
                    Console.Error.WriteLine(noise);
                }
            }

            Console.Error.WriteLine("fixture.cs(5,6): error CS1002: unrelated-diagnostic");
            Console.Error.WriteLine("stderr-anchor");
            return 17;
        }

        if (args[0] == "hold")
        {
            var stopBy = DateTime.UtcNow.AddSeconds(90);
            while (!File.Exists(args[1]) && DateTime.UtcNow < stopBy)
            {
                Thread.Sleep(25);
            }

            return 0;
        }

        Console.WriteLine(new string('x', 250_000));
        Console.Write("timeout-stdout");
        Console.Error.Write("timeout-stderr");
        Console.Out.Flush();
        Console.Error.Flush();

        var startInfo = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add("--runtimeconfig");
        startInfo.ArgumentList.Add(args[1]);
        startInfo.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        startInfo.ArgumentList.Add("hold");
        startInfo.ArgumentList.Add(args[0] + ".stop");
        using var parent = Process.GetCurrentProcess();
        using var holder = Process.Start(startInfo)!;
        File.WriteAllLines(args[0] + ".tmp", [GetIdentity(parent), GetIdentity(holder)]);
        File.Move(args[0] + ".tmp", args[0]);
        return 0;

        static string GetIdentity(Process process) =>
            OperatingSystem.IsLinux()
                ? File.ReadAllText(FormattableString.Invariant($"/proc/{process.Id}/stat")).TrimEnd()
                : FormattableString.Invariant($"{process.Id} {process.StartTime.ToUniversalTime().Ticks}");
        """;

    public string WorkingDirectory => _directory.FullName;

    public string NormalCommand => $"exec --runtimeconfig \"{_runtimeConfig}\" \"{_assemblyPath}\"";

    public string HoldPipesCommand => $"{NormalCommand} \"{_pidFile}\" \"{_runtimeConfig}\"";

    public DotNetPipeFixture()
    {
        var program = _program.Value;
        _directory = Directory.CreateTempSubdirectory("dotnet-pipes-");
        _assemblyPath = Path.Combine(_directory.FullName, "PipeFixture.dll");
        _pidFile = Path.Combine(_directory.FullName, "pids.txt");
        File.WriteAllBytes(_assemblyPath, program);
    }

    public string NoisyCommand(int repetitions) =>
        FormattableString.Invariant($"{NormalCommand} output {repetitions}");

    public string CrossPipeCommand(bool capStdout, bool capStderr, bool quoted) =>
        $"{NormalCommand} cross-pipe {(capStdout ? 1 : 0)} {(capStderr ? 1 : 0)} {(quoted ? 1 : 0)}";

    public async Task WaitForExitedParentAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!File.Exists(_pidFile))
        {
            await Task.Delay(25, deadline.Token);
        }

        var identities = ReadProcessIdentities();
        await WaitForTrackedExitAsync(identities.Parent, deadline.Token);

        using var holder = FindTrackedProcess(identities.Holder);
        Assert.True(holder is { HasExited: false }, "The child should still hold the pipes after the parent exits.");
    }

    public async ValueTask DisposeAsync()
    {
        if (File.Exists(_pidFile))
        {
            await StopTrackedProcessesAsync();
        }

        await DeleteFixtureDirectoryAsync();
    }

    private async Task StopTrackedProcessesAsync()
    {
        var identities = ReadProcessIdentities();
        try
        {
            await StopHolderAsync(identities.Holder);
        }
        finally
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await WaitForTrackedExitAsync(identities.Parent, deadline.Token);
        }
    }

    private async Task StopHolderAsync(ProcessIdentity identity)
    {
        using var holder = OperatingSystem.IsWindows() ? FindTrackedProcess(identity) : null;
        try
        {
            File.WriteAllText(_pidFile + ".stop", string.Empty);
        }
        finally
        {
            if (holder is not null)
            {
                await WaitForExitOrKillAsync(holder);
            }
            else if (!OperatingSystem.IsWindows())
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await WaitForTrackedExitAsync(identity, deadline.Token);
            }
        }
    }

    private async Task DeleteFixtureDirectoryAsync()
    {
        // Windows can temporarily deny deletion of the loaded DLL after the processes and
        // their handles are gone. Bound the wait and let the last deletion error fail the test.
        const int MaxWindowsRetries = 20;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                _directory.Delete(recursive: true);
                return;
            }
            catch (Exception exception) when (OperatingSystem.IsWindows()
                && attempt < MaxWindowsRetries
                && (exception is IOException or UnauthorizedAccessException))
            {
                await Task.Delay(100);
            }
        }
    }

    private static async Task WaitForTrackedExitAsync(ProcessIdentity identity, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            using var process = FindTrackedProcess(identity);
            if (process is not null)
            {
                await process.WaitForExitAsync(cancellationToken);
            }

            return;
        }

        // Poll the creation identity on Unix so an exit followed by PID reuse cannot extend the wait.
        while (true)
        {
            try
            {
                using var process = FindTrackedProcess(identity);
                if (process is null)
                {
                    return;
                }
            }
            catch (System.ComponentModel.Win32Exception) when (OperatingSystem.IsMacOS())
            {
                // StartTime can become unavailable before HasExited observes termination.
                // Retry within the caller's deadline until the PID disappears or its identity changes.
            }

            await Task.Delay(25, cancellationToken);
        }
    }

    private static async Task WaitForExitOrKillAsync(Process process)
    {
        var exit = process.WaitForExitAsync();
        try
        {
            await exit.WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (TimeoutException)
        {
            try
            {
                process.Kill();
            }
            catch (Exception exception) when ((exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                && process.HasExited)
            {
                // The process exited while the fallback kill was starting.
            }

            await exit.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private (ProcessIdentity Parent, ProcessIdentity Holder) ReadProcessIdentities()
    {
        // The complete file is visible before Windows necessarily releases the rename handle.
        using var stream = File.Open(_pidFile, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return (ParseIdentity(reader.ReadLine()!), ParseIdentity(reader.ReadLine()!));
    }

    private static ProcessIdentity ParseIdentity(string text)
    {
        var separator = text.IndexOf(' ');
        var id = int.Parse(text.AsSpan(0, separator), System.Globalization.CultureInfo.InvariantCulture);
        if (!OperatingSystem.IsLinux())
        {
            return new ProcessIdentity(id, long.Parse(text.AsSpan(separator + 1), System.Globalization.CultureInfo.InvariantCulture));
        }

        // Linux StartTime combines the start tick with a boot-time estimate local to each runtime.
        // The kernel starttime field is stable across processes. After comm, field 3 starts at index 0.
        var fields = text[(text.LastIndexOf(')') + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new ProcessIdentity(id, long.Parse(fields[19], System.Globalization.CultureInfo.InvariantCulture));
    }

    private static Process? FindTrackedProcess(ProcessIdentity identity)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(identity.Id);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var matches = false;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Bind the handle before reading its creation time. Waiting or killing then
                // uses the verified process object even if its numeric PID becomes stale.
                _ = process.SafeHandle;
            }

            matches = !process.HasExited && (OperatingSystem.IsLinux()
                ? ParseIdentity(File.ReadAllText(FormattableString.Invariant($"/proc/{identity.Id}/stat"))) == identity
                : process.StartTime.ToUniversalTime().Ticks == identity.StartStamp);
            return matches ? process : null;
        }
        catch (IOException exception) when (OperatingSystem.IsLinux()
            && (exception is FileNotFoundException or DirectoryNotFoundException))
        {
            return null;
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception) when (process.HasExited)
        {
            return null;
        }
        finally
        {
            if (!matches)
            {
                process.Dispose();
            }
        }
    }

    private static byte[] CompileProgram()
    {
        var compilation = CSharpCompilation.Create(
            "PipeFixture",
            [CSharpSyntaxTree.ParseText(Program)],
            MetadataReferenceSets.Runtime(),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return stream.ToArray();
    }

    private readonly record struct ProcessIdentity(int Id, long StartStamp);

    private static readonly Lazy<byte[]> _program = new(CompileProgram);
    private static readonly string _runtimeConfig =
        Path.ChangeExtension(typeof(DotNetPipeFixture).Assembly.Location, ".runtimeconfig.json");

    private readonly DirectoryInfo _directory;
    private readonly string _assemblyPath;
    private readonly string _pidFile;
}
