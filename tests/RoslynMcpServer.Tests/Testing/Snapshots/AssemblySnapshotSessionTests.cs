using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Services.Testing.Il.Models;
using RoslynMcpServer.Services.Testing.Snapshots;
using RoslynMcpServer.Services.Testing.Snapshots.Models;
using Xunit;

namespace RoslynMcpServer.Tests.Testing.Snapshots;

public sealed class AssemblySnapshotSessionTests
{
    [Fact]
    public async Task First_read_covers_every_output_assembly()
    {
        using var fixture = new AssemblySnapshotFixture();
        fixture.Write("Lib", AssemblySnapshotFixture.LibV1);
        fixture.Write("Other", "public class O { }");
        var session = fixture.Start(new AssemblySnapshotSession(), "Lib", "Other");

        await session.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);

        var before = session.PeekBefore()!;
        Assert.All(before.Entries, entry => Assert.Equal(AssemblySnapshotState.Read, entry.State));
        Assert.Equal(new[] { fixture.PathOf("Lib"), fixture.PathOf("Other") }, before.Entries.Select(entry => entry.Path));
        Assert.Contains(AssemblySnapshotFixture.Type("Lib", "A"), before.Find(fixture.PathOf("Lib"))!.Fingerprints!.Types.Keys);
        Assert.Equal("Other", before.Find(fixture.PathOf("Other"))!.Facts!.Name);
    }

    [Fact]
    public async Task Assembly_rewritten_after_publication_before_its_first_read_stays_unknown()
    {
        using var fixture = new AssemblySnapshotFixture();
        var lib = fixture.Write("Lib", AssemblySnapshotFixture.LibV1);
        fixture.Write("Other", "public class O { }");
        var session = new AssemblySnapshotSession();
        using var gate = new AssemblyReadGate(lib);
        session.BeforeFirstReadForTests = gate.WaitAsync;
        fixture.Start(session, "Lib", "Other");
        await gate.Entered.WaitAsync(AssemblySnapshotFixture.Timeout);

        // A build of the agent lands after publication and before the snapshot opened the file.
        fixture.Write("Lib", AssemblySnapshotFixture.LibV2, lastWriteTimeUtc: DateTime.UtcNow.AddSeconds(1));
        gate.Release();
        await session.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);

        Assert.Equal(AssemblySnapshotState.Unknown, session.PeekBefore()!.Find(lib)!.State);
        Assert.Equal(AssemblySnapshotState.Read, session.PeekBefore()!.Find(fixture.PathOf("Other"))!.State);
        var pair = (await session.GetPairAsync(fixture.Projects("Lib", "Other"), CancellationToken.None))!;
        Assert.Equal(AssemblySnapshotState.Read, pair.Current.Find(lib)!.State);
        Assert.Equal(AssemblySnapshotState.Unknown, pair.Before.Find(lib)!.State);
        Assert.Equal(AssemblySnapshotState.Unknown, session.PeekBefore()!.Find(lib)!.State);
    }

    [Fact]
    public async Task Assembly_rewritten_while_its_first_read_holds_the_file_stays_unknown_and_the_write_succeeds()
    {
        using var fixture = new AssemblySnapshotFixture();
        var lib = fixture.Write("Lib", AssemblySnapshotFixture.LibV1);
        var rewritten = AssemblySnapshotFixture.Compile("Lib", AssemblySnapshotFixture.LibV2 + "public class Extra { }");
        var session = new AssemblySnapshotSession();
        Exception? writeError = null;
        var writes = 0;
        session.Reader.WhileFileIsOpenForTests = path =>
        {
            if (path == lib && Interlocked.Increment(ref writes) == 1)
            {
                // The new file is still older than the publication: only the before/after comparison can catch it.
                writeError = Record.Exception(() =>
                    AssemblySnapshotFixture.WriteBytes(lib, rewritten, fixture.PublishedAtUtc.AddHours(-1)));
            }
        };
        fixture.Start(session, "Lib");
        await session.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);

        Assert.Null(writeError);
        Assert.Equal(rewritten, File.ReadAllBytes(lib));
        Assert.Equal(AssemblySnapshotState.Unknown, session.PeekBefore()!.Find(lib)!.State);
        var pair = (await session.GetPairAsync(fixture.Projects("Lib"), CancellationToken.None))!;
        var current = pair.Current.Find(lib)!;
        Assert.Equal(AssemblySnapshotState.Read, current.State);
        Assert.Contains(AssemblySnapshotFixture.Type("Lib", "Extra"), current.Fingerprints!.Types.Keys);
        Assert.Equal(AssemblySnapshotState.Unknown, pair.Before.Find(lib)!.State);
    }

    [Fact]
    public async Task Missing_assembly_stays_missing_before_and_is_read_in_the_current_snapshot_once_it_appears()
    {
        using var fixture = new AssemblySnapshotFixture();
        var session = fixture.Start(new AssemblySnapshotSession(), "Lib");
        await session.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);
        var lib = fixture.PathOf("Lib");
        Assert.Equal(AssemblySnapshotState.Missing, session.PeekBefore()!.Find(lib)!.State);

        fixture.Write("Lib", AssemblySnapshotFixture.LibV1, lastWriteTimeUtc: DateTime.UtcNow);
        var pair = (await session.GetPairAsync(fixture.Projects("Lib"), CancellationToken.None))!;

        Assert.Equal(AssemblySnapshotState.Read, pair.Current.Find(lib)!.State);
        Assert.Equal(AssemblySnapshotState.Missing, pair.Before.Find(lib)!.State);
        Assert.Equal(IlAssemblyFailureKind.Missing, pair.Before.Find(lib)!.Result!.Failure!.Reason);
    }

    [Fact]
    public async Task Rebuild_after_the_first_read_keeps_the_before_entry_and_the_current_one_names_the_changed_type()
    {
        using var fixture = new AssemblySnapshotFixture();
        var lib = fixture.Write("Lib", AssemblySnapshotFixture.LibV1);
        var session = fixture.Start(new AssemblySnapshotSession(), "Lib");
        await session.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);
        var original = session.PeekBefore()!.Find(lib)!;
        var a = AssemblySnapshotFixture.Type("Lib", "A");

        fixture.Write("Lib", AssemblySnapshotFixture.LibV2, lastWriteTimeUtc: fixture.PublishedAtUtc.AddMinutes(1));
        var first = (await session.GetPairAsync(fixture.Projects("Lib"), CancellationToken.None))!;

        Assert.Same(original, first.Before.Find(lib));
        Assert.Equal(AssemblySnapshotState.Read, first.Before.Find(lib)!.State);
        var firstChanges = original.Fingerprints!.Compare(first.Current.Find(lib)!.Fingerprints!);
        Assert.Equal(new[] { a }, firstChanges.Changed);
        Assert.Empty(firstChanges.Added);
        Assert.Empty(firstChanges.Removed);

        fixture.Write("Lib", AssemblySnapshotFixture.LibV3, lastWriteTimeUtc: fixture.PublishedAtUtc.AddMinutes(2));
        var second = (await session.GetPairAsync(fixture.Projects("Lib"), CancellationToken.None))!;

        Assert.Same(original, second.Before.Find(lib));
        Assert.NotSame(first.Current.Find(lib), second.Current.Find(lib));
        Assert.Equal(new[] { a }, original.Fingerprints.Compare(second.Current.Find(lib)!.Fingerprints!).Changed);
        Assert.Equal(new[] { a }, first.Current.Find(lib)!.Fingerprints!.Compare(second.Current.Find(lib)!.Fingerprints!).Changed);
    }

    [Fact]
    public async Task Current_snapshot_rereads_only_the_rebuilt_assembly()
    {
        using var fixture = new AssemblySnapshotFixture();
        fixture.Write("Lib", AssemblySnapshotFixture.LibV1);
        var other = fixture.Write("Other", "public class O { }");
        fixture.Write("Third", "public class T { }");
        var session = fixture.Start(new AssemblySnapshotSession(), "Lib", "Other", "Third");
        await session.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);
        Assert.Equal(3, session.Reader.FileReads);
        var projects = fixture.Projects("Lib", "Other", "Third");

        var unchanged = (await session.GetPairAsync(projects, CancellationToken.None))!;
        Assert.Equal(3, session.Reader.FileReads);
        Assert.All(unchanged.Current.Entries, entry => Assert.Same(unchanged.Before.Find(entry.Path), entry));

        fixture.Write("Other", "public class O { public int X; }", lastWriteTimeUtc: DateTime.UtcNow);
        var pair = (await session.GetPairAsync(projects, CancellationToken.None))!;

        Assert.Equal(4, session.Reader.FileReads);
        Assert.NotSame(pair.Before.Find(other), pair.Current.Find(other));
        Assert.Same(pair.Before.Find(fixture.PathOf("Lib")), pair.Current.Find(fixture.PathOf("Lib")));
        Assert.Same(pair.Before.Find(fixture.PathOf("Third")), pair.Current.Find(fixture.PathOf("Third")));
    }

    [Fact]
    public async Task Current_snapshot_retries_a_temporarily_locked_assembly_without_another_build()
    {
        using var fixture = new AssemblySnapshotFixture();
        var lib = fixture.Write("Lib", AssemblySnapshotFixture.LibV1);
        var session = fixture.Start(new AssemblySnapshotSession(), "Lib");
        await session.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);
        var original = session.PeekBefore()!.Find(lib)!;
        var projects = fixture.Projects("Lib");
        fixture.Write("Lib", AssemblySnapshotFixture.LibV2, lastWriteTimeUtc: fixture.PublishedAtUtc.AddMinutes(1));
        var stamp = File.GetLastWriteTimeUtc(lib);
        using (File.Open(lib, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var blocked = (await session.GetPairAsync(projects, CancellationToken.None))!.Current.Find(lib)!;
            Assert.Equal(AssemblySnapshotState.Unreadable, blocked.State);
            Assert.Equal(IlAssemblyFailureKind.Inaccessible, blocked.Result!.Failure!.Reason);
        }

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(lib));
        var recovered = (await session.GetPairAsync(projects, CancellationToken.None))!;
        var current = recovered.Current.Find(lib)!;
        Assert.Equal(AssemblySnapshotState.Read, current.State);
        Assert.Same(original, recovered.Before.Find(lib));
        Assert.Equal(new[] { AssemblySnapshotFixture.Type("Lib", "A") }, original.Fingerprints!.Compare(current.Fingerprints!).Changed);
    }

    [Fact]
    public async Task Concurrent_requests_read_a_rebuilt_assembly_once()
    {
        using var fixture = new AssemblySnapshotFixture();
        var lib = fixture.Write("Lib", AssemblySnapshotFixture.LibV1);
        var session = fixture.Start(new AssemblySnapshotSession(), "Lib");
        await session.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);
        fixture.Write("Lib", AssemblySnapshotFixture.LibV2, lastWriteTimeUtc: DateTime.UtcNow);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        session.Reader.WhileFileIsOpenForTests = path =>
        {
            entered.Set();
            release.Wait(AssemblySnapshotFixture.Timeout);
        };

        var first = Task.Run(() => session.GetPairAsync(fixture.Projects("Lib"), CancellationToken.None));
        Assert.True(entered.Wait(AssemblySnapshotFixture.Timeout));
        var second = Task.Run(() => session.GetPairAsync(fixture.Projects("Lib"), CancellationToken.None));

        // The first request holds the gate inside its read; the second one has to queue behind it.
        await AssemblySnapshotFixture.WaitUntilAsync(() => session.RequestsAtGateForTests == 2, "the second request reaches the gate");
        release.Set();
        var pairs = await Task.WhenAll(first, second).WaitAsync(AssemblySnapshotFixture.Timeout);

        Assert.Equal(2, session.Reader.FileReads);
        Assert.Same(pairs[0]!.Current.Find(lib), pairs[1]!.Current.Find(lib));
        Assert.Equal(AssemblySnapshotState.Read, pairs[1]!.Current.Find(lib)!.State);
    }

    [Fact]
    public async Task Damaged_assembly_is_unreadable_with_a_reason_and_the_others_are_read()
    {
        using var fixture = new AssemblySnapshotFixture();
        var lib = fixture.PathOf("Lib");
        AssemblySnapshotFixture.WriteBytes(lib, [0x4D, 0x5A, 0x00, 0x01, 0x02], fixture.BeforePublication);
        fixture.Write("Other", "public class O { }");
        var session = fixture.Start(new AssemblySnapshotSession(), "Lib", "Other");
        await session.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);

        var pair = (await session.GetPairAsync(fixture.Projects("Lib", "Other"), CancellationToken.None))!;

        var damaged = pair.Before.Find(lib)!;
        Assert.Equal(AssemblySnapshotState.Unreadable, damaged.State);
        Assert.Equal(IlAssemblyFailureKind.NotAnAssembly, damaged.Result!.Failure!.Reason);
        Assert.False(string.IsNullOrWhiteSpace(damaged.Reason));
        Assert.Equal(AssemblySnapshotState.Read, pair.Before.Find(fixture.PathOf("Other"))!.State);
        Assert.Equal(AssemblySnapshotState.Unreadable, pair.Current.Find(lib)!.State);
        Assert.Contains(pair.CurrentGraph.Unread, unread => unread.Path == lib);
        Assert.Contains("Other", pair.CurrentGraph.Graph.Assemblies);
    }

    [Fact]
    public async Task Replaced_current_result_is_released_while_the_before_result_stays()
    {
        using var fixture = new AssemblySnapshotFixture();
        var lib = fixture.Write("Lib", AssemblySnapshotFixture.LibV1);
        var session = fixture.Start(new AssemblySnapshotSession(), "Lib");
        await session.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);
        var originalHashes = session.PeekBefore()!.Find(lib)!.Fingerprints!.Types.ToDictionary();
        var before = new WeakReference(session.PeekBefore()!.Find(lib));

        fixture.Write("Lib", AssemblySnapshotFixture.LibV2, lastWriteTimeUtc: fixture.PublishedAtUtc.AddMinutes(1));
        var firstCurrent = await CurrentEntryAsync(session, fixture, lib);
        fixture.Write("Lib", AssemblySnapshotFixture.LibV3, lastWriteTimeUtc: fixture.PublishedAtUtc.AddMinutes(2));
        _ = await CurrentEntryAsync(session, fixture, lib);
        Collect();

        Assert.False(firstCurrent.IsAlive, "The first current result must be released after the second rebuild.");
        Assert.True(before.IsAlive);
        Assert.Same(before.Target, session.PeekBefore()!.Find(lib));
        Assert.Equal(originalHashes, session.PeekBefore()!.Find(lib)!.Fingerprints!.Types.ToDictionary());
    }

    [Fact]
    public async Task File_images_are_not_kept_after_parsing()
    {
        using var fixture = new AssemblySnapshotFixture();
        fixture.Write("Lib", AssemblySnapshotFixture.LibV1);
        fixture.Write("Other", "public class O { }");
        var images = new List<WeakReference>();
        var session = new AssemblySnapshotSession();
        session.Reader.ImageLoadedForTests = image =>
        {
            lock (images)
            {
                images.Add(new WeakReference(image));
            }
        };
        fixture.Start(session, "Lib", "Other");
        await session.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);
        fixture.Write("Lib", AssemblySnapshotFixture.LibV2, lastWriteTimeUtc: DateTime.UtcNow);
        var pair = await session.GetPairAsync(fixture.Projects("Lib", "Other"), CancellationToken.None);
        Collect();

        Assert.NotNull(pair);
        Assert.Equal(3, images.Count);
        Assert.All(images, image => Assert.False(image.IsAlive, "A file image must not outlive its parsing."));
        GC.KeepAlive(pair);
    }

    [Fact]
    public async Task Interrupt_turns_unread_assemblies_unknown_keeps_read_ones_and_rejects_late_results()
    {
        using var fixture = new AssemblySnapshotFixture();
        var lib = fixture.Write("Lib", AssemblySnapshotFixture.LibV1);
        var other = fixture.Write("Other", "public class O { }");
        var session = new AssemblySnapshotSession();
        using var gate = new AssemblyReadGate(other);
        session.BeforeFirstReadForTests = gate.WaitAsync;
        fixture.Start(session, "Lib", "Other");
        await gate.Entered.WaitAsync(AssemblySnapshotFixture.Timeout);
        await AssemblySnapshotFixture.WaitUntilAsync(() => session.PeekBefore()!.Find(lib) is not null, "Lib is read");
        var read = session.PeekBefore()!.Find(lib);
        var interrupted = session.FirstReadTask;

        session.Interrupt().Cancel();

        Assert.Same(read, session.PeekBefore()!.Find(lib));
        Assert.Equal(AssemblySnapshotState.Unknown, session.PeekBefore()!.Find(other)!.State);
        Assert.False(interrupted.IsCompleted);
        gate.Release();
        await interrupted.WaitAsync(AssemblySnapshotFixture.Timeout);
        Assert.Equal(AssemblySnapshotState.Unknown, session.PeekBefore()!.Find(other)!.State);
        var pair = (await session.GetPairAsync(fixture.Projects("Lib", "Other"), CancellationToken.None))!;
        Assert.Equal(AssemblySnapshotState.Unknown, pair.Before.Find(other)!.State);
        Assert.Equal(AssemblySnapshotState.Read, pair.Current.Find(other)!.State);
        Assert.Same(read, pair.Current.Find(lib));
    }

    [Fact]
    public async Task Pair_waits_for_the_first_read_and_carries_the_publication_moment()
    {
        using var fixture = new AssemblySnapshotFixture();
        var lib = fixture.Write("Lib", AssemblySnapshotFixture.LibV1);
        var session = new AssemblySnapshotSession();
        using var gate = new AssemblyReadGate(lib);
        session.BeforeFirstReadForTests = gate.WaitAsync;
        fixture.Start(session, "Lib");
        await gate.Entered.WaitAsync(AssemblySnapshotFixture.Timeout);

        var request = session.GetPairAsync(fixture.Projects("Lib"), CancellationToken.None);
        await Task.Delay(100);
        Assert.False(request.IsCompleted);
        gate.Release();
        var pair = (await request.WaitAsync(AssemblySnapshotFixture.Timeout))!;

        Assert.Equal(fixture.PublishedAtUtc, pair.PublishedAtUtc);
        Assert.Equal(AssemblySnapshotState.Read, pair.Before.Find(lib)!.State);
        Assert.Contains("Lib", pair.BeforeGraph.Graph.Assemblies);
        Assert.Contains("Lib", pair.CurrentGraph.Graph.Assemblies);
    }

    [Fact]
    public async Task Interrupt_during_a_read_keeps_the_assembly_unknown_and_drops_the_late_result()
    {
        // The worker is already past every check made before the read: only the admission after it can refuse.
        using var fixture = new AssemblySnapshotFixture();
        var lib = fixture.Write("Lib", AssemblySnapshotFixture.LibV1);
        var session = new AssemblySnapshotSession();
        var interruptions = 0;
        session.Reader.ImageLoadedForTests = image =>
        {
            if (image.Path == lib && Interlocked.Increment(ref interruptions) == 1)
            {
                session.Interrupt().Cancel();
            }
        };
        fixture.Start(session, "Lib");
        await session.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);

        Assert.Equal(1, interruptions);
        var before = session.PeekBefore()!.Find(lib)!;
        Assert.Equal(AssemblySnapshotState.Unknown, before.State);
        Assert.Contains("interrupted", before.Reason, StringComparison.Ordinal);
        var pair = (await session.GetPairAsync(fixture.Projects("Lib"), CancellationToken.None))!;
        Assert.Equal(AssemblySnapshotState.Unknown, pair.Before.Find(lib)!.State);
        Assert.Equal(AssemblySnapshotState.Read, pair.Current.Find(lib)!.State);

        // The current snapshot read the file itself: the late result of the first read did not seed it.
        Assert.Equal(2, session.Reader.FileReads);
        Assert.Equal(AssemblySnapshotState.Unknown, session.PeekBefore()!.Find(lib)!.State);
    }

    [Fact]
    public async Task Another_solution_started_during_a_read_gets_no_entry_of_the_old_one()
    {
        using var fixture = new AssemblySnapshotFixture();
        var lib = fixture.Write("Lib", AssemblySnapshotFixture.LibV1);
        var other = fixture.Write("Other", "public class O { }");
        var session = new AssemblySnapshotSession();
        var starts = 0;
        session.Reader.WhileFileIsOpenForTests = path =>
        {
            if (path == lib && Interlocked.Increment(ref starts) == 1)
            {
                session.Start(Path.Combine(fixture.Root, "Second.slnx"), DateTime.UtcNow, fixture.Inventory("Other"), NullLogger.Instance)
                    .Cancel();
            }
        };
        using var gate = new AssemblyReadGate(lib);
        session.BeforeFirstReadForTests = gate.WaitAsync;
        fixture.Start(session, "Lib");
        await gate.Entered.WaitAsync(AssemblySnapshotFixture.Timeout);
        var oldWorker = session.FirstReadTask;
        gate.Release();
        await oldWorker.WaitAsync(AssemblySnapshotFixture.Timeout);
        await session.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);

        Assert.Equal(1, starts);
        Assert.Null(session.PeekBefore()!.Find(lib));
        Assert.Equal(AssemblySnapshotState.Read, session.PeekBefore()!.Find(other)!.State);
        var pair = (await session.GetPairAsync(fixture.Projects("Lib", "Other"), CancellationToken.None))!;
        Assert.Null(pair.Before.Find(lib));
        Assert.Equal(AssemblySnapshotState.Read, pair.Current.Find(lib)!.State);
        Assert.Same(pair.Before.Find(other), pair.Current.Find(other));

        // One read of Lib by the old worker, one of Other by the new one, one of Lib by the request.
        Assert.Equal(3, session.Reader.FileReads);
    }

    [Fact]
    public async Task Pair_request_stops_waiting_for_the_first_read_when_cancelled()
    {
        using var fixture = new AssemblySnapshotFixture();
        var lib = fixture.Write("Lib", AssemblySnapshotFixture.LibV1);
        var session = new AssemblySnapshotSession();
        using var gate = new AssemblyReadGate(lib);
        session.BeforeFirstReadForTests = gate.WaitAsync;
        fixture.Start(session, "Lib");
        await gate.Entered.WaitAsync(AssemblySnapshotFixture.Timeout);
        using var cancellation = new CancellationTokenSource();

        var request = session.GetPairAsync(fixture.Projects("Lib"), cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(AssemblySnapshotFixture.Timeout));
        Assert.Equal(0, session.RequestsAtGateForTests);
        gate.Release();
        var pair = await session.GetPairAsync(fixture.Projects("Lib"), CancellationToken.None).WaitAsync(AssemblySnapshotFixture.Timeout);
        Assert.Equal(AssemblySnapshotState.Read, pair!.Current.Find(lib)!.State);
    }

    [Fact]
    public async Task Request_paused_while_another_solution_starts_returns_a_pair_of_its_own_baseline()
    {
        // Post-acceptance review POST-F-01: the current read of a request is held while a new baseline starts.
        // The pair must not carry the new baseline's label and snapshot before the edit together with the
        // projects and current read of the request made for the old one.
        using var fixture = new AssemblySnapshotFixture();
        var lib = fixture.Write("Lib", AssemblySnapshotFixture.LibV1);
        var other = fixture.Write("Other", "public class O { }");
        var session = fixture.Start(new AssemblySnapshotSession(), "Lib");
        await session.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);
        fixture.Write("Lib", AssemblySnapshotFixture.LibV2, lastWriteTimeUtc: fixture.PublishedAtUtc.AddMinutes(1));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var pauses = 0;
        session.Reader.WhileFileIsOpenForTests = path =>
        {
            if (path == lib && Interlocked.Increment(ref pauses) == 1)
            {
                entered.Set();
                release.Wait(AssemblySnapshotFixture.Timeout);
            }
        };

        var request = Task.Run(() => session.GetPairAsync(fixture.Projects("Lib"), CancellationToken.None));
        Assert.True(entered.Wait(AssemblySnapshotFixture.Timeout));
        var secondPath = Path.Combine(fixture.Root, "Second.slnx");
        session.Start(secondPath, DateTime.UtcNow, fixture.Inventory("Other"), NullLogger.Instance).Cancel();
        await session.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);
        release.Set();
        var pair = (await request.WaitAsync(AssemblySnapshotFixture.Timeout))!;

        Assert.Equal(Path.Combine(fixture.Root, "Solution.slnx"), pair.WorkspacePath);
        Assert.Equal(fixture.PublishedAtUtc, pair.PublishedAtUtc);
        Assert.Equal(AssemblySnapshotState.Read, pair.Before.Find(lib)!.State);
        Assert.Null(pair.Before.Find(other));
        Assert.Equal(AssemblySnapshotState.Read, pair.Current.Find(lib)!.State);
        Assert.True(pair.BelongsTo(Path.Combine(fixture.Root, "Solution.slnx"), fixture.PublishedAtUtc));
        Assert.False(pair.BelongsTo(secondPath, fixture.PublishedAtUtc));

        // The request for the old baseline left nothing in the new one: its first request reads Lib itself.
        var readsBefore = session.Reader.FileReads;
        var next = (await session.GetPairAsync(fixture.Projects("Lib", "Other"), CancellationToken.None))!;
        Assert.Equal(Path.GetFullPath(secondPath), next.WorkspacePath);
        Assert.Null(next.Before.Find(lib));
        Assert.Equal(readsBefore + 1, session.Reader.FileReads);
    }

    [Fact]
    public async Task Current_entry_of_a_path_left_out_of_the_next_request_is_released()
    {
        // Post-acceptance review POST-F-02: a path that is neither before the edit nor in the latest current
        // snapshot must not stay in the cache until the next baseline.
        using var fixture = new AssemblySnapshotFixture();
        var lib = fixture.Write("Lib", AssemblySnapshotFixture.LibV1);
        var other = fixture.Write("Other", "public class O { }");
        var session = fixture.Start(new AssemblySnapshotSession(), "Lib");
        await session.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);
        var before = session.PeekBefore()!.Find(lib);

        var added = await CurrentEntryAsync(session, fixture.Projects("Lib", "Other"), other);
        AssemblySnapshotPair? pair = await session.GetPairAsync(fixture.Projects("Lib"), CancellationToken.None);
        Assert.Null(pair!.Current.Find(other));
        pair = null;
        Collect();

        Assert.False(added.IsAlive, "A current entry of a path that left the solution must be released.");
        Assert.Same(before, session.PeekBefore()!.Find(lib));
        GC.KeepAlive(session);
    }

    [Fact]
    public async Task Path_left_out_of_a_request_comes_back_to_its_entry_before_the_edit()
    {
        // Pruning the cache keeps an unchanged assembly stored once: a path absent from one request falls back
        // to its entry before the edit instead of being read again into a second copy.
        using var fixture = new AssemblySnapshotFixture();
        var lib = fixture.Write("Lib", AssemblySnapshotFixture.LibV1);
        fixture.Write("Other", "public class O { }");
        var session = fixture.Start(new AssemblySnapshotSession(), "Lib", "Other");
        await session.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);
        var reads = session.Reader.FileReads;

        _ = await session.GetPairAsync(fixture.Projects("Other"), CancellationToken.None);
        var pair = (await session.GetPairAsync(fixture.Projects("Lib", "Other"), CancellationToken.None))!;

        Assert.Same(pair.Before.Find(lib), pair.Current.Find(lib));
        Assert.Equal(reads, session.Reader.FileReads);
    }

    [Fact]
    public async Task New_baseline_releases_both_snapshots_of_the_previous_one()
    {
        using var fixture = new AssemblySnapshotFixture();
        var lib = fixture.Write("Lib", AssemblySnapshotFixture.LibV1);
        fixture.Write("Other", "public class O { }");
        var session = fixture.Start(new AssemblySnapshotSession(), "Lib");
        var (before, current) = await PreviousBaselineEntriesAsync(session, fixture, lib);

        session.Start(Path.Combine(fixture.Root, "Second.slnx"), DateTime.UtcNow, fixture.Inventory("Other"), NullLogger.Instance)
            .Cancel();
        await session.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);
        Collect();

        Assert.False(before.IsAlive, "The snapshot before the edit of a replaced baseline must be released.");
        Assert.False(current.IsAlive, "The current snapshot of a replaced baseline must be released.");
        Assert.Equal(AssemblySnapshotState.Read, session.PeekBefore()!.Find(fixture.PathOf("Other"))!.State);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference Before, WeakReference Current)> PreviousBaselineEntriesAsync(
        AssemblySnapshotSession session,
        AssemblySnapshotFixture fixture,
        string path)
    {
        await session.FirstReadTask.WaitAsync(AssemblySnapshotFixture.Timeout);
        fixture.Write("Lib", AssemblySnapshotFixture.LibV2, lastWriteTimeUtc: fixture.PublishedAtUtc.AddMinutes(1));
        var pair = await session.GetPairAsync(fixture.Projects("Lib"), CancellationToken.None);
        return (new WeakReference(pair!.Before.Find(path)), new WeakReference(pair.Current.Find(path)));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> CurrentEntryAsync(
        AssemblySnapshotSession session,
        IReadOnlyList<IlAssemblyInput> projects,
        string path)
    {
        var pair = await session.GetPairAsync(projects, CancellationToken.None);
        return new WeakReference(pair!.Current.Find(path));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> CurrentEntryAsync(AssemblySnapshotSession session, AssemblySnapshotFixture fixture, string path)
    {
        var pair = await session.GetPairAsync(fixture.Projects("Lib"), CancellationToken.None);
        return new WeakReference(pair!.Current.Find(path));
    }

    private static void Collect()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}
