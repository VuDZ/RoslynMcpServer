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
