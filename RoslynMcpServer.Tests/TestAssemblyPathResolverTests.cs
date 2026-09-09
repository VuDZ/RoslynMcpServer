using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class TestAssemblyPathResolverTests
{
    [Fact]
    public void TryResolve_returns_dll_when_assembly_exists()
    {
        using var dir = new TempDir();
        var csproj = Path.Combine(dir.Path, "Foo.Tests.csproj");
        File.WriteAllText(csproj, "<Project />");
        var dll = Path.Combine(dir.Path, "Foo.Tests.dll");
        File.WriteAllBytes(dll, [0]);

        var result = TestAssemblyPathResolver.TryResolve(
            loadedWorkspacePath: Path.Combine(dir.Path, "App.sln"),
            targetCsprojPath: csproj,
            binariesDirectory: dir.Path,
            projects: [new TestAssemblyPathResolver.ProjectHint(csproj, "Foo.Tests")]);

        Assert.True(result.Success);
        Assert.Equal(Path.GetFullPath(dll), result.AssemblyPath);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void TryResolve_falls_back_to_csproj_name_when_assembly_name_empty()
    {
        using var dir = new TempDir();
        var csproj = Path.Combine(dir.Path, "Bar.Tests.csproj");
        File.WriteAllText(csproj, "<Project />");
        var dll = Path.Combine(dir.Path, "Bar.Tests.dll");
        File.WriteAllBytes(dll, [0]);

        var result = TestAssemblyPathResolver.TryResolve(
            loadedWorkspacePath: Path.Combine(dir.Path, "App.slnx"),
            targetCsprojPath: csproj,
            binariesDirectory: dir.Path,
            projects: [new TestAssemblyPathResolver.ProjectHint(csproj, "  ")]);

        Assert.True(result.Success);
        Assert.Equal(Path.GetFullPath(dll), result.AssemblyPath);
    }

    [Fact]
    public void TryResolve_fails_when_workspace_not_loaded()
    {
        var result = TestAssemblyPathResolver.TryResolve(
            loadedWorkspacePath: null,
            targetCsprojPath: @"C:\src\Foo.Tests.csproj",
            binariesDirectory: @"C:\out",
            projects: []);

        Assert.False(result.Success);
        Assert.Contains("load_workspace", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TryResolve_fails_when_loaded_workspace_is_csproj()
    {
        var result = TestAssemblyPathResolver.TryResolve(
            loadedWorkspacePath: @"C:\src\App.csproj",
            targetCsprojPath: @"C:\src\Foo.Tests.csproj",
            binariesDirectory: @"C:\out",
            projects: []);

        Assert.False(result.Success);
        Assert.Contains(".sln", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TryResolve_fails_when_target_is_not_csproj()
    {
        using var dir = new TempDir();
        var sln = Path.Combine(dir.Path, "App.sln");
        File.WriteAllText(sln, "Microsoft Visual Studio Solution File");

        var result = TestAssemblyPathResolver.TryResolve(
            loadedWorkspacePath: sln,
            targetCsprojPath: sln,
            binariesDirectory: dir.Path,
            projects: []);

        Assert.False(result.Success);
        Assert.Contains(".csproj", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TryResolve_fails_when_binaries_path_is_not_a_directory()
    {
        using var dir = new TempDir();
        var csproj = Path.Combine(dir.Path, "Foo.Tests.csproj");
        File.WriteAllText(csproj, "<Project />");
        var missing = Path.Combine(dir.Path, "missing-bin");

        var result = TestAssemblyPathResolver.TryResolve(
            loadedWorkspacePath: Path.Combine(dir.Path, "App.sln"),
            targetCsprojPath: csproj,
            binariesDirectory: missing,
            projects: [new TestAssemblyPathResolver.ProjectHint(csproj, "Foo.Tests")]);

        Assert.False(result.Success);
        Assert.Contains("not a directory", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TryResolve_fails_when_project_not_in_loaded_workspace()
    {
        using var dir = new TempDir();
        var csproj = Path.Combine(dir.Path, "Foo.Tests.csproj");
        File.WriteAllText(csproj, "<Project />");

        var result = TestAssemblyPathResolver.TryResolve(
            loadedWorkspacePath: Path.Combine(dir.Path, "App.sln"),
            targetCsprojPath: csproj,
            binariesDirectory: dir.Path,
            projects: [new TestAssemblyPathResolver.ProjectHint(Path.Combine(dir.Path, "Other.csproj"), "Other")]);

        Assert.False(result.Success);
        Assert.Contains("was not found", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TryResolve_fails_when_dll_missing()
    {
        using var dir = new TempDir();
        var csproj = Path.Combine(dir.Path, "Foo.Tests.csproj");
        File.WriteAllText(csproj, "<Project />");

        var result = TestAssemblyPathResolver.TryResolve(
            loadedWorkspacePath: Path.Combine(dir.Path, "App.sln"),
            targetCsprojPath: csproj,
            binariesDirectory: dir.Path,
            projects: [new TestAssemblyPathResolver.ProjectHint(csproj, "Foo.Tests")]);

        Assert.False(result.Success);
        Assert.Contains("test assembly not found", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("Foo.Tests.dll", result.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Solution-target build succeeded", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void TryResolve_requireExists_false_returns_path_when_dll_missing()
    {
        using var dir = new TempDir();
        var csproj = Path.Combine(dir.Path, "Foo.Tests.csproj");
        File.WriteAllText(csproj, "<Project />");
        var expectedDll = Path.GetFullPath(Path.Combine(dir.Path, "Foo.Tests.dll"));

        var result = TestAssemblyPathResolver.TryResolve(
            loadedWorkspacePath: Path.Combine(dir.Path, "App.sln"),
            targetCsprojPath: csproj,
            binariesDirectory: dir.Path,
            projects: [new TestAssemblyPathResolver.ProjectHint(csproj, "Foo.Tests")],
            requireExists: false);

        Assert.True(result.Success);
        Assert.Equal(expectedDll, result.AssemblyPath);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void TryResolve_requireExists_false_returns_path_when_directory_missing()
    {
        using var dir = new TempDir();
        var csproj = Path.Combine(dir.Path, "Foo.Tests.csproj");
        File.WriteAllText(csproj, "<Project />");
        var missing = Path.Combine(dir.Path, "missing-bin");
        var expectedDll = Path.GetFullPath(Path.Combine(missing, "Foo.Tests.dll"));

        var result = TestAssemblyPathResolver.TryResolve(
            loadedWorkspacePath: Path.Combine(dir.Path, "App.sln"),
            targetCsprojPath: csproj,
            binariesDirectory: missing,
            projects: [new TestAssemblyPathResolver.ProjectHint(csproj, "Foo.Tests")],
            requireExists: false);

        Assert.True(result.Success);
        Assert.Equal(expectedDll, result.AssemblyPath);
    }

    [Fact]
    public void TryResolve_fails_when_binaries_path_is_empty()
    {
        using var dir = new TempDir();
        var csproj = Path.Combine(dir.Path, "Foo.Tests.csproj");
        File.WriteAllText(csproj, "<Project />");

        var result = TestAssemblyPathResolver.TryResolve(
            loadedWorkspacePath: Path.Combine(dir.Path, "App.sln"),
            targetCsprojPath: csproj,
            binariesDirectory: "  ",
            projects: [new TestAssemblyPathResolver.ProjectHint(csproj, "Foo.Tests")],
            requireExists: false);

        Assert.False(result.Success);
        Assert.Contains("empty", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureAssemblyExists_after_build_includes_binariesPath_hint()
    {
        using var dir = new TempDir();
        var missing = Path.Combine(dir.Path, "Foo.Tests.dll");

        var result = TestAssemblyPathResolver.EnsureAssemblyExists(
            missing, afterSolutionTargetBuild: true);

        Assert.False(result.Success);
        Assert.Contains("test assembly not found", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("Solution-target build succeeded", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("binariesPath", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(".runtimeconfig.json", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureAssemblyExists_returns_ok_when_file_present()
    {
        using var dir = new TempDir();
        var dll = Path.Combine(dir.Path, "Foo.Tests.dll");
        File.WriteAllBytes(dll, [0]);

        var result = TestAssemblyPathResolver.EnsureAssemblyExists(
            dll, afterSolutionTargetBuild: true);

        Assert.True(result.Success);
        Assert.Equal(Path.GetFullPath(dll), result.AssemblyPath);
    }

    [Fact]
    public void TryResolve_matches_project_path_case_insensitively_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDir();
        var csproj = Path.Combine(dir.Path, "Foo.Tests.csproj");
        File.WriteAllText(csproj, "<Project />");
        var dll = Path.Combine(dir.Path, "Foo.Tests.dll");
        File.WriteAllBytes(dll, [0]);

        var result = TestAssemblyPathResolver.TryResolve(
            loadedWorkspacePath: Path.Combine(dir.Path, "App.sln"),
            targetCsprojPath: csproj,
            binariesDirectory: dir.Path,
            projects: [new TestAssemblyPathResolver.ProjectHint(csproj.ToUpperInvariant(), "Foo.Tests")]);

        Assert.True(result.Success);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("rmcp-test-asm-").FullName;

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
