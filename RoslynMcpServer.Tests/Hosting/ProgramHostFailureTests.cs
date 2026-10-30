using System.Diagnostics;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcpServer.Plugins;
using Xunit;

namespace RoslynMcpServer.Tests.Hosting;

public sealed class ProgramHostFailureTests : IDisposable
{
    [Fact]
    public async Task Failed_start_preserves_the_original_exception_when_stop_also_fails()
    {
        Directory.CreateDirectory(_root);
        var pluginDirectory = Path.Combine(_root, "plugin");
        CopySamplePlugin(pluginDirectory);
        WriteFailingPlugin(pluginDirectory);
        var stoppedMarker = Path.Combine(_root, "stop-called");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = _root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(typeof(IRoslynMcpPlugin).Assembly.Location);
        start.Environment["ROSLYN_MCP_HOME"] = Path.Combine(_root, "user");
        start.Environment["ROSLYN_MCP_LOG_DIR"] = Path.Combine(_root, "logs");
        start.Environment["ROSLYN_MCP_WORKSPACE"] = _root;
        start.Environment["ROSLYN_MCP_USAGE"] = "0";
        start.Environment[PluginDiscovery.EnvironmentVariableName] = pluginDirectory;
        start.Environment["STARTUP_FAILURE_STOP_MARKER"] = stoppedMarker;
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }

        var output = await stderr;
        await stdout;
        Assert.NotEqual(0, process.ExitCode);
        Assert.True(File.Exists(stoppedMarker), "The failing hosted service must reach StopAsync.");
        Assert.Contains("startup-primary-marker", output, StringComparison.Ordinal);
        Assert.DoesNotContain("shutdown-secondary-marker", output, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static void CopySamplePlugin(string destination)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "RoslynMcpServer.sln")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var source = Path.Combine(root.FullName, "samples", "RoslynMcpPlugin", "bin", configuration, "net10.0");
        Assert.True(Directory.Exists(source), "The sample plugin must be built with the solution.");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static void WriteFailingPlugin(string directory)
    {
        const string source = """
            using System;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Hosting;
            using RoslynMcpServer.Plugins;

            public sealed class StartupFailurePlugin : IRoslynMcpPlugin
            {
                public string Name => "sample";

                public void Register(RoslynMcpPluginContext context) =>
                    context.Services.AddSingleton<IHostedService>(new FailingService());

                private sealed class FailingService : IHostedService
                {
                    public Task StartAsync(CancellationToken cancellationToken) =>
                        Task.FromException(new InvalidOperationException("startup-primary-marker"));

                    public Task StopAsync(CancellationToken cancellationToken)
                    {
                        File.WriteAllText(Environment.GetEnvironmentVariable("STARTUP_FAILURE_STOP_MARKER")!, "stopped");
                        return Task.FromException(new InvalidOperationException("shutdown-secondary-marker"));
                    }
                }
            }
            """;
        var paths = Assert.IsType<string>(AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            .Split(Path.PathSeparator)
            .Append(typeof(IRoslynMcpPlugin).Assembly.Location)
            .Distinct(StringComparer.Ordinal);
        var compilation = CSharpCompilation.Create(
            "RoslynMcpPlugin",
            [CSharpSyntaxTree.ParseText(source)],
            paths.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var emitted = compilation.Emit(Path.Combine(directory, "RoslynMcpPlugin.dll"));
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        File.WriteAllText(
            Path.Combine(directory, PluginManifest.FileName),
            JsonSerializer.Serialize(new
            {
                id = "sample",
                entry = "RoslynMcpPlugin.dll",
                pluginType = "StartupFailurePlugin",
                toolPrefix = "sample_",
                minHostVersion = "1.0.0",
            }));
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mcp-host-failure-" + Guid.NewGuid().ToString("N"));
}
