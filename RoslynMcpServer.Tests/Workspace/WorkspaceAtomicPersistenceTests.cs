using System.Text;
using RoslynMcpServer.Services.Workspace;
using Xunit;

namespace RoslynMcpServer.Tests.Workspace;

public sealed class WorkspaceAtomicPersistenceTests : IDisposable
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_after_staging_a_prefix_keeps_the_existing_bytes(bool asyncWrite)
    {
        Directory.CreateDirectory(_root);
        var destination = Path.Combine(_root, "Existing.cs");
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        await File.WriteAllTextAsync(destination, "before", encoding);
        var original = await File.ReadAllBytesAsync(destination);

        void FailAfterPrefix()
        {
            Assert.Equal(original, File.ReadAllBytes(destination));
            var staged = Assert.Single(Directory.GetFiles(_root, "*.tmp"));
            Assert.Equal(1, new FileInfo(staged).Length);
            throw new IOException("Failed after writing a staged prefix.");
        }

        if (asyncWrite)
        {
            await Assert.ThrowsAsync<IOException>(() => WorkspaceFilePersistence.WriteTextAsync(
                null, destination, "after", encoding, FailAfterPrefix, CancellationToken.None));
        }
        else
        {
            Assert.Throws<IOException>(() => WorkspaceFilePersistence.WriteText(
                null, destination, "after", encoding, FailAfterPrefix));
        }

        Assert.Equal(original, await File.ReadAllBytesAsync(destination));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp"));
    }

    [Fact]
    public async Task Cancellation_after_staging_does_not_publish_or_leave_a_temporary_file()
    {
        Directory.CreateDirectory(_root);
        var destination = Path.Combine(_root, "Existing.cs");
        await File.WriteAllTextAsync(destination, "before");
        var original = await File.ReadAllBytesAsync(destination);
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WorkspaceFilePersistence.WriteTextAsync(
            null, destination, "after", Encoding.UTF8, cancellation.Cancel, cancellation.Token));

        Assert.Equal(original, await File.ReadAllBytesAsync(destination));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Rejected_hard_link_write_preserves_bytes_and_shared_identity(bool asyncWrite, bool linkDuringStaging)
    {
        Directory.CreateDirectory(_root);
        var destination = Path.Combine(_root, "Existing.cs");
        var alias = Path.Combine(_root, "Alias.cs");
        await File.WriteAllTextAsync(destination, "before", Encoding.UTF8);
        var original = await File.ReadAllBytesAsync(destination);

        void CreateAlias() => File.CreateHardLink(alias, destination);

        if (!linkDuringStaging)
        {
            CreateAlias();
        }

        Action? afterStagingPrefix = linkDuringStaging ? CreateAlias : null;
        var error = asyncWrite
            ? await Assert.ThrowsAsync<IOException>(() => WorkspaceFilePersistence.WriteTextAsync(
                null, destination, "after", Encoding.UTF8, afterStagingPrefix, CancellationToken.None))
            : Assert.Throws<IOException>(() => WorkspaceFilePersistence.WriteText(
                null, destination, "after", Encoding.UTF8, afterStagingPrefix));

        Assert.Contains("has multiple hard links", error.Message, StringComparison.Ordinal);
        Assert.Equal(original, await File.ReadAllBytesAsync(destination));
        Assert.Equal(original, await File.ReadAllBytesAsync(alias));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp"));

        await File.WriteAllTextAsync(alias, "updated in place");
        Assert.Equal("updated in place", await File.ReadAllTextAsync(destination));
    }

    [Fact]
    public async Task Symbolic_link_write_checks_the_resolved_targets_hard_links()
    {
        // Creating symbolic links on Windows may require a privilege the test runner lacks.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(_root);
        var destination = Path.Combine(_root, "Existing.cs");
        var symbolicLink = Path.Combine(_root, "SymbolicLink.cs");
        var hardLink = Path.Combine(_root, "HardLink.cs");
        await File.WriteAllTextAsync(destination, "before");
        File.CreateSymbolicLink(symbolicLink, destination);

        await WorkspaceFilePersistence.WriteTextAsync(null, symbolicLink, "after", Encoding.UTF8, CancellationToken.None);

        Assert.Equal(destination, new FileInfo(symbolicLink).LinkTarget);
        Assert.Equal("after", await File.ReadAllTextAsync(destination));
        File.CreateHardLink(hardLink, destination);

        var error = await Assert.ThrowsAsync<IOException>(() => WorkspaceFilePersistence.WriteTextAsync(
            null, symbolicLink, "rejected", Encoding.UTF8, CancellationToken.None));

        Assert.Contains("has multiple hard links", error.Message, StringComparison.Ordinal);
        Assert.Equal(destination, new FileInfo(symbolicLink).LinkTarget);
        Assert.Equal("after", await File.ReadAllTextAsync(destination));
        Assert.Equal("after", await File.ReadAllTextAsync(hardLink));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp"));
    }

    [Fact]
    public async Task Replacement_preserves_the_bom_and_existing_unix_mode()
    {
        Directory.CreateDirectory(_root);
        var destination = Path.Combine(_root, "Existing.cs");
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        await File.WriteAllTextAsync(destination, "before", encoding);
        const UnixFileMode Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(destination, Mode);
        }

        await WorkspaceFilePersistence.WriteTextAsync(null, destination, "after", encoding, CancellationToken.None);

        Assert.Equal(encoding.GetPreamble().Concat(encoding.GetBytes("after")), await File.ReadAllBytesAsync(destination));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(Mode, File.GetUnixFileMode(destination));
        }

        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp"));
    }

    [Fact]
    public async Task Replacement_respects_the_effective_permission_to_open_the_existing_file_for_write()
    {
        Directory.CreateDirectory(_root);
        var destination = Path.Combine(_root, "Existing.cs");
        await File.WriteAllTextAsync(destination, "before");
        const UnixFileMode PriorMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(destination, FileAttributes.ReadOnly);
        }
        else
        {
            File.SetUnixFileMode(destination, UnixFileMode.None);
        }

        var openFailure = Record.Exception(() =>
            File.Open(destination, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete).Dispose());
        try
        {
            if (openFailure is UnauthorizedAccessException)
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => WorkspaceFilePersistence.WriteTextAsync(
                    null, destination, "after", Encoding.UTF8, CancellationToken.None));
            }
            else
            {
                Assert.Null(openFailure);
                await WorkspaceFilePersistence.WriteTextAsync(null, destination, "after", Encoding.UTF8, CancellationToken.None);
                if (!OperatingSystem.IsWindows())
                {
                    Assert.Equal(UnixFileMode.None, File.GetUnixFileMode(destination));
                }
            }
        }
        finally
        {
            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(destination, FileAttributes.Normal);
            }
            else
            {
                File.SetUnixFileMode(destination, PriorMode);
            }
        }

        Assert.Equal(openFailure is null ? "after" : "before", await File.ReadAllTextAsync(destination));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "RoslynMcpAtomicWrite-" + Guid.NewGuid().ToString("N"));
}
