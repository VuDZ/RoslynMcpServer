using System.Text.Json;

namespace PipeRelay.Preflight;

internal static class IdentityBenchChecks
{
    public static int Run(string parentDirectory)
    {
        var parent = Path.GetFullPath(parentDirectory);
        var root = Path.Combine(parent, "identity-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var results = new List<object>();
        var failures = 0;
        try
        {
            Check("existing-scratch-and-output-survive", PreserveExistingScratch);
            Check("scratch-inside-source-is-refused", ScratchInsideSource);
            Check("scratch-containing-source-is-refused", ScratchContainsSource);
            Check("output-inside-source-is-refused", OutputInsideSource);
            Check("existing-output-is-preserved", PreserveExistingOutput);
            Console.WriteLine(JsonSerializer.Serialize(new { failures, results }));
            return failures == 0 ? 0 : 1;
        }
        finally
        {
            if (Path.GetDirectoryName(root) != parent)
            {
                throw new InvalidOperationException("The check directory escaped its owned parent.");
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        void Check(string name, Action<string> action)
        {
            var directory = Path.Combine(root, name);
            Directory.CreateDirectory(directory);
            try
            {
                action(directory);
                results.Add(new { name, passed = true });
            }
            catch (Exception exception)
            {
                failures++;
                results.Add(new { name, passed = false, error = exception.Message });
            }
        }
    }

    private static void PreserveExistingScratch(string root)
    {
        var source = CreateSource(root);
        var scratch = Path.Combine(root, "scratch");
        Directory.CreateDirectory(scratch);
        var sentinel = Path.Combine(scratch, "keep.txt");
        File.WriteAllText(sentinel, "existing data");
        var output = Path.Combine(scratch, "report.json");
        Invoke(source, scratch, output);
        Require(File.Exists(sentinel) && File.ReadAllText(sentinel) == "existing data",
            "The benchmark deleted pre-existing scratch data.");
        Require(File.Exists(output), "The benchmark deleted its output report.");
        Require(!Directory.EnumerateDirectories(scratch).Any(), "An owned run directory was left behind.");
        using var report = JsonDocument.Parse(File.ReadAllText(output));
        if (OperatingSystem.IsWindows())
        {
            Require(report.RootElement.GetProperty("Samples").EnumerateArray()
                    .All(sample => sample.GetProperty("Cache").GetString() == "warm"),
                "Windows samples claimed an unavailable cold cache.");
        }
    }

    private static void ScratchInsideSource(string root)
    {
        var source = CreateSource(root);
        Refuse(() => Invoke(source, Path.Combine(source, "scratch"), Path.Combine(root, "report.json")));
        Require(File.Exists(Path.Combine(source, "Fixture.dll")), "Source data was removed.");
    }

    private static void ScratchContainsSource(string root)
    {
        var source = CreateSource(root);
        Refuse(() => Invoke(source, root, Path.Combine(root, "report.json")));
        Require(File.Exists(Path.Combine(source, "Fixture.dll")), "Source data was removed.");
    }

    private static void OutputInsideSource(string root)
    {
        var source = CreateSource(root);
        Refuse(() => Invoke(source, Path.Combine(root, "scratch"), Path.Combine(source, "report.json")));
    }

    private static void PreserveExistingOutput(string root)
    {
        var source = CreateSource(root);
        var output = Path.Combine(root, "report.json");
        File.WriteAllText(output, "existing report");
        Refuse(() => Invoke(source, Path.Combine(root, "scratch"), output));
        Require(File.ReadAllText(output) == "existing report", "The benchmark overwrote an existing report.");
    }

    private static string CreateSource(string root)
    {
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(source);
        File.WriteAllBytes(Path.Combine(source, "Fixture.dll"), Enumerable.Range(0, 256).Select(i => (byte)i).ToArray());
        return source;
    }

    private static void Invoke(string source, string scratch, string output) =>
        IdentityBench.Run(["identity-bench", source, scratch, output, "1"]);

    private static void Refuse(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentException)
        {
            return;
        }

        throw new InvalidOperationException("An unsafe benchmark path was accepted.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
