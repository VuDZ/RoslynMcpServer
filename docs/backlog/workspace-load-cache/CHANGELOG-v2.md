# Changelog — proposal v1 to proposal-v2

Все изменения ниже обязательны по арбитражу. Исходные proposal/review/response/
arbitration файлы не изменены.

## Material changes

1. **Baseline и цели.** Основание обновлено с 1.3.5 до commit
   `9867318ddb5294ce144bf024b9a61a1a2e3814c3`, source 1.3.21; running MCP
   отделён. O1–O8 и A-LOAD/A-STICKY/A-WRITE/A-ADMISSION/A-PROVENANCE/A-LOADER
   включены в standalone spec. [R-03, E1-06]
2. **Этапность.** Epoch 2 переименована в unchanged-restart checkpoint; O4/O5
   обязательны для series completion; v1 не объявлен отменённым.
   [R-05, E2-04]
3. **Пять decision authorities.** Одно `go` заменено на experiment,
   implementation, public activation, next epoch и series complete.
   [E0-04, E2-06, H-01]
4. **Target profile.** До activation обязательны named solution, SDK/TFM/
   instances/features и заранее утверждённый numeric budget; multi-target target
   требует exact inner mapping. [R-06, E0-02, V-03, V-04]
5. **Host/persistence gate.** Epoch 0 теперь сначала выбирает production hydrate
   host и capability/write contract; Open+substitution не считается skip-DTB;
   silent read-only/custom-writer/reload решения запрещены.
   [R-01, E0-01, E1-01]
6. **State vocabulary.** Разделены request/fingerprint, base source,
   requested/effective cache and overlay, capture/write, validation, readiness,
   completeness, generation/publication. [R-04, C-04, C-05, E2-03, E4-02]
7. **Dependency closure.** Добавлены positive, known-absent, bounded regions,
   target inputs и evidence version; unknown отвергает request до hydrate/DLL
   load. [R-02, C-01, E0-02, E2-01]
8. **Completeness.** Формальный predicate roots/instances/edges/inputs отделён
   от ordinary diagnostics и analyzer capture; empty project имеет policy.
   [C-04]
9. **Две data tables.** Semantic hydration DTO отделён от dependency/admission
   evidence. [E1-07]
10. **Overlay readiness.** Capture берётся из base без shadow/session identity;
    hydrate проходит portable reconstruction, prepare/gate и publication; disk
    source не означает overlay-ready. [R-04, C-05, E1-04]
11. **Decoding.** Effective encoding/BOM/fallback/null/write-back и
    character/round-trip oracle заменили «encoding при необходимости».
    [C-09]
12. **Generated/obj inputs.** Значимые inputs остаются hashed; добавлены
    no-op/build/edit+build measurements вместо недоказанного исключения.
    [C-06]
13. **Lifecycle pipeline.** Discovery, coverage, capture/hydrate, validation,
    prepare/gate и publication разделены; добавлена transition/ownership table и
    разграничены store/manager/filesystem/CLR atomicity. [C-02, E1-02, E3-01]
14. **Membership и context.** `path -> all memberships` для sync отделён от
    deterministic query/edit context. [E1-03]
15. **Reconciliation boundary.** External disk event не создаёт project mutation;
    intentional AddDocument проходит capability/A-WRITE; добавлен byte-level
    baseline test. [E1-05]
16. **Reader ownership/store.** До activation требуется acquire/release,
    pause/crash/PID reuse/lazy lifetime и safe cleanup; store gate отделён от
    product activation. [C-08, E2-06]
17. **Bounded scanning.** Заданы roots/walk-up/explicit/negative regions,
    symlink policy и resource budget; budget exhaustion даёт fallback, не
    truncated hit. [C-07]
18. **Event classifier.** Explicit/potential/irrelevant/unknown roles заменили
    «любой Created → graph-dirty»; content-only разрешён только доказанной
    Document role, graph role приоритетна. [E3-03, E3-05]
19. **Watch coverage.** Добавлены ancestor, explicit `obj`, absent locations,
    grouping/limits и untrusted transition при watcher failure. [E3-07]
20. **Own-write protocol.** Time suppression заменён требованиями фактически
    saved bytes/hash/revision и pending state при mismatch/ABA. [E3-08]
21. **Three cadences.** RAM/index freshness, old disk validity и durable capture
    cadence разделены. [E3-06]
22. **Validation compatibility.** Strict/stat/watcher получают effective mode и
    producer-reader matrix; strict reader weak payload перепроверяет или misses.
    [E4-02]
23. **Partial tool contracts.** Для каждого semantic/refactoring tool требуется
    coverage result либо отказ; syntax test discovery отделён от CLI disk target
    и ambiguity не угадывается. [E4-03, E4-04]
24. **Verification oracle.** Full generated set/text/marker/diagnostics добавлен;
    fresh MSBuild остаётся независимым oracle. [E0-03, V-03; V-01 REJECT]
25. **V11.** Membership, graph input, non-member control и positive unchanged hit
    стали отдельными cases/reasons. [V-02]
26. **Performance.** Measurements охватывают miss+capture, disk/RAM hit,
    source edit, no-op/build/edit+build, live states, per-attempt reasons,
    prepare/first-semantic и hit-rate. [C-06, E0-04, V-04]

## Preserved unresolved gates

- Capture scheduling на момент v2 не выбран. [E2-05 — UNRESOLVED;
  E3-06] → U-ARB-01. Решение 2026-10-02 — в разделе ниже.
- Strict/watch/stale-read policy на момент v2 не выбрана. [E3-02, E3-04 — UNRESOLVED]
  → U-ARB-02. Решение 2026-10-01 — в разделе ниже.
- Workload/budget сохранён как U-ARB-03, без самостоятельного решения на
  момент v2. Production host на момент v2 не был выбран.

## Owner decisions after v2

- U-ARB-02, 2026-10-01: доверие watcher между подтверждениями.
  [UNRESOLVED-v2](UNRESOLVED-v2.md), [epoch-3](epoch-3-live-consistency/spec.md).
- U-ARB-06, 2026-10-01: admission profile `sdk-project-v1`.
  [UNRESOLVED-v2](UNRESOLVED-v2.md), [epoch-0](epoch-0-feasibility/spec.md).
- U-ARB-04, 2026-10-01: повторная проверка DLL и новый `LoadSessionId`.
  [UNRESOLVED-v2](UNRESOLVED-v2.md), [epoch-0](epoch-0-feasibility/spec.md).
- U-ARB-05, 2026-10-01: `AdhocWorkspace` и writer только для `.cs`.
  [UNRESOLVED-v2](UNRESOLVED-v2.md), [epoch-0](epoch-0-feasibility/spec.md).
- U-ARB-01, 2026-10-02: запись поколения до ответа `load_workspace`.
  [UNRESOLVED-v2](UNRESOLVED-v2.md), [epoch-2](epoch-2-conservative-disk-cache/spec.md).
- 2026-10-03: `sdk-project-v1` расширен `Microsoft.NET.Sdk.Razor` и
  `Microsoft.NET.Sdk.Web` (глобы `**/*.cshtml`, `**/*.razor`, `wwwroot/**`
  и хеш DLL `source-generators`). Пакетный статический ассет и свой target
  остаются `unknown`.
  [epoch-0](epoch-0-feasibility/spec.md#расширение-профиля-razor-и-web).
- 2026-10-03: baseline обычной загрузки офисного ПК — отчёт
  `20260922-120221-bondarev`. Hit-rate и miss-overhead не заданы.
  [epoch-0](epoch-0-feasibility/spec.md#выбранный-baseline-офисного-пк).

- 2026-10-03: OrchardCore/Roslyn не обязательны для target workload.
  Проверен кандидат BTCPayServer `v2.4.3`; baseline снят, admission
  и U-ARB-03 остаются открытыми.
  [BTCPayServer candidate](btcpay-candidate.md).

## Closed rejected recommendations

- Absent/explicit key semantics не изменены. [C-03 — REJECT]
- Epoch 2 acceptance не сведена к zero-DTB. [E2-02 — REJECT]
- Closed-subset 4C не удалена как «общий interpreter». [E4-01 — REJECT]
- Fresh MSBuild oracle не заменён same-host oracle. [V-01 — REJECT]

## Организация выполнения — 2026-10-03

Эпохи перенесены в каталоги `epoch-N-name/`, спецификации — в `spec.md`.
Каждая эпоха получила карту выполнения `README.md` и task-файлы с dependencies,
оценкой сложности 1–5, моделью Astra/Sol/Luna, границами правок и evidence.
Общие правила — [task-execution](task-execution.md). Incoming/outgoing ссылки
обновлены; текст требований эпох сохранён за исключением путей ссылок.
Архивные локальные спецификации остаются на прежних местах.

U-ARB-03 отложен до технической реализации кеша и вынесен в E4/task-13;
утверждение budgets всё равно предшествует cache-hit performance results.
Optional 4A/4B/4D имеют самостоятельную приёмку; обязательный O5 идёт через 4C
либо иной доказанный механизм. Суффикс модели — рекомендация, не acceptance gate.

## Migration and runtime impact

Код, public tools, defaults, product docs и версии не изменены. Cache v2 ещё не
выпущен, поэтому data migration не определена; будущая несовместимая schema даёт
miss. Evaluation cache обязан иметь namespace/lifetime отдельно от analyzer
shadow generations.

## Исправления execution pool — 2026-10-03

- Условная optional acceptance разделена на E4/task-12 (4A), task-14 (4B),
  task-15 (4D), с явными Depends on к соответствующей реализации.
- Все handoff/acceptance tasks имеют independent-acceptance role и fresh-session
  constraint: reviewer не автор design эпохи или проверяемой реализации.
  Модель Astra не является identity исполнителя.
- E4/task-16 готовит workload/budget/performance evidence; task-13 — отдельный
  независимый main-scope series gate. Optional activation verdicts туда не входят.
- У 22 Sol/Luna tasks есть четыре списка типов/files/signatures и явный packet
  producer. Design не accepted без окончательных implementation packets.
- E0/task-00 начинается с [current baseline audit](epoch-0-feasibility/current-baseline-audit.md):
  current source 1.5.4 vs historical 1.3.21, reused paths и remaining delta.
  Недоступный sdk-project-v1 spike harness отмечен evidence gap; E1/E3 contracts
  потребляют named audit. Канонические spec.md не переписаны.

## Профиль исполнителя задач — 2026-10-03

Во всех 48 задачах указаны рекомендуемый reasoning, риск ошибки с конкретной
причиной и необходимые способности из общего словаря. Карты эпох показывают
модель/reasoning, сложность и риск; модельные суффиксы Luna/Sol/Astra сохранены.
Оценка сложности 1–5 остаётся для человека; complexity=low/medium/high её
не дублирует. Риск отражает последствия ошибки, способности — требования
к исполнителю; они не дают дополнительных permissions и не меняют Depends on.
Правила и словарь — [task-execution](task-execution.md).
