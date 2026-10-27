namespace BuildHostFork.Trace;

/// <summary>
/// Inclusive host times of one project file across all hosts, and the client's operation times for it. Client times
/// include the host times they wait for, and a Resolve includes the loads of the referenced projects it started.
/// </summary>
internal sealed record ProjectCost(
    string Path,
    double RootEvaluationMs,
    int RootEvaluations,
    double TfmEvaluationMs,
    int TfmEvaluations,
    double DesignTimeBuildMs,
    int DesignTimeBuilds,
    double ProjectInstanceMs,
    double ExtractionMs,
    double ResponseMs,
    long ResponseChars,
    double ClientEvaluateMs,
    double ClientBuildMs,
    double ClientResolveMs)
{
    public double HostMs => RootEvaluationMs + TfmEvaluationMs + DesignTimeBuildMs + ProjectInstanceMs + ExtractionMs + ResponseMs;
}
