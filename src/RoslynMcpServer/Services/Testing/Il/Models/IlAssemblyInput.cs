namespace RoslynMcpServer.Services.Testing.Il.Models;

/// <summary>One loaded project as the graph sees it.</summary>
/// <param name="OutputFilePath">The output assembly of the project.</param>
/// <param name="IsTestProject">
/// True for a test project: assemblies lying in its output directory are what its test process can load by
/// name. Test classes are recognized by attributes in IL and do not depend on this flag.
/// </param>
internal sealed record IlAssemblyInput(string OutputFilePath, bool IsTestProject);
