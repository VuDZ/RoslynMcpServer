using RoslynMcpServer.Services.Testing.Il;
using RoslynMcpServer.Tests.SourceStructure;
using Xunit;

namespace RoslynMcpServer.Tests.Testing.Il;

/// <summary>
/// The production verdict of <see cref="IlFileAccessChecks"/> and its counterexample corpus: a loader that
/// opens the file once through its open options passes, and every other way to open a file in the checked
/// directory is a violation.
/// </summary>
public sealed class IlFileAccessChecksTests
{
    private const string CheckedDirectory = "Services/Testing/Il/";
    private const string LoaderOpen = "using var stream = new FileStream(path, CreateOpenOptions());";
    private const string ReaderBody = "return DemoLoader.Load(path).Length;";

    private const string Loader = """
        using System.IO;

        namespace Demo.Il;

        internal static class DemoLoader
        {
            public static byte[] Load(string path)
            {
                using var stream = new FileStream(path, CreateOpenOptions());
                return ReadToEnd(stream);
            }

            internal static FileStreamOptions CreateOpenOptions() => new() { Share = FileShare.ReadWrite | FileShare.Delete };

            private static byte[] ReadToEnd(FileStream stream)
            {
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                return buffer.ToArray();
            }
        }
        """;

    private const string Reader = """
        using System.IO;

        namespace Demo.Il;

        internal static class DemoReader
        {
            // File.OpenRead(path) in a comment and "File.ReadAllBytes" in a string are not references.
            public static string Note => "File.ReadAllBytes";

            public static int Read(string path)
            {
                return DemoLoader.Load(path).Length;
            }
        }
        """;

    [Fact]
    public void Production_il_code_opens_files_only_in_the_image_loader_through_its_open_options()
    {
        var directory = Path.Combine(RepositoryRoot.FindProductionProjectDirectory(), "Services", "Testing", "Il") + Path.DirectorySeparatorChar;

        var violations = IlFileAccessChecks.Verify(
            ProductionAnalysis.Instance,
            path => path.StartsWith(directory, StringComparison.Ordinal),
            "RoslynMcpServer.Services.Testing.Il",
            nameof(IlAssemblyImageLoader),
            nameof(IlAssemblyImageLoader.CreateOpenOptions));

        Assert.Empty(violations);
    }

    [Fact]
    public void Loader_that_opens_the_file_once_through_its_open_options_passes()
    {
        Assert.Empty(Verify(Loader, Reader));
    }

    [Theory]
    [InlineData("return (int)File.OpenRead(path).Length;")]
    [InlineData("return File.ReadAllBytes(path).Length;")]
    [InlineData("using var stream = File.Open(path, FileMode.Open); return (int)stream.Length;")]
    [InlineData("using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return (int)stream.Length;")]
    [InlineData("using var stream = new System.IO.FileStream(path, FileMode.Open); return (int)stream.Length;")]
    [InlineData("using FileStream stream = new(path, FileMode.Open); return (int)stream.Length;")]
    [InlineData("using var reader = new StreamReader(path); return reader.Read();")]
    [InlineData("return (int)new FileInfo(path).Length;")]
    public void File_opened_outside_the_loader_fails(string body)
    {
        var violations = Verify(Loader, Reader.Replace(ReaderBody, body, StringComparison.Ordinal));

        Assert.Contains(violations, violation => violation.Contains("is used outside DemoLoader", StringComparison.Ordinal));
    }

    [Fact]
    public void Static_member_of_a_file_type_used_without_its_type_name_fails()
    {
        var reader = Reader
            .Replace("using System.IO;", "using static System.IO.File;", StringComparison.Ordinal)
            .Replace(ReaderBody, "return ReadAllBytes(path).Length;", StringComparison.Ordinal);

        var violations = Verify(Loader, reader);

        Assert.Contains(violations, violation => violation.Contains("System.IO.File is used outside DemoLoader", StringComparison.Ordinal));
    }

    [Fact]
    public void Second_open_in_the_loader_fails()
    {
        var loader = Loader.Replace(LoaderOpen, LoaderOpen + " using var again = new FileStream(path, CreateOpenOptions());", StringComparison.Ordinal);

        var violations = Verify(loader, Reader);

        Assert.Contains(violations, violation => violation.Contains("expected one place that opens a file, found 2", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);")]
    [InlineData("using var stream = new FileStream(path, new FileStreamOptions { Share = FileShare.Read });")]
    [InlineData("using FileStream stream = new(path, FileMode.Open);")]
    public void Loader_that_does_not_open_the_file_with_its_open_options_fails(string open)
    {
        var violations = Verify(Loader.Replace(LoaderOpen, open, StringComparison.Ordinal), Reader);

        Assert.Contains(violations, violation => violation.Contains("the file is not opened with CreateOpenOptions()", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("using var stream = File.OpenRead(path);")]
    [InlineData("var bytes = File.ReadAllBytes(path); using var stream = new FileStream(path, CreateOpenOptions());")]
    public void Loader_that_uses_another_file_api_fails(string open)
    {
        var violations = Verify(Loader.Replace(LoaderOpen, open, StringComparison.Ordinal), Reader);

        Assert.Contains(violations, violation => violation.Contains("DemoLoader opens files only with a FileStream built from CreateOpenOptions()", StringComparison.Ordinal));
    }

    [Fact]
    public void Same_named_type_of_another_namespace_is_not_a_file_api()
    {
        var reader = Reader
            .Replace("using System.IO;", "using Demo.Other;", StringComparison.Ordinal)
            .Replace(ReaderBody, "return File.OpenRead(path);", StringComparison.Ordinal);
        const string other = "namespace Demo.Other; internal static class File { public static int OpenRead(string path) => path.Length; }";

        Assert.Empty(Verify(Loader, reader, SourceFile.Create(CheckedDirectory + "Other.cs", other)));
    }

    [Fact]
    public void File_outside_the_checked_directory_is_not_judged()
    {
        const string elsewhere = "namespace Demo.Elsewhere; internal static class Elsewhere { public static int Read(string path) => System.IO.File.ReadAllBytes(path).Length; }";

        Assert.Empty(Verify(Loader, Reader, SourceFile.Create("Services/Elsewhere/Elsewhere.cs", elsewhere)));
    }

    [Fact]
    public void Source_that_does_not_compile_is_not_a_pass()
    {
        var violations = Verify(Loader, Reader.Replace(ReaderBody, "return FileStream.Missing(path);", StringComparison.Ordinal));

        Assert.NotEmpty(violations);
    }

    [Fact]
    public void Missing_loader_and_empty_directory_are_not_a_pass()
    {
        const string lonely = "namespace Demo.Il; internal static class Lonely { }";
        var analysis = SourceSetAnalysis.Create(SourceSetScope.Demo("IL file access demo", [SourceFile.Create("Other/Lonely.cs", lonely)]));

        var violations = IlFileAccessChecks.Verify(analysis, InDirectory, "Demo.Il", "DemoLoader", "CreateOpenOptions");

        Assert.NotEmpty(violations);
    }

    private static IReadOnlyList<string> Verify(string loader, string reader, params SourceFile[] more)
    {
        SourceFile[] files = [SourceFile.Create(CheckedDirectory + "DemoLoader.cs", loader), SourceFile.Create(CheckedDirectory + "DemoReader.cs", reader), .. more];
        var analysis = SourceSetAnalysis.Create(SourceSetScope.Demo("IL file access demo", files));
        return IlFileAccessChecks.Verify(analysis, InDirectory, "Demo.Il", "DemoLoader", "CreateOpenOptions");
    }

    private static bool InDirectory(string path) => path.StartsWith(CheckedDirectory, StringComparison.Ordinal);
}
