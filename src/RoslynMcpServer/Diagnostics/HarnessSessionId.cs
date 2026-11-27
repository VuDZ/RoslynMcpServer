using System.Security.Cryptography;

namespace RoslynMcpServer.Diagnostics;

/// <summary>
/// Process-wide log token <c>[{harness}-{suffix}]</c>, for example <c>[cursor-k7m2]</c>.
/// The suffix separates two processes of the same harness that share one log file.
/// </summary>
internal static class HarnessSessionId
{
    internal static string CreateForCurrentProcess()
    {
        ParentProcessSnapshot parent;
        try
        {
            parent = ParentProcessProbe.Capture();
        }
        catch (Exception ex)
        {
            // Serilog is not configured yet. The command line is not part of the exception we raise.
            Console.Error.WriteLine($"[RoslynMcp] WARN: harness parent probe failed: {ex.Message}");
            parent = ParentProcessSnapshot.Empty;
        }

        var name = HarnessNameResolver.Resolve(parent.ImagePath, parent.CommandLine);
        return Format(name, CreateSuffix());
    }

    internal static string Format(string harnessName, string suffix) => "[" + harnessName + "-" + suffix + "]";

    internal static string CreateSuffix()
    {
        Span<char> chars = stackalloc char[SuffixLength];
        for (var i = 0; i < SuffixLength; i++)
        {
            chars[i] = SuffixAlphabet[RandomNumberGenerator.GetInt32(SuffixAlphabet.Length)];
        }

        return new string(chars);
    }

    internal const string PropertyName = "SessionId";

    internal const string LogOutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SessionId} {Message:lj}{NewLine}{Exception}";

    internal const int SuffixLength = 4;

    /// <summary>Crockford-like alphabet without 0/1/i/l/o/u, so suffixes stay readable in a log tail.</summary>
    internal const string SuffixAlphabet = "23456789abcdefghjkmnpqrstvwxyz";
}
