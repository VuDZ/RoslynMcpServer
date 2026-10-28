namespace PipeRelay.Transport;

internal sealed class BoundedStream(Stream inner, Action? cancelledReceived = null, Action? endOfInput = null) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        if (read == 0)
        {
            endOfInput?.Invoke();
        }

        Check(buffer.AsSpan(offset, read), ref _readBytes);
        Observe(buffer.AsSpan(offset, read));
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken);
        if (read == 0)
        {
            endOfInput?.Invoke();
        }

        Check(buffer.Span[..read], ref _readBytes);
        Observe(buffer.Span[..read]);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Write(byte[] buffer, int offset, int count)
    {
        Check(buffer.AsSpan(offset, count), ref _writeBytes);
        inner.Write(buffer, offset, count);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Check(buffer.Span, ref _writeBytes);
        return inner.WriteAsync(buffer, cancellationToken);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private static void Check(ReadOnlySpan<byte> bytes, ref int length)
    {
        foreach (var value in bytes)
        {
            if (value == '\n')
            {
                length = 0;
            }
            else if (++length > 65536)
            {
                throw new InvalidDataException("Internal JSON-RPC line exceeds 65536 bytes.");
            }
        }
    }

    private void Observe(ReadOnlySpan<byte> bytes)
    {
        if (cancelledReceived is null)
        {
            return;
        }

        foreach (var value in bytes)
        {
            if (value == '\n')
            {
                using var message = System.Text.Json.JsonDocument.Parse(_line.ToArray());
                if (message.RootElement.TryGetProperty("method", out var method)
                    && method.GetString() == "notifications/cancelled")
                {
                    cancelledReceived();
                }

                _line.Clear();
            }
            else
            {
                _line.Add(value);
            }
        }
    }

    private int _readBytes;
    private int _writeBytes;
    private readonly List<byte> _line = [];
}
