# Unresolved arbitration issues

Статус: **normative open gates**. Этот документ сохраняет все нерешённые
арбитражные вопросы без выбора варианта.

## U-ARB-01 — Capture scheduling и durable cadence

Findings: E2-05 — UNRESOLVED; E3-06 — ACCEPT WITH MODIFICATION.

Не выбраны foreground/background capture, момент delivery write outcome,
добавочная latency, ownership/cancellation при reset/shutdown/generation change,
bounded retry и cadence после content changes. Нужны measurements target
workload, capture duration/success и competing writes. Idle не доказывает
стабильность. До решения ни sync, ни background, ни write-after-each-edit не
являются normative.

## U-ARB-02 — Live freshness, lost events и availability

Findings: E3-02, E3-04 — UNRESOLVED; связаны C-02, E3-01, E4-02.

Не определены: обнаружение silent lost event на каждом call, stable-tree/
periodic assumptions, maximum unchanged-call overhead, допустимость
`freshness=unknown`, разрешённые read operations, отказ обязательных operations,
совместимость с `Banned`/`Unavailable`, timeout/retry locked inputs.
Нужны workload measurements и product decision freshness-vs-availability.
A-WRITE всегда запрещает write/refactoring на stale/unknown.

## U-ARB-03 — Репрезентативная нагрузка и численный budget

Findings: R-06, E0-02, E0-04, E2-04, V-03, V-04.

До activation владелец фиксирует target solution/size, SDK, TFM/instances,
generators/imports/tasks, overlay/metadata, workload mix, median/p95,
miss-overhead, hit-rate и resource budgets. 70% не является восстановленным
требованием. Unsupported обязательная target feature означает revise для цели.

## U-ARB-04 — Portable analyzer provenance и cache trust

Findings: R-04, C-05, C-01; risks N2/N3/N7.

Не установлены portable project-instance/analyzer identity, independent current
toolset resolution, создание нового same-session admission, threat model для
corruption, other OS principal и same-user modification, storage/permissions,
allowlist environment properties, secret retention/logging.
До доказательства disk hydrate может восстановить base graph, но не получает
overlay-ready и не загружает analyzer DLL только по DTO.

## U-ARB-05 — Production hydrate host и mutating contract

Findings: R-01, E0-01, E1-01.

Не выбран Workspace host без DTB, поддержка текущего edit API,
reload-on-project-mutation, владелец add/remove/rename/project persistence и
preflight/partial reporting. Adhoc read equivalence не решает persistence;
`MSBuildWorkspace.Open*` не решает skip-DTB. До решения production hydrate
не активируется.

## U-ARB-06 — Dependency-closure evidence

Findings: R-02, C-01, E0-02, E2-01.

Не доказано, даёт ли binlog/capture или supported MSBuild API полный набор
positive, negative и target dependencies полезного closed profile. Нужен spike:
conditional `Exists`, absent import, wildcard region, custom target, generated
configs и runtime-version matrix. Без полноты profile отвергается до hit; SDK
name или hashes только известных files недостаточны.
