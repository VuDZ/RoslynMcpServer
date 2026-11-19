namespace TestImpactIlProbe;

/// <summary>Where the coverage of each test class lies: <c>directory/Assembly/Namespace.Class.cobertura.xml</c>.</summary>
internal static class ClassReports
{
    /// <summary>
    /// The class name of the idle run: no test class has it, so the test host starts, searches the whole
    /// assembly for tests and runs none. A run of one class does the same search first, and what the search
    /// executes (theory data of every class, module initializers) lands in the coverage of that class.
    /// </summary>
    public const string IdleClass = "TestImpactIlProbe.NoSuchTestClass";

    public static string PathFor(string directory, string assembly, string testClass) =>
        Path.Combine(directory, assembly, testClass + Extension);

    /// <summary>The output of the run that produced the report, kept next to it.</summary>
    public static string LogFor(string directory, string assembly, string testClass) =>
        Path.Combine(directory, assembly, testClass + ".log");

    /// <summary>
    /// The files the idle run of a test assembly executed. The name has a hyphen and another extension, so it
    /// is neither the report of a class nor found as one.
    /// </summary>
    public static string IdlePathFor(string directory, string assembly) =>
        Path.Combine(directory, assembly, IdleName + ".xml");

    public static string IdleLogFor(string directory, string assembly) =>
        Path.Combine(directory, assembly, IdleName + ".log");

    public static IEnumerable<(string Assembly, string TestClass, string Path)> Find(string directory)
    {
        foreach (var assembly in Directory.EnumerateDirectories(directory))
        {
            foreach (var path in Directory.EnumerateFiles(assembly, "*" + Extension))
            {
                yield return (Path.GetFileName(assembly), Path.GetFileName(path)[..^Extension.Length], path);
            }
        }
    }

    public static IEnumerable<(string Assembly, string Path)> FindIdle(string directory)
    {
        foreach (var assembly in Directory.EnumerateDirectories(directory))
        {
            var path = IdlePathFor(directory, Path.GetFileName(assembly));
            if (File.Exists(path))
            {
                yield return (Path.GetFileName(assembly), path);
            }
        }
    }

    private const string Extension = ".cobertura.xml";
    private const string IdleName = "idle-run";
}
