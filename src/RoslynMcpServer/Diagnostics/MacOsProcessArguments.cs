using System.Buffers.Binary;
using System.Text;

namespace RoslynMcpServer.Diagnostics;

internal static class MacOsProcessArguments
{
    internal static string? Parse(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < sizeof(int))
        {
            return null;
        }

        var argumentCount = BinaryPrimitives.ReadInt32LittleEndian(buffer);
        if (argumentCount <= 0 || argumentCount > buffer.Length)
        {
            return null;
        }

        // KERN_PROCARGS2 contains argc, the executable path, NUL padding, argv, then the environment.
        // Stop at argc: a BuildHost name in the environment must never identify a process for termination.
        // https://github.com/apple-oss-distributions/xnu/blob/main/bsd/kern/kern_sysctl.c
        buffer = buffer[sizeof(int)..];
        var executableEnd = buffer.IndexOf((byte)0);
        if (executableEnd < 0)
        {
            return null;
        }

        buffer = buffer[(executableEnd + 1)..];
        while (!buffer.IsEmpty && buffer[0] == 0)
        {
            buffer = buffer[1..];
        }

        var arguments = new List<string>();
        for (var i = 0; i < argumentCount; i++)
        {
            var end = buffer.IndexOf((byte)0);
            if (end < 0)
            {
                return null;
            }

            arguments.Add(Encoding.UTF8.GetString(buffer[..end]));
            buffer = buffer[(end + 1)..];
        }

        return string.Join(' ', arguments);
    }
}
