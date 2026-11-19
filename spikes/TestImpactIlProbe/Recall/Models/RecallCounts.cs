namespace TestImpactIlProbe;

/// <summary>
/// How one selector does over all production files. A pair is a file and a test class that executes it.
/// </summary>
/// <remarks>
/// A run of one test class first searches its whole assembly for tests, and the search executes code too.
/// A pair is outside the idle run when the test assembly of the class does not execute the file with no
/// test selected: only those pairs are certain to come from the tests of the class. The counts over all
/// pairs include what the search executes for a class that never calls it; the counts outside the idle run
/// leave those pairs out, together with the classes that do call the same files.
/// </remarks>
/// <param name="FilesExecuted">Files at least one measured test class executes.</param>
/// <param name="FilesComplete">Of them, files whose answer holds every executing class.</param>
/// <param name="FilesLosing">Of them, files whose answer misses at least one executing class.</param>
/// <param name="FilesLosingOutsideIdleRun">Of them, files whose answer misses an executing class outside the idle run.</param>
/// <param name="FilesWithEmptyAnswer">Of them, files with no test class in the answer.</param>
/// <param name="Pairs">File and executing class pairs.</param>
/// <param name="KeptPairs">Pairs whose class is in the answer for the file.</param>
/// <param name="PairsOutsideIdleRun">Pairs outside the idle run.</param>
/// <param name="KeptPairsOutsideIdleRun">Of them, pairs whose class is in the answer for the file.</param>
/// <param name="SelectedPairs">File and selected class pairs, over all files.</param>
/// <param name="RecallByFile">Per executed file, the share of executing classes in the answer.</param>
/// <param name="PrecisionByFile">Per file with a non-empty answer, the share of the answer that executes the file.</param>
internal sealed record RecallCounts(
    int FilesExecuted,
    int FilesComplete,
    int FilesLosing,
    int FilesLosingOutsideIdleRun,
    int FilesWithEmptyAnswer,
    long Pairs,
    long KeptPairs,
    long PairsOutsideIdleRun,
    long KeptPairsOutsideIdleRun,
    long SelectedPairs,
    long Methods,
    long KeptMethods,
    Distribution RecallByFile,
    Distribution PrecisionByFile)
{
    /// <summary>The share of pairs the selector keeps: 1 means no executing test class is lost for any file.</summary>
    public double Recall => Share(KeptPairs, Pairs);

    /// <summary>The same share over the pairs outside the idle run.</summary>
    public double RecallOutsideIdleRun => Share(KeptPairsOutsideIdleRun, PairsOutsideIdleRun);

    /// <summary>The same share with every test class weighted by its test methods.</summary>
    public double RecallByMethods => Share(KeptMethods, Methods);

    /// <summary>The share of selected classes that execute the file they were selected for.</summary>
    public double Precision => Share(KeptPairs, SelectedPairs);

    private static double Share(long part, long whole) => whole == 0 ? 0 : Math.Round((double)part / whole, 4);
}
