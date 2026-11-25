using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

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
    public async Task UpdateDocumentInMemory_echo_does_not_add_a_second_input_revision()
    {
        await File.WriteAllTextAsync(_sourcePath, OriginalSourceText, BomFreeUtf8);
        var manager = await LoadAsync();
        var session = manager.DiskWatcherSession;
        Assert.NotNull(session);
        var before = session.InputRevisionForPath(_sourcePath);

        var result = await manager.UpdateDocumentInMemoryAsync(_sourcePath, EditedSourceText, CancellationToken.None);

        Assert.True(result.IsFullSuccess, result.FormatAdapterMessage("write failed"));
        var afterWrite = session.InputRevisionForPath(_sourcePath);
        Assert.True(afterWrite > before);
        manager.NotifyDiskWatcherChange(
            session,
            new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.GetDirectoryName(_sourcePath)!, Path.GetFileName(_sourcePath)));
        manager.ReconcileInputEvents();
        Assert.Equal(afterWrite, session.InputRevisionForPath(_sourcePath));
        Assert.Equal(session.Generation, manager.PullInputSnapshot().Generation);
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

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Concurrent_patches_preserve_both_edits_and_bom(bool inWorkspace, bool replaceAll)
    {
        var (first, second, content) = await ApplyInterleavedPatchesAsync(
            inWorkspace,
            replaceAll,
            competingOldString: "class Class1",
            competingNewString: "class Renamed");

        Assert.Contains("Patch applied successfully", first, StringComparison.Ordinal);
        Assert.Contains(replaceAll ? "2 replacement(s)" : "1 replacement(s)", first, StringComparison.Ordinal);
        Assert.Contains("Patch applied successfully", second, StringComparison.Ordinal);
        var secondValue = replaceAll ? 42 : 1;
        Assert.Equal($"namespace App; public sealed class Renamed {{ public int X = 42; public int Y = {secondValue}; }}", content);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Concurrent_patch_reports_conflict_when_its_fragment_is_gone(bool inWorkspace)
    {
        var (first, second, content) = await ApplyInterleavedPatchesAsync(
            inWorkspace,
            replaceAll: false,
            competingOldString: "= 1",
            competingNewString: "= 5");

        Assert.Contains("Error: Patch conflict", first, StringComparison.Ordinal);
        Assert.Contains("Patch applied successfully", second, StringComparison.Ordinal);
        Assert.Equal("namespace App; public sealed class Class1 { public int X = 5; public int Y = 5; }", content);
    }

    private async Task<(string First, string Second, string Content)> ApplyInterleavedPatchesAsync(
        bool inWorkspace,
        bool replaceAll,
        string competingOldString,
        string competingNewString)
    {
        var path = inWorkspace ? _sourcePath : Path.Combine(_outsideRoot, "Patched.cs");
        const string source = "namespace App; public sealed class Class1 { public int X = 1; public int Y = 1; }";
        await File.WriteAllTextAsync(path, source, Utf8WithBom);
        var manager = await LoadAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var gate = new FirstPatchReadGate();
        var firstTool = new UtilityTools(gate, manager);
        var secondTool = new UtilityTools(NullLogger<UtilityTools>.Instance, manager);

        var first = Task.Run(() => firstTool.ApplyPatch(path, "= 1", "= 42", replaceAll, deadline.Token));
        string firstMessage;
        string secondMessage;
        try
        {
            await gate.FirstRead.WaitAsync(deadline.Token);
            secondMessage = await secondTool.ApplyPatch(
                path,
                competingOldString,
                competingNewString,
                replaceAll: true,
                cancellationToken: deadline.Token);
        }
        finally
        {
            gate.Resume();
            firstMessage = await first;
        }

        var content = await File.ReadAllTextAsync(path);
        Assert.True(StartsWithUtf8Bom(await File.ReadAllBytesAsync(path)));
        if (inWorkspace)
        {
            var document = Assert.Single(
                manager.GetCurrentSolution()!.Projects.SelectMany(project => project.Documents),
                document => document.FilePath == path);
            Assert.Equal(content, (await document.GetTextAsync()).ToString());
        }

        return (firstMessage, secondMessage, content);
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

    private sealed class FirstPatchReadGate : ILogger<UtilityTools>, IDisposable
    {
        public Task FirstRead => _firstRead.Task;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (formatter(state, exception).StartsWith("ApplyPatch start", StringComparison.Ordinal)
                && _firstRead.TrySetResult())
            {
                // The tool has read the old text, so the second request can now commit first.
                _resume.Wait(TimeSpan.FromSeconds(30));
            }
        }

        public void Resume() => _resume.Set();

        public void Dispose() => _resume.Dispose();

        private readonly TaskCompletionSource _firstRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _resume = new();
    }
}
