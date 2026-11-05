using Microsoft.CodeAnalysis;
using RoslynMcpServer.Services.Testing.Baseline.Models;

namespace RoslynMcpServer.Services.Testing.Models;

/// <summary>
/// Maps current source tests to one concrete adapter class identity, including '+' nesting and closed generic names.
/// A source method may occur in several concrete classes. IsComplete proves that Tests contains every current test
/// of this runtime class; an incomplete inventory permits method filters, but never class collapse.
/// RuntimeName is null or UnresolvedReason is set when an identity is unknown or ambiguous; no source-name guess is safe.
/// HasFixtureArguments means runtime fixture constructor/source arguments, not ordinary C# constructor parameters
/// or generic type arguments alone (such as NUnit TestFixture(typeof(int))).
/// </summary>
internal sealed record VstestTestClass(
    ProjectId ProjectId,
    IReadOnlyList<BaselineMemberKey> Tests,
    string? RuntimeName,
    bool IsComplete,
    string? UnresolvedReason,
    bool HasFixtureArguments);
