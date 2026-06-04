using RoslynMcpServer.Diagnostics;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class MsBuildLogHighlighterTests
{
    [Fact]
    public void CollectHighlights_includes_msbuild_path_and_task_failure_context()
    {
        const string log = """
            MSBuild executable path = C:\Program Files\dotnet\sdk\10.0.204\MSBuild.dll
            line before failure
            error MSB4276: The SDK 'Microsoft.NET.Sdk' specified could not be found.
            Done executing task "MSBuild" -- FAILED.
            """;
        var highlights = MsBuildLogHighlighter.CollectHighlights(log);
        Assert.Contains(highlights, h => h.Contains("MSBuild executable path", StringComparison.Ordinal));
        Assert.Contains(highlights, h => h.Contains("MSB4276", StringComparison.Ordinal));
        Assert.Contains(highlights, h => h.Contains("Done executing task", StringComparison.Ordinal));
    }

    [Fact]
    public void CollectHighlights_includes_bare_task_failed_with_project_context()
    {
        const string log = """
            Building project "C:\repo\src\App\App.csproj" ...
            _FilterRestoreGraphProjectInputItems -- FAILED.
            """;
        var highlights = MsBuildLogHighlighter.CollectHighlights(log);
        Assert.Contains(highlights, h => h.Contains("_FilterRestoreGraphProjectInputItems", StringComparison.Ordinal));
        Assert.Contains(highlights, h => h.Contains("App.csproj", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryGetMsBuildExecutablePath_parses_detailed_restore_line()
    {
        const string log = "       MSBuild executable path = C:\\Program Files\\dotnet\\sdk\\9.0.314\\MSBuild.dll";
        var path = MsBuildLogHighlighter.TryGetMsBuildExecutablePath(log);
        Assert.Contains("9.0.314", path, StringComparison.Ordinal);
    }
}
