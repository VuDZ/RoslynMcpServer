using System.Reflection;
using RoslynMcpServer.Tests.Support;
using Xunit;

namespace RoslynMcpServer.Tests.PlatformSupport;

/// <summary>
/// Pins the tests that CI does not run outside Windows. Marking a failing test Windows-only is cheaper than
/// fixing it, and a silent mark would hide a Linux bug, so every entry is listed here with its reason.
/// </summary>
public sealed class WindowsOnlyTestInventoryTests
{
    [Fact]
    public void Windows_only_category_matches_the_reviewed_list()
    {
        Assert.Equal(
            _expected.Order(StringComparer.Ordinal).ToArray(),
            TestCategoryInventory.Names(TestCategories.RequiresWindows));
    }

    [Fact]
    public void Every_WPF_fact_is_in_the_windows_only_category()
    {
        var missing = TestCategoryInventory.TestMethods()
            .Where(method => method.GetCustomAttribute<WpfFactAttribute>() is not null
                && !TestCategoryInventory.HasCategory(method, TestCategories.RequiresWindows))
            .Select(TestCategoryInventory.Name)
            .ToArray();

        Assert.Empty(missing);
    }

    private static readonly string[] _expected =
    [
        // Process image and command line are read through Windows-only APIs.
        "HarnessSessionIdTests.Windows_reads_this_process_image_and_command_line",
        // The 64-bit host under Program Files exists only on Windows.
        "DotNetHostResolverTests.ResolveDotNetExecutable_on_64bit_windows_prefers_program_files_dotnet",
        // The regression creates a real NTFS junction with the Windows mklink command.
        "WorkspaceDiskWatcherStarterTests.Descriptor_across_a_junction_keeps_its_watcher_and_delivers_changes",
        // A real WPF build needs the Windows Desktop SDK.
        "WpfTemporaryProjectWatchingTests.Real_WPF_build_preserves_the_cached_graph_after_flush_and_repeated_loads",
        "WpfTemporaryProjectWatchingTests.Real_project_edit_delivered_during_WPF_build_still_invalidates_the_graph",
    ];
}
