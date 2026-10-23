using System.Reflection;
using System.Runtime.CompilerServices;

namespace RoslynMcpServer.Tests.Support;

/// <summary>
/// Roslyn's persistent index storage of this test process. Every solution file a test loads gets a cache
/// folder keyed by its path under LocalApplicationData, so temporary solutions would pile up run after run;
/// the folder of this process is deleted when the test assembly loads and when the process exits.
/// </summary>
internal static class RoslynIndexCache
{
    /// <summary>Cache folder of this process: one subfolder per solution path.</summary>
    public static string ProcessFolder { get; } = ResolveProcessFolder();

    [ModuleInitializer]
    internal static void DeleteOnLoadAndExit()
    {
        Delete();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Delete();
    }

    /// <summary>The storage database Roslyn opens for <paramref name="solutionPath"/>, if it exists.</summary>
    public static string? FindDatabase(string solutionPath)
    {
        if (!Directory.Exists(ProcessFolder))
        {
            return null;
        }

        // Roslyn cuts the file name to 20 characters and appends a checksum of the full path.
        var fileName = Path.GetFileName(solutionPath);
        var prefix = fileName.Length > 20 ? fileName[..20] : fileName;
        return Directory.GetDirectories(ProcessFolder, prefix + "-*")
            .Select(folder => Path.Combine(folder, "sqlite3", "v2", "storage.ide"))
            .FirstOrDefault(File.Exists);
    }

    private static void Delete()
    {
        try
        {
            if (Directory.Exists(ProcessFolder))
            {
                Directory.Delete(ProcessFolder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An open database keeps the folder on Windows; the next test run deletes it.
        }
    }

    // Roslyn computes the folder in the internal DefaultPersistentStorageConfiguration. Reading its two static
    // fields follows Roslyn's naming instead of re-implementing its path checksum.
    private static string ResolveProcessFolder()
    {
        var type = typeof(Microsoft.CodeAnalysis.Workspace).Assembly.GetType("Microsoft.CodeAnalysis.Host.DefaultPersistentStorageConfiguration", throwOnError: true)!;
        return Path.Combine(ReadStaticString(type, "s_cacheDirectory"), ReadStaticString(type, "s_moduleFileName"));
    }

    private static string ReadStaticString(Type type, string name) =>
        (string)type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
}
