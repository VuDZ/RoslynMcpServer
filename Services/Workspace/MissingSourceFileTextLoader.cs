using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services.Workspace;

/// <summary>
/// Loader for a document whose file was missing when the workspace was opened. While the file is still
/// missing it fails at once with the result Roslyn would give after its retries: empty text and the
/// error message. Once the file exists it reads it as an ordinary file.
/// </summary>
/// <remarks>
/// Roslyn 5.9.0 retries every <see cref="IOException"/>, including <see cref="FileNotFoundException"/>,
/// six times 200 ms apart and then returns empty text. A generated file linked into many projects costs
/// about one second per project and per read: 24 of 31 s of a first search in the office
/// (branch research/workspace-load-performance,
/// docs/workspace-load-performance/epoch-0-stage-measurement/evidence/office-first-search-2026-10-23.md).
/// <see cref="InvalidDataException"/> is not retried and yields the same empty text.
/// </remarks>
internal sealed class MissingSourceFileTextLoader(string path) : TextLoader
{
    public override Task<TextAndVersion> LoadTextAndVersionAsync(LoadTextOptions options, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"Could not find file '{path}'.");
        }

        // The file appeared after load. MSBuildWorkspace may have passed the project's code page as the
        // default encoding; it is not public, so detection falls back to UTF-8 like a plain FileTextLoader.
        return new FileTextLoader(path, defaultEncoding: null).LoadTextAndVersionAsync(options, cancellationToken);
    }
}
