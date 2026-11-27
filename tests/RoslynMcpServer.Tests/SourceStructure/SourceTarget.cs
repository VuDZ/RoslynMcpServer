namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// One target of a structural check: the source file it lives in plus the namespace and type name that
/// must contain it. The namespace keeps a same-named type from another namespace from counting as
/// found; the file carries the normalized text and is resolved inside the declared analysis scope.
/// </summary>
internal sealed record SourceTarget(SourceFile File, string NamespaceName, string TypeName)
{
    /// <summary>Type as it appears in diagnostics.</summary>
    public string DisplayName => $"{NamespaceName}.{TypeName}";

    /// <summary>Target built from text: the text is normalized by <see cref="SourceFile.Create"/>.</summary>
    public static SourceTarget FromText(string path, string text, string namespaceName, string typeName) =>
        new(SourceFile.Create(path, text), namespaceName, typeName);
}
