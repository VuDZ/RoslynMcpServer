namespace RoslynMcpServer.Services.Testing.Models;

/// <summary>What made epoch 1 select a test project whole.</summary>
internal enum ProjectExpansionKind
{
    /// <summary>A C# document of the owner project changed in a way the syntax comparison cannot narrow.</summary>
    CSharpSource,

    /// <summary>Another input of the owner project changed: an additional file, a resource, an evaluation input.</summary>
    NonCSharpInput,

    /// <summary>A project, solution or MSBuild file of the owner project changed.</summary>
    ProjectFile,

    /// <summary>A file that shapes the project graph changed: every test project stays selected until a reload.</summary>
    ProjectGraphFile,

    /// <summary>The non-C# input history of the baseline is incomplete.</summary>
    ObservationGap,
}
