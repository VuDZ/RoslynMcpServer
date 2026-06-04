using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Formatting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Hosting;
using RoslynMcpServer.Tools;
using Serilog;
using System.Reflection;

// MSBuild path must match process bitness (64-bit MCP + x86 SDK → BadImageFormatException).
MsBuildBootstrapper.Register();

// Force-load C# language / workspace assemblies before any Roslyn workspace use
try
{
    // CSharp.Workspaces + Microsoft.CodeAnalysis.CSharp (LanguageNames lives in Workspaces, not CSharp.*)
    _ = typeof(CSharpFormattingOptions).Assembly;
    _ = typeof(SyntaxFactory).Assembly;
    _ = Assembly.Load(new AssemblyName("Microsoft.CodeAnalysis.Features"));
    _ = Assembly.Load(new AssemblyName("Microsoft.CodeAnalysis.CSharp.Features"));
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[ERR] Failed to force load C# services: {ex.Message}");
}

// Do not set CurrentDirectory to AppContext.BaseDirectory: that points at bin/Release/.../win-x64 and breaks
// tools that default to Environment.CurrentDirectory (SearchCode, execute_dotnet_command, scratchpad, etc.).
// `dotnet run` normally keeps cwd at the project folder; published/self-contained runs may differ — use env below.
ApplyOptionalWorkspaceRootFromEnvironment();

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();

var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "mcp-.log");
builder.Services.AddSerilog((_, configuration) =>
{
    configuration
        .MinimumLevel.Information()
        .Enrich.FromLogContext()
        .WriteTo.File(
            path: logPath,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            shared: true);
});

// MCP stdio transport + tools (see RoslynMcpServiceCollectionExtensions).
builder.Services
    .AddRoslynMcpServerTools()
    .WithPrompts<BasicPrompts>();

await builder.Build().RunAsync();

static void ApplyOptionalWorkspaceRootFromEnvironment()
{
    var raw = Environment.GetEnvironmentVariable("ROSLYN_MCP_WORKSPACE");
    if (string.IsNullOrWhiteSpace(raw))
    {
        return;
    }

    try
    {
        var full = Path.GetFullPath(raw.Trim());
        if (Directory.Exists(full))
        {
            Directory.SetCurrentDirectory(full);
            Console.Error.WriteLine($"[RoslynMcp] ROSLYN_MCP_WORKSPACE: cwd = {full}");
        }
        else
        {
            Console.Error.WriteLine($"[RoslynMcp] WARN: ROSLYN_MCP_WORKSPACE directory not found: {full}");
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[RoslynMcp] WARN: ROSLYN_MCP_WORKSPACE invalid ({raw}): {ex.Message}");
    }
}
