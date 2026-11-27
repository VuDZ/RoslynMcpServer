namespace RoslynMcpServer.Services.Models;

/// <summary>
/// Role of one occurrence of a physical path. An extension is not a role:
/// XAML and resources stay <see cref="AdditionalFile"/> or <see cref="Unknown"/>
/// until a producer or an evaluated item kind says otherwise.
/// </summary>
internal enum InputRole
{
    None = 0,
    UserInput,
    Generated,
    AdditionalFile,
    AnalyzerConfig,
    EvaluationInput,
    MetadataDependency,
    Output,
    Unknown,
    PotentialMembership,
}
