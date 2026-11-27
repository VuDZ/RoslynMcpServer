using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Config;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.Tools;
using Xunit;

namespace RoslynMcpServer.Tests.Navigation;

[Collection("NavigationOverflowStore")]
public sealed class NavigationToolsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_coordinates_without_name_select_the_requested_overload(bool references)
    {
        await using var fixture = await Fixture.CreateAsync();
        var (line, column) = fixture.Position("Work(1)");

        var result = references
            ? await fixture.Tools.FindSymbolReferences(filePath: fixture.SourcePath, line: line, column: column)
            : await fixture.Tools.FindSymbolDefinition(filePath: fixture.SourcePath, line: line, column: column);

        if (references)
        {
            Assert.Contains("Found 1 usages for 'Work'", result, StringComparison.Ordinal);
            Assert.Contains($"{fixture.SourcePath}:{line}:{column}", result, StringComparison.Ordinal);
        }
        else
        {
            var (declarationLine, declarationColumn) = fixture.Position("Work(int value)");
            Assert.Contains("Found 1 declaration symbol(s)", result, StringComparison.Ordinal);
            Assert.Contains($"  Line: {declarationLine}", result, StringComparison.Ordinal);
            Assert.Contains($"  Column: {declarationColumn}", result, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task File_and_name_work_with_and_without_line(bool references, bool withLine)
    {
        await using var fixture = await Fixture.CreateAsync();
        var name = withLine ? "Work" : "Run";
        var (usageLine, _) = fixture.Position("Work(1)");
        int? line = withLine ? usageLine : null;

        var result = references
            ? await fixture.Tools.FindSymbolReferences(name, filePath: fixture.SourcePath, line: line)
            : await fixture.Tools.FindSymbolDefinition(name, filePath: fixture.SourcePath, line: line);

        if (references)
        {
            var expected = withLine ? "Found 1 usages for 'Work'" : "No usages found for `Run`";
            Assert.Contains(expected, result, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains($"Found 1 declaration symbol(s) matching `{name}`", result, StringComparison.Ordinal);
            var (declarationLine, _) = fixture.Position(withLine ? "Work(int value)" : "Run()");
            Assert.Contains($"  Line: {declarationLine}", result, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(false, null, null)]
    [InlineData(false, 6, null)]
    [InlineData(false, null, 25)]
    [InlineData(true, null, null)]
    [InlineData(true, 6, null)]
    [InlineData(true, null, 25)]
    public async Task Missing_name_requires_both_coordinates(bool references, int? line, int? column)
    {
        var tools = new NavigationTools(SolutionManagerTestFactory.Create(), NullLogger<NavigationTools>.Instance);
        var result = references
            ? await tools.FindSymbolReferences(filePath: "Sample.cs", line: line, column: column)
            : await tools.FindSymbolDefinition(filePath: "Sample.cs", line: line, column: column);

        Assert.Contains("provide `symbolName`", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_coordinates_without_file_are_rejected(bool references)
    {
        var tools = new NavigationTools(SolutionManagerTestFactory.Create(), NullLogger<NavigationTools>.Instance);
        var result = references
            ? await tools.FindSymbolReferences(line: 1, column: 1)
            : await tools.FindSymbolDefinition(line: 1, column: 1);

        Assert.Contains("`filePath` with both `line` and `column`", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_coordinates_without_name_still_validate_position(bool references)
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = references
            ? await fixture.Tools.FindSymbolReferences(filePath: fixture.SourcePath, line: 999, column: 1)
            : await fixture.Tools.FindSymbolDefinition(filePath: fixture.SourcePath, line: 999, column: 1);

        Assert.Contains("past the end of the file", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("name", null)]
    [InlineData("name", false)]
    [InlineData("name", true)]
    [InlineData("file", true)]
    [InlineData("position", true)]
    public async Task Definition_preview_uses_each_partial_location_and_survives_overflow(string mode, bool? preview)
    {
        await using var fixture = await Fixture.CreateAsync();
        var (line, column) = fixture.Position("Widget");
        var result = await fixture.Tools.FindSymbolDefinition(
            symbolName: mode == "position" ? null : "Widget",
            filePath: mode == "name" ? null : fixture.SourcePath,
            line: mode == "position" ? line : null,
            column: mode == "position" ? column : null,
            maxResults: 1,
            preview: preview);

        Assert.Contains("Full name: Demo.Widget", result, StringComparison.Ordinal);
        Assert.Equal(preview == true, result.Contains("Source:", StringComparison.Ordinal));
        if (preview == true)
        {
            Assert.Contains("public partial class Widget", result, StringComparison.Ordinal);
        }

        var cursor = result.Split('`').Single(part => Guid.TryParseExact(part, "N", out _));
        var tail = await fixture.Tools.FindSymbolDefinition(overflowCursor: cursor);
        Assert.Contains("Full name: Demo.Widget", tail, StringComparison.Ordinal);
        Assert.Equal(preview == true, tail.Contains("Source:", StringComparison.Ordinal));
        if (preview == true)
        {
            Assert.Contains("public partial class Widget { public int Count; }", result + tail, StringComparison.Ordinal);
            Assert.Contains("Source: `public partial class Widget`", result + tail, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Definition_preview_truncates_and_escapes_source_text()
    {
        var tree = CSharpSyntaxTree.ParseText("class Widget {}", path: "Sample.cs");
        var compilation = CSharpCompilation.Create("Preview", new[] { tree });
        var symbol = compilation.GetTypeByMetadataName("Widget");
        Assert.NotNull(symbol);

        var result = DefinitionLocationFormatter.FormatLocation(symbol, symbol.Locations.Single(), "`" + new string('x', 500));

        Assert.Contains("Source: `'" + new string('x', 399) + "…`", result, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', 400), result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Definition_preview_inherits_config_and_explicit_false_wins()
    {
        await using var fixture = await Fixture.CreateAsync(configPreview: true);

        var configured = await fixture.Tools.FindSymbolDefinition("Run");
        var disabled = await fixture.Tools.FindSymbolDefinition("Run", preview: false);

        Assert.Contains("Source:", configured, StringComparison.Ordinal);
        Assert.DoesNotContain("Source:", disabled, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Implementations_count_shared_class_once_and_keep_both_relations_in_overflow()
    {
        await using var fixture = await Fixture.CreateAsync(additionalSource: """
            namespace Demo;

            public interface IProvider { }
            public interface IProvider<out T> : IProvider { }
            public class Provider : IProvider<string> { }
            """);

        var firstPage = await fixture.Tools.FindImplementations("IProvider", maxResults: 3);
        Assert.Contains("Found **2** matching base type(s), **1** unique related type(s).", firstPage, StringComparison.Ordinal);
        Assert.Contains("### `Demo.IProvider` (interface)", firstPage, StringComparison.Ordinal);
        Assert.Contains("### `Demo.IProvider<T>` (interface)", firstPage, StringComparison.Ordinal);
        Assert.Contains("Showing 3 of 4 line(s)", firstPage, StringComparison.Ordinal);
        Assert.Contains("1. `Provider`", firstPage, StringComparison.Ordinal);

        var cursor = firstPage.Split('`').Single(part => Guid.TryParseExact(part, "N", out _));
        var tail = await fixture.Tools.FindImplementations("IProvider", overflowCursor: cursor);
        Assert.Contains("1. `Provider`", tail, StringComparison.Ordinal);

        var direct = await fixture.Tools.FindImplementations("Demo.IProvider", transitive: false);
        Assert.Contains("**Base symbol:** `IProvider` (interface)", direct, StringComparison.Ordinal);
        Assert.DoesNotContain("1. `Provider`", direct, StringComparison.Ordinal);
        Assert.DoesNotContain("matching base type(s)", direct, StringComparison.Ordinal);

        var generic = await fixture.Tools.FindImplementations("Demo.IProvider<T>", transitive: false);
        Assert.Contains("`global::Demo.IProvider<T>`", generic, StringComparison.Ordinal);
        Assert.Contains("1. `Provider`", generic, StringComparison.Ordinal);
        Assert.DoesNotContain("matching base type(s)", generic, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Implementations_qualified_names_round_trip_for_nested_and_global_bases()
    {
        await using var fixture = await Fixture.CreateAsync(additionalSource: """
            public interface IItem { }
            public interface IItem<T> { }
            public class GlobalItem : IItem { }
            public class GlobalGenericItem : IItem<string> { }

            namespace Demo
            {
                public class Box<TOuter>
                {
                    public interface IItem { }
                    public interface IItem<TFirst, TSecond> { }
                }

                public class Alternate<T>
                {
                    public interface IItem { }
                }

                public class Alternate<T, U>
                {
                    public interface IItem<TItem> { }
                }

                public class NonGenericNestedItem : Box<string>.IItem { }
                public class NestedItem : Box<string>.IItem<int, long> { }
                public interface @event<T> { }
                public class EventItem : @event<string> { }
            }
            """);

        var shortName = await fixture.Tools.FindImplementations("IItem");
        Assert.Contains("### `global::IItem`", shortName, StringComparison.Ordinal);
        Assert.Contains("### `global::IItem<T>`", shortName, StringComparison.Ordinal);
        Assert.Contains("### `Demo.Box<TOuter>.IItem`", shortName, StringComparison.Ordinal);
        Assert.Contains("### `Demo.Box<TOuter>.IItem<TFirst, TSecond>`", shortName, StringComparison.Ordinal);

        var global = await fixture.Tools.FindImplementations("global::IItem");
        Assert.Contains("1. `GlobalItem`", global, StringComparison.Ordinal);
        Assert.DoesNotContain("GlobalGenericItem", global, StringComparison.Ordinal);

        var globalGeneric = await fixture.Tools.FindImplementations("global::IItem<T>");
        Assert.Contains("1. `GlobalGenericItem`", globalGeneric, StringComparison.Ordinal);

        var nested = await fixture.Tools.FindImplementations("Demo.Box<TOuter>.IItem<TFirst, TSecond>");
        Assert.Contains("1. `NestedItem`", nested, StringComparison.Ordinal);
        Assert.DoesNotContain("matching base type(s)", nested, StringComparison.Ordinal);

        var nonGenericNested = await fixture.Tools.FindImplementations("Demo.Box<TOuter>.IItem");
        Assert.Contains("1. `NonGenericNestedItem`", nonGenericNested, StringComparison.Ordinal);

        var legacy = await fixture.Tools.FindImplementations("Demo.Box.IItem");
        Assert.Contains("1. `NonGenericNestedItem`", legacy, StringComparison.Ordinal);
        Assert.DoesNotContain("1. `NestedItem`", legacy, StringComparison.Ordinal);

        var ambiguousContainer = await fixture.Tools.FindImplementations("Demo.Alternate.IItem");
        Assert.Contains("No declaration matches FQN `Demo.Alternate.IItem`.", ambiguousContainer, StringComparison.Ordinal);
        Assert.Contains("  - `Demo.Alternate<T>.IItem`", ambiguousContainer, StringComparison.Ordinal);
        Assert.Contains("  - `Demo.Alternate<T, U>.IItem<TItem>`", ambiguousContainer, StringComparison.Ordinal);

        var escaped = await fixture.Tools.FindImplementations("Demo.@event<T>");
        Assert.Contains("1. `EventItem`", escaped, StringComparison.Ordinal);

        var legacyEscaped = await fixture.Tools.FindImplementations("Demo.event");
        Assert.Contains("1. `EventItem`", legacyEscaped, StringComparison.Ordinal);

        var miss = await fixture.Tools.FindImplementations("Demo.IItem<T>");
        Assert.Contains("No declaration matches FQN `Demo.IItem<T>`.", miss, StringComparison.Ordinal);
        Assert.Contains("  - `global::IItem<T>`", miss, StringComparison.Ordinal);
        Assert.Contains("  - `Demo.Box<TOuter>.IItem<TFirst, TSecond>`", miss, StringComparison.Ordinal);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public string SourcePath => Path.Combine(_root, "Sample.cs");

        public NavigationTools Tools { get; }

        private Fixture(string root, SolutionManager manager)
        {
            _root = root;
            _manager = manager;
            Tools = new NavigationTools(manager, NullLogger<NavigationTools>.Instance);
        }

        public static async Task<Fixture> CreateAsync(bool configPreview = false, string? additionalSource = null)
        {
            MsBuildBootstrapper.Register();
            var root = Path.Combine(Path.GetTempPath(), "NavigationTools-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var projectPath = Path.Combine(root, "App.csproj");
            File.WriteAllText(projectPath, """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>""");
            File.WriteAllText(Path.Combine(root, "Sample.cs"), Source);
            File.WriteAllText(Path.Combine(root, "Other.cs"), "namespace Demo; public partial class Widget { public int Count; }");
            if (additionalSource is not null)
            {
                File.WriteAllText(Path.Combine(root, "Implementations.cs"), additionalSource);
            }
            File.WriteAllText(Path.Combine(root, RoslynMcpFileSettings.FileName), configPreview ? "{\"preview\":true}" : "{}");
            var settings = RoslynMcpFileSettings.LoadFromDirectories(null, root);
            var manager = SolutionManagerTestFactory.Create(settings);
            var fixture = new Fixture(root, manager);
            try
            {
                var load = await manager.LoadAndPrepareAsync(projectPath, false, CancellationToken.None);
                Assert.NotNull(load.Solution);
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        public (int Line, int Column) Position(string needle)
        {
            var offset = Source.IndexOf(needle, StringComparison.Ordinal);
            Assert.True(offset >= 0);
            var position = SourceText.From(Source).Lines.GetLinePosition(offset);
            return (position.Line + 1, position.Character + 1);
        }

        public async ValueTask DisposeAsync()
        {
            await _manager.ClearWorkspaceAsync();
            Directory.Delete(_root, recursive: true);
        }

        private const string Source = """
            namespace Demo;
            public partial class Widget
            {
                public void Work(int value) { }
                public void Work(string value) { }
                public void Run() { Work(1); }
            }
            """;

        private readonly string _root;
        private readonly SolutionManager _manager;
    }
}
