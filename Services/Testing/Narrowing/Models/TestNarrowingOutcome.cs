namespace RoslynMcpServer.Services.Testing.Narrowing.Models;

/// <summary>What narrowing a test project selected whole by epoch 1 gave.</summary>
internal enum TestNarrowingOutcome
{
    /// <summary>The project stays whole; the reason is named.</summary>
    WholeProject,

    /// <summary>The widest stage has test classes.</summary>
    Narrowed,

    /// <summary>
    /// Every stage is empty and no fingerprint of a type or an assembly on the path changed: the edit did not
    /// change compiled code, such as an edit of a comment (owner decision M-05).
    /// </summary>
    CodeUnchanged,

    /// <summary>
    /// Every stage is empty although fingerprints on the path changed: no test class reaches the changed
    /// types in the graph (owner decision M-05). The changed types are named.
    /// </summary>
    ChangedTypesWithoutTests,
}
