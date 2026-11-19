namespace TestImpactIlProbe;

/// <summary>
/// The command line that runs one test class under a coverage collector. Test platforms and collectors
/// differ, so the line is a template: <c>{assembly}</c> is the test assembly file, <c>{exe}</c> the same path
/// as an executable, <c>{class}</c> the full name of the test class, <c>{filter}</c> a VSTest filter for it,
/// and <c>{report}</c> the path the collector must write a Cobertura report to (a file, or a directory of them).
/// </summary>
internal sealed record TestRunTemplate(string Text)
{
    /// <summary>A test project run by VSTest: <c>dotnet test Tests.dll --filter</c>.</summary>
    public static TestRunTemplate VsTest { get; } = new(
        "dotnet-coverage collect -f cobertura -o \"{report}\" -- dotnet test \"{assembly}\" --filter \"{filter}\"");

    /// <summary>A test executable that takes Microsoft.Testing.Platform options of xUnit v3.</summary>
    public static TestRunTemplate Mtp { get; } = new(
        "dotnet-coverage collect -f cobertura -o \"{report}\" -- \"{exe}\" --filter-class \"{class}\"");

    /// <summary>
    /// A test executable with the xUnit v3 in-process runner. A project can run through
    /// Microsoft.Testing.Platform under <c>dotnet test</c> and still have this runner in its executable,
    /// which then rejects <c>--filter-class</c>.
    /// </summary>
    public static TestRunTemplate XunitV3 { get; } = new(
        "dotnet-coverage collect -f cobertura -o \"{report}\" -- \"{exe}\" -class \"{class}\"");

    /// <param name="custom">
    /// A command line, or <c>@path</c> of a file that holds one: a shell that calls the probe can spoil the
    /// quotes of a line passed as an argument.
    /// </param>
    public static TestRunTemplate Choose(string? runner, string? custom) => (custom, runner) switch
    {
        (not null, _) when custom.StartsWith('@') => new TestRunTemplate(File.ReadAllText(custom[1..]).Trim()),
        (not null, _) => new TestRunTemplate(custom),
        (null, null or "vstest") => VsTest,
        (null, "mtp") => Mtp,
        (null, "xunit3") => XunitV3,
        _ => throw new InvalidOperationException($"Unknown --runner {runner}: use vstest, mtp or xunit3, or pass --run with a command line."),
    };

    public string Expand(string assemblyPath, string testClass, string report)
    {
        var executable = Path.ChangeExtension(assemblyPath, OperatingSystem.IsWindows() ? ".exe" : null);
        return Text
            .Replace("{assembly}", assemblyPath, StringComparison.Ordinal)
            .Replace("{exe}", executable, StringComparison.Ordinal)
            .Replace("{class}", testClass, StringComparison.Ordinal)
            .Replace("{filter}", VsTestFilter(testClass), StringComparison.Ordinal)
            .Replace("{report}", report, StringComparison.Ordinal);
    }

    /// <summary>
    /// VSTest has no "tests of this class" filter, only "the full name contains". The name of a test is
    /// <c>Namespace.Class.Method</c>, or <c>Namespace.Class+Nested.Method</c> for a nested class, so the class
    /// name is followed by a separator: without it <c>OrderTests</c> would also run <c>OrderTestsBase</c>.
    /// </summary>
    public static string VsTestFilter(string testClass) =>
        $"FullyQualifiedName~{testClass}.|FullyQualifiedName~{testClass}+";
}
