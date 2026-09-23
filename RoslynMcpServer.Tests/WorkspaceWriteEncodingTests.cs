using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests;

/// <summary>
/// End-to-end checks on real files for the two writes one edit performs.
/// <c>MSBuildWorkspace</c> rewrites a changed document during <c>TryApplyChanges</c> with
/// <c>SourceText.Encoding</c>, so an <c>Encoding.UTF8</c> candidate added a BOM to files that had none. A plain
/// <c>File.WriteAllText</c> without an encoding is the mirror trap: for a path outside the loaded snapshot it is
/// the only write, and it stripped a BOM the file had.
/// </summary>
public sealed class WorkspaceWriteEncodingTests : IDisposable
{
    private const string OriginalSourceText = "namespace App; public sealed class Class1 { }";
    private const string OriginalFragment = "public sealed class Class1 { }";
    private const string EditedFragment = "public sealed class Class1 { public int X { get; set; } }";
    private const string EditedSourceText = "namespace App; " + EditedFragment;

    private static readonly UTF8Encoding BomFreeUtf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "RoslynMcpWriteEncoding-" + Guid.NewGuid().ToString("N"));

    /// <summary>Sibling temp directory: every path here is outside the loaded project, so no document owns it.</summary>
    private readonly string _outsideRoot = Path.Combine(
        Path.GetTempPath(),
        "RoslynMcpWriteEncodingOutside-" + Guid.NewGuid().ToString("N"));

    private readonly string _csprojPath;
    private readonly string _sourcePath;

    public WorkspaceWriteEncodingTests()
    {
        MsBuildBootstrapper.Register();
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outsideRoot);
        _csprojPath = Path.Combine(_root, "App.csproj");
        _sourcePath = Path.Combine(_root, "Class1.cs");
        File.WriteAllText(
            _csprojPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
    }

    public void Dispose()
    {
        DeleteBestEffort(_root);
        DeleteBestEffort(_outsideRoot);
    }

    [Fact]
    public async Task UpdateDocumentInMemoryAsync_does_not_add_bom_to_bom_free_file()
    {
        await File.WriteAllTextAsync(_sourcePath, OriginalSourceText, BomFreeUtf8);
        var manager = await LoadAsync();

        var result = await manager.UpdateDocumentInMemoryAsync(_sourcePath, EditedSourceText, CancellationToken.None);

        Assert.True(result.IsFullSuccess, result.FormatAdapterMessage("write failed"));
        Assert.False(StartsWithUtf8Bom(await File.ReadAllBytesAsync(_sourcePath)));
        Assert.Equal(EditedSourceText, await File.ReadAllTextAsync(_sourcePath));
    }

    [Fact]
    public async Task UpdateDocumentInMemoryAsync_keeps_bom_of_bom_file()
    {
        await File.WriteAllTextAsync(_sourcePath, OriginalSourceText, Utf8WithBom);
        var manager = await LoadAsync();

        var result = await manager.UpdateDocumentInMemoryAsync(_sourcePath, EditedSourceText, CancellationToken.None);

        Assert.True(result.IsFullSuccess, result.FormatAdapterMessage("write failed"));
        Assert.True(StartsWithUtf8Bom(await File.ReadAllBytesAsync(_sourcePath)));
        Assert.Equal(EditedSourceText, await File.ReadAllTextAsync(_sourcePath));
    }

    [Fact]
    public async Task ApplyPatch_does_not_add_bom_to_workspace_sources()
    {
        await File.WriteAllTextAsync(_sourcePath, OriginalSourceText, BomFreeUtf8);
        var tool = new UtilityTools(NullLogger<UtilityTools>.Instance, await LoadAsync());

        var message = await tool.ApplyPatch(
            _sourcePath,
            OriginalFragment,
            EditedFragment,
            replaceAll: false,
            CancellationToken.None);

        Assert.Contains("Patch applied successfully", message, StringComparison.Ordinal);
        Assert.Equal(EditedSourceText, await File.ReadAllTextAsync(_sourcePath));
        Assert.False(StartsWithUtf8Bom(await File.ReadAllBytesAsync(_sourcePath)), message);
    }

    [Fact]
    public async Task WriteFile_does_not_add_bom_to_workspace_sources()
    {
        await File.WriteAllTextAsync(_sourcePath, OriginalSourceText, BomFreeUtf8);
        var tool = new EditingTools(NullLogger<EditingTools>.Instance, await LoadAsync());

        var message = await tool.WriteFile(_sourcePath, EditedSourceText, CancellationToken.None);

        Assert.Contains("Successfully wrote", message, StringComparison.Ordinal);
        Assert.False(StartsWithUtf8Bom(await File.ReadAllBytesAsync(_sourcePath)), message);
    }

    [Fact]
    public async Task WriteFile_keeps_bom_of_in_project_non_source_file()
    {
        // No DocumentId matches a non-.cs path, so this goes through the manual workspace-file write.
        var path = Path.Combine(_root, "settings.json");
        await File.WriteAllTextAsync(path, """{ "key": "value" }""", Utf8WithBom);
        var tool = new EditingTools(NullLogger<EditingTools>.Instance, await LoadAsync());

        var message = await tool.WriteFile(path, """{ "key": "changed" }""", CancellationToken.None);

        Assert.Contains("Successfully wrote", message, StringComparison.Ordinal);
        Assert.True(StartsWithUtf8Bom(await File.ReadAllBytesAsync(path)), message);
    }

    [Fact]
    public async Task WriteFile_keeps_bom_of_file_outside_workspace()
    {
        var path = Path.Combine(_outsideRoot, "Outside.cs");
        await File.WriteAllTextAsync(path, OriginalSourceText, Utf8WithBom);
        var tool = new EditingTools(NullLogger<EditingTools>.Instance, await LoadAsync());

        var message = await tool.WriteFile(path, EditedSourceText, CancellationToken.None);

        Assert.Contains("Successfully wrote", message, StringComparison.Ordinal);
        Assert.Equal(EditedSourceText, await File.ReadAllTextAsync(path));
        Assert.True(StartsWithUtf8Bom(await File.ReadAllBytesAsync(path)), message);
    }

    [Fact]
    public async Task WriteFile_new_file_outside_workspace_has_no_bom()
    {
        var path = Path.Combine(_outsideRoot, "BrandNew.cs");
        var tool = new EditingTools(NullLogger<EditingTools>.Instance, await LoadAsync());

        var message = await tool.WriteFile(path, OriginalSourceText, CancellationToken.None);

        Assert.Contains("Successfully wrote", message, StringComparison.Ordinal);
        Assert.False(StartsWithUtf8Bom(await File.ReadAllBytesAsync(path)), message);
    }

    [Fact]
    public async Task ApplyPatch_keeps_bom_of_file_outside_workspace()
    {
        var path = Path.Combine(_outsideRoot, "Patched.cs");
        await File.WriteAllTextAsync(path, OriginalSourceText, Utf8WithBom);
        var tool = new UtilityTools(NullLogger<UtilityTools>.Instance, await LoadAsync());

        var message = await tool.ApplyPatch(
            path,
            OriginalFragment,
            EditedFragment,
            replaceAll: false,
            CancellationToken.None);

        Assert.Contains("Patch applied successfully", message, StringComparison.Ordinal);
        Assert.Equal(EditedSourceText, await File.ReadAllTextAsync(path));
        Assert.True(StartsWithUtf8Bom(await File.ReadAllBytesAsync(path)), message);
    }

    private async Task<SolutionManager> LoadAsync()
    {
        var manager = SolutionManagerTestFactory.Create();
        await manager.LoadAndPrepareAsync(
            _csprojPath,
            shadowCopyInSolutionAnalyzers: false,
            CancellationToken.None);
        return manager;
    }

    private static void DeleteBestEffort(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // best-effort temp cleanup; the workspace may still hold a watcher
        }
    }

    private static bool StartsWithUtf8Bom(byte[] bytes)
    {
        return bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
    }
}
