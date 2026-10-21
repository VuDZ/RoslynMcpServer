using RoslynMcpServer.Services.Analyzers;
using Xunit;

namespace RoslynMcpServer.Tests.Analyzers;

public sealed class InProcessAnalyzerAssemblyLoaderPathTests
{
    [Fact]
    public void Path_keys_ignore_case_only_on_Windows()
    {
        var equal = InProcessAnalyzerAssemblyLoader.PathComparer.Equals(
            "/opt/shadow/Analyzer.dll",
            "/opt/shadow/analyzer.dll");

        Assert.Equal(OperatingSystem.IsWindows(), equal);
    }

    [Fact]
    public void Identity_keys_ignore_case_on_every_operating_system()
    {
        Assert.True(InProcessAnalyzerAssemblyLoader.IdentityComparer.Equals(
            "Sample.Analyzer,1.0.0.0",
            "sample.analyzer,1.0.0.0"));
    }
}
