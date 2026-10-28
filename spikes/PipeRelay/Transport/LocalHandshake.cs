using System.Buffers.Binary;
using System.Text.Json;
using PipeRelay.Transport.Models;

namespace PipeRelay.Transport;

internal static class LocalHandshake
{
    public static async Task<Handshake> Read(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 1 or > 4096)
        {
            throw new InvalidDataException("Handshake length is outside the 4096-byte limit.");
        }

        var body = new byte[length];
        await stream.ReadExactlyAsync(body, cancellationToken);
        return JsonSerializer.Deserialize<Handshake>(body)
            ?? throw new InvalidDataException("Handshake is missing.");
    }

    public static async Task Write(Stream stream, Handshake handshake, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(handshake);
        if (body.Length > 4096)
        {
            throw new InvalidDataException("Handshake exceeds the 4096-byte limit.");
        }

        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static void Validate(Handshake handshake, string key)
    {
        if (handshake.Version != 1 || handshake.Identity != BuildIdentity.Value || handshake.Key != key
            || string.IsNullOrWhiteSpace(handshake.HarnessId) || handshake.HarnessId.Length > 80)
        {
            throw new InvalidDataException("Incompatible local handshake.");
        }
    }
}
