namespace TestImpactIlProbe;

/// <summary>What the coverage reports say about one source file.</summary>
internal enum FileCoverage
{
    /// <summary>
    /// No report names the file: it has no executable lines, or its assembly was not loaded while the tests ran.
    /// </summary>
    NotInReport,

    /// <summary>The file was instrumented and no test executed a line of it.</summary>
    NotCovered,

    /// <summary>At least one line of the file was executed.</summary>
    Covered,
}
