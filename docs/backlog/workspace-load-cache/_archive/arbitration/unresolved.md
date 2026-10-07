# Неразрешённые вопросы

Эти вопросы нельзя решить из имеющихся требований, кода и документального
evidence. Они не разрешают исполнителю выбрать удобный вариант молча.

## U-ARB-01 — Capture scheduling и durable cadence

Связанные findings: E2-05, E3-06.

Не выбраны:

- foreground capture до ответа `load_workspace` или background capture;
- момент, когда ответ может сообщать write outcome;
- допустимая добавочная latency;
- ownership/cancellation при reset, shutdown и смене generation;
- cadence durable capture после content changes.

Нужны измерения capture duration/success rate на целевой нагрузке, частоты
конкурирующих writes и допустимого response budget. Idle не является
доказательством стабильности. После evidence выбрать schedule, bounded retry,
status delivery и shutdown contract. До решения ни background, ни synchronous,
ни «писать после каждой правки» не являются нормативными.

## U-ARB-02 — Live freshness, silent lost events и availability

Связанные findings: E3-02, E3-04; также C-02, E3-01, E4-02.

O4 требует видеть внешний sync на следующем semantic call, O8 запрещает тихий
stale result. Материалы не определяют:

- обязан ли каждый call обнаруживать событие, потерянное watcher без overflow/error;
- допустимы ли stable-tree assumptions или периодическое подтверждение;
- maximum unchanged-call overhead;
- можно ли выдавать last-known snapshot с `freshness=unknown`;
- какие read operations могут это делать и какие обязаны отказать;
- как unknown сочетается с analyzer `Banned`/`Unavailable`;
- timeout/retry behavior при временно locked DLL/config.

Нужны workload measurements и product decision freshness-vs-availability.
Независимо от выбора write/refactoring на stale/unknown base остаются запрещены
A-WRITE. Нельзя ослаблять analyzer admission ради доступности.

## U-ARB-03 — Репрезентативная нагрузка и численный budget

Связанные findings: R-06, E0-02, E0-04, E2-04, V-03, V-04.

Документы не называют конкретное большое решение и не устанавливают 70% как
первичное требование. До public activation владелец должен зафиксировать:

- target solution и размер;
- обязательные SDK, TFM/project-instance contexts, generators, imports/tasks;
- overlay/metadata modes;
- смесь unchanged/edit/build/restart/live calls;
- median/p95, miss-overhead, hit-rate и resource budgets.

Если обязательная feature target solution не поддерживается, результат — revise
для этой цели, а не общий success по меньшему fixture.

## U-ARB-04 — Portable analyzer provenance и cache trust

Связанные findings: R-04, C-05, C-01; Astra N2/N3/N7.

Текущий analyzer provenance привязан к `LoadSessionId`/`ProjectId` и не переносится
между процессами. Cache manifest и checksum, изменяемые одним principal, сами по
себе не доказывают, что DLL принадлежит текущему project instance.

Нужно установить:

- переносимый project-instance/analyzer identity;
- evidence source и повторную проверку current toolset/SDK resolution;
- способ создания нового same-session admission без обхода gate;
- threat model: corruption, other principal, same-user modification;
- trusted storage/permissions;
- allowlist persisted environment/global properties, secret retention и logging.

До доказательства disk hydrate может восстановить base graph, но не получает
статус overlay-ready и не загружает analyzer DLL только по cache DTO.

## U-ARB-05 — Production hydrate host и mutating contract

Связанные findings: R-01, E0-01, E1-01.

Публичные API не доказывают выбранный host без эксперимента. Требуется решить:

- какой Workspace реально hydrates без DTB;
- поддерживается ли весь текущий edit API;
- допустим ли reload-on-project-mutation;
- кто persist-ит add/remove/rename document и project changes;
- как сообщаются preflight rejection и partial persistence.

Adhoc read-equivalence не решает persistence; `MSBuildWorkspace.Open*` не решает
skip-DTB. До выбора production hydrate не активируется.

## U-ARB-06 — Dependency-closure evidence

Связанные findings: R-02, C-01, E0-02, E2-01.

Не доказано, способен ли существующий binlog/capture или поддерживаемый MSBuild
API дать полный набор positive, negative и target dependencies для полезного
closed profile. Нужен spike с условным `Exists`, absent import, wildcard region,
custom target, generated configs и runtime-version matrix.

Если полнота не доказана, соответствующий profile отвергается до hit. Нельзя
компенсировать отсутствие evidence allowlist-ом имени SDK или checksum-ами только
известных файлов.
