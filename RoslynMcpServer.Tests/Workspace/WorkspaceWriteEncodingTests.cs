using System.Diagnostics;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Services;
using RoslynMcpServer.Services.Workspace;
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

    [Theory]
    [InlineData("", "App")]
    [InlineData("<CodePage>28591</CodePage>", "Café")]
    public async Task Classic_project_writes_preserve_ASCII_or_require_explicit_conversion(
        string codePageProperty,
        string className)
    {
        var encoding = codePageProperty.Length == 0 ? BomFreeUtf8 : Encoding.Latin1;
        var original = $"class {className}\n{{\n    public int X => 1;\n}}\n";
        await WriteClassicProjectAsync(_csprojPath, codePageProperty);
        await File.WriteAllBytesAsync(_sourcePath, encoding.GetBytes(original));
        var manager = await LoadAsync();
        manager.SilenceDiskWatchersForTests();
        var solution = (await manager.GetPublishedSolutionAsync())!;
        var id = Assert.Single(solution.GetDocumentIdsWithFilePath(_sourcePath));
        Assert.Equal(original, (await solution.GetDocument(id)!.GetTextAsync()).ToString());

        var patch = new UtilityTools(NullLogger<UtilityTools>.Instance, manager);
        var patched = original.Replace("X => 1", "X => 2", StringComparison.Ordinal);
        var patchResult = await patch.ApplyPatch(_sourcePath, "X => 1", "X => 2", replaceAll: false, CancellationToken.None);
        if (codePageProperty.Length == 0)
        {
            Assert.Contains("Patch applied successfully", patchResult, StringComparison.Ordinal);
            await AssertPublishedAsync(patched, BomFreeUtf8);
        }
        else
        {
            Assert.Contains("update_file_content", patchResult, StringComparison.Ordinal);
            await AssertPublishedAsync(original, encoding);
        }

        var write = new EditingTools(NullLogger<EditingTools>.Instance, manager);
        var replaced = original.Replace("X => 1", "X => 3", StringComparison.Ordinal);
        var writeResult = await write.WriteFile(_sourcePath, replaced, CancellationToken.None);
        Assert.Contains("Successfully wrote", writeResult, StringComparison.Ordinal);
        await AssertPublishedAsync(replaced, codePageProperty.Length == 0 ? BomFreeUtf8 : Utf8WithBom);

        async Task AssertPublishedAsync(string expected, Encoding expectedEncoding)
        {
            Assert.Equal(
                expectedEncoding.GetPreamble().Concat(expectedEncoding.GetBytes(expected)),
                await File.ReadAllBytesAsync(_sourcePath));
            var published = (await manager.GetPublishedSolutionAsync())!;
            var text = await published.GetDocument(id)!.GetTextAsync();
            Assert.Equal(expected, text.ToString());
            Assert.Equal(expectedEncoding.CodePage, text.Encoding!.CodePage);
            Assert.Equal(expectedEncoding.GetPreamble(), text.Encoding.GetPreamble());
        }
    }

    [Fact]
    public async Task Classic_projects_with_ASCII_compatible_code_pages_can_edit_a_linked_ASCII_source()
    {
        const string Original = "class App { public int X => 1; }";
        await File.WriteAllTextAsync(_sourcePath, Original, BomFreeUtf8);
        await WriteClassicProjectAsync(_csprojPath, "<CodePage>28591</CodePage>");
        await WriteClassicProjectAsync(Path.Combine(_root, "Other.csproj"), "<CodePage>65001</CodePage>");
        var solutionPath = Path.Combine(_root, "Both.slnx");
        await File.WriteAllTextAsync(solutionPath, """<Solution><Project Path="App.csproj" /><Project Path="Other.csproj" /></Solution>""");
        var manager = SolutionManagerTestFactory.Create();
        await manager.LoadAndPrepareAsync(solutionPath, shadowCopyInSolutionAnalyzers: false, CancellationToken.None);
        manager.SilenceDiskWatchersForTests();
        var before = (await manager.GetPublishedSolutionAsync())!;
        Assert.Equal(2, before.GetDocumentIdsWithFilePath(_sourcePath).Length);

        var patch = new UtilityTools(NullLogger<UtilityTools>.Instance, manager);
        var conflictingSource = Original.Replace("App", "Café", StringComparison.Ordinal);
        var conflictingBytes = BomFreeUtf8.GetBytes(conflictingSource);
        await File.WriteAllBytesAsync(_sourcePath, conflictingBytes);
        var rejected = await patch.ApplyPatch(_sourcePath, "X => 1", "X => 2", replaceAll: false, CancellationToken.None);
        Assert.Contains("conflicting source encodings", rejected, StringComparison.Ordinal);
        Assert.Equal(conflictingBytes, await File.ReadAllBytesAsync(_sourcePath));

        await File.WriteAllTextAsync(_sourcePath, Original, BomFreeUtf8);
        var patchResult = await patch.ApplyPatch(_sourcePath, "X => 1", "X => 2", replaceAll: false, CancellationToken.None);
        Assert.Contains("Patch applied successfully", patchResult, StringComparison.Ordinal);
        Assert.Equal(
            BomFreeUtf8.GetBytes(Original.Replace("X => 1", "X => 2", StringComparison.Ordinal)),
            await File.ReadAllBytesAsync(_sourcePath));
        var write = new EditingTools(NullLogger<EditingTools>.Instance, manager);
        const string Changed = "class Changed { }";
        var writeResult = await write.WriteFile(_sourcePath, Changed, CancellationToken.None);
        Assert.Contains("Successfully wrote", writeResult, StringComparison.Ordinal);
        Assert.Equal(BomFreeUtf8.GetBytes(Changed), await File.ReadAllBytesAsync(_sourcePath));
        var published = (await manager.GetPublishedSolutionAsync())!;
        foreach (var id in before.GetDocumentIdsWithFilePath(_sourcePath))
        {
            Assert.Equal(Changed, (await published.GetDocument(id)!.GetTextAsync()).ToString());
        }
    }

    [Theory]
    [InlineData(65001, 65001, false)]
    [InlineData(65001, 1200, false)]
    [InlineData(1200, 65001, false)]
    [InlineData(28591, 28591, true)]
    [InlineData(65001, 28591, true)]
    [InlineData(65001, 1200, true)]
    public async Task ApplyPatch_writes_disk_when_restoring_the_cached_document_text(
        int originalCodePage,
        int diskCodePage,
        bool latin1Project)
    {
        var originalEncoding = originalCodePage == 65001 && !latin1Project ? BomFreeUtf8 : Encoding.GetEncoding(originalCodePage);
        var diskEncoding = diskCodePage == 65001 ? BomFreeUtf8 : Encoding.GetEncoding(diskCodePage);
        var className = diskEncoding.Preamble.IsEmpty ? "App" : "Café";
        var original = $"class {className} {{ int X => 1; }}";
        if (latin1Project)
        {
            // The imported CodePage applies to BOM-free files, even if the old snapshot used a BOM.
            await File.WriteAllTextAsync(
                Path.Combine(_root, "Directory.Build.props"),
                "<Project><PropertyGroup><CodePage>28591</CodePage></PropertyGroup></Project>");
        }

        await File.WriteAllTextAsync(_sourcePath, original, originalEncoding);
        var manager = await LoadAsync();
        manager.SilenceDiskWatchersForTests();
        var before = (await manager.GetPublishedSolutionAsync())!;
        var id = Assert.Single(before.GetDocumentIdsWithFilePath(_sourcePath));
        Assert.Equal(original, (await before.GetDocument(id)!.GetTextAsync()).ToString());
        await File.WriteAllTextAsync(
            _sourcePath,
            original.Replace("X => 1", "X => 2", StringComparison.Ordinal),
            diskEncoding);

        var tool = new UtilityTools(NullLogger<UtilityTools>.Instance, manager);
        var message = await tool.ApplyPatch(_sourcePath, "X => 2", "X => 1", replaceAll: false, CancellationToken.None);

        Assert.Contains("Patch applied successfully", message, StringComparison.Ordinal);
        var expectedEncoding = diskEncoding.Preamble.IsEmpty ? BomFreeUtf8 : diskEncoding;
        var expectedBytes = expectedEncoding.GetPreamble().Concat(expectedEncoding.GetBytes(original)).ToArray();
        Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(_sourcePath));
        var published = (await manager.GetPublishedSolutionAsync())!;
        var publishedText = await published.GetDocument(id)!.GetTextAsync();
        Assert.Equal(original, publishedText.ToString());
        Assert.Equal(expectedEncoding.CodePage, publishedText.Encoding!.CodePage);
        Assert.Equal(expectedEncoding.GetPreamble(), publishedText.Encoding.GetPreamble());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Default_code_page_redetects_external_utf8_before_writing(bool fullWrite)
    {
        const string Original = "class C { const string Value = \"Ж Marker\"; }";
        const string Expected = "class C { const string Value = \"Ж Б\"; }";
        var windows1251 = CodePagesEncodingProvider.Instance.GetEncoding(1251)!;
        await File.WriteAllBytesAsync(_sourcePath, windows1251.GetBytes(Original));
        var manager = await LoadAsync();
        manager.SilenceDiskWatchersForTests();
        var before = (await manager.GetPublishedSolutionAsync())!;
        var id = Assert.Single(before.GetDocumentIdsWithFilePath(_sourcePath));
        var cached = await before.GetDocument(id)!.GetTextAsync();
        Assert.Contains("Marker", cached.ToString(), StringComparison.Ordinal);
        await File.WriteAllTextAsync(_sourcePath, Original, BomFreeUtf8);

        if (fullWrite)
        {
            var tool = new EditingTools(NullLogger<EditingTools>.Instance, manager);
            Assert.Contains("Successfully wrote", await tool.WriteFile(_sourcePath, Expected), StringComparison.Ordinal);
        }
        else
        {
            var tool = new UtilityTools(NullLogger<UtilityTools>.Instance, manager);
            var result = await tool.ApplyPatch(_sourcePath, "Ж Marker", "Ж Б");
            Assert.Contains("Patch applied successfully", result, StringComparison.Ordinal);
        }

        Assert.Equal(Utf8WithBom.GetPreamble().Concat(Utf8WithBom.GetBytes(Expected)), await File.ReadAllBytesAsync(_sourcePath));
        var published = (await manager.GetPublishedSolutionAsync())!;
        var text = await published.GetDocument(id)!.GetTextAsync();
        Assert.Equal(Expected, text.ToString());
        Assert.Equal(Utf8WithBom.GetPreamble(), text.Encoding!.GetPreamble());
        await manager.ClearWorkspaceAsync();
        await manager.LoadAndPrepareAsync(_csprojPath, shadowCopyInSolutionAnalyzers: false, CancellationToken.None);
        var reloaded = (await manager.GetPublishedSolutionAsync())!;
        var reloadedId = Assert.Single(reloaded.GetDocumentIdsWithFilePath(_sourcePath));
        Assert.Equal(Expected, (await reloaded.GetDocument(reloadedId)!.GetTextAsync()).ToString());
    }

    [Fact]
    public async Task Default_code_page_preserves_ambiguous_non_ASCII_bytes_when_patch_is_rejected()
    {
        // The OS determines which ANSI code page is available; a UTF-8 system has no legacy fallback.
        var fallback = CodePagesEncodingProvider.Instance.GetEncoding(0) ?? BomFreeUtf8;
        var sample = fallback.IsSingleByte ? fallback.GetString([0xC6]) : "Ж";
        var encoding = fallback.IsSingleByte ? fallback : BomFreeUtf8;
        var original = $"class C {{ const string Value = \"{sample} Marker\"; }}";
        await File.WriteAllBytesAsync(_sourcePath, encoding.GetBytes(original));
        var manager = await LoadAsync();
        manager.SilenceDiskWatchersForTests();
        var before = (await manager.GetPublishedSolutionAsync())!;
        var id = Assert.Single(before.GetDocumentIdsWithFilePath(_sourcePath));
        Assert.Equal(original, (await before.GetDocument(id)!.GetTextAsync()).ToString());

        var tool = new UtilityTools(NullLogger<UtilityTools>.Instance, manager);
        var result = await tool.ApplyPatch(_sourcePath, "Marker", "changed");

        Assert.Contains("update_file_content", result, StringComparison.Ordinal);
        Assert.Equal(encoding.GetBytes(original), await File.ReadAllBytesAsync(_sourcePath));
        var published = (await manager.GetPublishedSolutionAsync())!;
        var text = await published.GetDocument(id)!.GetTextAsync();
        Assert.Equal(original, text.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Writing_must_preserve_an_untouched_literal_if_the_output_becomes_valid_utf8(bool fullWrite)
    {
        // UTF-8 systems have no legacy auto-detection fallback; exercise the explicit legacy reader there.
        var fallback = CodePagesEncodingProvider.Instance.GetEncoding(0);
        var autoDetect = fallback is { IsSingleByte: true };
        var legacy = autoDetect ? fallback! : CodePagesEncodingProvider.Instance.GetEncoding(1251)!;
        if (!autoDetect)
        {
            await File.WriteAllTextAsync(Path.Combine(_root, "Directory.Build.props"),
                "<Project><PropertyGroup><CodePage>1251</CodePage></PropertyGroup></Project>");
        }

        var untouched = legacy.GetString([0xD0, 0x96]);
        var oldValue = legacy.GetString([0xDF]);
        var original = $$"""class C { const string Keep = "{{untouched}}"; const string Edit = "{{oldValue}}"; }""";
        var expected = original.Replace($"Edit = \"{oldValue}\"", "Edit = \"X\"", StringComparison.Ordinal);
        await File.WriteAllBytesAsync(_sourcePath, legacy.GetBytes(original));
        var manager = await LoadAsync();
        manager.SilenceDiskWatchersForTests();
        var before = (await manager.GetPublishedSolutionAsync())!;
        var id = Assert.Single(before.GetDocumentIdsWithFilePath(_sourcePath));
        Assert.Equal(original, (await before.GetDocument(id)!.GetTextAsync()).ToString());

        if (fullWrite)
        {
            var tool = new EditingTools(NullLogger<EditingTools>.Instance, manager);
            Assert.Contains("Successfully wrote", await tool.WriteFile(_sourcePath, expected), StringComparison.Ordinal);
            var compiled = (await BuildFixtureAssemblyAsync()).GetType("C")!;
            const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Static;
            Assert.Equal(untouched, compiled.GetField("Keep", Flags)!.GetRawConstantValue());
            Assert.Equal("X", compiled.GetField("Edit", Flags)!.GetRawConstantValue());
        }
        else
        {
            var tool = new UtilityTools(NullLogger<UtilityTools>.Instance, manager);
            var message = await tool.ApplyPatch(_sourcePath, $"Edit = \"{oldValue}\"", "Edit = \"X\"");
            Assert.Contains("update_file_content", message, StringComparison.Ordinal);
            Assert.Equal(legacy.GetBytes(original), await File.ReadAllBytesAsync(_sourcePath));
        }

        var expectedText = fullWrite ? expected : original;
        var published = (await manager.GetPublishedSolutionAsync())!;
        Assert.Equal(expectedText, (await published.GetDocument(id)!.GetTextAsync()).ToString());
        await manager.ClearWorkspaceAsync();
        await manager.LoadAndPrepareAsync(_csprojPath, shadowCopyInSolutionAnalyzers: false, CancellationToken.None);
        var reloaded = (await manager.GetPublishedSolutionAsync())!;
        var reloadedId = Assert.Single(reloaded.GetDocumentIdsWithFilePath(_sourcePath));
        Assert.Equal(expectedText, (await reloaded.GetDocument(reloadedId)!.GetTextAsync()).ToString());
        var diskEncoding = fullWrite ? Utf8WithBom : legacy;
        Assert.Equal(
            diskEncoding.GetPreamble().Concat(diskEncoding.GetBytes(expectedText)),
            await File.ReadAllBytesAsync(_sourcePath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<CodePage>28591</CodePage>")]
    public async Task ApplyPatch_treats_additional_files_as_disk_text(string codePageProperty)
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "Directory.Build.props"), $$"""
            <Project>
              <PropertyGroup>{{codePageProperty}}</PropertyGroup>
              <ItemGroup><AdditionalFiles Include="payload.txt" /></ItemGroup>
            </Project>
            """);
        var path = Path.Combine(_root, "payload.txt");
        await File.WriteAllTextAsync(path, "Café", BomFreeUtf8);
        var manager = await LoadAsync();
        var project = Assert.Single((await manager.GetPublishedSolutionAsync())!.Projects);
        Assert.Contains(project.AdditionalDocuments, document => document.FilePath == path);

        var tool = new UtilityTools(NullLogger<UtilityTools>.Instance, manager);
        var message = await tool.ApplyPatch(path, "Café", "été", replaceAll: false, CancellationToken.None);

        Assert.Contains("Patch applied successfully", message, StringComparison.Ordinal);
        Assert.Equal(BomFreeUtf8.GetBytes("été"), await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task ApplyPatch_rejects_ambiguous_bytes_when_project_metadata_is_unavailable()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_root, "Directory.Build.props"),
            "<Project><PropertyGroup><CodePage>28591</CodePage></PropertyGroup></Project>");
        const string Original = "class C { } // initial";
        await File.WriteAllTextAsync(_sourcePath, Original, Utf8WithBom);
        var manager = await LoadAsync();
        manager.SilenceDiskWatchersForTests();
        var snapshot = await manager.GetProjectListSnapshotAsync(CancellationToken.None);
        var id = Assert.Single(snapshot.Solution!.GetDocumentIdsWithFilePath(_sourcePath));
        Assert.Equal(Original, (await snapshot.Solution.GetDocument(id)!.GetTextAsync()).ToString());
        var metadata = snapshot.Metadata[id.ProjectId];
        Assert.Equal(28591, metadata.CodePage);
        // Simulate a load with no conclusive source of project encoding.
        typeof(SolutionManager).GetField("_loadedProjectMetadata", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(manager, snapshot.Metadata.SetItem(id.ProjectId, metadata with { CodePage = null }));
        var bytes = "class C { } // é"u8.ToArray();
        await File.WriteAllBytesAsync(_sourcePath, bytes);
        var tool = new UtilityTools(NullLogger<UtilityTools>.Instance, manager);

        var message = await tool.ApplyPatch(_sourcePath, "é", "x", replaceAll: false, CancellationToken.None);

        Assert.Contains("Cannot determine the project's source encoding", message, StringComparison.Ordinal);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(_sourcePath));
        var published = (await manager.GetPublishedSolutionAsync())!;
        Assert.Equal(Original, (await published.GetDocument(id)!.GetTextAsync()).ToString());
    }

    [Theory]
    [InlineData(65001, false)]
    [InlineData(28591, true)]
    [InlineData(932, false)]
    public async Task ApplyPatch_rejects_bytes_that_cannot_be_decoded_losslessly(int projectCodePage, bool utf8Bom)
    {
        await File.WriteAllTextAsync(
            Path.Combine(_root, "Directory.Build.props"),
            $"<Project><PropertyGroup><CodePage>{projectCodePage}</CodePage></PropertyGroup></Project>");
        const string Original = "class Class1 { int X => 1; }";
        await File.WriteAllTextAsync(_sourcePath, Original, Utf8WithBom);
        var manager = await LoadAsync();
        manager.SilenceDiskWatchersForTests();
        var before = (await manager.GetPublishedSolutionAsync())!;
        var id = Assert.Single(before.GetDocumentIdsWithFilePath(_sourcePath));
        Assert.Equal(Original, (await before.GetDocument(id)!.GetTextAsync()).ToString());

        // FA 4A is invalid UTF-8. In code page 932 it decodes to a character that re-encodes as 87 54.
        byte[] external = [.. (utf8Bom ? Utf8WithBom.GetPreamble() : Array.Empty<byte>()),
            .. "class Class1 { int X => 2; } // "u8, 0xFA, 0x4A];
        await File.WriteAllBytesAsync(_sourcePath, external);
        var tool = new UtilityTools(NullLogger<UtilityTools>.Instance, manager);
        var message = await tool.ApplyPatch(_sourcePath, "X => 2", "X => 1", replaceAll: false, CancellationToken.None);

        Assert.StartsWith("Error:", message, StringComparison.Ordinal);
        Assert.Equal(external, await File.ReadAllBytesAsync(_sourcePath));
        var published = (await manager.GetPublishedSolutionAsync())!;
        Assert.Equal(Original, (await published.GetDocument(id)!.GetTextAsync()).ToString());
    }

    [Fact]
    public async Task ApplyPatch_uses_UTF8_with_BOM_for_replacements_outside_the_original_code_page()
    {
        const string Original = "class Café { }";
        await File.WriteAllTextAsync(
            Path.Combine(_root, "Directory.Build.props"),
            "<Project><PropertyGroup><CodePage>28591</CodePage></PropertyGroup></Project>");
        var originalBytes = Encoding.Latin1.GetBytes(Original);
        await File.WriteAllBytesAsync(_sourcePath, originalBytes);
        var manager = await LoadAsync();
        manager.SilenceDiskWatchersForTests();
        var before = (await manager.GetPublishedSolutionAsync())!;
        var id = Assert.Single(before.GetDocumentIdsWithFilePath(_sourcePath));
        Assert.Equal(Original, (await before.GetDocument(id)!.GetTextAsync()).ToString());

        var tool = new UtilityTools(NullLogger<UtilityTools>.Instance, manager);
        var converted = await tool.ApplyPatch(_sourcePath, "Café", "Ж", replaceAll: false, CancellationToken.None);
        Assert.Contains("Patch applied successfully", converted, StringComparison.Ordinal);
        await AssertPublishedAsync("class Ж { }");

        var applied = await tool.ApplyPatch(_sourcePath, "Ж", "été", replaceAll: false, CancellationToken.None);
        Assert.Contains("Patch applied successfully", applied, StringComparison.Ordinal);
        await AssertPublishedAsync("class été { }");

        async Task AssertPublishedAsync(string expected)
        {
            Assert.Equal(
                Utf8WithBom.GetPreamble().Concat(Utf8WithBom.GetBytes(expected)),
                await File.ReadAllBytesAsync(_sourcePath));
            var after = (await manager.GetPublishedSolutionAsync())!;
            Assert.Equal(expected, (await after.GetDocument(id)!.GetTextAsync()).ToString());
        }
    }

    [Theory]
    [InlineData(28591, "ï»¿", true, true)]
    [InlineData(65001, "\uFEFF", true, true)]
    [InlineData(1200, "\0", true, false)]
    [InlineData(65001, "\uFEFF", false, false)]
    public async Task ApplyPatch_preserves_the_requested_prefix_or_rejects_ambiguous_encoding(
        int codePage,
        string prefix,
        bool inWorkspace,
        bool accepted)
    {
        const string Original = "// A";
        await File.WriteAllTextAsync(
            Path.Combine(_root, "Directory.Build.props"),
            $"<Project><PropertyGroup><CodePage>{codePage}</CodePage></PropertyGroup></Project>");
        var encoding = codePage == 65001 ? BomFreeUtf8 : Encoding.GetEncoding(codePage);
        byte[] originalBytes = [.. encoding.Preamble, .. encoding.GetBytes(Original)];
        await File.WriteAllBytesAsync(_sourcePath, originalBytes);
        var target = _sourcePath;
        if (!inWorkspace)
        {
            target = Path.Combine(_outsideRoot, "Other.cs");
            await File.WriteAllBytesAsync(target, originalBytes);
        }

        var manager = await LoadAsync();
        manager.SilenceDiskWatchersForTests();
        var before = (await manager.GetPublishedSolutionAsync())!;
        var id = Assert.Single(before.GetDocumentIdsWithFilePath(_sourcePath));
        Assert.Equal(Original, (await before.GetDocument(id)!.GetTextAsync()).ToString());
        var tool = new UtilityTools(NullLogger<UtilityTools>.Instance, manager);

        var message = await tool.ApplyPatch(target, Original, prefix + "// B", replaceAll: false, CancellationToken.None);

        var published = (await manager.GetPublishedSolutionAsync())!;
        var text = (await published.GetDocument(id)!.GetTextAsync()).ToString();
        if (accepted)
        {
            var expected = prefix + "// B";
            Assert.Contains("Patch applied successfully", message, StringComparison.Ordinal);
            Assert.Equal(Utf8WithBom.GetPreamble().Concat(Utf8WithBom.GetBytes(expected)), await File.ReadAllBytesAsync(target));
            Assert.Equal(expected, text);
        }
        else
        {
            Assert.Contains("would change the byte-order mark", message, StringComparison.Ordinal);
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(target));
            Assert.Equal(Original, text);
        }
    }

    [Fact]
    public async Task ApplyPatch_rejects_an_unpaired_surrogate_outside_the_workspace()
    {
        var path = Path.Combine(_outsideRoot, "Other.cs");
        var originalBytes = BomFreeUtf8.GetBytes(OriginalSourceText);
        await File.WriteAllBytesAsync(path, originalBytes);
        var tool = new UtilityTools(NullLogger<UtilityTools>.Instance, SolutionManagerTestFactory.Create());

        var message = await tool.ApplyPatch(path, OriginalFragment, "\uD800", replaceAll: false, CancellationToken.None);

        Assert.Contains("cannot be represented exactly in utf-8", message, StringComparison.Ordinal);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task ApplyPatch_rejects_a_character_that_the_code_page_decodes_as_a_shift_control()
    {
        const string Original = "// marker";
        await File.WriteAllTextAsync(
            Path.Combine(_root, "Directory.Build.props"),
            "<Project><PropertyGroup><CodePage>50220</CodePage></PropertyGroup></Project>");
        var originalBytes = Encoding.ASCII.GetBytes(Original);
        await File.WriteAllBytesAsync(_sourcePath, originalBytes);
        var tool = new UtilityTools(NullLogger<UtilityTools>.Instance, await LoadAsync());

        // ISO-2022-JP can encode U+000E without fallback, but decoding consumes it as a shift control.
        var message = await tool.ApplyPatch(_sourcePath, "marker", "\u000E", replaceAll: false, CancellationToken.None);

        Assert.StartsWith("Error:", message, StringComparison.Ordinal);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(_sourcePath));
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Loaded_ASCII_source_edits_do_not_require_SDK_or_project_evaluation(bool patch, bool unavailableSdk)
    {
        await File.WriteAllTextAsync(_sourcePath, OriginalSourceText, BomFreeUtf8);
        var manager = await LoadAsync();
        manager.SilenceDiskWatchersForTests();
        var original = Assert.Single(manager.GetCurrentSolution()!.GetDocumentIdsWithFilePath(_sourcePath));
        if (unavailableSdk)
        {
            await File.WriteAllTextAsync(
                Path.Combine(_root, "global.json"),
                """{"sdk":{"version":"999.0.100","rollForward":"disable"}}""");
        }
        else
        {
            await File.WriteAllTextAsync(
                Path.Combine(_root, "Directory.Build.props"),
                """<Project><Import Project="Missing.props" /></Project>""");
        }

        var message = patch
            ? await new UtilityTools(NullLogger<UtilityTools>.Instance, manager).ApplyPatch(
                _sourcePath, OriginalFragment, EditedFragment)
            : await new EditingTools(NullLogger<EditingTools>.Instance, manager).WriteFile(_sourcePath, EditedSourceText);

        Assert.Contains(patch ? "Patch applied successfully" : "Successfully wrote", message, StringComparison.Ordinal);
        Assert.Equal(BomFreeUtf8.GetBytes(EditedSourceText), await File.ReadAllBytesAsync(_sourcePath));
        var published = (await manager.GetPublishedSolutionAsync())!.GetDocument(original)!;
        Assert.Equal(EditedSourceText, (await published.GetTextAsync()).ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyPatch_requires_explicit_conversion_when_non_ASCII_stays_outside_the_matches(bool replaceAll)
    {
        const string Source = """class C { const string Keep = "Café"; const string One = "Marker"; }""";
        var source = replaceAll ? Source + "\n// Marker" : Source;
        var originalBytes = BomFreeUtf8.GetBytes(source);
        await File.WriteAllBytesAsync(_sourcePath, originalBytes);
        var manager = await LoadAsync();
        manager.SilenceDiskWatchersForTests();
        var before = (await manager.GetPublishedSolutionAsync())!;
        var document = before.GetDocument(Assert.Single(before.GetDocumentIdsWithFilePath(_sourcePath)))!;
        Assert.Equal(source, (await document.GetTextAsync()).ToString());

        var patcher = new UtilityTools(NullLogger<UtilityTools>.Instance, manager);
        var message = await patcher.ApplyPatch(_sourcePath, "Marker", "changed", replaceAll);

        Assert.StartsWith("Error:", message, StringComparison.Ordinal);
        Assert.Contains("file was left unchanged", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("original encoding", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UTF-8 with BOM", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("approval", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("update_file_content", message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(_sourcePath));
        var after = (await manager.GetPublishedSolutionAsync())!.GetDocument(document.Id)!;
        Assert.Equal(source, (await after.GetTextAsync()).ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyPatch_converts_to_UTF8_BOM_when_matches_cover_all_non_ASCII(bool replaceAll)
    {
        await File.WriteAllTextAsync(
            Path.Combine(_root, "Directory.Build.props"),
            "<Project><PropertyGroup><CodePage>28591</CodePage></PropertyGroup></Project>");
        const string Original = "// Café  Marker\n";
        const string Changed = "// 雪\n";
        var source = replaceAll ? Original + Original : Original;
        var expected = replaceAll ? Changed + Changed : Changed;
        await File.WriteAllBytesAsync(_sourcePath, Encoding.Latin1.GetBytes(source));
        var manager = await LoadAsync();
        manager.SilenceDiskWatchersForTests();
        var document = Assert.Single(manager.GetCurrentSolution()!.GetDocumentIdsWithFilePath(_sourcePath));

        var patcher = new UtilityTools(NullLogger<UtilityTools>.Instance, manager);
        var message = await patcher.ApplyPatch(
            _sourcePath,
            replaceAll ? "Café\nMarker" : "Café  Marker",
            "雪",
            replaceAll);

        Assert.Contains("Patch applied successfully", message, StringComparison.Ordinal);
        Assert.Equal(Utf8WithBom.GetPreamble().Concat(Utf8WithBom.GetBytes(expected)), await File.ReadAllBytesAsync(_sourcePath));
        var published = (await manager.GetPublishedSolutionAsync())!.GetDocument(document)!;
        Assert.Equal(expected, (await published.GetTextAsync()).ToString());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task WriteFile_uses_UTF8_BOM_for_non_ASCII_unless_the_file_already_has_a_BOM(bool exists, bool hasBom)
    {
        const string Changed = """namespace App; public sealed class Class1 { public const string Value = "Café"; }""";
        if (exists)
        {
            await File.WriteAllTextAsync(_sourcePath, OriginalSourceText, hasBom ? Encoding.Unicode : BomFreeUtf8);
        }
        var manager = await LoadAsync();
        var tool = new EditingTools(NullLogger<EditingTools>.Instance, manager);

        var message = await tool.WriteFile(_sourcePath, Changed);

        Assert.Contains("Successfully wrote", message, StringComparison.Ordinal);
        var encoding = hasBom ? Encoding.Unicode : Utf8WithBom;
        Assert.Equal(encoding.GetPreamble().Concat(encoding.GetBytes(Changed)), await File.ReadAllBytesAsync(_sourcePath));
    }

    [Theory]
    [InlineData("project", false)]
    [InlineData("project", true)]
    [InlineData("import", false)]
    [InlineData("import", true)]
    [InlineData("optional-import", false)]
    [InlineData("optional-import", true)]
    [InlineData("computed-import", false)]
    [InlineData("computed-import", true)]
    [InlineData("glob-import", false)]
    [InlineData("glob-import", true)]
    [InlineData("conditional-property", false)]
    [InlineData("conditional-property", true)]
    public async Task Non_ASCII_source_edits_survive_changes_to_the_project_code_page(string changedInput, bool patch)
    {
        const string Source = """namespace App; public class Sample { public const string Value = "Marker"; }""";
        const string Changed = """namespace App; public class Sample { public const string Value = "Café"; }""";
        var propsPath = Path.Combine(_root, "Inputs", "Encoding.settings.xml");
        var import = changedInput switch
        {
            "project" => "",
            "import" => """<Import Project="Inputs/Encoding.settings.xml" />""",
            "computed-import" => """
                <Import Project="$(MSBuildThisFileDirectory)Inputs/Encoding.settings.xml"
                        Condition="Exists('$(MSBuildThisFileDirectory)Inputs/Encoding.settings.xml')" />
                """,
            "glob-import" => """<Import Project="Inputs/*.xml" />""",
            "conditional-property" => """
                <PropertyGroup Condition="Exists('use-latin1')"><CodePage>28591</CodePage></PropertyGroup>
                """,
            _ => """<Import Project="Inputs/Encoding.settings.xml" Condition="Exists('Inputs/Encoding.settings.xml')" />"""
        };
        var project = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><CodePage>65001</CodePage></PropertyGroup>
              {import}
            </Project>
            """;
        Directory.CreateDirectory(Path.GetDirectoryName(propsPath)!);
        if (changedInput == "import")
        {
            await File.WriteAllTextAsync(propsPath, CodePageProperties(65001));
        }

        await File.WriteAllTextAsync(_csprojPath, project);
        await File.WriteAllTextAsync(_sourcePath, Source, BomFreeUtf8);
        var manager = await LoadAsync();
        manager.SilenceDiskWatchersForTests();
        var originalSnapshot = (await manager.GetPublishedSolutionAsync())!;
        var originalDocument = Assert.Single(originalSnapshot.Projects.Single().Documents, document => document.FilePath == _sourcePath);

        var changedPath = changedInput switch
        {
            "project" => _csprojPath,
            "conditional-property" => Path.Combine(_root, "use-latin1"),
            _ => propsPath,
        };
        var content = changedInput switch
        {
            "project" => project.Replace("65001", "28591", StringComparison.Ordinal),
            "conditional-property" => "",
            _ => CodePageProperties(28591),
        };
        await File.WriteAllTextAsync(changedPath, content);

        var message = patch
            ? await new UtilityTools(NullLogger<UtilityTools>.Instance, manager).ApplyPatch(_sourcePath, "Marker", "Café")
            : await new EditingTools(NullLogger<EditingTools>.Instance, manager).WriteFile(_sourcePath, Changed);

        Assert.Contains(patch ? "Patch applied successfully" : "Successfully wrote", message, StringComparison.Ordinal);
        Assert.Equal(Utf8WithBom.GetPreamble().Concat(Utf8WithBom.GetBytes(Changed)), await File.ReadAllBytesAsync(_sourcePath));
        var snapshot = (await manager.GetPublishedSolutionAsync())!;
        Assert.Equal(Changed, (await snapshot.GetDocument(originalDocument.Id)!.GetTextAsync()).ToString());
        if (changedInput == "project")
        {
            await AssertSampleBuildAsync("Café");
        }

        static string CodePageProperties(int page) =>
            $"<Project><PropertyGroup><CodePage>{page}</CodePage></PropertyGroup></Project>";
    }

    [Theory]
    [InlineData("property", false)]
    [InlineData("property", true)]
    [InlineData("target", false)]
    [InlineData("target", true)]
    [InlineData("buildArgs", false)]
    [InlineData("buildArgs", true)]
    [InlineData("sameEncoding", false)]
    [InlineData("sameEncoding", true)]
    public async Task Non_ASCII_source_edits_preserve_the_value_in_an_ordinary_build(string codePageSource, bool patch)
    {
        const string Source = """namespace App; public class Sample { public const string Value = "Marker"; }""";
        const string Changed = """namespace App; public class Sample { public const string Value = "Café"; }""";
        var ordinaryBuild = codePageSource switch
        {
            "target" => """
                <Target Name="SetCompilerCodePage" BeforeTargets="CoreCompile" Condition="'$(DesignTimeBuild)' != 'true'">
                  <PropertyGroup><CodePage>28591</CodePage></PropertyGroup>
                </Target>
                <Target Name="RestoreCodePageAfterCompile" AfterTargets="CoreCompile" Condition="'$(DesignTimeBuild)' != 'true'">
                  <PropertyGroup><CodePage>65001</CodePage></PropertyGroup>
                </Target>
                """,
            "property" => """
                <PropertyGroup Condition="'$(DesignTimeBuild)' == ''"><CodePage>28591</CodePage></PropertyGroup>
                """,
            _ => string.Empty,
        };
        var witnessPath = Path.Combine(_root, "OrdinaryBuildWitness.txt");
        await File.WriteAllTextAsync(
            _csprojPath,
            $"""
             <Project Sdk="Microsoft.NET.Sdk">
               <PropertyGroup><TargetFramework>net10.0</TargetFramework><CodePage>65001</CodePage></PropertyGroup>
               <Target Name="OrdinaryBuildWitness" BeforeTargets="CoreCompile" Condition="'$(DesignTimeBuild)' != 'true'">
                 <WriteLinesToFile File="$(MSBuildProjectDirectory)/OrdinaryBuildWitness.txt" Lines="The build ran." Overwrite="true" />
               </Target>
               {ordinaryBuild}
             </Project>
             """);
        await File.WriteAllTextAsync(_sourcePath, Source, BomFreeUtf8);
        var manager = await LoadAsync();
        if (codePageSource == "buildArgs")
        {
            manager.ApplySessionBuildArgs("-p:CodePage=28591");
        }

        var original = Assert.Single(manager.GetCurrentSolution()!.Projects.Single().Documents,
            document => document.FilePath == _sourcePath);

        var result = patch
            ? await new UtilityTools(NullLogger<UtilityTools>.Instance, manager).ApplyPatch(_sourcePath, "Marker", "Café")
            : await new EditingTools(NullLogger<EditingTools>.Instance, manager).WriteFile(_sourcePath, Changed);

        Assert.Contains(patch ? "Patch applied successfully" : "Successfully wrote", result, StringComparison.Ordinal);
        Assert.False(File.Exists(witnessPath));
        Assert.Equal(Utf8WithBom.GetPreamble().Concat(Utf8WithBom.GetBytes(Changed)), await File.ReadAllBytesAsync(_sourcePath));
        var published = (await manager.GetPublishedSolutionAsync())!.GetDocument(original.Id)!;
        Assert.Equal(Changed, (await published.GetTextAsync()).ToString());
        await AssertSampleBuildAsync("Café", codePageSource == "buildArgs" ? " -p:CodePage=28591" : "");
        Assert.True(File.Exists(witnessPath));
    }

    [Theory]
    [InlineData("compileItem", false)]
    [InlineData("compileItem", true)]
    [InlineData("targetItem", false)]
    [InlineData("targetItem", true)]
    [InlineData("taskOutput", false)]
    [InlineData("taskOutput", true)]
    [InlineData("referenceProperties", false)]
    [InlineData("referenceProperties", true)]
    public async Task Non_ASCII_source_edits_use_a_BOM_with_ordinary_only_readers(string readerContext, bool patch)
    {
        const string Source = """namespace App; public class Sample { public const string Value = "Marker"; }""";
        const string Changed = """namespace App; public class Sample { public const string Value = "Café"; }""";
        var ordinaryReader = readerContext switch
        {
            "compileItem" => """
                <ItemGroup Condition="'$(DesignTimeBuild)' != 'true'">
                  <Compile Include="../Class1.cs" />
                </ItemGroup>
                """,
            "targetItem" => """
                <Target Name="LinkSource" BeforeTargets="CoreCompile" Condition="'$(DesignTimeBuild)' != 'true'">
                  <ItemGroup><Compile Include="../Class1.cs" /></ItemGroup>
                </Target>
                """,
            "taskOutput" => """
                <Target Name="LinkSource" BeforeTargets="CoreCompile" Condition="'$(DesignTimeBuild)' != 'true'">
                  <CreateItem Include="../Class1.cs">
                    <Output TaskParameter="Include" ItemName="Compile" />
                  </CreateItem>
                </Target>
                """,
            _ => """
                <ItemGroup Condition="'$(DesignTimeBuild)' != 'true'">
                  <ProjectReference Include="../App.csproj" AdditionalProperties="CodePage=28591" />
                </ItemGroup>
                """,
        };
        var otherDirectory = Directory.CreateDirectory(Path.Combine(_root, "Other"));
        var solutionPath = Path.Combine(_root, "Both.slnx");
        await File.WriteAllTextAsync(solutionPath, """<Solution><Project Path="App.csproj" /><Project Path="Other/Other.csproj" /></Solution>""");
        await File.WriteAllTextAsync(_csprojPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><CodePage>65001</CodePage></PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(otherDirectory.FullName, "Other.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><CodePage>28591</CodePage></PropertyGroup>
              {ordinaryReader}
            </Project>
            """);
        await File.WriteAllTextAsync(_sourcePath, Source, BomFreeUtf8);
        var manager = SolutionManagerTestFactory.Create();
        await manager.LoadAndPrepareAsync(solutionPath, shadowCopyInSolutionAnalyzers: false, CancellationToken.None);
        var loaded = manager.GetCurrentSolution()!;
        Assert.Equal(2, loaded.ProjectIds.Count);
        var original = Assert.Single(loaded.GetDocumentIdsWithFilePath(_sourcePath));

        var result = patch
            ? await new UtilityTools(NullLogger<UtilityTools>.Instance, manager).ApplyPatch(_sourcePath, "Marker", "Café")
            : await new EditingTools(NullLogger<EditingTools>.Instance, manager).WriteFile(_sourcePath, Changed);

        Assert.Contains(patch ? "Patch applied successfully" : "Successfully wrote", result, StringComparison.Ordinal);
        Assert.Equal(Utf8WithBom.GetPreamble().Concat(Utf8WithBom.GetBytes(Changed)), await File.ReadAllBytesAsync(_sourcePath));
        var published = (await manager.GetPublishedSolutionAsync())!.GetDocument(original)!;
        Assert.Equal(Changed, (await published.GetTextAsync()).ToString());
    }

    [Theory]
    [InlineData(65001, false)]
    [InlineData(28591, false)]
    [InlineData(28591, true)]
    public async Task Non_ASCII_source_edits_use_a_BOM_with_frameworks_omitted_from_the_workspace(int otherCodePage, bool patch)
    {
        const string Source = """namespace App; public class Sample { public const string Value = "Marker"; }""";
        const string Changed = """namespace App; public class Sample { public const string Value = "Café"; }""";
        await File.WriteAllTextAsync(_csprojPath, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFrameworks>net10.0;net11.0</TargetFrameworks><CodePage>65001</CodePage></PropertyGroup>
              <PropertyGroup Condition="'$(TargetFramework)' == 'net11.0'"><CodePage>{otherCodePage}</CodePage></PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(_sourcePath, Source, BomFreeUtf8);
        var manager = SolutionManagerTestFactory.Create();
        await manager.LoadAndPrepareAsync(_csprojPath, shadowCopyInSolutionAnalyzers: false, CancellationToken.None,
            targetFramework: "net10.0");
        var original = Assert.Single(manager.GetCurrentSolution()!.GetDocumentIdsWithFilePath(_sourcePath));

        var result = patch
            ? await new UtilityTools(NullLogger<UtilityTools>.Instance, manager).ApplyPatch(_sourcePath, "Marker", "Café")
            : await new EditingTools(NullLogger<EditingTools>.Instance, manager).WriteFile(_sourcePath, Changed);

        Assert.Contains(patch ? "Patch applied successfully" : "Successfully wrote", result, StringComparison.Ordinal);
        Assert.Equal(Utf8WithBom.GetPreamble().Concat(Utf8WithBom.GetBytes(Changed)), await File.ReadAllBytesAsync(_sourcePath));
        var published = (await manager.GetPublishedSolutionAsync())!.GetDocument(original)!;
        Assert.Equal(Changed, (await published.GetTextAsync()).ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Non_ASCII_source_edits_use_a_BOM_with_target_time_framework_dispatch(bool item)
    {
        const string Source = """namespace App; public class Sample { public const string Value = "Marker"; }""";
        const string Changed = """namespace App; public class Sample { public const string Value = "Café"; }""";
        var dispatchChange = item
            ? """<ItemGroup><_TargetFramework Include="net11.0" /></ItemGroup>"""
            : """<PropertyGroup><TargetFrameworks>net10.0;net11.0</TargetFrameworks></PropertyGroup>""";
        await File.WriteAllTextAsync(_csprojPath, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFrameworks>net10.0</TargetFrameworks><CodePage>65001</CodePage></PropertyGroup>
              <PropertyGroup Condition="'$(TargetFramework)' == 'net11.0'"><CodePage>28591</CodePage></PropertyGroup>
              <Target Name="AddOrdinaryFramework" BeforeTargets="_ComputeTargetFrameworkItems"
                      Condition="'$(DesignTimeBuild)' != 'true'">
                {dispatchChange}
              </Target>
            </Project>
            """);
        await File.WriteAllTextAsync(_sourcePath, Source, BomFreeUtf8);
        var manager = await LoadAsync();
        var original = Assert.Single(manager.GetCurrentSolution()!.GetDocumentIdsWithFilePath(_sourcePath));

        var result = await new UtilityTools(NullLogger<UtilityTools>.Instance, manager)
            .ApplyPatch(_sourcePath, "Marker", "Café");

        Assert.Contains("Patch applied successfully", result, StringComparison.Ordinal);
        Assert.Equal(Utf8WithBom.GetPreamble().Concat(Utf8WithBom.GetBytes(Changed)), await File.ReadAllBytesAsync(_sourcePath));
        var published = (await manager.GetPublishedSolutionAsync())!.GetDocument(original)!;
        Assert.Equal(Changed, (await published.GetTextAsync()).ToString());
    }

    [Fact]
    public async Task WriteFile_preserves_a_BOM_marked_source_when_project_encoding_changes()
    {
        await File.WriteAllTextAsync(_sourcePath, OriginalSourceText, Utf8WithBom);
        var manager = await LoadAsync();
        manager.SilenceDiskWatchersForTests();
        await File.WriteAllTextAsync(
            _csprojPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><CodePage>28591</CodePage></PropertyGroup>
            </Project>
            """);
        var tool = new EditingTools(NullLogger<EditingTools>.Instance, manager);
        const string Changed = """namespace App; public class Sample { public const string Value = "Café"; }""";

        var message = await tool.WriteFile(_sourcePath, Changed);

        Assert.Contains("Successfully wrote", message, StringComparison.Ordinal);
        Assert.Equal(Utf8WithBom.GetPreamble().Concat(Utf8WithBom.GetBytes(Changed)), await File.ReadAllBytesAsync(_sourcePath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WriteFile_uses_a_BOM_when_additional_frameworks_can_compile_the_source(bool addedFrameworkOwnsSource)
    {
        var itemGroup = addedFrameworkOwnsSource
            ? ""
            : """<ItemGroup Condition="'$(TargetFramework)' == 'net11.0'"><Compile Remove="Class1.cs" /></ItemGroup>""";
        await File.WriteAllTextAsync(
            _csprojPath,
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFrameworks>net10.0</TargetFrameworks><CodePage>65001</CodePage></PropertyGroup>
              <PropertyGroup Condition="Exists('additional-target')"><TargetFrameworks>net10.0;net11.0</TargetFrameworks></PropertyGroup>
              <PropertyGroup Condition="'$(TargetFramework)' == 'net11.0'"><CodePage>28591</CodePage></PropertyGroup>
              {itemGroup}
            </Project>
            """);
        await File.WriteAllTextAsync(_sourcePath, OriginalSourceText, BomFreeUtf8);
        var manager = await LoadAsync();
        manager.SilenceDiskWatchersForTests();
        await File.WriteAllTextAsync(Path.Combine(_root, "additional-target"), "");
        var tool = new EditingTools(NullLogger<EditingTools>.Instance, manager);
        const string Changed = """namespace App; public class Sample { public const string Value = "Café"; }""";

        var message = await tool.WriteFile(_sourcePath, Changed);

        Assert.Contains("Successfully wrote", message, StringComparison.Ordinal);
        Assert.Equal(Utf8WithBom.GetPreamble().Concat(Utf8WithBom.GetBytes(Changed)), await File.ReadAllBytesAsync(_sourcePath));
    }

    [Fact]
    public async Task ApplyPatch_rejects_a_shebang_if_the_loaded_reader_cannot_read_ASCII()
    {
        const string Source = "// Hello";
        await File.WriteAllTextAsync(_sourcePath, Source, Utf8WithBom);
        await File.WriteAllTextAsync(
            _csprojPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><CodePage>1200</CodePage></PropertyGroup>
            </Project>
            """);
        var manager = await LoadAsync();
        manager.SilenceDiskWatchersForTests();
        var document = Assert.Single(manager.GetCurrentSolution()!.GetDocumentIdsWithFilePath(_sourcePath));

        var tool = new UtilityTools(NullLogger<UtilityTools>.Instance, manager);
        var message = await tool.ApplyPatch(_sourcePath, Source, "#!/usr/bin/env dotnet\n" + Source);

        Assert.Contains("source encoding cannot preserve", message, StringComparison.Ordinal);
        Assert.Equal(Utf8WithBom.GetPreamble().Concat(Utf8WithBom.GetBytes(Source)), await File.ReadAllBytesAsync(_sourcePath));
        var published = (await manager.GetPublishedSolutionAsync())!.GetDocument(document)!;
        Assert.Equal(Source, (await published.GetTextAsync()).ToString());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task WriteFile_restores_the_cached_text_only_after_persistence(bool rejectWrite, bool externalUtf16)
    {
        const string Requested = "class Class1\n{\n    int X => 1;\n}\n";
        const string External = "class Class1\n{\n    int X => 2;\n}\n";
        await File.WriteAllTextAsync(_sourcePath, Requested, BomFreeUtf8);
        var manager = await LoadAsync();
        manager.SilenceDiskWatchersForTests();
        var before = (await manager.GetPublishedSolutionAsync())!;
        var id = Assert.Single(before.GetDocumentIdsWithFilePath(_sourcePath));
        Assert.Equal(Requested, (await before.GetDocument(id)!.GetTextAsync()).ToString());
        var diskEncoding = externalUtf16 ? Encoding.Unicode : BomFreeUtf8;
        await File.WriteAllTextAsync(_sourcePath, External, diskEncoding);
        using var readLock = rejectWrite && OperatingSystem.IsWindows() ? File.OpenRead(_sourcePath) : null;
        if (rejectWrite && !OperatingSystem.IsWindows())
        {
            manager.FailNextDocumentWritePath = _sourcePath;
        }

        var tools = new EditingTools(NullLogger<EditingTools>.Instance, manager);
        var message = await tools.WriteFile(_sourcePath, Requested, CancellationToken.None);

        if (rejectWrite)
        {
            Assert.Contains("Workspace write PartialPersistence", message, StringComparison.Ordinal);
            Assert.Equal(BomFreeUtf8.GetBytes(External), await File.ReadAllBytesAsync(_sourcePath));
        }
        else
        {
            Assert.Contains("Successfully wrote", message, StringComparison.Ordinal);
            var expected = diskEncoding.GetPreamble().Concat(diskEncoding.GetBytes(Requested)).ToArray();
            Assert.Equal(expected, await File.ReadAllBytesAsync(_sourcePath));
        }

        var published = (await manager.GetPublishedSolutionAsync())!;
        Assert.Equal(Requested, (await published.GetDocument(id)!.GetTextAsync()).ToString());
    }

    [Theory]
    [InlineData(OriginalSourceText)]
    [InlineData(EditedSourceText)]
    public async Task WriteFile_publishes_existing_disk_bytes_without_writing(string requested)
    {
        await File.WriteAllTextAsync(_sourcePath, OriginalSourceText, BomFreeUtf8);
        var manager = await LoadAsync();
        manager.SilenceDiskWatchersForTests();
        var before = (await manager.GetPublishedSolutionAsync())!;
        var id = Assert.Single(before.GetDocumentIdsWithFilePath(_sourcePath));
        var cached = await before.GetDocument(id)!.GetTextAsync();
        Assert.Equal(OriginalSourceText, cached.ToString());
        Assert.Equal(BomFreeUtf8.CodePage, cached.Encoding!.CodePage);

        await File.WriteAllTextAsync(_sourcePath, requested, Encoding.Unicode);
        var diskBytes = await File.ReadAllBytesAsync(_sourcePath);
        manager.FailNextDocumentWritePath = _sourcePath;
        using var readLock = OperatingSystem.IsWindows() ? File.OpenRead(_sourcePath) : null;
        var tools = new EditingTools(NullLogger<EditingTools>.Instance, manager);
        var message = await tools.WriteFile(_sourcePath, requested, CancellationToken.None);

        Assert.Contains("Successfully wrote", message, StringComparison.Ordinal);
        Assert.Equal(_sourcePath, manager.FailNextDocumentWritePath);
        Assert.Equal(diskBytes, await File.ReadAllBytesAsync(_sourcePath));
        var published = (await manager.GetPublishedSolutionAsync())!;
        var confirmed = await published.GetDocument(id)!.GetTextAsync();
        Assert.Equal(requested, confirmed.ToString());
        Assert.Equal(Encoding.Unicode.CodePage, confirmed.Encoding!.CodePage);
        Assert.Equal(Encoding.Unicode.GetPreamble(), confirmed.Encoding.GetPreamble());
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

    [Theory]
    [InlineData("", "Café", false, false)]
    [InlineData("<CodePage>28591</CodePage>", "Café", false, false)]
    [InlineData("<CodePage>28591</CodePage>", "Café", false, true)]
    [InlineData("<CodePage>28591</CodePage>", "雪", true, false)]
    public async Task WriteFile_new_source_preserves_its_literal_on_project_load(
        string codePageProperty,
        string literal,
        bool linked,
        bool precreateEmpty)
    {
        var externalGlob = $"../{Path.GetFileName(_outsideRoot)}/*.cs";
        var linkedFiles = linked ? $"""<ItemGroup><Compile Include="{externalGlob}" /></ItemGroup>""" : string.Empty;
        await File.WriteAllTextAsync(_csprojPath, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework>{{codePageProperty}}</PropertyGroup>
              {{linkedFiles}}
            </Project>
            """);
        var path = Path.Combine(linked ? _outsideRoot : _root, "BrandNew.cs");
        var source = $$"""public class BrandNew { public const string Value = "{{literal}}"; }""";
        var manager = await LoadAsync();
        if (precreateEmpty)
        {
            await File.WriteAllBytesAsync(path, []);
        }
        var tool = new EditingTools(NullLogger<EditingTools>.Instance, manager);

        var message = await tool.WriteFile(path, source, CancellationToken.None);

        Assert.Contains("Successfully wrote", message, StringComparison.Ordinal);
        Assert.Equal(Utf8WithBom.GetPreamble().Concat(Utf8WithBom.GetBytes(source)), await File.ReadAllBytesAsync(path));
        await manager.ClearWorkspaceAsync();
        await manager.LoadAndPrepareAsync(_csprojPath, shadowCopyInSolutionAnalyzers: false, CancellationToken.None);
        var project = Assert.Single((await manager.GetPublishedSolutionAsync())!.Projects);
        var document = Assert.Single(project.Documents, document => document.FilePath == path);
        Assert.Equal(source, (await document.GetTextAsync()).ToString());
        var compilation = await project.GetCompilationAsync();
        var field = Assert.Single(compilation!.GetTypeByMetadataName("BrandNew")!.GetMembers("Value"));
        Assert.Equal(literal, Assert.IsAssignableFrom<Microsoft.CodeAnalysis.IFieldSymbol>(field).ConstantValue);
        var build = await DotNetCliRunner.RunAsync("build App.csproj -c Release -p:NuGetAudit=false", _root, CancellationToken.None);
        Assert.True(build.ExitCode == 0, build.CombinedOutput);
        var assembly = Assembly.Load(await File.ReadAllBytesAsync(Path.Combine(_root, "bin", "Release", "net10.0", "App.dll")));
        Assert.Equal(literal, assembly.GetType("BrandNew")!.GetField("Value")!.GetRawConstantValue());
    }

    [Fact]
    public async Task WriteFile_creates_a_directly_executable_CSharp_script()
    {
        const string Script = "#!/usr/bin/env dotnet\nSystem.Console.WriteLine(\"Script launched\");\n";
        var path = Path.Combine(_outsideRoot, "hello.cs");
        var tool = new EditingTools(NullLogger<EditingTools>.Instance, await LoadAsync());
        var result = await tool.WriteFile(path, Script);
        Assert.Contains("Successfully wrote", result, StringComparison.Ordinal);
        Assert.Equal(BomFreeUtf8.GetBytes(Script), await File.ReadAllBytesAsync(path));

        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            using var process = Process.Start(new ProcessStartInfo(path)
            {
                WorkingDirectory = _outsideRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
            Assert.True(process.ExitCode == 0, await error);
            Assert.Contains("Script launched", await output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unloaded_project_source_writes_preserve_the_requested_literals(bool unrelatedWorkspaceLoaded)
    {
        var manager = unrelatedWorkspaceLoaded ? await LoadAsync() : SolutionManagerTestFactory.Create();
        var editor = new EditingTools(NullLogger<EditingTools>.Instance, manager);
        var patcher = new UtilityTools(NullLogger<UtilityTools>.Instance, manager);
        await File.WriteAllTextAsync(Path.Combine(_outsideRoot, "UnknownOwner.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><CodePage>28591</CodePage></PropertyGroup>
            </Project>
            """);

        (string Name, string Original)[] sources =
        [
            ("Empty", ""),
            ("Existing", "class Placeholder {}"),
            ("Patched", "class Patched { public const string Value = \"Marker\"; }"),
        ];
        foreach (var (name, original) in sources)
        {
            var path = Path.Combine(_outsideRoot, name + ".cs");
            await File.WriteAllTextAsync(path, original, BomFreeUtf8);
            var literal = name == "Patched" ? "Cafe" : "Café";
            var expected = $"class {name} {{ public const string Value = \"{literal}\"; }}";
            if (name == "Patched")
            {
                Assert.Contains("Load its project before patching", await patcher.ApplyPatch(path, "Marker", "Café"), StringComparison.Ordinal);
                Assert.Equal(BomFreeUtf8.GetBytes(original), await File.ReadAllBytesAsync(path));
                Assert.Contains("Patch applied successfully", await patcher.ApplyPatch(path, "Marker", literal), StringComparison.Ordinal);
                Assert.Equal(BomFreeUtf8.GetBytes(expected), await File.ReadAllBytesAsync(path));
            }
            else
            {
                Assert.Contains("Successfully wrote", await editor.WriteFile(path, expected), StringComparison.Ordinal);
                Assert.Equal(Utf8WithBom.GetPreamble().Concat(Utf8WithBom.GetBytes(expected)), await File.ReadAllBytesAsync(path));
            }
        }

        var ambiguousPath = Path.Combine(_outsideRoot, "Ambiguous.cs");
        var originalBytes = "class Ambiguous { const string Keep = \"Café\"; const string Edit = \"Marker\"; }"u8.ToArray();
        await File.WriteAllBytesAsync(ambiguousPath, originalBytes);
        var refusal = await patcher.ApplyPatch(ambiguousPath, "Marker", "Café");
        Assert.Contains("Load its project before patching", refusal, StringComparison.Ordinal);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(ambiguousPath));

        var build = await DotNetCliRunner.RunAsync("build UnknownOwner.csproj -c Release -p:NuGetAudit=false", _outsideRoot, CancellationToken.None);
        Assert.True(build.ExitCode == 0, build.CombinedOutput);
        var dll = Path.Combine(_outsideRoot, "bin", "Release", "net10.0", "UnknownOwner.dll");
        var assembly = Assembly.Load(await File.ReadAllBytesAsync(dll));
        foreach (var name in new[] { "Empty", "Existing", "Patched" })
        {
            Assert.Equal(name == "Patched" ? "Cafe" : "Café", assembly.GetType(name)!.GetField("Value")!.GetRawConstantValue());
        }
    }

    [Theory]
    [InlineData(52936, "~{VP~}", "中", false)]
    [InlineData(20105, "$", "¤", true)]
    public async Task Unregistered_ASCII_patch_preserves_the_compiled_value_in_seven_bit_code_pages(
        int codePage,
        string rawLiteral,
        string compiledLiteral,
        bool patchAllowed)
    {
        await File.WriteAllTextAsync(_csprojPath, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><CodePage>{{codePage}}</CodePage></PropertyGroup>
            </Project>
            """);
        var source = $"public class Patch {{ public const string Keep = \"{rawLiteral}\"; public const string Edit = \"Marker\"; }}";
        await File.WriteAllBytesAsync(_sourcePath, Encoding.ASCII.GetBytes(source));
        var patcher = new UtilityTools(NullLogger<UtilityTools>.Instance, SolutionManagerTestFactory.Create());
        var result = await patcher.ApplyPatch(_sourcePath, "Marker", "X");

        Assert.Contains(patchAllowed ? "Patch applied successfully" : "Load its project before patching", result, StringComparison.Ordinal);
        var expected = patchAllowed ? source.Replace("Marker", "X", StringComparison.Ordinal) : source;
        Assert.Equal(Encoding.ASCII.GetBytes(expected), await File.ReadAllBytesAsync(_sourcePath));
        var build = await DotNetCliRunner.RunAsync("build App.csproj -c Release -p:NuGetAudit=false", _root, CancellationToken.None);
        Assert.True(build.ExitCode == 0, build.CombinedOutput);
        var assembly = Assembly.Load(await File.ReadAllBytesAsync(Path.Combine(_root, "bin", "Release", "net10.0", "App.dll")));
        var type = assembly.GetType("Patch")!;
        Assert.Equal(compiledLiteral, type.GetField("Keep")!.GetRawConstantValue());
        Assert.Equal(patchAllowed ? "X" : "Marker", type.GetField("Edit")!.GetRawConstantValue());
    }

    [Theory]
    [InlineData("$Marker", "$X", false)]
    [InlineData("Marker$", "X$", false)]
    [InlineData("$\r\nMarker", "$\nX", true)]
    [InlineData("Marker", "$Marker", true)]
    [InlineData("$Marker", "$Marker", false)]
    public async Task Unregistered_patch_requires_the_entire_requested_fragment_to_be_portable(string oldText, string newText, bool replaceAll)
    {
        const string Source = "// $Marker, Marker$, $   Marker\r\n// $  Marker\r\n";
        var bytes = Encoding.ASCII.GetBytes(Source);
        await File.WriteAllBytesAsync(_sourcePath, bytes);
        var tool = new UtilityTools(NullLogger<UtilityTools>.Instance, SolutionManagerTestFactory.Create());
        var result = await tool.ApplyPatch(_sourcePath, oldText, newText, replaceAll);

        Assert.Contains("Load its project before patching", result, StringComparison.Ordinal);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(_sourcePath));
    }

    [Fact]
    public async Task Unregistered_replace_all_changes_only_the_matched_tokens_around_national_variant_characters()
    {
        const string Source = "// Start  Marker\r\n// $Keep\r\n// Start\tMarker\r\n";
        await File.WriteAllBytesAsync(_sourcePath, Encoding.ASCII.GetBytes(Source));
        var tool = new UtilityTools(NullLogger<UtilityTools>.Instance, SolutionManagerTestFactory.Create());
        var result = await tool.ApplyPatch(_sourcePath, "Start\nMarker", "Result", replaceAll: true);

        Assert.Contains("Patch applied successfully", result, StringComparison.Ordinal);
        const string Expected = "// Result\r\n// $Keep\r\n// Result\r\n";
        var bytes = await File.ReadAllBytesAsync(_sourcePath);
        Assert.Equal(Encoding.ASCII.GetBytes(Expected), bytes);
        Assert.Equal(Expected.Replace('$', '¤'), CodePagesEncodingProvider.Instance.GetEncoding(20105)!.GetString(bytes));
    }

    [Fact]
    public async Task WriteFile_refuses_an_ambiguous_BOM_free_script_before_creating_it()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "Directory.Build.props"),
            "<Project><PropertyGroup><CodePage>28591</CodePage></PropertyGroup></Project>");
        var path = Path.Combine(_root, "hello.cs");
        var tool = new EditingTools(NullLogger<EditingTools>.Instance, await LoadAsync());
        var result = await tool.WriteFile(path, "#!/usr/bin/env dotnet\nSystem.Console.WriteLine(\"Café\");");

        Assert.Contains("cannot preserve the text", result, StringComparison.Ordinal);
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("$")]
    [InlineData("~{VP~}")]
    public async Task WriteFile_refuses_nonportable_ASCII_shebang_without_a_known_reader(string literal)
    {
        var path = Path.Combine(_outsideRoot, "hello.cs");
        var tool = new EditingTools(NullLogger<EditingTools>.Instance, SolutionManagerTestFactory.Create());
        var result = await tool.WriteFile(path, $"#!/usr/bin/env dotnet\nSystem.Console.WriteLine(\"{literal}\");\n");

        Assert.Contains("ambiguous without a loaded owning project", result, StringComparison.Ordinal);
        Assert.False(File.Exists(path));
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

    private static Task WriteClassicProjectAsync(string path, string codePageProperty) =>
        File.WriteAllTextAsync(path, $$"""
            <Project ToolsVersion="Current" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
                <OutputType>Library</OutputType>
                <OutputPath>bin/$(MSBuildProjectName)/</OutputPath>
                {{codePageProperty}}
              </PropertyGroup>
              <ItemGroup><Compile Include="Class1.cs" /></ItemGroup>
              <Import Project="$(MSBuildToolsPath)/Microsoft.CSharp.targets" />
            </Project>
            """);

    private async Task<SolutionManager> LoadAsync()
    {
        var manager = SolutionManagerTestFactory.Create();
        await manager.LoadAndPrepareAsync(
            _csprojPath,
            shadowCopyInSolutionAnalyzers: false,
            CancellationToken.None);
        return manager;
    }

    private async Task AssertSampleBuildAsync(string expected, string additionalArguments = "")
    {
        var assembly = await BuildFixtureAssemblyAsync(additionalArguments);
        Assert.Equal(expected, assembly.GetType("App.Sample")!.GetField("Value")!.GetRawConstantValue());
    }

    private async Task<Assembly> BuildFixtureAssemblyAsync(string additionalArguments = "")
    {
        var build = await DotNetCliRunner.RunAsync(
            "build App.csproj -c Release -p:NuGetAudit=false" + additionalArguments,
            _root,
            CancellationToken.None);
        Assert.True(build.ExitCode == 0, build.CombinedOutput);
        var assemblyPath = Path.Combine(_root, "bin", "Release", "net10.0", "App.dll");
        return Assembly.Load(await File.ReadAllBytesAsync(assemblyPath));
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
