# Verification v2

Статус: **normative test plan**, не отчёт о выполнении.

## 1. Independent oracle

Production hydrate host сравнивается с отдельным fresh `MSBuildWorkspace` при
одинаковых effective properties/toolset/mode и стабильном дереве. Oracle не
обязан иметь тот же CLR host type. Нормализуются случайные IDs и допустимые temp
paths, но не graph/options, generated output, persistence, encoding или semantics.

Trace: V-01 — REJECT; исходный independent oracle сохранён.

Для supported generator in-process oracle сравнивает полный generated document
set/texts, exact marker/constant и diagnostics. Недоступный обязательный output
означает failed/not-run. Rejected generator profile проверяет только admission,
не equivalence.

Trace: E0-03, V-03 — ACCEPT WITH MODIFICATION.

## 2. Functional matrix

| ID | Scenario | Mandatory result |
|---|---|---|
| V01 | Two SDK projects + ProjectReference | Equal graph/references/navigation/diagnostics |
| V02 | Nullable/langversion/defines/encoding fixtures | Equal options, characters, semantics, round-trip bytes |
| V03 | Inner TFM/multi-target graph | Exact instances or rejected before hit |
| V04 | Analyzer/generator/AdditionalFiles/editorconfig | Full oracle for supported; admission-only for rejected |
| V05 | Required generated compile/config in `obj` | Hashed/probed; missing or changed → miss |
| V06 | Linked source + external import | All memberships/evidence or unsupported before hit |
| V07 | Text/document/project mutations after hydrate | Capability result, disk and next semantic consistent |
| V08 | Overlay on/off/omitted/reset/restart | Sticky semantics; no shadow refs on disk |
| V09 | Cancel/load/prepare failure and key change | No partial publication; ownership/dirty state correct |
| V10 | Changed bytes with same size/mtime | Strict detects; weaker mode reports weaker guarantee |
| V11a | Add/delete member, old hashes/csproj unchanged | `membership_changed`; no disk publication; correct fallback |
| V11b | Compile Remove/graph input change | Graph reason; no disk publication; correct fallback |
| V11c | New non-member control | No invalidation |
| V11d | Unchanged positive request | Actual disk hit; not merely fallback equality |
| V12 | Known absent `Exists` input becomes present | Miss/reject before hydrate |
| V13 | Ancestor props/NuGet/assets/SDK/current toolset | Incompatible/input miss |
| V14 | DLL/analyzer dependency replaced same path | Miss; no stale execution |
| V15 | Absent/explicit properties, TFM, schema | Distinct key or incompatible as specified |
| V16 | Junction cycle/retarget/path case; root-project worst-case scan | Bounded, platform-correct behavior or bounded fallback |
| V17 | Two writers/readers, crash, paused reader/PID reuse, competing cleanup | Whole generation or safe fallback; active generation retained |
| V18 | Corrupt/oversize/no-space/permissions/cleanup | Bounded resources; ordinary load works |
| V19 | Overflow, new file, directory rename/delete, editor temp file | Coverage recovery; temp is irrelevant and causes no DTB |
| V20 | Own write with suppressed echo/external overwrite | All memberships/revisions; newer event retained |
| V21 | Change during probe/load/hydrate/flush | Candidate reject/retry; queue preserved |
| V22 | Ancestor/linked/explicit `obj`/custom extension | Change detected or profile rejected |
| V23 | New PID, RAM, force, reset, false→true | Source/policy/capture/write/readiness distinct |
| V24 | Partial A/B/full scope | No key collision; per-tool coverage/refusal |
| V24a | Partial `get_test_list` vs explicit CLI target | Discovery reports requested roots/incomplete; CLI keeps disk target |
| V24b | Ambiguous project/binary resolution | Explicit error; never first-match selection |
| V25 | Metadata true/false and DLL states | Honest scope/fallback |
| V26 | Symbol index, same text/different defines | Candidates match context and Roslyn verification |

Trace: V-02 — ACCEPT WITH MODIFICATION; C-01, C-04, C-07, C-08,
E2-06, E3-03, E3-07 downstream fixtures.

## 3. Completeness and admission fixtures

Обязательны missing root/reference, wrapped soft warning, legally empty project,
unknown condition/custom target, absent path becoming present. Для каждого
profile результаты разделяются:

- positive equivalence: passed/failed/not run;
- negative admission: passed/failed/not run;
- unsupported: обнаружен до hydrate/publication.

Unsupported не является passed equivalence.

Trace: C-01, C-04, E0-02.

## 4. Lifecycle and concurrency

Проверить same-key/different-key, old/candidate ownership, cancel, prepare
failure, simultaneous semantic/edit/reset, stale candidate, event during flush,
reader lazy lifetime, no recursive acquire и запрет write на stale/unknown.

Trace: C-02, C-08, E1-02, E3-01.

## 5. Measurements

До результатов фиксируются target workload и numeric budgets. Сценарии:

1. disabled baseline;
2. absent-cache miss + capture;
3. disk hit;
4. RAM hit;
5. source edit/restart;
6. no-op build, build, edit+build+restart;
7. live unchanged/content/graph/overflow.

Для каждого attempt записываются source, hit/miss reason, capture/write outcome,
changed input categories, visited entries, bytes, probe/capture/hydrate/prepare/
first-semantic durations, DTB count, peak memory и cache size. Не менее 10
повторов для ориентировочных median/p95; порядок чередуется, OS cache state
указывается.

Trace: C-06, E0-04, V-04 — ACCEPT WITH MODIFICATION.

## 6. Decision gates

- `experiment allowed`: functional evidence может быть fixture-only.
- `implementation allowed`: production lifecycle regression matrix пройдена.
- `public activation allowed`: ноль unexplained semantic differences,
  admission/store guarantees, actual cross-process hit, заранее утверждённый
  e2e budget и acceptable hit-rate на named workload.
- `next epoch allowed`: blockers соответствующей эпохи закрыты.
- `series complete`: O1–O8 закрыты, включая O4/O5.

Zero-DTB, `default=false` и store correctness отдельно не разрешают activation.
Не утверждать выполнение build/test/runtime checks, если они не запускались.

Trace: E2-02 — REJECT; E2-06, V-04, H-01 — ACCEPT WITH MODIFICATION.
