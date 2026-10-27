using System.Reflection;
using System.Runtime.CompilerServices;

namespace RoslynMcpServer.Tests.Support;

/// <summary>
/// Roslyn's persistent index storage of this test process. Every solution file a test loads gets a cache
/// folder keyed by its path, so temporary solutions would pile up run after run; the cache of this process
/// is deleted when the test assembly loads and when the process exits.
/// </summary>
/// <remarks>
/// Roslyn names the cache folder after the main module of the process. On Windows that is testhost.exe of
/// this build, a folder no server shares. On Unix the tests run as <c>dotnet testhost.dll</c>, and the folder
/// of <c>dotnet</c> is shared by every framework-dependent server and tool on the machine, so deleting it
/// wiped the warm cache of a running server and of a parallel test run. There the cache base is moved to a
/// folder of this process: Roslyn reads LocalApplicationData once, in its static constructor, which runs here
/// while <c>XDG_DATA_HOME</c> and <c>HOME</c> point to that folder; both are restored at once, before any test.
/// </remarks>
internal static class RoslynIndexCache
{
    /// <summary>Cache folder of this process: one subfolder per solution path.</summary>
    public static string ProcessFolder { get; } = ResolveProcessFolder();

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

    [ModuleInitializer]
    internal static void DeleteOnLoadAndExit()
    {
        Delete();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Delete();
    }

    private static void Delete()
    {
        var folder = _processBase ?? ProcessFolder;
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
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
        if (!OperatingSystem.IsWindows())
        {
            // Linux takes LocalApplicationData from XDG_DATA_HOME, macOS from HOME (~/Library/Application Support).
            _processBase = Path.Combine(Path.GetTempPath(), "RoslynMcpIndexCache-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N"));
            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            var home = Environment.GetEnvironmentVariable("HOME");
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", _processBase);
            Environment.SetEnvironmentVariable("HOME", _processBase);
            try
            {
                RuntimeHelpers.RunClassConstructor(type.TypeHandle);
            }
            finally
            {
                Environment.SetEnvironmentVariable("XDG_DATA_HOME", dataHome);
                Environment.SetEnvironmentVariable("HOME", home);
            }
        }

        var folder = Path.Combine(ReadStaticString(type, "s_cacheDirectory"), ReadStaticString(type, "s_moduleFileName"));
        if (_processBase is not null && !folder.StartsWith(_processBase, StringComparison.Ordinal))
        {
            // The static constructor ran before this one; deleting its folder would hit the shared cache again.
            throw new InvalidOperationException($"Roslyn's index cache is outside the test folder: {folder}.");
        }

        return folder;
    }

    private static string ReadStaticString(Type type, string name) =>
        (string)type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    private static string? _processBase;
}
