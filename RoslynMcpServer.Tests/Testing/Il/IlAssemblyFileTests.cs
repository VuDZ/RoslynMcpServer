using RoslynMcpServer.Services.Testing.Il;
using RoslynMcpServer.Services.Testing.Il.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing.Il;

/// <summary>How an assembly file is opened and read, and what a file that cannot be read turns into.</summary>
[Collection(IlShopCollection.Name)]
public sealed class IlAssemblyFileTests : IDisposable
{
    public IlAssemblyFileTests(IlShopFixture shop)
    {
        _shop = shop;
        _directory = Path.Combine(Path.GetTempPath(), "RoslynMcpIlFiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    /// <summary>
    /// A build of the agent may write an output assembly while it is being read. On Windows a reader that
    /// opens the file without sharing write and delete access makes that build fail, so this case has to
    /// run there; elsewhere it still checks that the pause is inside the window in which the file is open.
    /// </summary>
    [Fact]
    public async Task Another_thread_overwrites_and_deletes_the_file_while_the_read_is_stopped_inside_it()
    {
        var path = CopyOf(_shop.LibPath, "Held.dll");
        var replacement = File.ReadAllBytes(_shop.PluginsPath);
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var reading = Task.Run(() => IlAssemblyImageLoader.Load(
            path,
            whileFileIsOpenForTests: () =>
            {
                opened.SetResult();
                Assert.True(release.Wait(_timeout), "The test did not release the stopped read.");
            }));

        // A read that ends before it stops has failed or skipped the stop: awaiting it reports which.
        var first = await Task.WhenAny(reading, opened.Task).WaitAsync(_timeout);
        if (first != opened.Task)
        {
            var ended = await reading;
            Assert.Fail($"The read ended without stopping where the file is open: {ended.Failure?.Message ?? "it returned an image"}.");
        }

        // Exclusive access is refused only while somebody holds the file: this proves that the reader is
        // stopped with the file open and the writes below happen inside the read.
        var exclusive = Record.Exception(() => OpenExclusively(path));
        var overwrite = Record.Exception(() => File.WriteAllBytes(path, replacement));
        var delete = Record.Exception(() => File.Delete(path));
        release.Set();
        var load = await reading;

        Assert.IsAssignableFrom<IOException>(exclusive);
        Assert.Null(overwrite);
        Assert.Null(delete);
        Assert.False(File.Exists(path));

        // What the stopped reader gets is the business of its caller, which compares the file before and
        // after the read; here it only has to end with a result.
        Assert.True((load.Image is null) != (load.Failure is null));
    }

    /// <summary>
    /// The sharing mode itself, without the operating system: on Linux the case above passes with any mode.
    /// <see cref="IlFileAccessChecksTests"/> proves that the loader opens the file with these options and
    /// that nothing else in the IL graph code opens a file.
    /// </summary>
    [Fact]
    public void File_is_opened_for_reading_with_write_and_delete_shared()
    {
        var options = IlAssemblyImageLoader.CreateOpenOptions();

        Assert.Equal(FileShare.ReadWrite | FileShare.Delete, options.Share);
        Assert.Equal(FileAccess.Read, options.Access);
        Assert.Equal(FileMode.Open, options.Mode);
    }

    [Fact]
    public void File_is_closed_when_the_image_is_returned()
    {
        var path = CopyOf(_shop.LibPath, "Closed.dll");

        var load = IlAssemblyImageLoader.Load(path);
        var afterLoad = Record.Exception(() => OpenExclusively(path));
        var read = IlAssemblyReader.ReadFile(path);
        var afterRead = Record.Exception(() => OpenExclusively(path));
        var result = IlDependencyGraphBuilder.BuildFromFiles([new IlAssemblyInput(path, IsTestProject: false)], IlNeighborMap.Empty);
        var afterBuild = Record.Exception(() => OpenExclusively(path));

        Assert.NotNull(load.Image);
        Assert.NotNull(read.Facts);
        Assert.Empty(result.Unread);
        Assert.Null(afterLoad);
        Assert.Null(afterRead);
        Assert.Null(afterBuild);
    }

    [Fact]
    public void Image_holds_the_whole_file_and_is_parsed_without_the_file()
    {
        // The same buffer serves the graph and whatever else the caller computes from the content, so the
        // file is opened once: after the load nothing needs it anymore.
        var path = CopyOf(_shop.LibPath, "Buffered.dll");
        var expected = File.ReadAllBytes(path);

        var load = IlAssemblyImageLoader.Load(path);
        File.Delete(path);
        var read = IlAssemblyReader.Read(load.Image!);

        Assert.Null(load.Failure);
        Assert.Equal(path, load.Image!.Path);
        Assert.Equal(expected, load.Image.Content.ToArray());
        Assert.Null(read.Failure);
        Assert.Equal(path, read.Path);
        Assert.Equal(IlShopFixture.LibAssembly, read.Facts!.Name);
        Assert.Equal(Describe(IlAssemblyReader.ReadFile(_shop.LibPath)), Describe(read));
    }

    [Fact]
    public void Missing_and_damaged_assemblies_are_named_with_the_reason_and_the_graph_is_built_from_the_rest()
    {
        var missingFile = Path.Combine(_directory, "Absent.dll");
        var missingDirectory = Path.Combine(_directory, "absent", "Absent.dll");
        var text = Path.Combine(_directory, "Text.dll");
        File.WriteAllText(text, "This is not a portable executable.");
        var empty = Path.Combine(_directory, "Empty.dll");
        File.WriteAllBytes(empty, []);
        var cutShort = Path.Combine(_directory, "CutShort.dll");

        // What a reader sees of a file that is being written: valid headers and no metadata behind them.
        File.WriteAllBytes(cutShort, File.ReadAllBytes(_shop.LibPath)[..1024]);
        var directory = Path.Combine(_directory, "Directory.dll");
        Directory.CreateDirectory(directory);
        IlAssemblyInput[] inputs =
        [
            new(missingFile, IsTestProject: false),
            new(missingDirectory, IsTestProject: true),
            new(text, IsTestProject: false),
            new(_shop.PluginsPath, IsTestProject: false),
            new(empty, IsTestProject: false),
            new(cutShort, IsTestProject: true),
            new(directory, IsTestProject: false),
        ];

        var result = IlDependencyGraphBuilder.BuildFromFiles(inputs, IlNeighborMapBuilder.Build(inputs));

        Assert.Equal(
            new Dictionary<string, IlAssemblyFailureKind>
            {
                [missingFile] = IlAssemblyFailureKind.Missing,
                [missingDirectory] = IlAssemblyFailureKind.Missing,
                [text] = IlAssemblyFailureKind.NotAnAssembly,
                [empty] = IlAssemblyFailureKind.NotAnAssembly,
                [cutShort] = IlAssemblyFailureKind.NotAnAssembly,
                [directory] = IlAssemblyFailureKind.Inaccessible,
            },
            result.Unread.ToDictionary(unread => unread.Path, unread => unread.Reason));
        Assert.All(result.Unread, unread => Assert.False(string.IsNullOrWhiteSpace(unread.Message)));
        Assert.Equal([IlShopFixture.PluginsAssembly], result.Graph.Assemblies);
        Assert.Contains(new IlTypeName(IlShopFixture.PluginsAssembly, "Shop.Plugins.ReportPlugin"), result.Graph.Types);
    }

    [Fact]
    public void Read_of_a_missing_or_damaged_file_returns_the_failure_instead_of_throwing()
    {
        var missing = Path.Combine(_directory, "Nowhere.dll");
        var damaged = Path.Combine(_directory, "Damaged.dll");
        File.WriteAllBytes(damaged, [0x4D, 0x5A, 1, 2, 3]);

        var missingLoad = IlAssemblyImageLoader.Load(missing);
        var missingRead = IlAssemblyReader.ReadFile(missing);
        var damagedLoad = IlAssemblyImageLoader.Load(damaged);
        var damagedRead = IlAssemblyReader.Read(damagedLoad.Image!);

        Assert.Null(missingLoad.Image);
        Assert.Equal(new { Path = missing, Reason = IlAssemblyFailureKind.Missing }, new { missingLoad.Failure!.Path, missingLoad.Failure.Reason });
        Assert.Null(missingRead.Facts);
        Assert.Equal(missing, missingRead.Path);
        Assert.Equal(IlAssemblyFailureKind.Missing, missingRead.Failure!.Reason);
        Assert.Null(damagedLoad.Failure);
        Assert.Null(damagedRead.Facts);
        Assert.Equal(new { Path = damaged, Reason = IlAssemblyFailureKind.NotAnAssembly }, new { damagedRead.Failure!.Path, damagedRead.Failure.Reason });
    }

    [Fact]
    public void Empty_output_path_is_an_unread_assembly_and_not_an_exception()
    {
        IlAssemblyInput[] inputs = [new(string.Empty, IsTestProject: true), new(_shop.PluginsPath, IsTestProject: false)];

        var result = IlDependencyGraphBuilder.BuildFromFiles(inputs, IlNeighborMapBuilder.Build(inputs));

        var unread = Assert.Single(result.Unread);
        Assert.Equal(string.Empty, unread.Path);
        Assert.Equal(IlAssemblyFailureKind.Inaccessible, unread.Reason);
        Assert.Equal([IlShopFixture.PluginsAssembly], result.Graph.Assemblies);
    }

    private string CopyOf(string source, string name)
    {
        var path = Path.Combine(_directory, name);
        File.Copy(source, path);
        return path;
    }

    private static void OpenExclusively(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
    }

    private static IReadOnlyList<string> Describe(IlAssemblyReadResult read) =>
        IlGraphDescription.Describe(IlDependencyGraphBuilder.Build([read], IlNeighborMap.Empty));

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    private readonly IlShopFixture _shop;
    private readonly string _directory;
}
