using System.Diagnostics;
using System.Xml.Linq;
using Xunit;

namespace RoslynMcpServer.Tests.WorkspaceStateCache;

internal static class RoundtripProcess
{
    public const string DirectoryVariable = "ROSLYN_TASK06_DIRECTORY";

    public static async Task RunAsync(string directory, string method, CancellationToken cancellationToken)
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
            "vstest", typeof(CrossProcessRoundtripTests).Assembly.Location,
            "--TestCaseFilter:FullyQualifiedName=RoslynMcpServer.Tests.WorkspaceStateCache.CrossProcessRoundtripTests." + method,
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
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        var stdout = await output;
        var stderr = await error;
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
}
