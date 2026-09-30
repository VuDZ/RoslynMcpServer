namespace RoslynMcpServer.Services.Models;

/// <summary>Evidence category. Distinct from <see cref="InputRole"/>.</summary>
internal enum InputCategoryKind
{
    CSharp,
    Xaml,
    Resources,
    AdditionalFiles,
    AnalyzerConfigs,
    Imports,
    WalkUpProps,
    RestoreInputs,
    ProjectReferences,
    MetadataDependencies,
    ExternalGlobs,
    CustomTasks,
    MultiTargeting,
}
