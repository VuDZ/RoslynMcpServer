# Affected tests probe

Standalone level-1 prototype. It is not part of the main solution and it does not change the server.
The host already excludes `spikes/**` from its compile.

The office solution is deferred: it is not a git repository. Run this against a public git solution
and record the commit the commands print. Roslyn (`Roslyn.slnx`) is the intended corpus. Limit the
graph with `--test-project` (the name may be repeated), for example two to four
`Microsoft.CodeAnalysis.CSharp` test projects, and write that scope down with the commit.

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

`oracle --member <documentation-id> --test-project <name> --clone-ack` inserts the `tia-probe` throw, runs the
level-0 test projects that sit in the selected scope, and restores the file. The JSON carries `fCha`, `fRta`,
`rCha`, `l0`, and `oracle` (units that failed after the mutation and passed in the control run). An invalid pair
names the reason (`timeout`, `build failed`, `missing results`, `zero executed`, `skipped mismatch`) and is not
a completeness point. Pass `--clone-ack` only on a clone or worktree. A backup named `*.tia-backup` is left next
to the file if the restore does not match the original bytes. Do not commit the mutation.

Initial reverse limits are depth 30, 20 000 members, and 60 seconds. Change them only by editing
`ProbeLimits` and recording the change in the measurement report.

Blind spots that are not edges: reflection, `dynamic`, attribute arguments, DI registration read from
configuration, and source that is not in the solution. Metadata and NuGet symbols are leaves.
The completeness campaign itself is a later run; this project does not publish that report.
