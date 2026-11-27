namespace RoslynMcpServer.Services.Testing.Narrowing.Models;

/// <summary>The stages of the answer; every stage includes the previous one.</summary>
internal enum TestClassStageLevel
{
    /// <summary>Test classes whose compiled code changed, and the new and changed tests of epoch 1.</summary>
    Changed,

    /// <summary>Also, test classes that reach a changed type through references in code.</summary>
    References,

    /// <summary>Also, through types a container or a framework finds by scanning an assembly.</summary>
    Discovery,

    /// <summary>Also, through code that loads assemblies by name.</summary>
    DiscoveryWithLoads,
}
