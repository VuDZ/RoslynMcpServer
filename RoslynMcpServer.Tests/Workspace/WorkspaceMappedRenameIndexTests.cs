using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

public sealed class WorkspaceMappedRenameIndexTests
{
    [Fact]
    public void Index_matches_the_original_path_check_for_both_rename_endpoints()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var parent = Path.Combine(root, "RenameRoot", "Known");
        var file = Path.Combine(parent, "Input.cs");
        var unrelated = Path.Combine(root, "Unknown", "Endpoint.cs");
        var separator = Path.DirectorySeparatorChar;
        AssertMatchesScan(file, file, file + separator, parent, parent + separator + separator, root,
            parent.ToUpperInvariant(), parent + "Sibling", unrelated, null, " ", "\0");
        AssertMatchesScan(file + separator, file, file + separator, parent, unrelated);
        AssertMatchesScan(root, root, root + separator, parent, unrelated);

        if (OperatingSystem.IsWindows())
        {
            AssertMatchesScan(file, parent.Replace('\\', '/'), parent.Replace('\\', '/') + "//");
            AssertMatchesScan(@"\\server\share\Known\Input.cs", @"\\SERVER\SHARE", @"\\server\share\",
                @"\\server", @"//server/share/known/", @"\\server\share\KnownSibling", @"\\server\other");
            AssertMatchesScan(@"\\?\C:\Known/A\Input.cs", @"\\?\C:\Known/A", @"\\?\C:\Known\A",
                @"\\?\C:\Known/A//", @"\\?\C:\");
            AssertMatchesScan(@"\\?\C:\Known\\", @"\\?\C:\Known", @"\\?\C:\Known/", @"\\?\C:\KnownSibling");
        }

        Assert.False(WorkspaceDiskEventClassifier.HasMappedRenamePath(file, parent, map: null));
    }

    [Fact]
    public void An_exact_path_with_a_trailing_separator_does_not_supply_a_strict_descendant()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RenameRoot");
        var map = Map(directory + Path.DirectorySeparatorChar);

        Assert.True(map.HasMappedRenameEndpoint(directory));
        Assert.False(map.HasMappedDescendants(directory));
        Assert.True(map.HasMappedDescendants(Path.GetDirectoryName(directory)));
    }

    private static void AssertMatchesScan(string path, params string?[] endpoints)
    {
        var map = Map(path);
        foreach (var endpoint in endpoints)
        {
            var canonical = InputPathCanon.TryCanonicalize(endpoint);
            var oldMatch = canonical is not null && (map.Find(canonical) is not null || map.Paths.Any(entry =>
                WorkspaceDiskPathFilter.IsPathUnderDirectory(entry.CanonicalPath, canonical, InputPathCanon.Comparison)));

            Assert.Equal(oldMatch, WorkspaceDiskEventClassifier.HasMappedRenamePath(endpoint, null, map));
            Assert.Equal(oldMatch, WorkspaceDiskEventClassifier.HasMappedRenamePath(null, endpoint, map));
        }
    }

    private static WorkspaceInputMap Map(string path) =>
        new(Guid.NewGuid(), loadGraphComplete: true, InputCoverage.Unknown, InputCoverage.Unknown, [], [], [],
            [new InputPathEntry(Path.GetFullPath(path), [])], [], [], [], []);
}
