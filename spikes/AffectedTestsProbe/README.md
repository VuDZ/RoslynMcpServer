# Affected tests probe

Standalone level-1 prototype. It is not part of the main solution and it does not change the server.
The host already excludes `spikes/**` from its compile.

The `oracle`, `summary`, and `sample` commands need a public git solution that builds; record the commit the
commands print. For a solution that is not in git or does not build, use `impact` (below). Roslyn (`Roslyn.slnx`) is the intended corpus. Limit the
graph with `--test-project` (the name may be repeated), for example two to four
`Microsoft.CodeAnalysis.CSharp` test projects, and write that scope down with the commit.

## Office run without a build (`impact`)

`impact` answers "which tests does this edit affect" by reverse search only. It needs a solution that loads in
Roslyn (restored packages), not one that builds, and it needs neither git nor a clone: it changes no files.
It reports what each answer costs and how much smaller it is than level 0. It does not measure completeness;
that needs the mutation oracle, and the oracle needs a build.

```powershell
dotnet run --project spikes/AffectedTestsProbe/AffectedTestsProbe.csproj -c Release -- impact --solution <path.sln> --files files.txt --sample 20 --out impact-run
```

- `--files` lists source files you edited (one path per line, relative to the solution directory or absolute).
  Each file is one query over every member with a body in it, as an edit of that file would start it.
- `--members` lists documentation ids (`M:Ns.Type.Method(System.Int32)`), one query per member.
- `--sample N --seed S` adds N random members with a body from production projects that a test project depends on.
  A project whose name contains `Test` is not sampled. Without `--files` and `--members` the sample is 20.
- `--test-project <name>` limits the test projects; by default every test project is used.
- `--no-quality` skips the final load-quality pass (declaration errors and projects without framework references).

The run writes two files into `--out`:

- `raw.json` — solution path, files, member ids, and the test names of each answer. Keep it private.
- `summary.json` — counts, member kinds, times, and memory only. It has no names or paths and may be shared.

For each query the summary has the answer (`reverse`; `level0` when a limit was hit; `missing`), search time,
visited members, the size of the answer and of level 0 in test methods, classes, and projects, their ratio, and the
memory still held after a full collection. Phases (`load`, `prewarm`, `test-roots`, `sample`, `load-quality`) have
their time and memory too. The first query pays for the compilations it opens; later queries reuse them.

Read `quality` before trusting the numbers: projects with `noFramework` were not restored, and many declaration
errors such as `CS0246` mean missing references. Reverse search over such projects misses references.

## Memory

The forward walk, the sample, the test-root index, and fact extraction keep a bound tree only while that
project is being read. Those passes do not call `Project.GetCompilationAsync` or `Document.GetSemanticModelAsync`:
the workspace would retain every semantic model until the process exits. On the compiler solution that was
tens of gigabytes after the facts had already been copied out. Each C# project is compiled from its own
syntax trees, the bodies are read, and that compilation is dropped. A second compilation of the same trees
never receives a semantic model and is held only so the next project can see its source. Document syntax
trees remain in the workspace. A Visual Basic project is held as that kind of reference; its bodies are not
bound. A source type's key includes the project, so two target frameworks of one assembly stay distinct.

`SymbolFinder`, used by reverse search, needs symbols that belong to the workspace, so that path still
calls `GetCompilationAsync` and `GetSemanticModelAsync`. Those models stay for the process. A single
`reverse` process drops them on exit. `reverse-batch` keeps every compilation that search opened.

## Precheck

The shared edge rules are checked on a small in-memory solution, not on this repository:

```powershell
dotnet run --project spikes/AffectedTestsProbe/AffectedTestsProbe.csproj -c Release -- precheck
```

Both directions must find a production member that is reached only from a fixture constructor, a fixture
`InitializeAsync`, a static constructor, an instance field initializer, or one test method's data source.
`dotnet test spikes/AffectedTestsProbe/AffectedTestsProbe.Tests/AffectedTestsProbe.Tests.csproj -c Release`
runs the same check plus the oracle's case-pairing rules.

## Commands

`summary` builds the forward CHA and RTA graphs.

```powershell
dotnet run --project spikes/AffectedTestsProbe/AffectedTestsProbe.csproj -c Release -- summary --solution <Roslyn.slnx> --test-project <TestProject> --out summary.json
```

`sample` freezes frame B (the completeness sample) before building either graph, then selects frame A
from the forward graph. The printed hash is the frozen list.

`reverse` answers one member in a fresh process and does not build the forward graph. Its JSON has `prepareMs`
(load, prewarm, and test-root binding) and `search.elapsedMs` (the walk only). Add them for the first-query price.
`reverse-batch --members ids.txt --repeat 3` is one process: the first member is `sequential-first`, later members
are `subsequent`, and the repeats are `warmed`. That first member is not part of the first-query series.
A search that hits a limit reports level 0 for that member.

`oracle --member <documentation-id> --test-project <name> --clone-ack` inserts the `tia-probe` throw, runs each
level-0 target framework in the selected scope as its own `dotnet test -f`, and restores the file. Fully qualified
names come from `TestExecution.StatsChange` and the last batch on `TestExecution.Completed` in that run's `--diag`
log. The JSON carries `fCha`, `fRta`, `rCha`, `l0`, and `oracle` (units that failed after the mutation and passed
in the control run). An invalid pair names the reason (`timeout`, `build failed`, `crash`, `missing results`,
`zero executed`, `skipped mismatch`) and does not report a judgment. The mutation timeout is three times that
target's control run, and at least five minutes. Pass `--clone-ack` only on a clone or worktree. A backup named
`*.tia-backup` is left next to the file if the restore does not match the original bytes. Do not commit the mutation.

Initial reverse limits are depth 30, 20 000 members, and 60 seconds. Change them only by editing
`ProbeLimits` and recording the change in the measurement report.

Blind spots that are not edges: reflection, `dynamic`, attribute arguments, DI registration read from
configuration, and source that is not in the solution. Metadata and NuGet symbols are leaves.
The completeness campaign itself is a later run; this project does not publish that report.
