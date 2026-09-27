namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// Production source text plus the target checked inside it: the path is used for diagnostics, while
/// the namespace and type name keep a same-named type from another namespace from counting as found.
/// </summary>
internal sealed record SourceTarget(string SourceText, string SourcePath, string NamespaceName, string TypeName);
