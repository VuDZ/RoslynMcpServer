using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Testing.Models;
using RoslynMcpServer.Services.Testing.Snapshots.Models;

namespace RoslynMcpServer.Services.Testing.Narrowing.Models;

/// <summary>
/// Everything narrowing needs, taken in advance, so that the decision itself reads no file. Times of output
/// assemblies are the stamps of the entries in <see cref="Pair"/>: the content compared is the content dated.
/// </summary>
/// <param name="Solution">Projects, their references, assembly names, output and reference assembly paths.</param>
/// <param name="Impact">The epoch 1 answer.</param>
/// <param name="Classes">The current test-class inventory of epoch 1.</param>
/// <param name="EditedFiles">The C# documents changed since load, by project occurrence.</param>
/// <param name="ReferenceAssemblyTimesUtc">
/// Last write times of the reference assemblies that exist, by <see cref="Project.OutputRefFilePath"/>; a
/// declared path that is missing here has no file.
/// </param>
internal sealed record TestNarrowingInput(
    Solution Solution,
    TestImpactResult Impact,
    IReadOnlyList<VstestTestClass> Classes,
    IReadOnlyList<EditedSourceFile> EditedFiles,
    IReadOnlyDictionary<string, DateTime> ReferenceAssemblyTimesUtc,
    AssemblySnapshotPair Pair);
