namespace RoslynMcpServer.Services.Testing.Narrowing.Models;

/// <summary>Why a test class is in a stage; a class can have several reasons.</summary>
[Flags]
internal enum TestClassSelectionReasons
{
    None = 0,

    /// <summary>The fingerprint of its top-level type changed, or the type was added.</summary>
    ChangedFingerprint = 1,

    /// <summary>It holds a new or changed test found by epoch 1.</summary>
    ChangedTest = 2,

    /// <summary>It reaches a changed, added or removed type at the stage.</summary>
    ReachesChangedType = 4,

    /// <summary>The graph has no test class for it, so what it reaches is unknown.</summary>
    NoGraphNode = 8,
}
