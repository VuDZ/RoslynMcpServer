using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Services.Build;

/// <summary>
/// One successful invocation remembered for this process. Storing a proof does not
/// make the project current: reuse also requires input content hashes on the cursor.
/// </summary>
internal sealed class BuildProof
{
    public ProjectId ProjectId { get; }

    public BuildContext Context { get; }

    public BuildInputCursor Cursor { get; }

    public string OutputIdentity { get; }

    public bool OutputPathContested { get; }

    public Guid Generation => Cursor.Generation;

    public BuildProof(
        ProjectId projectId,
        BuildContext context,
        BuildInputCursor cursor,
        string outputIdentity,
        bool outputPathContested)
    {
        ArgumentNullException.ThrowIfNull(projectId);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputIdentity);
        ProjectId = projectId;
        Context = context;
        Cursor = cursor;
        OutputIdentity = outputIdentity;
        OutputPathContested = outputPathContested;
    }
}
