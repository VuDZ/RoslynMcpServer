using System.Reflection;
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
        var actual = TestMethods()
            .Where(HasWindowsOnlyCategory)
            .Select(Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(_expected.Order(StringComparer.Ordinal).ToArray(), actual);
    }

    [Fact]
    public void Every_WPF_fact_is_in_the_windows_only_category()
    {
        var missing = TestMethods()
            .Where(method => method.GetCustomAttribute<WpfFactAttribute>() is not null && !HasWindowsOnlyCategory(method))
            .Select(Name)
            .ToArray();

        Assert.Empty(missing);
    }

    private static IEnumerable<MethodInfo> TestMethods() =>
        typeof(WindowsOnlyTestInventoryTests).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributes<FactAttribute>(inherit: true).Any());

    private static bool HasWindowsOnlyCategory(MethodInfo method) =>
        method.GetCustomAttributesData()
            .Concat(method.DeclaringType!.GetCustomAttributesData())
            .Any(IsWindowsOnlyCategory);

    // xUnit v2 TraitAttribute exposes its name and value only as constructor arguments.
    private static bool IsWindowsOnlyCategory(CustomAttributeData attribute) =>
        attribute.AttributeType == typeof(TraitAttribute)
        && attribute.ConstructorArguments is [{ Value: "Category" }, { Value: TestCategories.RequiresWindows }];

    private static string Name(MethodInfo method) => method.DeclaringType!.Name + "." + method.Name;

    private static readonly string[] _expected =
    [
        // Process image and command line are read through Windows-only APIs.
        "HarnessSessionIdTests.Windows_reads_this_process_image_and_command_line",
        // The 64-bit host under Program Files exists only on Windows.
        "DotNetHostResolverTests.ResolveDotNetExecutable_on_64bit_windows_prefers_program_files_dotnet",
        // A real WPF build needs the Windows Desktop SDK.
        "WpfTemporaryProjectWatchingTests.Real_WPF_build_preserves_the_cached_graph_after_flush_and_repeated_loads",
        "WpfTemporaryProjectWatchingTests.Real_project_edit_delivered_during_WPF_build_still_invalidates_the_graph",
    ];
}
