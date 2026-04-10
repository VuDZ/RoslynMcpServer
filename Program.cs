using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Formatting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Tools;
using Serilog;

// --- MSBuild Locator (explicit VS 17+ preference) ---
var instances = MSBuildLocator.QueryVisualStudioInstances().ToList();

var queriedLines = new List<string>();
foreach (var i in instances)
{
    var detail =
        $"{i.Name} ({i.Version}) at {i.MSBuildPath}, VisualStudioRootPath={i.VisualStudioRootPath}";
    Console.Error.WriteLine($"[DEBUG] Found MSBuild: {detail}");
    queriedLines.Add(detail);
}

MsBuildEnvironmentInfo.QueriedInstanceLines = queriedLines;

var instance = instances.FirstOrDefault(i => i.Version.Major >= 17) ?? instances.FirstOrDefault();

if (instance is not null)
{
    MSBuildLocator.RegisterInstance(instance);
    Console.Error.WriteLine($"[DEBUG] Using MSBuild from: {instance.MSBuildPath}");
}
else
{
    Console.Error.WriteLine("[WARN] No MSBuild instance from QueryVisualStudioInstances; falling back to RegisterDefaults.");
    MSBuildLocator.RegisterDefaults();
}

MsBuildEnvironmentInfo.RefreshRegisteredInstance();

// Force-load C# language / workspace assemblies before any Roslyn workspace use
try
{
    // CSharp.Workspaces + Microsoft.CodeAnalysis.CSharp (LanguageNames lives in Workspaces, not CSharp.*)
    _ = typeof(CSharpFormattingOptions).Assembly;
    _ = typeof(SyntaxFactory).Assembly;
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

// Add the MCP services: the transport to use (stdio) and the tools to register.
// Inbound JSON-RPC logging: see McpInboundProtocolLogger (env MCP_LOG_INCOMING_RPC=0 to disable).
builder.Services
    .AddMcpServer(o => McpInboundProtocolLogger.Register(o))
    .WithStdioServerTransport()
    .WithTools<RoslynTools>()
    .WithTools<WorkspaceTools>()
    .WithTools<CodeAnalysisTools>()
    .WithTools<NavigationTools>()
    .WithTools<EditingTools>()
    .WithTools<BuildTools>()
    .WithTools<TestTools>()
    .WithTools<UtilityTools>()
    .WithTools<ServerLifecycleTools>()
    .WithPrompts<BasicPrompts>();

// Single long-lived Roslyn workspace + solution cache for the MCP process.
builder.Services.AddSingleton<SolutionManager>();

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
