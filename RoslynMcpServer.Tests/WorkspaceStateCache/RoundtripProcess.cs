using System.Diagnostics;
using System.Xml.Linq;
using Xunit;

namespace RoslynMcpServer.Tests.WorkspaceStateCache;

internal static class RoundtripProcess
{
    public const string DirectoryVariable = "ROSLYN_TASK06_DIRECTORY";

    public static async Task RunAsync(string directory, Type testClass, string method, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
        {
            "vstest", testClass.Assembly.Location,
            $"--TestCaseFilter:FullyQualifiedName={testClass.FullName}.{method}",
            "--logger:trx;LogFileName=child.trx", "--ResultsDirectory:" + directory,
        })
        {
            start.ArgumentList.Add(argument);
        }
        // Only this child receives the handoff. The parent process and neighboring tests remain ordinary.
        start.Environment[DirectoryVariable] = directory;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        var exitTask = process.WaitForExitAsync();
        var cleanupTimeout = TimeSpan.FromSeconds(5);
        try
        {
            await exitTask.WaitAsync(cancellationToken);
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            await exitTask.WaitAsync(cleanupTimeout);
            throw;
        }
        // An inherited pipe can stay open after the VSTest process has exited.
        var streams = await Task.WhenAll(output, error).WaitAsync(cleanupTimeout, cancellationToken);
        var stdout = streams[0];
        var stderr = streams[1];
        await File.WriteAllTextAsync(Path.Combine(directory, "child.stdout.txt"), stdout, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "child.stderr.txt"), stderr, cancellationToken);
        Assert.True(process.ExitCode == 0, stdout + "\n" + stderr);
        var counters = XDocument.Load(Path.Combine(directory, "child.trx"))
            .Descendants().Single(element => element.Name.LocalName == "Counters");
        Assert.Equal("1", counters.Attribute("total")?.Value);
        Assert.Equal("1", counters.Attribute("passed")?.Value);
        Assert.Equal("0", counters.Attribute("notExecuted")?.Value);
        var receipt = await File.ReadAllTextAsync(Path.Combine(directory, "child.pid"), cancellationToken);
        Assert.NotEqual(Environment.ProcessId, int.Parse(receipt));
    }

    internal static Task WriteReceiptAsync(string directory, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(Path.Combine(directory, "child.pid"), Environment.ProcessId.ToString(), cancellationToken);
}
