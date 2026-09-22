using RoslynMcpServer.Services;
using Xunit;

namespace RoslynMcpServer.Tests;

public sealed class WorkspaceHealthReporterTests
{
    [Fact]
    public void HasRestoreAssets_missing_obj_does_not_throw()
    {
        var root = CreateTempRoot();
        try
        {
            var projectDir = Path.Combine(root, "src", "App");
            Directory.CreateDirectory(projectDir);
            var project = Path.Combine(projectDir, "App.csproj");
            File.WriteAllText(project, "<Project />");

            var found = WorkspaceHealthReporter.HasRestoreAssets(project, outputFilePath: null);

            Assert.False(found);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void HasRestoreAssets_finds_sdk_obj_beside_project()
    {
        var root = CreateTempRoot();
        try
        {
            var projectDir = Path.Combine(root, "src", "App");
            Directory.CreateDirectory(Path.Combine(projectDir, "obj"));
            var project = Path.Combine(projectDir, "App.csproj");
            File.WriteAllText(project, "<Project />");
            File.WriteAllText(Path.Combine(projectDir, "obj", "project.assets.json"), "{}");

            Assert.True(WorkspaceHealthReporter.HasRestoreAssets(project, outputFilePath: null));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void HasRestoreAssets_finds_arcade_artifacts_obj()
    {
        var root = CreateTempRoot();
        try
        {
            var projectDir = Path.Combine(root, "src", "Compilers", "CSharp", "Portable");
            Directory.CreateDirectory(projectDir);
            var project = Path.Combine(projectDir, "Microsoft.CodeAnalysis.CSharp.csproj");
            File.WriteAllText(project, "<Project />");
            var assetsDir = Path.Combine(root, "artifacts", "obj", "Microsoft.CodeAnalysis.CSharp");
            Directory.CreateDirectory(assetsDir);
            File.WriteAllText(Path.Combine(assetsDir, "project.assets.json"), "{}");

            Assert.True(WorkspaceHealthReporter.HasRestoreAssets(project, outputFilePath: null));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void HasRestoreAssets_finds_obj_sibling_of_output_bin()
    {
        var root = CreateTempRoot();
        try
        {
            var projectDir = Path.Combine(root, "src", "App");
            Directory.CreateDirectory(projectDir);
            var project = Path.Combine(projectDir, "App.csproj");
            File.WriteAllText(project, "<Project />");
            var assetsDir = Path.Combine(root, "custom", "obj", "App");
            Directory.CreateDirectory(assetsDir);
            File.WriteAllText(Path.Combine(assetsDir, "project.assets.json"), "{}");
            var output = Path.Combine(root, "custom", "bin", "App", "Debug", "net10.0", "App.dll");

            Assert.True(WorkspaceHealthReporter.HasRestoreAssets(project, output));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "rms-health-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
