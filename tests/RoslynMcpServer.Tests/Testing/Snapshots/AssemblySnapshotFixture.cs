using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcpServer.Services.Testing.Il.Models;
using RoslynMcpServer.Services.Testing.Snapshots;
using RoslynMcpServer.Services.Testing.Snapshots.Models;
using RoslynMcpServer.Tests.Testing.Il;

namespace RoslynMcpServer.Tests.Testing.Snapshots;

/// <summary>
/// A temporary directory with output assemblies compiled in the test. Every assembly lies in a directory of
/// its own, like the output of a project, and is written with a time well before <see cref="PublishedAtUtc"/>
/// unless a test asks for another one, so the race rule is triggered only where a test means it.
/// </summary>
internal sealed class AssemblySnapshotFixture : IDisposable
{
    internal const string LibV1 = """
        public class A { public int Run() => 1; }
        public class B { public int Other() => 2; }
        """;

    internal const string LibV2 = """
        public class A { public int Run() => 20; }
        public class B { public int Other() => 2; }
        """;

    internal const string LibV3 = """
        public class A { public int Run() => 30; }
        public class B { public int Other() => 2; }
        """;

    internal string Root { get; }

    /// <summary>The publication moment the tests give to the session.</summary>
    internal DateTime PublishedAtUtc { get; } = DateTime.UtcNow;

    internal DateTime BeforePublication => PublishedAtUtc.AddDays(-1);

    internal AssemblySnapshotFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "RoslynMcpAssemblySnapshot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A background read of an abandoned session may still hold a file for a moment.
        }
    }

    internal string PathOf(string name) => Path.Combine(Root, name, "bin", name + ".dll");

    /// <summary>Compiles <paramref name="source"/> as assembly <paramref name="name"/> and writes it to its output path.</summary>
    internal string Write(string name, string source, DateTime? lastWriteTimeUtc = null)
    {
        var path = PathOf(name);
        WriteBytes(path, Compile(name, source), lastWriteTimeUtc ?? BeforePublication);
        return path;
    }

    internal static byte[] Compile(string name, string source) =>
        [.. IlFingerprintCompiler.Compile(source, name).Content];

    internal static void WriteBytes(string path, byte[] content, DateTime lastWriteTimeUtc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
    }

    internal AssemblySnapshotInventory Inventory(params string[] names) =>
        new([.. names.Select(PathOf)], names.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase));

    internal IReadOnlyList<IlAssemblyInput> Projects(params string[] names) =>
        [.. names.Select(name => new IlAssemblyInput(PathOf(name), IsTestProject: false))];

    internal AssemblySnapshotSession Start(AssemblySnapshotSession session, params string[] names)
    {
        session.Start(Path.Combine(Root, "Solution.slnx"), PublishedAtUtc, Inventory(names), NullLogger.Instance).Cancel();
        return session;
    }

    internal static IlTypeName Type(string assembly, string name) => new(assembly, name);

    internal static async Task WaitUntilAsync(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Timed out waiting until " + description + ".");
            }

            await Task.Delay(10);
        }
    }

    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
}
