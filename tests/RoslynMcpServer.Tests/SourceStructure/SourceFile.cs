namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// One source file of an analyzed scope. <see cref="Text"/> is normalized to <c>\n</c> here, at the
/// single entry point of the analysis: a raw string literal in a test file takes the line endings of
/// the working copy, while mutation anchors are written with <c>\n</c>. Without normalization the
/// verdict depends on the checkout markup (CRLF on Windows without <c>.gitattributes</c>) instead of
/// on the code, which is how five corpus facts of the sanitized-entry check turned red in CI.
/// </summary>
internal sealed class SourceFile
{
    /// <summary>Display or absolute path, used only for diagnostics.</summary>
    public string Path { get; }

    /// <summary>Source text with <c>\n</c> line endings only. Line numbers still match the working copy.</summary>
    public string Text { get; }

    private SourceFile(string path, string text)
    {
        Path = path;
        Text = text;
    }

    public static SourceFile Create(string path, string text) => new(path, Normalize(text));

    public static SourceFile ReadFromDisk(string path) => Create(path, File.ReadAllText(path));

    /// <summary>Line endings are normalized to <c>\n</c>; every analyzer input passes through here.</summary>
    public static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
}
