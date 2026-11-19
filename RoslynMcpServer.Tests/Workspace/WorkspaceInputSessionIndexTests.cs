using Microsoft.CodeAnalysis;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

public sealed class WorkspaceInputSessionIndexTests
{
    [Fact]
    public void Reobserving_a_path_updates_the_notice_in_its_original_position()
    {
        var session = new WorkspaceInputSession([]);
        var path = Path.Combine(Path.GetTempPath(), "first.cs");
        var other = Path.Combine(Path.GetTempPath(), "second.cs");
        var owner = ProjectId.CreateNewId();
        var secondOwner = ProjectId.CreateNewId();
        var first = new InputPathNotice(path, InputRole.UserInput, owner, false);
        var interleaved = new InputPathNotice(other, InputRole.Unknown, null, false);
        var additional = new InputPathNotice(path, InputRole.AdditionalFile, secondOwner, false);
        var alias = OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
        var existing = first with { CanonicalPath = alias, CountsAsInputRevision = true };

        Record(session, first);
        Record(session, interleaved);
        Record(session, additional);
        Record(session, existing);

        Assert.Equal(new[] { existing, interleaved, additional }, session.Notices);
        var snapshot = Assert.Single(session.Pull().Paths, entry => entry.CanonicalPath == path);
        Assert.Equal(new[] { owner, secondOwner }, snapshot.Owners);
        Assert.Equal(new[] { InputRole.UserInput, InputRole.AdditionalFile }, snapshot.Roles);
        Assert.Equal(1, snapshot.InputRevision);
    }

    [Fact]
    public void Gaps_preserve_removal_reinsertion_and_existing_scope_equivalence()
    {
        var session = new WorkspaceInputSession([]);
        var first = Path.Combine(Path.GetTempPath(), "First.cs");
        var alias = OperatingSystem.IsWindows() ? first.ToUpperInvariant() : first;
        var second = Path.Combine(Path.GetTempPath(), "Second.cs");
        var original = new InputCoverageScope(false, [first, second], [ProjectId.CreateNewId()]);
        var same = new InputCoverageScope(false, [alias, second], [ProjectId.CreateNewId()]);
        var reversed = new InputCoverageScope(false, [second, first], []);
        var withGap = Map(session, ["map reason"]);
        var withoutGap = Map(session, []);

        Assert.True(session.TryPublishInputMap(withGap));
        session.NoteCoverageUnknown("runtime reason", original);
        session.NoteCoverageUnknown("runtime reason", same);
        session.NoteCoverageUnknown("runtime reason", reversed);
        session.NoteCoverageUnknown(InputCoverageReason.DirectoryRenameInsideMembership);
        session.NoteCoverageUnknown(InputCoverageReason.DirectoryRenameInsideMembership,
            new InputCoverageScope(true, [first], [ProjectId.CreateNewId()]));
        Assert.Equal(new[] { "map reason", "runtime reason", InputCoverageReason.DirectoryRenameInsideMembership },
            session.ObservationUnknownReasons);
        Assert.Equal(4, session.Pull().Gaps.Count);

        Assert.True(session.TryAcceptMembershipEvaluation(withoutGap, subscriptionsConfirmed: true));
        Assert.True(session.TryPublishInputMap(withGap));
        session.NoteCoverageUnknown(InputCoverageReason.DirectoryRenameInsideMembership);
        var gaps = session.Pull().Gaps;
        Assert.Equal(new[] { "runtime reason", "map reason", InputCoverageReason.DirectoryRenameInsideMembership },
            session.ObservationUnknownReasons);
        Assert.Equal(4, gaps.Count);
        Assert.Same(original, gaps[0].Scope);
        Assert.Same(reversed, gaps[1].Scope);

        session.NoteCoverageUnknown("map reason");
        Assert.Equal(2, session.Pull().Gaps.Count(gap => gap.Reason == "map reason"));
        Assert.True(session.TryPublishInputMap(withoutGap));
        Assert.Single(session.Pull().Gaps, gap => gap.Reason == "map reason");
    }

    private static void Record(WorkspaceInputSession session, InputPathNotice notice) =>
        session.RecordDiskEvent(new DiskEventDecision(notice.CanonicalPath, provenIrrelevant: false,
            dirtyUserSource: false, compositionStale: false, graphFile: false, [notice]));

    private static WorkspaceInputMap Map(WorkspaceInputSession session, string[] reasons) =>
        new(session.Generation, loadGraphComplete: true, InputCoverage.Complete, InputCoverage.Complete,
            reasons, [], [], [], [], [], [], []);
}
