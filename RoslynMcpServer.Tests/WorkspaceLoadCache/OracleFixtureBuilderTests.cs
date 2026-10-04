using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using RoslynMcpServer.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

/// <summary>
/// Validates the oracle fixtures themselves: owned-root confinement, exact original bytes,
/// restore after mutation, honest negative admission expectations, and the independently
/// observed Razor generator marker. These checks do not run an admission or oracle comparison;
/// that comparison is task-04.
/// </summary>
public sealed class OracleFixtureBuilderTests
{
    public OracleFixtureBuilderTests(ITestOutputHelper output)
    {
        MsBuildBootstrapper.Register();
        _output = output;
    }

    // FixtureScenario is internal, so public theory methods take the scenario name and parse it.
    [Theory]
    [InlineData("ProjectGraph")]
    [InlineData("Encodings")]
    [InlineData("MultiTarget")]
    [InlineData("RazorGenerator")]
    [InlineData("GeneratedObj")]
    [InlineData("LinkedImport")]
    [InlineData("AbsentImport")]
    [InlineData("CustomTarget")]
    [InlineData("ExplicitCompile")]
    [InlineData("WebAssets")]
    [InlineData("ExternalAsset")]
    [InlineData("MissingReference")]
    [InlineData("EmptyProject")]
    public async Task CreateAsync_materializes_expected_bytes_and_passes_verification(string scenarioName)
    {
        var scenario = Parse(scenarioName);
        await using var fixture = await OracleFixtureBuilder.CreateAsync(scenario, OwnParent(), CancellationToken.None);
        Assert.True(Directory.Exists(fixture.Root));
        Assert.Equal(Path.GetFullPath(fixture.Root), fixture.Root);
        Assert.NotEmpty(fixture.Expected.ExpectedInstanceKeys);
        Assert.Equal(fixture.Expected.ExpectedSourceCharacters.Keys, fixture.Expected.ExpectedSourceBytes.Keys);
        foreach (var (key, expectedBytes) in fixture.Expected.ExpectedSourceBytes)
        {
            if (IsGeneratedOutputKey(key))
            {
                continue;
            }
            var actual = await File.ReadAllBytesAsync(fixture.FilePath(RelativePathOf(key)));
            Assert.Equal(expectedBytes.ToArray(), actual);
        }
        await fixture.VerifyRestoredAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData("ProjectGraph")]
    [InlineData("Encodings")]
    [InlineData("MultiTarget")]
    [InlineData("RazorGenerator")]
    [InlineData("GeneratedObj")]
    [InlineData("LinkedImport")]
    [InlineData("AbsentImport")]
    [InlineData("CustomTarget")]
    [InlineData("ExplicitCompile")]
    [InlineData("WebAssets")]
    [InlineData("ExternalAsset")]
    [InlineData("MissingReference")]
    [InlineData("EmptyProject")]
    public async Task RestoreAsync_returns_exact_original_bytes_after_mutation(string scenarioName)
    {
        var scenario = Parse(scenarioName);
        await using var fixture = await OracleFixtureBuilder.CreateAsync(scenario, OwnParent(), CancellationToken.None);
        var originals = Snapshot(fixture.Root);
        Assert.NotEmpty(originals);
        Assert.False(File.Exists(fixture.FilePath("App/Local.props")));
        MutateEverythingUnder(fixture.Root);
        await fixture.RestoreAsync(CancellationToken.None);
        await fixture.VerifyRestoredAsync(CancellationToken.None);
        Assert.Equal(originals, Snapshot(fixture.Root));
        Assert.False(File.Exists(fixture.FilePath("App/Local.props")));
    }

    [Theory]
    [InlineData("ProjectGraph")]
    [InlineData("Encodings")]
    [InlineData("MultiTarget")]
    [InlineData("RazorGenerator")]
    [InlineData("GeneratedObj")]
    [InlineData("LinkedImport")]
    [InlineData("AbsentImport")]
    [InlineData("CustomTarget")]
    [InlineData("ExplicitCompile")]
    [InlineData("WebAssets")]
    [InlineData("ExternalAsset")]
    [InlineData("MissingReference")]
    [InlineData("EmptyProject")]
    public async Task Expectations_record_observed_negative_admission_only(string scenarioName)
    {
        var scenario = Parse(scenarioName);
        // No positive admission branch is proven (accepted task-02 findings), so every fixture
        // keeps the honest unknown disposition and the observed reason categories. Changing this
        // assertion requires a design-owned remediation, not a fixture-side decision.
        await using var fixture = await OracleFixtureBuilder.CreateAsync(scenario, OwnParent(), CancellationToken.None);
        var expected = fixture.Expected;
        Assert.False(expected.RequiredReasonCategories.IsDefault);
        Assert.False(expected.ExpectedInstanceKeys.IsDefault);
        Assert.False(expected.ExpectedEdgeKeys.IsDefault);
        Assert.False(expected.RequiredDiagnosticIds.IsDefault);
        Assert.Equal("unknown", expected.AdmissionDisposition);
        Assert.Equal(expected.RequiredReasonCategories.Order(StringComparer.Ordinal), expected.RequiredReasonCategories);
        Assert.Equal(expected.RequiredDiagnosticIds.Order(StringComparer.Ordinal), expected.RequiredDiagnosticIds);
        Assert.Equal(expected.ExpectedInstanceKeys.Order(StringComparer.Ordinal), expected.ExpectedInstanceKeys);
        Assert.Equal(expected.ExpectedInstanceKeys.Distinct().Count(), expected.ExpectedInstanceKeys.Length);
        foreach (var reason in CoreUnknownReasons)
        {
            Assert.Contains(reason, expected.RequiredReasonCategories);
        }
        if (scenario == FixtureScenario.ExternalAsset)
        {
            Assert.Contains("external-static-asset", expected.RequiredReasonCategories);
        }
        Assert.All(expected.ExpectedInstanceKeys, key => Assert.True(
            key.EndsWith("|net9.0", StringComparison.Ordinal) || key.EndsWith("|net10.0", StringComparison.Ordinal),
            "Instance keys must name the exact inner TFM: " + key));
        if (scenario == FixtureScenario.Encodings)
        {
            Assert.Equal(new[] { "CS0219" }, expected.RequiredDiagnosticIds);
        }
        if (scenario == FixtureScenario.RazorGenerator)
        {
            Assert.Equal(new[] { "CS0219", "CS0542" }, expected.RequiredDiagnosticIds);
        }
        if (scenario != FixtureScenario.RazorGenerator)
        {
            Assert.Empty(expected.ExpectedGeneratedTexts);
        }
    }

    [Fact]
    public async Task FilePath_rejects_paths_outside_the_owned_root()
    {
        await using var fixture = await OracleFixtureBuilder.CreateAsync(FixtureScenario.ProjectGraph, OwnParent(), CancellationToken.None);
        var inside = fixture.FilePath("App/Use.cs");
        Assert.StartsWith(fixture.Root + Path.DirectorySeparatorChar, inside, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => fixture.FilePath(@"C:\elsewhere\Use.cs"));
        Assert.Throws<ArgumentException>(() => fixture.FilePath("..\\..\\escaped.cs"));
        Assert.Throws<ArgumentException>(() => fixture.FilePath("a/../../escaped.cs"));
        Assert.Throws<ArgumentException>(() => fixture.FilePath(""));
        Assert.Throws<ArgumentException>(() => fixture.FilePath("   "));
    }

    [Fact]
    public async Task CreateAsync_propagates_cancellation_and_cleans_up_the_child_root()
    {
        var parent = OwnParent();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => OracleFixtureBuilder.CreateAsync(FixtureScenario.ProjectGraph, parent, cancelled.Token));
        Assert.Empty(Directory.GetDirectories(parent));
    }

    [Fact]
    public async Task CreateAsync_rejects_a_scenario_outside_the_finite_set()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => OracleFixtureBuilder.CreateAsync((FixtureScenario)997, OwnParent(), CancellationToken.None));
    }

    [Fact]
    public async Task RestoreAsync_removes_bin_and_obj_nested_inside_each_other()
    {
        await using var fixture = await OracleFixtureBuilder.CreateAsync(FixtureScenario.GeneratedObj, OwnParent(), CancellationToken.None);
        var originals = Snapshot(fixture.Root);
        var projectPath = fixture.FilePath("App/App.csproj");
        var originalBytes = await File.ReadAllBytesAsync(projectPath);
        await File.WriteAllBytesAsync(projectPath, originalBytes.Concat(new byte[] { 32 }).ToArray());
        Directory.CreateDirectory(fixture.FilePath("App/bin/nested/obj"));
        File.WriteAllText(fixture.FilePath("App/bin/nested/obj/x.txt"), "nested output");
        Directory.CreateDirectory(fixture.FilePath("App/obj/deep/bin"));
        File.WriteAllText(fixture.FilePath("App/obj/deep/bin/y.txt"), "nested output");
        await fixture.RestoreAsync(CancellationToken.None);
        await fixture.VerifyRestoredAsync(CancellationToken.None);
        Assert.Equal(originals, Snapshot(fixture.Root));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(projectPath));
        Assert.False(Directory.Exists(fixture.FilePath("App/bin")));
        Assert.False(Directory.Exists(fixture.FilePath("App/obj")));
    }

    [Fact]
    public async Task CreateAsync_reports_failed_cleanup_after_materialization_started()
    {
        var parent = OwnParent();
        using var cancellation = new CancellationTokenSource();
        FileStream? heldLock = null;
        try
        {
            // The afterFileWritten seam suspends the builder inside its materialization loop,
            // so cancelling and locking there is a deterministic handshake: no polling and no
            // window in which the builder could finish before the cancellation is observed.
            var aggregate = await Assert.ThrowsAsync<AggregateException>(() =>
                OracleFixtureBuilder.CreateAsync(
                    FixtureScenario.ProjectGraph,
                    parent,
                    writtenPath =>
                    {
                        if (!writtenPath.EndsWith("App.csproj", StringComparison.Ordinal))
                        {
                            return;
                        }
                        cancellation.Cancel();
                        heldLock = new FileStream(writtenPath, FileMode.Open, FileAccess.Read, FileShare.None);
                    },
                    cancellation.Token));
            // Cancellation may surface as OperationCanceledException or its TaskCanceledException
            // subtype depending on which await observed the token; the token must be preserved.
            var original = Assert.IsAssignableFrom<OperationCanceledException>(aggregate.InnerExceptions[0]);
            Assert.Equal(cancellation.Token, original.CancellationToken);
            var cleanupFailure = aggregate.InnerExceptions[1];
            Assert.True(cleanupFailure is IOException or UnauthorizedAccessException,
                "The cleanup failure must be reported, not swallowed: " + cleanupFailure);
            var root = Assert.Single(Directory.GetDirectories(parent));
            Assert.True(Directory.Exists(root), "The locked owned root must remain visible to the caller.");
        }
        finally
        {
            heldLock?.Dispose();
        }
        Assert.Null(OracleFixtureBuilder.TryDeleteRoot(Directory.GetDirectories(parent).Single()));
        Assert.Empty(Directory.GetDirectories(parent));
    }

    [Fact]
    public async Task Failed_cleanup_of_the_error_path_is_reported_not_swallowed()
    {
        var directory = Path.Combine(OwnParent(), "locked-root");
        Directory.CreateDirectory(directory);
        var lockedPath = Path.Combine(directory, "locked.txt");
        await File.WriteAllTextAsync(lockedPath, "locked");
        using (new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var failure = OracleFixtureBuilder.TryDeleteRoot(directory);
            Assert.NotNull(failure);
            Assert.True(Directory.Exists(directory), "The locked root must remain visible, not silently claimed as cleaned.");
        }
        Assert.Null(OracleFixtureBuilder.TryDeleteRoot(directory));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task Encoding_fixture_bytes_carry_literal_preambles_and_characters()
    {
        await using var fixture = await OracleFixtureBuilder.CreateAsync(FixtureScenario.Encodings, OwnParent(), CancellationToken.None);
        var bytes = fixture.Expected.ExpectedSourceBytes;
        var characters = fixture.Expected.ExpectedSourceCharacters;
        var utf8BomKey = MembershipKey(fixture, "App/Utf8Bom.cs");
        var utf8Key = MembershipKey(fixture, "App/Utf8.cs");
        var utf16Key = MembershipKey(fixture, "App/Utf16.cs");
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[utf8BomKey].Take(3).ToArray());
        Assert.Equal(new byte[] { 0xFF, 0xFE }, bytes[utf16Key].Take(2).ToArray());
        Assert.NotEqual(new byte[] { 0xEF }, bytes[utf8Key].Take(1).ToArray());
        // The golden values are literal encodings: decoding with the BCL reproduces the expected
        // characters verbatim, and the disk bytes equal the expectation without any re-encoding.
        Assert.Equal(characters[utf8BomKey], new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            .GetString(bytes[utf8BomKey].Skip(3).ToArray()));
        Assert.Equal(characters[utf8Key], new UTF8Encoding(false).GetString(bytes[utf8Key].ToArray()));
        Assert.Equal(characters[utf16Key], Encoding.Unicode.GetString(bytes[utf16Key].Skip(2).ToArray()));
        Assert.Contains("Привет Ω", characters[utf8BomKey]);
        foreach (var key in new[] { utf8BomKey, utf8Key, utf16Key })
        {
            Assert.Equal(bytes[key].ToArray(),
                await File.ReadAllBytesAsync(fixture.FilePath(RelativePathOf(key))));
        }
    }

    [Fact]
    public async Task GeneratedObj_expectations_pin_the_observed_sdk_output_paths_and_bytes()
    {
        await using var fixture = await OracleFixtureBuilder.CreateAsync(FixtureScenario.GeneratedObj, OwnParent(), CancellationToken.None);
        var characters = fixture.Expected.ExpectedSourceCharacters;
        var bytes = fixture.Expected.ExpectedSourceBytes;
        Assert.Contains(characters.Keys, key => key.EndsWith("|compile|App/obj/Debug/net10.0/App.AssemblyInfo.cs", StringComparison.Ordinal));
        Assert.Contains(characters.Keys, key => key.EndsWith("|compile|App/obj/Debug/net10.0/.NETCoreApp,Version=v10.0.AssemblyAttributes.cs", StringComparison.Ordinal));
        var editorConfigKey = Assert.Single(characters.Keys, key =>
            key.EndsWith("|analyzer-config|App/obj/Debug/net10.0/App.GeneratedMSBuildEditorConfig.editorconfig", StringComparison.Ordinal));
        Assert.Contains("build_property.ProjectDir = " + fixture.Root + "\\App\\\r\n", characters[editorConfigKey]);
        Assert.Contains("[assembly: System.Reflection.AssemblyCompanyAttribute(\"App\")]", characters.Values.Single(value => value.Contains("AssemblyCompanyAttribute")));
        foreach (var (key, value) in characters)
        {
            // All three generated obj inputs were observed as BOM-free UTF-8 of their exact text.
            Assert.Equal(value, new UTF8Encoding(false).GetString(bytes[key].ToArray()));
        }
    }

    [FreshMsBuildRazorFact]
    public async Task Fresh_msbuild_reproduces_the_observed_razor_generator_output()
    {
        var parent = OwnParent();
        await using var fixture = await OracleFixtureBuilder.CreateAsync(FixtureScenario.RazorGenerator, parent, CancellationToken.None);
        var artifacts = Path.Combine(parent, Path.GetFileName(fixture.Root) + "-evidence");
        var project = fixture.FilePath("App/App.csproj");
        var restore = await DependencyEvidenceRunner.RunAsync(fixture.Root, artifacts, "marker-restore",
            ["restore", project, "--configfile", fixture.FilePath("NuGet.config")], CancellationToken.None);
        Assert.True(restore.ExitCode == 0, restore.StandardOutput + restore.StandardError);
        var build = await DependencyEvidenceRunner.RunAsync(fixture.Root, artifacts, "marker-build",
            ["build", project, "--no-restore", "-c", "Debug", "-p:NoWarn=CS0219"], CancellationToken.None);
        // The ordinary build fails on the observed fixture conflict; that is the expected fixture
        // diagnostic and never a host build pass.
        Assert.NotEqual(0, build.ExitCode);
        Assert.Contains("error CS0542:", build.StandardOutput);
        Assert.True(Directory.Exists(fixture.FilePath("App/obj")), "The build stage must produce generated output for the restore check.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>
        {
            ["Configuration"] = "Debug",
            ["TargetFramework"] = "net10.0",
        });
        var opened = await workspace.OpenProjectAsync(project, cancellationToken: timeout.Token);
        Assert.DoesNotContain(workspace.Diagnostics, diagnostic => diagnostic.Kind == WorkspaceDiagnosticKind.Failure);
        var compilation = await opened.GetCompilationAsync(timeout.Token);
        Assert.NotNull(compilation);
        var generated = await opened.GetSourceGeneratedDocumentsAsync(timeout.Token);
        var document = Assert.Single(generated);
        Assert.Equal("Marker_razor.g.cs", document.Name);
        var text = (await document.GetTextAsync(timeout.Token)).ToString();
        var golden = Assert.Single(fixture.Expected.ExpectedGeneratedTexts);
        Assert.Equal(golden.Value, text);
        var markerType = compilation.GetTypeByMetadataName("E0Razor.Marker");
        var marker = markerType?.GetMembers("Marker").OfType<IFieldSymbol>().SingleOrDefault()?.ConstantValue as string;
        Assert.Equal("E0RazorMarker", marker);
        Assert.Equal("E0RazorMarker", fixture.Expected.ExpectedConstants.Single().Value);
        var diagnostics = compilation.GetDiagnostics(timeout.Token);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "CS0219" && diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "CS0542" && diagnostic.Severity == DiagnosticSeverity.Error);
        await fixture.RestoreAsync(CancellationToken.None);
        await fixture.VerifyRestoredAsync(CancellationToken.None);
        _output.WriteLine("Retained fresh-marker witness evidence: " + artifacts);
    }

    private static FixtureScenario Parse(string scenarioName) => Enum.Parse<FixtureScenario>(scenarioName);

    private static void MutateEverythingUnder(string root)
    {
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToList())
        {
            var mutated = File.ReadAllBytes(path);
            Array.Resize(ref mutated, mutated.Length + 1);
            mutated[^1] = 32;
            File.WriteAllBytes(path, mutated);
        }
        var first = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).First();
        File.Delete(first);
        File.WriteAllText(Path.Combine(root, "junk.txt"), "unexpected file");
        // The documented absent-import mutation: Local.props appears at the known-absent path.
        // The App directory exists in every single-project scenario; LinkedImport stores its
        // projects under A/ and B/, so the directory is created on demand here.
        var appDirectory = Path.Combine(root, "App");
        Directory.CreateDirectory(appDirectory);
        File.WriteAllText(Path.Combine(appDirectory, "Local.props"), "<Project><PropertyGroup><DefineConstants>APPEARED</DefineConstants></PropertyGroup></Project>");
        Directory.CreateDirectory(Path.Combine(root, "App", "bin"));
        Directory.CreateDirectory(Path.Combine(root, "App", "obj"));
        File.WriteAllText(Path.Combine(root, "App", "bin", "leftover.txt"), "stale output");
        File.WriteAllText(Path.Combine(root, "App", "obj", "leftover.txt"), "stale output");
        // Generated output nested inside generated output: deleting the outer bin/obj must not
        // fail on the inner one already removed with its parent.
        Directory.CreateDirectory(Path.Combine(root, "App", "bin", "nested", "obj"));
        File.WriteAllText(Path.Combine(root, "App", "bin", "nested", "obj", "x.txt"), "nested output");
    }

    private static SortedDictionary<string, byte[]> Snapshot(string root) => new(
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                path => File.ReadAllBytes(path)),
        StringComparer.Ordinal);

    private static string OwnParent()
    {
        var parent = Path.Combine(Path.GetTempPath(), "roslyn-oracle-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        return parent;
    }

    private static string MembershipKey(OracleFixtureCase fixture, string relativePath)
        => Assert.Single(fixture.Expected.ExpectedSourceBytes.Keys,
            key => key.EndsWith("|" + relativePath, StringComparison.Ordinal));

    // Membership keys are "project|tfm|role|root-relative-path"; the path is the fourth segment.
    private static string RelativePathOf(string membershipKey) => membershipKey.Split('|')[3];

    private static bool IsGeneratedOutputKey(string membershipKey)
        => RelativePathOf(membershipKey).Split('/').Any(segment => segment is "bin" or "obj");

    private static bool PinnedSdkWithAspNetCoreIsInstalled()
    {
        var roots = new[]
        {
            Environment.GetEnvironmentVariable("DOTNET_ROOT"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"),
        };
        return roots.Any(root => !string.IsNullOrWhiteSpace(root)
            && Directory.Exists(Path.Combine(root, "sdk", "10.0.300"))
            && Directory.Exists(Path.Combine(root, "shared", "Microsoft.AspNetCore.App")));
    }

    /// <summary>
    /// Skips the fresh-marker check when the pinned SDK or the local ASP.NET Core prerequisite is
    /// unavailable: a missing prerequisite is not-run, never a network fallback or a pass.
    /// </summary>
    private sealed class FreshMsBuildRazorFactAttribute : FactAttribute
    {
        public FreshMsBuildRazorFactAttribute()
        {
            if (!PinnedSdkWithAspNetCoreIsInstalled())
            {
                Skip = "The pinned SDK 10.0.300 or the local ASP.NET Core framework prerequisite is unavailable.";
            }
        }
    }

    private static readonly string[] CoreUnknownReasons =
    [
        "analyzer-dependency-closure-unproven",
        "custom-target",
        "restore-resolution-closure-unproven",
        "toolset-identity-input-set-unproven",
    ];

    private readonly ITestOutputHelper _output;
}
