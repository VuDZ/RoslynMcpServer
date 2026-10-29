using System.Text.Json;
using RoslynMcpServer.Diagnostics;

namespace RoslynMcpServer.Tests.UsageStatistics;

internal sealed class UsageTestFiles : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "RoslynMcpUsage-" + Guid.NewGuid().ToString("N"));
    internal string UsageDirectory => Path.Combine(Root, "usage");
    internal string ProjectPath => Path.Combine(Root, "project", "App.csproj");

    internal UsageTestFiles()
    {
        Directory.CreateDirectory(Root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    internal void CreateProject()
    {
        MsBuildBootstrapper.Register();
        Directory.CreateDirectory(Path.GetDirectoryName(ProjectPath)!);
        File.WriteAllText(ProjectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(ProjectPath)!, "Widget.cs"), "namespace App; public sealed class Widget { }");
    }

    internal JsonElement[] ReadEvents() =>
        Directory.Exists(UsageDirectory)
            ? Directory.GetFiles(UsageDirectory, "usage-*.jsonl")
                .SelectMany(File.ReadAllLines)
                .Select(line => JsonSerializer.Deserialize<JsonElement>(line))
                .OrderBy(item => item.GetProperty("seq").GetInt64())
                .ToArray()
            : [];


}
