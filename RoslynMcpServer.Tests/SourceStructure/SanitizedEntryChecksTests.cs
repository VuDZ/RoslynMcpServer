using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace RoslynMcpServer.Tests.SourceStructure;

/// <summary>
/// Regression examples for the check code itself: a safe shape change keeps the checks green, a
/// contract violation turns them red, and an ambiguous target produces a diagnostic. Examples
/// declared as executable mutations must compile: a check failure on a non-compiling source proves
/// nothing.
/// </summary>
public sealed class SanitizedEntryChecksTests
{
    private const string DemoPath = "Demo/DemoManager.cs";
    private const string DemoNamespace = "Demo";
    private const string DemoType = "DemoManager";

    private const string AsyncEntry = "GetSanitizedPublishedSolutionAsync";
    private const string SyncEntry = "GetSanitizedPublishedSolution";
    private const string RawEntry = "GetPublishedSolutionAfterDiskSyncAsync";
    private const string HelperEntry = "GetOrCreateSanitizedPublishedSolution";

    // Indentation levels of the demo source: mutations are built from the same constants, so anchors
    // do not drift away from the fixture when indentation changes.
    private const string MemberIndent = "    ";
    private const string BodyIndent = "        ";
    private const string NestedIndent = "            ";

    private const string AsyncSignature = MemberIndent + "public async Task<Snapshot?> " + AsyncEntry;
    private const string SyncSignature = MemberIndent + "public Snapshot? " + SyncEntry + "()";
    private const string RawSignature = MemberIndent + "public async Task<Snapshot?> " + RawEntry;
    private const string HelperSignature = MemberIndent + "private Snapshot? " + HelperEntry + "()";
    private const string ClassEnd = "\n}";

    private const string HelperCallStatement = NestedIndent + "return " + HelperEntry + "();";

    /// <summary>Lock acquisition, protected <c>try</c> and <c>finally</c> release of the synchronous entry.</summary>
    private const string SyncLockTakeBlock =
        BodyIndent + "_workspaceLock.Wait();\n"
        + BodyIndent + "try\n"
        + BodyIndent + "{\n"
        + HelperCallStatement + "\n"
        + BodyIndent + "}\n"
        + BodyIndent + "finally\n"
        + BodyIndent + "{\n"
        + NestedIndent + "_workspaceLock.Release();\n"
        + BodyIndent + "}";

    /// <summary>The same acquisition with a protected <c>try</c> but without <c>finally</c>.</summary>
    private const string SyncLockBeforeTry =
        BodyIndent + "_workspaceLock.Wait();\n"
        + BodyIndent + "try\n"
        + BodyIndent + "{\n"
        + HelperCallStatement + "\n"
        + BodyIndent + "}";

    private const string Baseline = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Demo;

        internal sealed class DemoManager
        {
            private readonly SemaphoreSlim _workspaceLock = new(1, 1);
            private readonly SemaphoreSlim _otherLock = new(1, 1);
            private Snapshot? _solution;

            public async Task<Snapshot?> GetSanitizedPublishedSolutionAsync(CancellationToken cancellationToken = default)
            {
                await _workspaceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    return GetOrCreateSanitizedPublishedSolution();
                }
                finally
                {
                    _workspaceLock.Release();
                }
            }

            public Snapshot? GetSanitizedPublishedSolution()
            {
                _workspaceLock.Wait();
                try
                {
                    return GetOrCreateSanitizedPublishedSolution();
                }
                finally
                {
                    _workspaceLock.Release();
                }
            }

            public async Task<Snapshot?> GetPublishedSolutionAfterDiskSyncAsync(CancellationToken cancellationToken = default)
            {
                await _workspaceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    return _solution;
                }
                finally
                {
                    _workspaceLock.Release();
                }
            }

            private Snapshot? GetOrCreateSanitizedPublishedSolution()
            {
                return _solution;
            }
        }

        internal sealed class Snapshot
        {
        }

        internal sealed class PrepareOutcome
        {
            public IReadOnlyList<int> Results { get; } = new List<int>();
        }
        """;

    private static readonly Lazy<IReadOnlyList<MetadataReference>> _platformReferences = new(CreatePlatformReferences);

    [Fact]
    public void Demo_baseline_compiles() => AssertCompiles(Baseline);

    [Fact]
    public void Demo_baseline_passes_sanitized_and_raw_checks()
    {
        Assert.Empty(VerifySanitized(Baseline));
        Assert.Empty(VerifyRaw(Baseline));
    }

    [Fact]
    public void Method_reordering_keeps_success()
    {
        var rawMember = CutMember(RawSignature, HelperSignature);
        var helperMember = CutMember(HelperSignature, ClassEnd);
        var reordered = Baseline
            .Replace(rawMember, string.Empty, StringComparison.Ordinal)
            .Replace(helperMember, helperMember + "\n" + rawMember, StringComparison.Ordinal);

        AssertCompiles(reordered);
        Assert.Empty(VerifySanitized(reordered));
        Assert.Empty(VerifyRaw(reordered));
    }

    [Fact]
    public void Foreign_member_with_Results_and_forbidden_names_in_comments_keep_success()
    {
        var foreignMember =
            MemberIndent + "private int CompletePrepare(PrepareOutcome prepared)\n"
            + MemberIndent + "{\n"
            + BodyIndent + "return prepared.Results.Count;\n"
            + MemberIndent + "}\n\n";

        var withForeignMember = MutateWholeSource(SyncSignature, foreignMember + SyncSignature);
        var withCommentsAndStrings = withForeignMember.Replace(
            BodyIndent + "_workspaceLock.Wait();",
            BodyIndent + "_workspaceLock.Wait();\n"
            + BodyIndent + "// GetSanitizedPublishedSolution(); .Result GetAwaiter()\n"
            + BodyIndent + "var note = \"_workspaceLock.Release(); .Result GetSanitizedPublishedSolution();\";",
            StringComparison.Ordinal);

        AssertCompiles(withCommentsAndStrings);
        Assert.Empty(VerifySanitized(withCommentsAndStrings));
        Assert.Empty(VerifyRaw(withCommentsAndStrings));
    }

    [Fact]
    public void This_qualified_self_call_and_lock_keep_success()
    {
        var mutated = Baseline
            .Replace(
                HelperEntry + "();",
                "this." + HelperEntry + "();",
                StringComparison.Ordinal)
            .Replace("_workspaceLock.", "this._workspaceLock.", StringComparison.Ordinal);

        AssertCompiles(mutated);
        Assert.Empty(VerifySanitized(mutated));
        Assert.Empty(VerifyRaw(mutated));
    }

    [Fact]
    public void This_qualified_published_solution_return_keeps_success()
    {
        var mutated = MutateMember(
            RawSignature,
            HelperSignature,
            NestedIndent + "return _solution;",
            NestedIndent + "return this._solution;");

        AssertCompiles(mutated);
        Assert.Empty(VerifyRaw(mutated));
    }

    [Fact]
    public void Direct_public_sync_call_from_async_entry_fails()
    {
        var mutated = MutateMember(
            AsyncSignature,
            SyncSignature,
            HelperCallStatement,
            NestedIndent + "return " + SyncEntry + "();");

        AssertCompiles(mutated);
        AssertViolation(
            VerifySanitized(mutated),
            $"{DemoType}.{AsyncEntry}: ссылка на публичный синхронный вход {SyncEntry}");
    }

    [Fact]
    public void This_qualified_public_sync_call_from_async_entry_fails()
    {
        var mutated = MutateMember(
            AsyncSignature,
            SyncSignature,
            HelperCallStatement,
            NestedIndent + "return this." + SyncEntry + "();");

        AssertCompiles(mutated);
        AssertViolation(
            VerifySanitized(mutated),
            $"{DemoType}.{AsyncEntry}: ссылка на публичный синхронный вход {SyncEntry}");
    }

    [Fact]
    public void Missing_lock_wait_fails()
    {
        var mutated = MutateMember(SyncSignature, RawSignature, BodyIndent + "_workspaceLock.Wait();\n", string.Empty);

        AssertCompiles(mutated);
        AssertViolation(VerifySanitized(mutated), "нет вызова _workspaceLock.Wait() в теле метода");
    }

    [Fact]
    public void Missing_lock_release_fails()
    {
        var mutated = MutateMember(
            SyncSignature,
            RawSignature,
            NestedIndent + "_workspaceLock.Release();\n",
            string.Empty);

        AssertCompiles(mutated);
        AssertViolation(VerifySanitized(mutated), "нет вызова _workspaceLock.Release() в finally защищённого try");
    }

    [Fact]
    public void Foreign_lock_receiver_fails()
    {
        var mutated = MutateMember(SyncSignature, RawSignature, "_workspaceLock", "_otherLock");

        AssertCompiles(mutated);
        AssertViolation(VerifySanitized(mutated), "Wait() вызван у _otherLock, а не у _workspaceLock");
    }

    [Fact]
    public void Release_outside_finally_fails()
    {
        var mutated = MutateMember(
            SyncSignature,
            RawSignature,
            SyncLockTakeBlock,
            BodyIndent + "_workspaceLock.Wait();\n"
            + BodyIndent + "Snapshot? snapshot = null;\n"
            + BodyIndent + "try\n"
            + BodyIndent + "{\n"
            + NestedIndent + "snapshot = " + HelperEntry + "();\n"
            + BodyIndent + "}\n"
            + BodyIndent + "finally\n"
            + BodyIndent + "{\n"
            + BodyIndent + "}\n"
            + "\n"
            + BodyIndent + "_workspaceLock.Release();\n"
            + BodyIndent + "return snapshot;");

        AssertCompiles(mutated);
        AssertViolation(VerifySanitized(mutated), "нет вызова _workspaceLock.Release() в finally защищённого try");
    }

    [Fact]
    public void Snapshot_read_before_lock_fails()
    {
        var mutated = MutateMember(
            SyncSignature,
            RawSignature,
            SyncLockBeforeTry,
            BodyIndent + "var snapshot = " + HelperEntry + "();\n"
            + BodyIndent + "_workspaceLock.Wait();\n"
            + BodyIndent + "try\n"
            + BodyIndent + "{\n"
            + NestedIndent + "return snapshot;\n"
            + BodyIndent + "}");

        AssertCompiles(mutated);
        AssertViolation(VerifySanitized(mutated), "должно быть внутри защищённого try");
    }

    [Fact]
    public void Task_result_in_sync_entry_fails()
    {
        var mutated = MutateMember(
            SyncSignature,
            RawSignature,
            BodyIndent + "_workspaceLock.Wait();",
            BodyIndent + "_workspaceLock.Wait();\n"
            + BodyIndent + "var ignored = Task.FromResult(0).Result;");

        AssertCompiles(mutated);
        AssertViolation(VerifySanitized(mutated), "обращение к члену Result");
    }

    [Fact]
    public void GetAwaiter_get_result_in_sync_entry_fails()
    {
        var mutated = MutateMember(
            SyncSignature,
            RawSignature,
            BodyIndent + "_workspaceLock.Wait();",
            BodyIndent + "_workspaceLock.Wait();\n"
            + BodyIndent + "Task.CompletedTask.GetAwaiter().GetResult();");

        AssertCompiles(mutated);
        AssertViolation(VerifySanitized(mutated), "обращение к члену GetAwaiter");
    }

    [Fact]
    public void WaitAsync_result_is_a_syntax_only_example()
    {
        // SemaphoreSlim.WaitAsync() returns Task without a Result property: the example does not
        // compile, so it is not treated as an executable mutation. Only the `.Result` detection is
        // checked here.
        var mutated = MutateMember(
            SyncSignature,
            RawSignature,
            BodyIndent + "_workspaceLock.Wait();",
            BodyIndent + "_workspaceLock.WaitAsync().Result;");

        Assert.NotEmpty(CompilationErrors(mutated));
        AssertViolation(VerifySanitized(mutated), "обращение к члену Result");
    }

    [Fact]
    public void Helper_call_only_inside_local_function_fails()
    {
        var mutated = MutateMember(
            SyncSignature,
            RawSignature,
            HelperCallStatement,
            NestedIndent + "Snapshot? Read()\n"
            + NestedIndent + "{\n"
            + NestedIndent + "    return " + HelperEntry + "();\n"
            + NestedIndent + "}\n"
            + "\n"
            + NestedIndent + "return _solution;");

        AssertCompiles(mutated);
        AssertViolation(VerifySanitized(mutated), "должно быть внутри защищённого try");
    }

    [Fact]
    public void Helper_call_only_inside_lambda_fails()
    {
        var mutated = MutateMember(
            SyncSignature,
            RawSignature,
            HelperCallStatement,
            NestedIndent + "Func<Snapshot?> read = () => " + HelperEntry + "();\n"
            + NestedIndent + "return read();");

        AssertCompiles(mutated);
        AssertViolation(VerifySanitized(mutated), "должно быть внутри защищённого try");
    }

    [Fact]
    public void Raw_entry_returning_sanitized_snapshot_fails()
    {
        var mutated = MutateMember(
            RawSignature,
            HelperSignature,
            NestedIndent + "return _solution;",
            NestedIndent + "return " + HelperEntry + "();");

        AssertCompiles(mutated);
        var violations = VerifyRaw(mutated);
        AssertViolation(violations, "тело не возвращает _solution");
        AssertViolation(violations, $"запрещённая ссылка на {HelperEntry}");
    }

    [Fact]
    public void Missing_target_method_is_a_diagnostic_error()
    {
        var mutated = MutateMember(
            SyncSignature,
            RawSignature,
            SyncSignature,
            MemberIndent + "public Snapshot? RenamedEntry()");

        AssertCompiles(mutated);
        AssertViolation(VerifySanitized(mutated), $"{SyncEntry}: метод не найден");
    }

    [Fact]
    public void Ambiguous_target_method_is_a_diagnostic_error()
    {
        var overload =
            MemberIndent + "public Snapshot? " + SyncEntry + "(int unused)\n"
            + MemberIndent + "{\n"
            + BodyIndent + "return _solution;\n"
            + MemberIndent + "}\n\n";

        var mutated = MutateWholeSource(SyncSignature, overload + SyncSignature);

        AssertCompiles(mutated);
        AssertViolation(VerifySanitized(mutated), "нужна однозначная сигнатура");
    }

    [Fact]
    public void Partial_type_is_a_diagnostic_error()
    {
        var mutated = MutateWholeSource(
                "internal sealed class DemoManager\n",
                "internal sealed partial class DemoManager\n")
            + "\ninternal sealed partial class DemoManager\n{\n}\n";

        AssertCompiles(mutated);
        AssertViolation(VerifySanitized(mutated), "partial-типы эта проверка не объединяет");
    }

    [Fact]
    public void Method_without_body_is_a_diagnostic_error()
    {
        const string source = """
            #nullable enable
            namespace Demo;

            internal abstract class DemoManager
            {
                public abstract Snapshot? GetSanitizedPublishedSolution();
            }

            internal sealed class Snapshot
            {
            }
            """;

        AssertCompiles(source);
        AssertViolation(VerifySanitized(source), "нет тела метода для проверки");
    }

    [Fact]
    public void Truncated_source_is_a_diagnostic_error()
    {
        var truncated = Baseline[..Baseline.IndexOf(HelperSignature, StringComparison.Ordinal)];

        AssertViolation(VerifySanitized(truncated), "исходник не разобран");
    }

    [Fact]
    public void Type_from_another_namespace_is_a_diagnostic_error()
    {
        var target = new SourceTarget(Baseline, DemoPath, "Other.Namespace", DemoType);

        AssertViolation(SanitizedEntryChecks.VerifySanitizedEntries(target), "тип не найден");
        AssertViolation(SanitizedEntryChecks.VerifyRawPublishedEntry(target), "тип не найден");
    }

    private static IReadOnlyList<string> VerifySanitized(string sourceText) =>
        SanitizedEntryChecks.VerifySanitizedEntries(new SourceTarget(sourceText, DemoPath, DemoNamespace, DemoType));

    private static IReadOnlyList<string> VerifyRaw(string sourceText) =>
        SanitizedEntryChecks.VerifyRawPublishedEntry(new SourceTarget(sourceText, DemoPath, DemoNamespace, DemoType));

    private static string CutMember(string signature, string terminator)
    {
        var start = Baseline.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"маркер члена не найден: {signature}");
        var end = Baseline.IndexOf(terminator, start, StringComparison.Ordinal);
        Assert.True(end > start, $"терминатор члена не найден: {terminator}");
        return Baseline[start..end];
    }

    /// <summary>Replaces a fragment inside the given member: the mutation does not touch neighbors.</summary>
    private static string MutateMember(string signature, string terminator, string oldValue, string newValue)
    {
        var member = CutMember(signature, terminator);
        Assert.Contains(oldValue, member, StringComparison.Ordinal);
        return Baseline.Replace(
            member,
            member.Replace(oldValue, newValue, StringComparison.Ordinal),
            StringComparison.Ordinal);
    }

    private static string MutateWholeSource(string oldValue, string newValue)
    {
        Assert.Contains(oldValue, Baseline, StringComparison.Ordinal);
        return Baseline.Replace(oldValue, newValue, StringComparison.Ordinal);
    }

    private static void AssertCompiles(string sourceText) => Assert.Empty(CompilationErrors(sourceText));

    private static void AssertViolation(IReadOnlyList<string> violations, string expectedFragment) =>
        Assert.Contains(violations, violation => violation.Contains(expectedFragment, StringComparison.Ordinal));

    private static IReadOnlyList<string> CompilationErrors(string sourceText)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceText, path: DemoPath);
        var compilation = CSharpCompilation.Create(
            "SourceStructureDemo",
            new[] { tree },
            _platformReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => $"{diagnostic.Id}: {diagnostic.GetMessage()}")
            .ToList();
    }

    private static IReadOnlyList<MetadataReference> CreatePlatformReferences()
    {
        var paths = new List<string>
        {
            typeof(object).Assembly.Location,
            typeof(SemaphoreSlim).Assembly.Location,
            typeof(Task).Assembly.Location,
        };

        var systemRuntime = Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!,
            "System.Runtime.dll");
        if (File.Exists(systemRuntime))
        {
            paths.Add(systemRuntime);
        }

        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
    }
}
