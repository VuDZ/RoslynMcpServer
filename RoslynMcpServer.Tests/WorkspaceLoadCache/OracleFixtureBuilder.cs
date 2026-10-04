using System.Collections.Immutable;
using System.Text;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

/// <summary>
/// Materializes the finite oracle fixture scenarios of implementation packet E0/task-03 under a
/// unique owned child directory. The builder only writes fixture bytes; it never builds, restores,
/// registers MSBuild, loads a DLL or runs an oracle.
/// Expected dispositions record the observed negative admission state of the accepted task-02
/// findings: no request has a proven positive admission branch yet, so every scenario expects
/// "unknown" together with the observed reason categories until a design-owned remediation lands.
/// Golden strings are literals from independently observed task-02 evidence; "{fixtureRoot}"
/// placeholders in golden texts are substituted with the created root path.
/// </summary>
internal static class OracleFixtureBuilder
{
    /// <summary>Creates a unique owned fixture root under <paramref name="parentDirectory"/>.</summary>
    /// <exception cref="ArgumentException">The scenario is outside the finite set or the parent is empty.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled; the child root is removed.</exception>
    public static Task<OracleFixtureCase> CreateAsync(
        FixtureScenario scenario,
        string parentDirectory,
        CancellationToken cancellationToken)
        => CreateAsync(scenario, parentDirectory, afterFileWritten: null, cancellationToken);

    /// <summary>
    /// Internal fault-injection seam over the same materialization loop: <paramref name="afterFileWritten"/>
    /// runs synchronously after each file write while the builder is suspended, so a test can
    /// deterministically cancel the token or lock a file inside the owned root before the loop
    /// continues; callback exceptions enter the same cleanup-on-failure path.
    /// </summary>
    internal static async Task<OracleFixtureCase> CreateAsync(
        FixtureScenario scenario,
        string parentDirectory,
        Action<string>? afterFileWritten,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(scenario))
        {
            throw new ArgumentException("The scenario is not part of the finite fixture set.", nameof(scenario));
        }
        var root = Path.Combine(Path.GetFullPath(parentDirectory), "oracle-fixture-" + scenario + "-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var manifest = Compose(scenario, root);
            foreach (var (relativePath, bytes) in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = Path.Combine(root, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
                afterFileWritten?.Invoke(path);
            }
            return new OracleFixtureCase(
                scenario,
                root,
                manifest.Request,
                manifest.Expected,
                manifest.Files.ToImmutableSortedDictionary(
                    pair => pair.Key, pair => pair.Value.ToImmutableArray(), StringComparer.Ordinal));
        }
        catch (Exception original)
        {
            // The original exception stays primary so cancellation keeps propagating its token,
            // but a failed cleanup is reported, never swallowed: an owned root left on disk on
            // the failure path is aggregated with the original exception.
            var cleanupFailure = TryDeleteRoot(root);
            if (cleanupFailure is not null)
            {
                throw new AggregateException(original, cleanupFailure);
            }
            throw;
        }
    }

    /// <summary>
    /// Attempts to delete the owned child root and returns the failure instead of throwing, so the
    /// caller reports it together with the original failure; a silent null means the root is gone.
    /// Internal for regression tests of the cleanup-failure reporting.
    /// </summary>
    internal static Exception? TryDeleteRoot(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return exception;
        }
    }

    private static Manifest Compose(FixtureScenario scenario, string root) => scenario switch
    {
        FixtureScenario.ProjectGraph => ProjectGraph(root),
        FixtureScenario.Encodings => Encodings(root),
        FixtureScenario.MultiTarget => MultiTarget(root),
        FixtureScenario.RazorGenerator => RazorGenerator(root),
        FixtureScenario.GeneratedObj => GeneratedObj(root),
        FixtureScenario.LinkedImport => LinkedImport(root),
        FixtureScenario.AbsentImport => AbsentImport(root),
        FixtureScenario.CustomTarget => CustomTarget(root),
        FixtureScenario.ExplicitCompile => ExplicitCompile(root),
        FixtureScenario.WebAssets => WebAssets(root),
        FixtureScenario.ExternalAsset => ExternalAsset(root),
        FixtureScenario.MissingReference => MissingReference(root),
        FixtureScenario.EmptyProject => EmptyProject(root),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "The scenario is not part of the finite fixture set."),
    };

    // V01: two projects, one edge, navigation resolves the Lib source and the constant 42.
    private static Manifest ProjectGraph(string root)
    {
        const string valueCs = "namespace Lib; public static class Value { public const int Number = 42; }\n";
        const string useCs = "namespace App; public static class Use { public static int Read() { return Lib.Value.Number; } }\n";
        var files = TextFiles(
            ("global.json", SdkPinJson),
            ("NuGet.config", ClearedFeedsConfig),
            ("Lib/Lib.csproj", ProjectXml(Sdk, Net10Properties)),
            ("Lib/Value.cs", valueCs),
            ("App/App.csproj", ProjectXml(Sdk, Net10Properties, "<ItemGroup><ProjectReference Include=\"../Lib/Lib.csproj\"/></ItemGroup>")),
            ("App/Use.cs", useCs));
        var app = Instance("App/App.csproj", "net10.0");
        var lib = Instance("Lib/Lib.csproj", "net10.0");
        var characters = Pairs(
            (Membership(app, "compile", "App/Use.cs"), useCs),
            (Membership(lib, "compile", "Lib/Value.cs"), valueCs));
        var expected = new FixtureExpectation(
            "unknown",
            UnknownReasons(),
            [app, lib],
            [app + "->" + lib],
            Pairs(
                (Option(app, "NullableContextOptions"), "Enable"),
                (Option(lib, "NullableContextOptions"), "Enable")),
            characters,
            Utf8BytesOf(characters),
            Pairs(),
            Pairs(
                (Constant(app, "Lib.Value.Number"), "42"),
                (Constant(lib, "Lib.Value.Number"), "42")),
            []);
        return new Manifest(files, ProjectRequest(root, "App/App.csproj", "Lib/Lib.csproj"), expected);
    }

    // V02: the same non-ASCII text in UTF-8 BOM, BOM-free UTF-8 and UTF-16 LE, with the
    // editorconfig-promoted CS0219 as an expected fixture diagnostic, not a load failure.
    private static Manifest Encodings(string root)
    {
        var utf8BomCs = EncodingSource("Utf8Bom");
        var utf8Cs = EncodingSource("Utf8");
        var utf16Cs = EncodingSource("Utf16");
        var csproj = ProjectXml(
            Sdk,
            Net10Properties + "<PropertyGroup><DefineConstants>$(DefineConstants);E0_ENCODING</DefineConstants></PropertyGroup>");
        var files = RawFiles(
            ("global.json", Utf8(SdkPinJson)),
            ("NuGet.config", Utf8(ClearedFeedsConfig)),
            ("App/App.csproj", Utf8(csproj)),
            ("App/Utf8Bom.cs", Utf8WithBom(utf8BomCs)),
            ("App/Utf8.cs", Utf8(utf8Cs)),
            ("App/Utf16.cs", Utf16LeWithBom(utf16Cs)),
            ("App/.editorconfig", Utf8(EditorConfigPromotingCs0219)));
        var app = Instance("App/App.csproj", "net10.0");
        var characters = Pairs(
            (Membership(app, "compile", "App/Utf8Bom.cs"), utf8BomCs),
            (Membership(app, "compile", "App/Utf8.cs"), utf8Cs),
            (Membership(app, "compile", "App/Utf16.cs"), utf16Cs),
            (Membership(app, "analyzer-config", "App/.editorconfig"), EditorConfigPromotingCs0219));
        var expected = new FixtureExpectation(
            "unknown",
            UnknownReasons(),
            [app],
            [],
            Pairs((Option(app, "NullableContextOptions"), "Enable")),
            characters,
            Utf8BytesOf(
                characters,
                (Membership(app, "compile", "App/Utf8Bom.cs"), Utf8WithBom(utf8BomCs)),
                (Membership(app, "compile", "App/Utf16.cs"), Utf16LeWithBom(utf16Cs))),
            Pairs(),
            Pairs(),
            ["CS0219"]);
        return new Manifest(files, ProjectRequest(root, "App/App.csproj"), expected);
    }

    // V03: two inner TFMs with conditional defines; positive exact mapping is not proven while the
    // net9 reference pack is unavailable, so the define outcome is expressed through the constants.
    private static Manifest MultiTarget(string root)
    {
        const string flavorCs =
            "namespace App;\n\npublic static class Flavor\n{\n#if E0_NINE\n    public const int Value = 9;\n#elif E0_TEN\n    public const int Value = 10;\n#endif\n}\n";
        var csproj = ProjectXml(
            Sdk,
            "<PropertyGroup><TargetFrameworks>net9.0;net10.0</TargetFrameworks><Nullable>enable</Nullable><LangVersion>14</LangVersion></PropertyGroup>"
            + "<PropertyGroup Condition=\"'$(TargetFramework)'=='net9.0'\"><DefineConstants>$(DefineConstants);E0_NINE</DefineConstants></PropertyGroup>"
            + "<PropertyGroup Condition=\"'$(TargetFramework)'=='net10.0'\"><DefineConstants>$(DefineConstants);E0_TEN</DefineConstants></PropertyGroup>");
        var files = TextFiles(
            ("global.json", SdkPinJson),
            ("NuGet.config", ClearedFeedsConfig),
            ("App/App.csproj", csproj),
            ("App/Flavor.cs", flavorCs));
        var net9 = Instance("App/App.csproj", "net9.0");
        var net10 = Instance("App/App.csproj", "net10.0");
        var characters = Pairs(
            (Membership(net9, "compile", "App/Flavor.cs"), flavorCs),
            (Membership(net10, "compile", "App/Flavor.cs"), flavorCs));
        var expected = new FixtureExpectation(
            "unknown",
            UnknownReasons(),
            [net10, net9],
            [],
            Pairs(
                (Option(net9, "NullableContextOptions"), "Enable"),
                (Option(net10, "NullableContextOptions"), "Enable")),
            characters,
            Utf8BytesOf(characters),
            Pairs(),
            Pairs(
                (Constant(net9, "App.Flavor.Value"), "9"),
                (Constant(net10, "App.Flavor.Value"), "10")),
            []);
        return new Manifest(files, ProjectRequest(root, "App/App.csproj"), expected);
    }

    // V04: the exact task-02 Razor fixture, kept byte-identical so the observed generated golden
    // (including its content checksum) stays valid. CS0542 is the observed fixture conflict.
    private static Manifest RazorGenerator(string root)
    {
        var files = TextFiles(
            ("global.json", SdkPinJson),
            ("NuGet.config", ClearedFeedsConfig),
            ("App/App.csproj", RazorProjectXml),
            ("App/Marker.razor", MarkerRazorText),
            ("App/Use.cs", RazorUseCs),
            ("App/.editorconfig", EditorConfigPromotingCs0219));
        var app = Instance("App/App.csproj", "net10.0");
        var characters = Pairs(
            (Membership(app, "compile", "App/Use.cs"), RazorUseCs),
            (Membership(app, "additional", "App/Marker.razor"), MarkerRazorText),
            (Membership(app, "analyzer-config", "App/.editorconfig"), EditorConfigPromotingCs0219));
        var expected = new FixtureExpectation(
            "unknown",
            UnknownReasons(),
            [app],
            [],
            Pairs((Option(app, "NullableContextOptions"), "Enable")),
            characters,
            Utf8BytesOf(characters),
            Pairs((RazorGeneratedKey, RazorGoldenTemplate.Replace("{fixtureRoot}", root))),
            Pairs((Constant(app, "E0Razor.Marker.Marker"), "E0RazorMarker")),
            ["CS0219", "CS0542"]);
        return new Manifest(files, ProjectRequest(root, "App/App.csproj"), expected);
    }

    // V05: the SDK-generated assembly info, framework attributes and MSBuild editor config in obj
    // are explicit observed inputs; paths and bytes come from the task-02 findings.
    private static Manifest GeneratedObj(string root)
    {
        const string valueCs = "public static class Value { public const string Text = \"E0\"; }\n";
        var files = TextFiles(
            ("global.json", SdkPinJson),
            ("NuGet.config", ClearedFeedsConfig),
            ("App/App.csproj", ProjectXml(Sdk, Net10Properties)),
            ("App/Value.cs", valueCs));
        var app = Instance("App/App.csproj", "net10.0");
        var characters = Pairs(
            (Membership(app, "compile", "App/Value.cs"), valueCs),
            (Membership(app, "compile", "App/obj/Debug/net10.0/App.AssemblyInfo.cs"), AssemblyInfoText),
            (Membership(app, "compile", "App/obj/Debug/net10.0/.NETCoreApp,Version=v10.0.AssemblyAttributes.cs"), AssemblyAttributesText),
            (Membership(app, "analyzer-config", "App/obj/Debug/net10.0/App.GeneratedMSBuildEditorConfig.editorconfig"),
                GeneratedEditorConfigTemplate.Replace("{fixtureRoot}", root)));
        var expected = new FixtureExpectation(
            "unknown",
            UnknownReasons(),
            [app],
            [],
            Pairs((Option(app, "NullableContextOptions"), "Enable")),
            characters,
            Utf8BytesOf(characters),
            Pairs(),
            Pairs(),
            []);
        return new Manifest(files, ProjectRequest(root, "App/App.csproj"), expected);
    }

    // V06: one physical shared source with two exact memberships; the external common.props import
    // has properties only, no Target, so it stays inside the profile's bounded evidence idea.
    private static Manifest LinkedImport(string root)
    {
        const string sharedCs = "namespace Shared; public static class Link { public const int Number = 42; }\n";
        const string commonProps = "<Project><PropertyGroup><DefineConstants>LINKED_IMPORT</DefineConstants></PropertyGroup></Project>";
        const string linkedItems = "<ItemGroup><Compile Include=\"../shared/Shared.cs\" Link=\"Shared.cs\"/></ItemGroup>";
        var files = TextFiles(
            ("global.json", SdkPinJson),
            ("NuGet.config", ClearedFeedsConfig),
            ("A/A.csproj", ProjectXml(Sdk, Net10Properties, "<Import Project=\"../common.props\"/>" + linkedItems + "<ItemGroup><ProjectReference Include=\"../B/B.csproj\"/></ItemGroup>")),
            ("B/B.csproj", ProjectXml(Sdk, Net10Properties, "<Import Project=\"../common.props\"/>" + linkedItems)),
            ("shared/Shared.cs", sharedCs),
            ("common.props", commonProps));
        var a = Instance("A/A.csproj", "net10.0");
        var b = Instance("B/B.csproj", "net10.0");
        var characters = Pairs(
            (Membership(a, "compile", "shared/Shared.cs"), sharedCs),
            (Membership(b, "compile", "shared/Shared.cs"), sharedCs));
        var expected = new FixtureExpectation(
            "unknown",
            UnknownReasons(),
            [a, b],
            [a + "->" + b],
            Pairs(
                (Option(a, "NullableContextOptions"), "Enable"),
                (Option(b, "NullableContextOptions"), "Enable")),
            characters,
            Utf8BytesOf(characters),
            Pairs(),
            Pairs(
                (Constant(a, "Shared.Link.Number"), "42"),
                (Constant(b, "Shared.Link.Number"), "42")),
            []);
        return new Manifest(files, ProjectRequest(root, "A/A.csproj", "B/B.csproj"), expected);
    }

    // V12: the conditional import stays absent; creating App/Local.props later is the documented
    // known-absent mutation that must refuse admission before hydrate.
    private static Manifest AbsentImport(string root)
    {
        const string valueCs = "public static class Value { public const string Text = \"E0\"; }\n";
        var files = TextFiles(
            ("global.json", SdkPinJson),
            ("NuGet.config", ClearedFeedsConfig),
            ("App/App.csproj", ProjectXml(Sdk, Net10Properties, "<Import Project=\"Local.props\" Condition=\"Exists('Local.props')\"/>")),
            ("App/Value.cs", valueCs));
        var app = Instance("App/App.csproj", "net10.0");
        return new Manifest(
            files,
            ProjectRequest(root, "App/App.csproj"),
            SimpleUnknownExpectation(app, ("App/Value.cs", valueCs)));
    }

    // The project-owned Target before CoreCompile keeps the whole request in the unknown
    // custom-target category; its execution outcome stays distinct from the admission outcome.
    private static Manifest CustomTarget(string root)
    {
        const string valueCs = "public static class Value { public const string Text = \"E0\"; }\n";
        var files = TextFiles(
            ("global.json", SdkPinJson),
            ("NuGet.config", ClearedFeedsConfig),
            ("App/App.csproj", ProjectXml(Sdk, Net10Properties, CustomTargetXml)),
            ("App/Value.cs", valueCs),
            ("App/target-input.txt", "target input\n"));
        var app = Instance("App/App.csproj", "net10.0");
        return new Manifest(
            files,
            ProjectRequest(root, "App/App.csproj"),
            SimpleUnknownExpectation(app, ("App/Value.cs", valueCs)));
    }

    // V07: default Compile items disabled with an explicit include; add/remove/rename writer
    // support stays unsupported before bytes. This is a capability layout, not a new admission rule.
    private static Manifest ExplicitCompile(string root)
    {
        const string onlyCs = "public static class Only { public const int Value = 7; }\n";
        var files = TextFiles(
            ("global.json", SdkPinJson),
            ("NuGet.config", ClearedFeedsConfig),
            ("App/App.csproj", ProjectXml(
                Sdk,
                Net10Properties + "<PropertyGroup><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>",
                "<ItemGroup><Compile Include=\"Only.cs\"/></ItemGroup>")),
            ("App/Only.cs", onlyCs));
        var app = Instance("App/App.csproj", "net10.0");
        return new Manifest(
            files,
            ProjectRequest(root, "App/App.csproj"),
            SimpleUnknownExpectation(app, ("App/Only.cs", onlyCs)));
    }

    // Web static assets inside the project, including the hidden .well-known region member.
    private static Manifest WebAssets(string root)
    {
        var files = TextFiles(
            ("global.json", SdkPinJson),
            ("NuGet.config", ClearedFeedsConfig),
            ("App/App.csproj", ProjectXml(WebSdk, Net10Properties)),
            ("App/Program.cs", WebProgramCs),
            ("App/wwwroot/index.txt", "local asset"),
            ("App/wwwroot/.well-known/e0.txt", "hidden asset"));
        var app = Instance("App/App.csproj", "net10.0");
        return new Manifest(
            files,
            ProjectRequest(root, "App/App.csproj"),
            SimpleUnknownExpectation(app, ("App/Program.cs", WebProgramCs)));
    }

    // A linked Content asset outside the project and SDK directories keeps the whole request
    // rejected in the external-static-asset category.
    private static Manifest ExternalAsset(string root)
    {
        var files = TextFiles(
            ("global.json", SdkPinJson),
            ("NuGet.config", ClearedFeedsConfig),
            ("App/App.csproj", ProjectXml(
                WebSdk,
                Net10Properties,
                "<ItemGroup><Content Include=\"../external/asset.txt\" Link=\"wwwroot/asset.txt\" CopyToOutputDirectory=\"PreserveNewest\"/></ItemGroup>")),
            ("App/Program.cs", WebProgramCs),
            ("external/asset.txt", "external asset"));
        var app = Instance("App/App.csproj", "net10.0");
        var compile = Pairs((Membership(app, "compile", "App/Program.cs"), WebProgramCs));
        var expected = new FixtureExpectation(
            "unknown",
            UnknownReasons("external-static-asset"),
            [app],
            [],
            Pairs((Option(app, "NullableContextOptions"), "Enable")),
            compile,
            Utf8BytesOf(compile),
            Pairs(),
            Pairs(),
            []);
        return new Manifest(files, ProjectRequest(root, "App/App.csproj"), expected);
    }

    // A missing ProjectReference target cannot give completeness; the expected instance inventory
    // stays the single present project with no edge.
    private static Manifest MissingReference(string root)
    {
        const string useCs = "namespace App; public static class Use { public static int Read() { return 1; } }\n";
        var files = TextFiles(
            ("global.json", SdkPinJson),
            ("NuGet.config", ClearedFeedsConfig),
            ("App/App.csproj", ProjectXml(Sdk, Net10Properties, "<ItemGroup><ProjectReference Include=\"../Missing/Missing.csproj\"/></ItemGroup>")),
            ("App/Use.cs", useCs));
        var app = Instance("App/App.csproj", "net10.0");
        return new Manifest(
            files,
            ProjectRequest(root, "App/App.csproj"),
            SimpleUnknownExpectation(app, ("App/Use.cs", useCs)));
    }

    // A legal empty project: no user compile input at all. Zero user files is not zero-project
    // success; the generated input roles remain unconfirmed by task-02 evidence.
    private static Manifest EmptyProject(string root)
    {
        var files = TextFiles(
            ("global.json", SdkPinJson),
            ("NuGet.config", ClearedFeedsConfig),
            ("App/App.csproj", ProjectXml(
                Sdk,
                Net10Properties + "<PropertyGroup><GenerateAssemblyInfo>false</GenerateAssemblyInfo><GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute></PropertyGroup>")));
        var app = Instance("App/App.csproj", "net10.0");
        var expected = new FixtureExpectation(
            "unknown",
            UnknownReasons(),
            [app],
            [],
            Pairs((Option(app, "NullableContextOptions"), "Enable")),
            Pairs(),
            Utf8BytesOf(Pairs()),
            Pairs(),
            Pairs(),
            []);
        return new Manifest(files, ProjectRequest(root, "App/App.csproj"), expected);
    }

    private static FixtureExpectation SimpleUnknownExpectation(string instance, params (string Path, string Text)[] compileSources)
    {
        var characters = Pairs(compileSources.Select(source => (Membership(instance, "compile", source.Path), source.Text)));
        return new FixtureExpectation(
            "unknown",
            UnknownReasons(),
            [instance],
            [],
            Pairs((Option(instance, "NullableContextOptions"), "Enable")),
            characters,
            Utf8BytesOf(characters),
            Pairs(),
            Pairs(),
            []);
    }

    private static ImmutableArray<string> UnknownReasons(params string[] extraReasons)
    {
        var reasons = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var reason in CoreUnknownReasons)
        {
            reasons.Add(reason);
        }
        foreach (var reason in extraReasons)
        {
            reasons.Add(reason);
        }
        return reasons.ToImmutableArray();
    }

    private static SortedDictionary<string, byte[]> TextFiles(params (string Path, string Text)[] entries)
        => RawFiles(entries.Select(entry => (entry.Path, Utf8(entry.Text))).ToArray());

    private static SortedDictionary<string, byte[]> RawFiles(params (string Path, byte[] Bytes)[] entries)
        => new(entries.ToDictionary(entry => entry.Path, entry => entry.Bytes), StringComparer.Ordinal);

    private static ExperimentRequest ProjectRequest(string root, params string[] projectRelativePaths)
    {
        var projects = projectRelativePaths
            .Select(path => Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar))))
            .Order(PathComparer)
            .ToImmutableArray();
        return new ExperimentRequest(projects[0], "Debug", "AnyCPU", null, projects, "full", false);
    }

    private static string ProjectXml(string sdk, string propertyGroups, string items = "")
        => "<Project Sdk=\"" + sdk + "\">" + propertyGroups + items + "</Project>\n";

    private static string EncodingSource(string className)
        => "namespace E0Encodings;\n\npublic static class " + className + "\n{\n    public const string Text = \"Привет Ω\";\n\n"
        + "    public static int Unused()\n    {\n        int unused = 0;\n        return 1;\n    }\n}\n";

    private static string Instance(string project, string framework) => project + "|" + framework;

    private static string Membership(string instance, string role, string path) => instance + "|" + role + "|" + path;

    private static string Option(string instance, string property) => instance + "|" + property;

    private static string Constant(string instance, string member) => instance + "|" + member;

    private static ImmutableSortedDictionary<string, string> Pairs(params (string Key, string Value)[] entries)
        => entries.ToImmutableSortedDictionary(entry => entry.Key, entry => entry.Value);

    private static ImmutableSortedDictionary<string, string> Pairs(IEnumerable<(string Key, string Value)> entries)
        => entries.ToImmutableSortedDictionary(entry => entry.Key, entry => entry.Value);

    // The default source encoding is BOM-free UTF-8; the encoding fixture overrides the two
    // preamble variants explicitly so the golden bytes stay literal.
    private static ImmutableSortedDictionary<string, ImmutableArray<byte>> Utf8BytesOf(
        ImmutableSortedDictionary<string, string> characters,
        params (string Key, byte[] Bytes)[] overrides)
    {
        var bytes = characters.ToImmutableSortedDictionary(
            pair => pair.Key, pair => Utf8(pair.Value).ToImmutableArray());
        foreach (var (key, value) in overrides)
        {
            bytes = bytes.SetItem(key, value.ToImmutableArray());
        }
        return bytes;
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static byte[] Utf8WithBom(string text) => [0xEF, 0xBB, 0xBF, .. Utf8(text)];

    private static byte[] Utf16LeWithBom(string text) => [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(text)];

    private sealed record Manifest(
        SortedDictionary<string, byte[]> Files,
        ExperimentRequest Request,
        FixtureExpectation Expected);

    private const string Sdk = "Microsoft.NET.Sdk";
    private const string WebSdk = "Microsoft.NET.Sdk.Web";
    private const string Net10Properties =
        "<PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><LangVersion>14</LangVersion></PropertyGroup>";
    private const string SdkPinJson = "{\"sdk\":{\"version\":\"10.0.300\",\"rollForward\":\"disable\"}}\n";
    private const string ClearedFeedsConfig = "<configuration><packageSources><clear /></packageSources></configuration>\n";
    private const string EditorConfigPromotingCs0219 = "root = true\n[*.cs]\ndotnet_diagnostic.CS0219.severity = error\n";
    private const string WebProgramCs = "public static class Program { public static void Main() { } }\n";
    private const string CustomTargetXml =
        "<Target Name=\"ReadTargetInput\" BeforeTargets=\"CoreCompile\"><Exec Command=\"cmd /c type &quot;$(MSBuildProjectDirectory)\\target-input.txt&quot; &gt; &quot;$(MSBuildProjectDirectory)\\target-output.txt&quot;\" /></Target>";
    private const string RazorProjectXml = """
        <Project Sdk="Microsoft.NET.Sdk.Razor">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <LangVersion>14</LangVersion>
            <RootNamespace>E0Razor</RootNamespace>
            <EnableDefaultRazorGenerateItems>true</EnableDefaultRazorGenerateItems>
          </PropertyGroup>
          <ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /></ItemGroup>
        </Project>
        """ + "\n";
    private const string MarkerRazorText = "<h1>Witness</h1>\n@code { public const string Marker = \"E0RazorMarker\"; }\n";
    private const string RazorUseCs =
        "namespace E0Razor; public static class Use { public static string Read() { int unused = 0; return Marker.Marker; } }\n";
    private const string RazorGeneratedKey =
        "App/App.csproj|net10.0|Microsoft.CodeAnalysis.Razor.Compiler/Microsoft.NET.Sdk.Razor.SourceGenerators.RazorSourceGenerator|Marker_razor.g.cs";

    // The four literals below are exact byte observations retained by the task-02 evidence
    // (Razor witness and standard fixture obj outputs); keep them verbatim including CRLF,
    // trailing spaces and the "{fixtureRoot}" placeholders.

    private const string RazorGoldenTemplate =
        "#pragma checksum \"{fixtureRoot}\\App\\Marker.razor\" \"{8829d00f-11b8-4213-878b-770e8597ac16}\" \"415790b2cdac29296b972c7e584c387167d4e1a890efffba7a8198b83936de01\"\r\n" +
        "// <auto-generated/>\r\n" +
        "#pragma warning disable 1591\r\n" +
        "namespace E0Razor\r\n" +
        "{\r\n" +
        "    #line default\r\n" +
        "    using global::System;\r\n" +
        "    using global::System.Collections.Generic;\r\n" +
        "    using global::System.Linq;\r\n" +
        "    using global::System.Threading.Tasks;\r\n" +
        "    using global::Microsoft.AspNetCore.Components;\r\n" +
        "    #line default\r\n" +
        "    #line hidden\r\n" +
        "    #nullable restore\r\n" +
        "    public partial class Marker : global::Microsoft.AspNetCore.Components.ComponentBase\r\n" +
        "    #nullable disable\r\n" +
        "    {\r\n" +
        "        #pragma warning disable 1998\r\n" +
        "        protected override void BuildRenderTree(global::Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder __builder)\r\n" +
        "        {\r\n" +
        "            __builder.AddMarkupContent(0, \"<h1>Witness</h1>\");\r\n" +
        "        }\r\n" +
        "        #pragma warning restore 1998\r\n" +
        "#nullable restore\r\n" +
        "#line (2,8)-(2,55) \"{fixtureRoot}\\App\\Marker.razor\"\r\n" +
        " public const string Marker = \"E0RazorMarker\"; \r\n" +
        "\r\n" +
        "#line default\r\n" +
        "#line hidden\r\n" +
        "#nullable disable\r\n" +
        "\r\n" +
        "    }\r\n" +
        "}\r\n" +
        "#pragma warning restore 1591\r\n";

    private const string AssemblyInfoText =
        "//------------------------------------------------------------------------------\r\n" +
        "// <auto-generated>\r\n" +
        "//     This code was generated by a tool.\r\n" +
        "//\r\n" +
        "//     Changes to this file may cause incorrect behavior and will be lost if\r\n" +
        "//     the code is regenerated.\r\n" +
        "// </auto-generated>\r\n" +
        "//------------------------------------------------------------------------------\r\n" +
        "\r\n" +
        "using System;\r\n" +
        "using System.Reflection;\r\n" +
        "\r\n" +
        "[assembly: System.Reflection.AssemblyCompanyAttribute(\"App\")]\r\n" +
        "[assembly: System.Reflection.AssemblyConfigurationAttribute(\"Debug\")]\r\n" +
        "[assembly: System.Reflection.AssemblyFileVersionAttribute(\"1.0.0.0\")]\r\n" +
        "[assembly: System.Reflection.AssemblyInformationalVersionAttribute(\"1.0.0\")]\r\n" +
        "[assembly: System.Reflection.AssemblyProductAttribute(\"App\")]\r\n" +
        "[assembly: System.Reflection.AssemblyTitleAttribute(\"App\")]\r\n" +
        "[assembly: System.Reflection.AssemblyVersionAttribute(\"1.0.0.0\")]\r\n" +
        "\r\n" +
        "// Generated by the MSBuild WriteCodeFragment class.\r\n" +
        "\r\n";

    private const string AssemblyAttributesText =
        "// <autogenerated />\r\n" +
        "using System;\r\n" +
        "using System.Reflection;\r\n" +
        "[assembly: global::System.Runtime.Versioning.TargetFrameworkAttribute(\".NETCoreApp,Version=v10.0\", FrameworkDisplayName = \".NET 10.0\")]\r\n";

    private const string GeneratedEditorConfigTemplate =
        "is_global = true\r\n" +
        "build_property.TargetFramework = net10.0\r\n" +
        "build_property.TargetFrameworkIdentifier = .NETCoreApp\r\n" +
        "build_property.TargetFrameworkVersion = v10.0\r\n" +
        "build_property.TargetPlatformMinVersion = \r\n" +
        "build_property.UsingMicrosoftNETSdkWeb = \r\n" +
        "build_property.ProjectTypeGuids = \r\n" +
        "build_property.InvariantGlobalization = \r\n" +
        "build_property.PlatformNeutralAssembly = \r\n" +
        "build_property.EnforceExtendedAnalyzerRules = \r\n" +
        "build_property.EntryPointFilePath = \r\n" +
        "build_property._SupportedPlatformList = Linux,macOS,Windows\r\n" +
        "build_property.RootNamespace = App\r\n" +
        "build_property.ProjectDir = {fixtureRoot}\\App\\\r\n" +
        "build_property.EnableComHosting = \r\n" +
        "build_property.EnableGeneratedComInterfaceComImportInterop = \r\n" +
        "build_property.EffectiveAnalysisLevelStyle = 10.0\r\n" +
        "build_property.EnableCodeStyleSeverity = \r\n";

    private static readonly string[] CoreUnknownReasons =
    [
        // Restore closure, toolset identity input set and analyzer dependency closure are not
        // proven by public evidence, and every observed SDK 10.0.300 request imported the
        // Target-bearing workload manifest outside the SDK directory (task-02 findings).
        "analyzer-dependency-closure-unproven",
        "custom-target",
        "restore-resolution-closure-unproven",
        "toolset-identity-input-set-unproven",
    ];

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
