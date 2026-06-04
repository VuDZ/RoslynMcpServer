using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using RoslynMcpServer.Hosting;
using RoslynMcpServer.Tools;

namespace RoslynMcpServer.Tests;

/// <summary>
/// MCP SDK invokes tool methods via <see cref="ActivatorUtilities.CreateInstance"/> on the tool host type.
/// Every constructor parameter must be registered in DI — these tests mirror production registration.
/// </summary>
public sealed class McpToolActivationTests
{
    public static TheoryData<Type> McpToolHostTypes =>
        new(McpToolRegistry.ToolTypes);

    [Theory]
    [MemberData(nameof(McpToolHostTypes))]
    public void ActivatorUtilities_creates_each_mcp_tool_host(Type toolType)
    {
        using var host = BuildHost();
        var instance = ActivatorUtilities.CreateInstance(host.Services, toolType);
        Assert.NotNull(instance);
        Assert.IsType(toolType, instance, exactMatch: false);
    }

    [Fact]
    public void ProjectTools_does_not_require_NuGetTools_in_constructor()
    {
        var parameters = typeof(ProjectTools)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single()
            .GetParameters();

        Assert.DoesNotContain(parameters, p => p.ParameterType == typeof(NuGetTools));
    }

    [Fact]
    public void Add_package_reference_tool_is_on_ProjectTools_not_NuGetTools()
    {
        Assert.Contains(
            GetMcpToolMethodNames(typeof(ProjectTools)),
            name => name == "add_package_reference");

        Assert.DoesNotContain(
            GetMcpToolMethodNames(typeof(NuGetTools)),
            name => name == "add_package_reference");
    }

    [Fact]
    public void List_outdated_packages_tool_is_on_NuGetTools()
    {
        Assert.Contains(
            GetMcpToolMethodNames(typeof(NuGetTools)),
            name => name == "list_outdated_packages");
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddRoslynMcpServerTools();
        return builder.Build();
    }

    private static IEnumerable<string> GetMcpToolMethodNames(Type toolHostType) =>
        toolHostType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()!.Name
                         ?? m.Name);
}
