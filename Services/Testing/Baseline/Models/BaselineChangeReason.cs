namespace RoslynMcpServer.Services.Testing.Baseline.Models;

internal enum BaselineChangeReason
{
    MemberSyntax,
    TypeSurface,
    ImportsAndNamespaces,
    GlobalUsings,
    AssemblyAndModuleAttributes,
    Directives,
    UnparseableContent,
    AmbiguousMemberKey,
    NewExtensionMethod,
    OperatorOrConversion,
}
