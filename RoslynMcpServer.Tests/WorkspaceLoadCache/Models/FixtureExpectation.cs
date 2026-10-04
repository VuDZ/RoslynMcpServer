using System.Collections.Immutable;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

/// <summary>
/// Independently authored expected admission outcome and semantic goldens for one
/// <see cref="FixtureScenario"/>. Values are literals from independently observed evidence,
/// never values captured from the host under test.
/// Key conventions: instance "project.csproj|TFM" relative to the fixture root with '/'
/// separators; edge "from-&gt;to"; option "instance|property"; membership
/// "instance|role|root-relative-path" with roles compile|additional|analyzer-config; generated
/// document "instance|generatorIdentity|hintName"; constant "instance|fully.qualified.Type.Member".
/// All collections are non-default; keys are unique and sorted; reason categories and diagnostic
/// ids are sorted. A reason category is satisfied by an admission reason equal to it or prefixed
/// with it followed by ':'. Full diagnostics comparison belongs to task-04.
/// </summary>
internal sealed record FixtureExpectation(
    string AdmissionDisposition,
    ImmutableArray<string> RequiredReasonCategories,
    ImmutableArray<string> ExpectedInstanceKeys,
    ImmutableArray<string> ExpectedEdgeKeys,
    ImmutableSortedDictionary<string, string> RequiredOptionValues,
    ImmutableSortedDictionary<string, string> ExpectedSourceCharacters,
    ImmutableSortedDictionary<string, ImmutableArray<byte>> ExpectedSourceBytes,
    ImmutableSortedDictionary<string, string> ExpectedGeneratedTexts,
    ImmutableSortedDictionary<string, string> ExpectedConstants,
    ImmutableArray<string> RequiredDiagnosticIds);
