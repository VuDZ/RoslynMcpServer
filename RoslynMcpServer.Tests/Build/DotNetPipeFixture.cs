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

        if (args[0] == "hold")
        {
            var stopBy = DateTime.UtcNow.AddSeconds(90);
            while (!File.Exists(args[1]) && DateTime.UtcNow < stopBy)
            {
                Thread.Sleep(25);
            }

            return 0;
        }

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
        using var holder = Process.Start(startInfo)!;
        File.WriteAllLines(args[0] + ".tmp", [Environment.ProcessId.ToString(), holder.Id.ToString()]);
        File.Move(args[0] + ".tmp", args[0]);
        return 0;
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

    public async Task WaitForExitedParentAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!File.Exists(_pidFile))
        {
            await Task.Delay(25, deadline.Token);
        }

        var pids = ReadProcessIds();
        using var parent = FindProcess(pids.Parent);
        if (parent is not null)
        {
            await parent.WaitForExitAsync(deadline.Token);
        }

        using var holder = FindProcess(pids.Holder);
        Assert.True(holder is { HasExited: false }, "The child should still hold the pipes after the parent exits.");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (File.Exists(_pidFile))
            {
                using var holder = FindProcess(ReadProcessIds().Holder);
                if (holder is { HasExited: false })
                {
                    await StopHolderAsync(holder);
                }
            }
        }
        finally
        {
            _directory.Delete(recursive: true);
        }
    }

    private async Task StopHolderAsync(Process holder)
    {
        File.WriteAllText(_pidFile + ".stop", string.Empty);
        var exit = holder.WaitForExitAsync();
        try
        {
            await exit.WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (TimeoutException)
        {
            try
            {
                holder.Kill();
            }
            catch (InvalidOperationException) when (holder.HasExited)
            {
                // The holder exited while the fallback kill was starting.
            }

            await exit.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private (int Parent, int Holder) ReadProcessIds()
    {
        var pids = File.ReadAllLines(_pidFile);
        return (int.Parse(pids[0], System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(pids[1], System.Globalization.CultureInfo.InvariantCulture));
    }

    private static Process? FindProcess(int pid)
    {
        try
        {
            return Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            return null;
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

    private static readonly Lazy<byte[]> _program = new(CompileProgram);
    private static readonly string _runtimeConfig =
        Path.ChangeExtension(typeof(DotNetPipeFixture).Assembly.Location, ".runtimeconfig.json");

    private readonly DirectoryInfo _directory;
    private readonly string _assemblyPath;
    private readonly string _pidFile;
}
