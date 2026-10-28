using System.Security.Cryptography;
using System.Text;

namespace PipeRelay;

internal static class BuildIdentity
{
    public static string Value { get; } = Compute(AppContext.BaseDirectory);

    public static string Compute(string directory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var appHost = OperatingSystem.IsWindows() ? "PipeRelay.exe" : "PipeRelay";
        foreach (var path in Directory.EnumerateFiles(directory)
            .Where(path => Path.GetExtension(path) == ".dll" || Path.GetFileName(path) == appHost)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(path) + "\0"));
            hash.AppendData(SHA256.HashData(File.ReadAllBytes(path)));
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
