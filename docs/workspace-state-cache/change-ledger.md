# Change ledger — specification v2

Дата: **2026-10-07**. Scope: применение всех 14 P к тексту specification и связанных
tasks; не implementation/activation acceptance. Основание — [Proposed Changes](archive/arbitration/proposed-changes.md)
и [Decision Ledger](archive/arbitration/decision-ledger.md). Original P остаются PROPOSED
в неизменённом historical input; запрос владельца разрешает подготовить новую версию,
не присваивает результаты spikes. Последующие human decisions H-001/H-002 выбирают XML exclusion и strong-name B; U-001 resolved.

Все **14 Status: applied** означают применение предписанного текста, включая
нормативные open gates P-001/P-011. Частично пропущенных либо blocked P нет.
U-001 и outcomes S-001/S-002 не входят в applied verdict; см. [unresolved](unresolved.md),
[spikes](spikes.md) и [revision blockers](revision-blockers.md). Не переоценивались
16 findings/15 clusters; полного REJECT/DEFERRED в arbitration нет.

Affected перечисляет основной contract и task sections. Каждый task сохраняет
исходную структуру/ID; изменённые contracts отмечены «Уточнения specification v2».
Механические изменения индексов/ссылок вынесены в Derived consistency change ниже.
Проверка фактического состояния — [revision report](preparation-report.md).

## P-001

Status: applied

Ранний capability gate. Arbitration: **SPIKE**, [ARB-002](archive/arbitration/findings/ARB-002.md). Sources: [review-ds/F-003](archive/review-ds/findings/F-003.md).

Affected:
- [README.md](README.md) — соответствующий contract раздел.
- [epoch-0-snapshot-roundtrip/spec.md](epoch-0-snapshot-roundtrip/spec.md) — соответствующий contract раздел.
- [epoch-0-snapshot-roundtrip/README.md](epoch-0-snapshot-roundtrip/README.md) — соответствующий contract раздел.
- [execution.md](execution.md) — соответствующий contract раздел.
- [spikes.md](spikes.md) — соответствующий contract раздел.
- [epoch-0-snapshot-roundtrip/task-00-current-seams-and-controls-med.md](epoch-0-snapshot-roundtrip/task-00-current-seams-and-controls-med.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-0-snapshot-roundtrip/task-02-snapshot-schema-hi.md](epoch-0-snapshot-roundtrip/task-02-snapshot-schema-hi.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-0-snapshot-roundtrip/task-03-ordinary-capture-med.md](epoch-0-snapshot-roundtrip/task-03-ordinary-capture-med.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-0-snapshot-roundtrip/task-04-snapshot-codec-low.md](epoch-0-snapshot-roundtrip/task-04-snapshot-codec-low.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-0-snapshot-roundtrip/task-05-isolated-hydrate-hi.md](epoch-0-snapshot-roundtrip/task-05-isolated-hydrate-hi.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-0-snapshot-roundtrip/task-06-real-cross-process-roundtrip-med.md](epoch-0-snapshot-roundtrip/task-06-real-cross-process-roundtrip-med.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-0-snapshot-roundtrip/task-08-real-control-capability-hi.md](epoch-0-snapshot-roundtrip/task-08-real-control-capability-hi.md) — «Уточнения specification v2» / acceptance controls.

Implementation:
Порядок U-001 → bounded S-001 → accepted executable positive slice. Schema sketch/inspection допустимы раньше, full capture не prerequisite собственного schema. Dependent positive codec/hydrate требуют successful capability. Negative report блокирует positive scope и возвращает его на решение; references не удаляются, negative-only не принят.

Notes:
S-001 not-run; итог эксперимента не выбран. U-001 resolved по H-001/H-002 (B). Отдельный task-08 не заменяет full cross-process round-trip.

## P-002

Status: applied

Identity и матрица параметров. Arbitration: **ACCEPT WITH MODIFICATION**, [ARB-003](archive/arbitration/findings/ARB-003.md). Sources: [review-ds/F-001](archive/review-ds/findings/F-001.md), [review-grok/F-005](archive/review-grok/findings/F-005.md).

Affected:
- [README.md](README.md) — соответствующий contract раздел.
- [epoch-1-disk-cache-mvp/spec.md](epoch-1-disk-cache-mvp/spec.md) — соответствующий contract раздел.
- [epoch-0-snapshot-roundtrip/spec.md](epoch-0-snapshot-roundtrip/spec.md) — соответствующий contract раздел.
- [benchmark.md](benchmark.md) — соответствующий contract раздел.
- [epoch-0-snapshot-roundtrip/task-02-snapshot-schema-hi.md](epoch-0-snapshot-roundtrip/task-02-snapshot-schema-hi.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-0-snapshot-roundtrip/task-06-real-cross-process-roundtrip-med.md](epoch-0-snapshot-roundtrip/task-06-real-cross-process-roundtrip-med.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-1-disk-cache-mvp/task-06-load-save-and-restore-hi.md](epoch-1-disk-cache-mvp/task-06-load-save-and-restore-hi.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-1-disk-cache-mvp/task-09-restart-and-failure-integration-med.md](epoch-1-disk-cache-mvp/task-09-restart-and-failure-integration-med.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-1-disk-cache-mvp/task-10-mvp-benchmark-med.md](epoch-1-disk-cache-mvp/task-10-mvp-benchmark-med.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-2-content-refresh/task-06-content-restart-benchmark-med.md](epoch-2-content-refresh/task-06-content-restart-benchmark-med.md) — «Уточнения specification v2» / acceptance controls.

Implementation:
Разделены raw requested с absence, independently pre-open merged globals по ordinary precedence и evaluated values. Canonical path/current globals используются для disk admission; absent не wildcard, saved defaults не oracle, RAM matcher прежний. Закрытая матрица определяет workspace/config/platform/TFM, overlay fresh admission, buildArgs suffix, presentation flags и policy/bypass. Добавлены omitted/Release, FileSettings, inherited Loaded*, Any CPU и mode controls.

Notes:
Split keys либо shared clean base с доказанной parity/fresh mode admission допустимы. Более строгие partitions дают лишь лишние misses; key encoding не назначен.

## P-003

Status: applied

Overlay support и fallback. Arbitration: **ACCEPT WITH MODIFICATION**, [ARB-004](archive/arbitration/findings/ARB-004.md). Sources: [review-ds/F-002](archive/review-ds/findings/F-002.md).

Affected:
- [README.md](README.md) — соответствующий contract раздел.
- [epoch-1-disk-cache-mvp/spec.md](epoch-1-disk-cache-mvp/spec.md) — соответствующий contract раздел.
- [benchmark.md](benchmark.md) — соответствующий contract раздел.
- [epoch-1-disk-cache-mvp/task-05-fresh-session-preparation-hi.md](epoch-1-disk-cache-mvp/task-05-fresh-session-preparation-hi.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-1-disk-cache-mvp/task-09-restart-and-failure-integration-med.md](epoch-1-disk-cache-mvp/task-09-restart-and-failure-integration-med.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-1-disk-cache-mvp/task-10-mvp-benchmark-med.md](epoch-1-disk-cache-mvp/task-10-mvp-benchmark-med.md) — «Уточнения specification v2» / acceptance controls.

Implementation:
Без доказанного fresh binding overlay-on выполняет whole-request ordinary load до disk publication; это достаточный минимальный E1 outcome. Не фабриковать provenance, не удалять refs, не создавать cache-induced Unavailable для пригодного ordinary request. Реальные bans/restart-required остаются. New-PID overlay benchmark показывает actual fallback отдельно от off.

Notes:
Обязательный overlay hit и permanent ban adapter не введены. Existing classifier-or-adapter freedom сохранена; отдельный overlay human choice не требуется по ARB-004.

## P-004

Status: applied

Graph/input generation binding. Arbitration: **ACCEPT**, [ARB-005](archive/arbitration/findings/ARB-005.md). Sources: [review-astra/F-001](archive/review-astra/findings/F-001.md).

Affected:
- [README.md](README.md) — соответствующий contract раздел.
- [epoch-1-disk-cache-mvp/spec.md](epoch-1-disk-cache-mvp/spec.md) — соответствующий contract раздел.
- [epoch-2-content-refresh/spec.md](epoch-2-content-refresh/spec.md) — соответствующий contract раздел.
- [epoch-1-disk-cache-mvp/task-00-input-manifest-hi.md](epoch-1-disk-cache-mvp/task-00-input-manifest-hi.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-1-disk-cache-mvp/task-06-load-save-and-restore-hi.md](epoch-1-disk-cache-mvp/task-06-load-save-and-restore-hi.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-1-disk-cache-mvp/task-09-restart-and-failure-integration-med.md](epoch-1-disk-cache-mvp/task-09-restart-and-failure-integration-med.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-2-content-refresh/task-04-refreshed-envelope-capture-med.md](epoch-2-content-refresh/task-04-refreshed-envelope-capture-med.md) — «Уточнения specification v2» / acceptance controls.

Implementation:
Reusable capture требует evidence, что evaluated options/memberships/references принадлежат manifest generation. Post-load stability не binding; pre/post equality не ABA proof. Новая dependency/region требует bounded reconfirmation либо запрета capture. Нет binding → no reusable capture при сохранении успешного ordinary load. Deterministic props-after-consumption control обязателен, контракт действует и после E2 refresh.

Notes:
Dependency path source P-011 и consumed bytes — разные доказательства. Universal closure/filesystem snapshot не назначены.

## P-005

Status: applied

Environment policy. Arbitration: **ACCEPT**, [ARB-006](archive/arbitration/findings/ARB-006.md). Sources: [review-astra/F-002](archive/review-astra/findings/F-002.md).

Affected:
- [README.md](README.md) — соответствующий contract раздел.
- [epoch-1-disk-cache-mvp/spec.md](epoch-1-disk-cache-mvp/spec.md) — соответствующий contract раздел.
- [benchmark.md](benchmark.md) — соответствующий contract раздел.
- [epoch-1-disk-cache-mvp/task-00-input-manifest-hi.md](epoch-1-disk-cache-mvp/task-00-input-manifest-hi.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-1-disk-cache-mvp/task-06-load-save-and-restore-hi.md](epoch-1-disk-cache-mvp/task-06-load-save-and-restore-hi.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-1-disk-cache-mvp/task-09-restart-and-failure-integration-med.md](epoch-1-disk-cache-mvp/task-09-restart-and-failure-integration-med.md) — «Уточнения specification v2» / acceptance controls.

Implementation:
Profile задаёт significant environment dependencies и independent current context до reuse; capture/lookup используют одну policy. Unknown significant dependency → unsupported/fallback. Absent→present/changed-value new-PID controls при прежних project bytes сравнивают semantic result. Raw secrets не сохраняются.

Notes:
Compatible conservative fingerprint либо proven relevant subset допустимы. Изменяющий ordinary semantics controlled environment и blanket недоказанная allowlist не разрешены автоматически.

## P-006

Status: applied

Capture eligibility и portable health. Arbitration: **ACCEPT WITH MODIFICATION**, [ARB-007](archive/arbitration/findings/ARB-007.md). Sources: [review-ds/F-004](archive/review-ds/findings/F-004.md).

Affected:
- [README.md](README.md) — соответствующий contract раздел.
- [epoch-1-disk-cache-mvp/spec.md](epoch-1-disk-cache-mvp/spec.md) — соответствующий contract раздел.
- [epoch-1-disk-cache-mvp/task-00-input-manifest-hi.md](epoch-1-disk-cache-mvp/task-00-input-manifest-hi.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-1-disk-cache-mvp/task-03-atomic-envelope-store-med.md](epoch-1-disk-cache-mvp/task-03-atomic-envelope-store-med.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-1-disk-cache-mvp/task-06-load-save-and-restore-hi.md](epoch-1-disk-cache-mvp/task-06-load-save-and-restore-hi.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-1-disk-cache-mvp/task-07-existing-watcher-attachment-hi.md](epoch-1-disk-cache-mvp/task-07-existing-watcher-attachment-hi.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-1-disk-cache-mvp/task-09-restart-and-failure-integration-med.md](epoch-1-disk-cache-mvp/task-09-restart-and-failure-integration-med.md) — «Уточнения specification v2» / acceptance controls.

Implementation:
Capture только пригодного ordinary load без blocking failure. Envelope хранит portable graph-health/coverage и нужные diagnostic origins; hydrate-empty не повышает Unknown до Complete и не стирает ordinary verdict. Fresh watcher/execution/session state пересчитывается. Добавлены blocking no-capture, health/loadGraphComplete/coverage parity и fresh watcher failure controls.

Notes:
Полный LastDiagnostics и old Banned/Unavailable не назначены к переносу. Не заявлена ложная whole-graph completeness.

## P-007

Status: applied

Load-entry policy. Arbitration: **ACCEPT WITH MODIFICATION**, [ARB-008](archive/arbitration/findings/ARB-008.md). Sources: [review-ds/F-005](archive/review-ds/findings/F-005.md).

Affected:
- [README.md](README.md) — соответствующий contract раздел.
- [epoch-1-disk-cache-mvp/spec.md](epoch-1-disk-cache-mvp/spec.md) — соответствующий contract раздел.
- [benchmark.md](benchmark.md) — соответствующий contract раздел.
- [epoch-1-disk-cache-mvp/task-06-load-save-and-restore-hi.md](epoch-1-disk-cache-mvp/task-06-load-save-and-restore-hi.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-1-disk-cache-mvp/task-09-restart-and-failure-integration-med.md](epoch-1-disk-cache-mvp/task-09-restart-and-failure-integration-med.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-1-disk-cache-mvp/task-10-mvp-benchmark-med.md](epoch-1-disk-cache-mvp/task-10-mvp-benchmark-med.md) — «Уточнения specification v2» / acceptance controls.

Implementation:
ExplicitLoad с useDiskCache=true включает только supported scope. ConfigFile без отдельного opt-in disk-disabled: нет lookup/capture/write, disabled/not-attempted outcome; FileSettings globals работают. Negative lazy и explicit enabled controls обязательны; explicit benchmark не lazy speedup.

Notes:
Новый config key/default не вводится подразумеваемо; future config opt-in не prerequisite E1.

## P-008

Status: applied

Executable artifact E0→E1. Arbitration: **ACCEPT WITH MODIFICATION**, [ARB-009](archive/arbitration/findings/ARB-009.md). Sources: [review-ds/F-006](archive/review-ds/findings/F-006.md).

Affected:
- [execution.md](execution.md) — соответствующий contract раздел.
- [epoch-0-snapshot-roundtrip/spec.md](epoch-0-snapshot-roundtrip/spec.md) — соответствующий contract раздел.
- [epoch-1-disk-cache-mvp/spec.md](epoch-1-disk-cache-mvp/spec.md) — соответствующий contract раздел.
- [epoch-0-snapshot-roundtrip/task-02-snapshot-schema-hi.md](epoch-0-snapshot-roundtrip/task-02-snapshot-schema-hi.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-0-snapshot-roundtrip/task-04-snapshot-codec-low.md](epoch-0-snapshot-roundtrip/task-04-snapshot-codec-low.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-0-snapshot-roundtrip/task-05-isolated-hydrate-hi.md](epoch-0-snapshot-roundtrip/task-05-isolated-hydrate-hi.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-1-disk-cache-mvp/task-03-atomic-envelope-store-med.md](epoch-1-disk-cache-mvp/task-03-atomic-envelope-store-med.md) — «Уточнения specification v2» / acceptance controls.

Implementation:
Task outputs/acceptance фиксируют ownership/location/version DTO/codec/hydrate. E1 использует тот же executable codec либо явно фиксирует transfer/replacement и проверяет actual shipped artifact bounds/malformed/round-trip suite. Production не зависит от Tests assembly, prototype evidence не заменяет final checks.

Notes:
Location/assembly остаются свободны. Inert production-compatible E0 components допустимы; Tests-only и обязательный rewrite не назначены.

## P-009

Status: applied

Одна apply boundary. Arbitration: **ACCEPT WITH MODIFICATION**, [ARB-010](archive/arbitration/findings/ARB-010.md). Sources: [review-ds/F-007](archive/review-ds/findings/F-007.md).

Affected:
- [README.md](README.md) — соответствующий contract раздел.
- [execution.md](execution.md) — соответствующий contract раздел.
- [epoch-0-snapshot-roundtrip/spec.md](epoch-0-snapshot-roundtrip/spec.md) — соответствующий contract раздел.
- [epoch-1-disk-cache-mvp/spec.md](epoch-1-disk-cache-mvp/spec.md) — соответствующий contract раздел.
- [epoch-0-snapshot-roundtrip/task-05-isolated-hydrate-hi.md](epoch-0-snapshot-roundtrip/task-05-isolated-hydrate-hi.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-1-disk-cache-mvp/task-04-manager-host-ownership-hi.md](epoch-1-disk-cache-mvp/task-04-manager-host-ownership-hi.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-1-disk-cache-mvp/task-05-fresh-session-preparation-hi.md](epoch-1-disk-cache-mvp/task-05-fresh-session-preparation-hi.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-1-disk-cache-mvp/task-08-hydrated-csharp-write-hi.md](epoch-1-disk-cache-mvp/task-08-hydrated-csharp-write-hi.md) — «Уточнения specification v2» / acceptance controls.

Implementation:
Production Workspace.TryApplyChanges имеет ровно один reference site в existing SolutionManager.TryApplyWorkspaceChanges. Новый host не добавляет второй; public construction без apply допустим при соблюдении write/preflight/publication. Isolated experiment calls не переносятся вслепую. После integration весь SourceStructure/exact inventories обязателен.

Notes:
Wrapper не предписан для любого initial construction; actual writes не получают bypass. Код wrapper/analysis limits этой revision не менялись.

## P-010

Status: applied

Occurrence roles и all owners. Arbitration: **ACCEPT**, [ARB-011](archive/arbitration/findings/ARB-011.md). Sources: [review-ds/F-008](archive/review-ds/findings/F-008.md).

Affected:
- [README.md](README.md) — соответствующий contract раздел.
- [epoch-0-snapshot-roundtrip/spec.md](epoch-0-snapshot-roundtrip/spec.md) — соответствующий contract раздел.
- [epoch-1-disk-cache-mvp/spec.md](epoch-1-disk-cache-mvp/spec.md) — соответствующий contract раздел.
- [epoch-2-content-refresh/spec.md](epoch-2-content-refresh/spec.md) — соответствующий contract раздел.
- [epoch-0-snapshot-roundtrip/task-02-snapshot-schema-hi.md](epoch-0-snapshot-roundtrip/task-02-snapshot-schema-hi.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-0-snapshot-roundtrip/task-03-ordinary-capture-med.md](epoch-0-snapshot-roundtrip/task-03-ordinary-capture-med.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-0-snapshot-roundtrip/task-05-isolated-hydrate-hi.md](epoch-0-snapshot-roundtrip/task-05-isolated-hydrate-hi.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-0-snapshot-roundtrip/task-06-real-cross-process-roundtrip-med.md](epoch-0-snapshot-roundtrip/task-06-real-cross-process-roundtrip-med.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-1-disk-cache-mvp/task-00-input-manifest-hi.md](epoch-1-disk-cache-mvp/task-00-input-manifest-hi.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-1-disk-cache-mvp/task-07-existing-watcher-attachment-hi.md](epoch-1-disk-cache-mvp/task-07-existing-watcher-attachment-hi.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-1-disk-cache-mvp/task-09-restart-and-failure-integration-med.md](epoch-1-disk-cache-mvp/task-09-restart-and-failure-integration-med.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-2-content-refresh/task-00-startup-diff-roles-hi.md](epoch-2-content-refresh/task-00-startup-diff-roles-hi.md) — «Уточнения specification v2» / acceptance controls.

Implementation:
Portable occurrence roles/all-memberships/owners и подтверждённые producers имеют source и rebinding к новым IDs в одной map. Unknown не становится UserInput; имя/obj path не provenance, evaluation role приоритетна. GeneratedOnDisk не переносит generator text/execution provenance. User/generated/unknown/additional/config/output/shared/multi-role parity обязательна.

Notes:
Public flags либо proven portable role facts допустимы; health P-006 отдельный dimension.

## P-011

Status: applied

Actual evidence source. Arbitration: **SPIKE**, [ARB-012](archive/arbitration/findings/ARB-012.md). Sources: [review-grok/F-002](archive/review-grok/findings/F-002.md).

Affected:
- [README.md](README.md) — соответствующий contract раздел.
- [epoch-1-disk-cache-mvp/spec.md](epoch-1-disk-cache-mvp/spec.md) — соответствующий contract раздел.
- [epoch-1-disk-cache-mvp/README.md](epoch-1-disk-cache-mvp/README.md) — соответствующий contract раздел.
- [execution.md](execution.md) — соответствующий contract раздел.
- [spikes.md](spikes.md) — соответствующий contract раздел.
- [epoch-1-disk-cache-mvp/task-00-input-manifest-hi.md](epoch-1-disk-cache-mvp/task-00-input-manifest-hi.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-1-disk-cache-mvp/task-11-raw-import-restore-evidence-hi.md](epoch-1-disk-cache-mvp/task-11-raw-import-restore-evidence-hi.md) — «Уточнения specification v2» / acceptance controls.

Implementation:
S-002 оставлен открытым до actual design-time raw-log проверки и принятия manifest adapter. Category→event/property→path→all consumer instances/completeness boundary должны быть доказаны. Current map/snapshot не closure, None не proof отсутствия raw paths. Incomplete→unsupported, недостаточный evidence запрещает reusable capture; без real positive E1 не готов.

Notes:
S-002 not-run. Второй log/Embed/source collection не назначены заранее; P-004 generation binding остаётся отдельным contract.

## P-012

Status: applied

Config discovery и known absence. Arbitration: **ACCEPT**, [ARB-013](archive/arbitration/findings/ARB-013.md). Sources: [review-grok/F-003](archive/review-grok/findings/F-003.md).

Affected:
- [README.md](README.md) — соответствующий contract раздел.
- [epoch-1-disk-cache-mvp/spec.md](epoch-1-disk-cache-mvp/spec.md) — соответствующий contract раздел.
- [epoch-2-content-refresh/spec.md](epoch-2-content-refresh/spec.md) — соответствующий contract раздел.
- [epoch-1-disk-cache-mvp/task-00-input-manifest-hi.md](epoch-1-disk-cache-mvp/task-00-input-manifest-hi.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-1-disk-cache-mvp/task-01-text-and-directory-probe-med.md](epoch-1-disk-cache-mvp/task-01-text-and-directory-probe-med.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-1-disk-cache-mvp/task-09-restart-and-failure-integration-med.md](epoch-1-disk-cache-mvp/task-09-restart-and-failure-integration-med.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-2-content-refresh/task-05-content-and-graph-mutations-med.md](epoch-2-content-refresh/task-05-content-and-graph-mutations-med.md) — «Уточнения specification v2» / acceptance controls.

Implementation:
До первого E1 hit обязательны presence/known-absence probes применимых ancestor .editorconfig/.globalconfig, explicit configs/regions. Actual discovery учитывает разную applicability и project/linked boundaries; repo root не universal boundary, unknown→fallback. Create/delete ancestor controls сравниваются со fresh ordinary, known-content mutation отдельно.

Notes:
Bounded point/region probes допустимы, полный recursive ancestor scan не введён.

## P-013

Status: applied

Content-only predicate. Arbitration: **ACCEPT**, [ARB-014](archive/arbitration/findings/ARB-014.md). Sources: [review-grok/F-004](archive/review-grok/findings/F-004.md).

Affected:
- [epoch-2-content-refresh/spec.md](epoch-2-content-refresh/spec.md) — соответствующий contract раздел.
- [epoch-2-content-refresh/task-00-startup-diff-roles-hi.md](epoch-2-content-refresh/task-00-startup-diff-roles-hi.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-2-content-refresh/task-01-existing-source-refresh-med.md](epoch-2-content-refresh/task-01-existing-source-refresh-med.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-2-content-refresh/task-02-additional-and-config-refresh-med.md](epoch-2-content-refresh/task-02-additional-and-config-refresh-med.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-2-content-refresh/task-05-content-and-graph-mutations-med.md](epoch-2-content-refresh/task-05-content-and-graph-mutations-med.md) — «Уточнения specification v2» / acceptance controls.

Implementation:
Finite observable evidence подтверждает независимость changed source/additional/config bytes от evaluated graph/options вне refresh. Known Source/SDK name недостаточны; unknown/non-admitted target/import/condition → graph dirty/ordinary fallback. Positive supported mutations и negative DefineConstants/AllowUnsafe target control сохраняются.

Notes:
Произвольные task bodies не анализируются. Permanent запрет всех custom imports и universal closure scope не добавлены.

## P-014

Status: applied

Resolution-preserving metadata. Arbitration: **ACCEPT**, [ARB-015](archive/arbitration/findings/ARB-015.md). Sources: [review-astra/F-003](archive/review-astra/findings/F-003.md).

Affected:
- [epoch-2-content-refresh/spec.md](epoch-2-content-refresh/spec.md) — соответствующий contract раздел.
- [benchmark.md](benchmark.md) — соответствующий contract раздел.
- [epoch-2-content-refresh/task-00-startup-diff-roles-hi.md](epoch-2-content-refresh/task-00-startup-diff-roles-hi.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-2-content-refresh/task-03-fresh-metadata-references-med.md](epoch-2-content-refresh/task-03-fresh-metadata-references-med.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-2-content-refresh/task-05-content-and-graph-mutations-med.md](epoch-2-content-refresh/task-05-content-and-graph-mutations-med.md) — «Уточнения specification v2» / acceptance controls.
- [epoch-2-content-refresh/task-06-content-restart-benchmark-med.md](epoch-2-content-refresh/task-06-content-restart-benchmark-med.md) — «Уточнения specification v2» / acceptance controls.

Implementation:
Metadata refresh требует proven сохранения relevant resolution constraints/inventory. Same path/even identity не достаточны; identity/resolution-sensitive/unknown → ordinary fallback. Safe positive refresh и exact-version DLL replacement negative against fresh ordinary обязательны; all-fallback не закрывает E2 positive scope.

Notes:
Reference semantics зависят от U-001. Normal MSBuild resolution не называется no-DTB reuse; full binary hash вместо mvp-fast не назначен.

## Сохранённый unresolved outcome

[ARB-001](archive/arbitration/findings/ARB-001.md), source
[review-grok/F-001](archive/review-grok/findings/F-001.md) → [U-001](unresolved.md).
Arbitration U-001 outcome не имел P; последующее H-001 выбирает XML исключение. Последующее H-002 выбирает strong-name B и закрывает U-001; outcome не посчитан reviewer-confirmed fix.
U-001 отражён в README/E0/execution/benchmark/affected tasks с deadline.
Полный trace охватывает 16 source findings и 15 ARB; объединение двух identity
sources сохранено в P-002. Историческое unresolved defense по overlay не перенесено
как новое human decision: ARB-004 допускает existing fallback (P-003).

## Derived consistency change — P-001 / P-011

Выделены E0/task-08 S-001 и E1/task-11 S-002; это два bounded experiments,
предписанных арбитражем. Existing task IDs/суффиксы сохранены. Final E0/task-02/03/04/05
получили dependency S-001; E1/task-00 — S-002. Preliminary schema sketch исключён
из final gate, circular capture prerequisite не вводится. Карты epochs, root counts
и scope статистики синхронизированы: **9 / 12 / 7 / 7**, **35 tasks**.
Открытые human/spike decisions не стали accepted outputs.

## Derived consistency change — P-002 / P-003 / P-005 / P-007 / P-014

Benchmark compare context и E0/E1/E2 benchmark tasks учитывают raw/merged/evaluated
properties, current environment, actual overlay/entry outcome и metadata eligibility.
Это следствие новых admission contracts; timings/n/real-positive requirements,
useful accounting и mvp-fast threshold/chunks/blind spot сохранены.

## Derived consistency change — P-004 / P-006 / P-010 / P-012 / P-013 / P-014

Related capture/envelope/map/refresh tasks и E1/E2 tests получили те же predicates,
portable facts, failure semantics и required controls. Stable-success wording E1/task-06
уточнён eligibility/binding; E2 capture section применяет исходный E1 generation contract.
Это не второй index, не новые blanket read bans и не выбор implementation механизма.

## Derived consistency change — P-001–P-014

На стадии revision структура исходных 46 documents/tasks сохранена в отдельной spec-v2. Внутренние links rebased
на новую версию, history/rationale links — на unchanged originals. README/task/spec
statuses помечены revised-for-review, implementation planned; дата и evidence links
согласованы. Старый preparation report заменён отчётом именно revision, исторический
report сохранён в исходной версии. Review instructions описывают application recheck
без reopening disputes. Confirmed fix/review-round counters не выдуманы.

[docs index](../README.md) механически маршрутизирует к актуальной specification v2 и отражает
35 tasks/open gates. На стадии revision original topic inputs не изменялись;
последующий archive move выполнен по запросу владельца и описан отдельно ниже.
Public default/activation/code/config и optional E3 requirements не изменены.

## H-001 — Решение владельца

Status: applied

Источник: [H-001](human-decisions.md), прямое разрешение владельца 2026-10-07
исключить external binary XML documentation из MVP guarantee. Это новый human
normative input после arbitration, не пятнадцатый P и не пересмотр ARB-001.

Affected: README, unresolved, E0/E1/E2 specs, execution, spikes, benchmark,
E0/task-02/03/05/06/08, E1/task-05/06/08/09/10, E2/task-03/05/06,
epoch READMEs, preparation-report, validation, docs index.

Implementation: provider/text equality внешних metadata/DLL references исключены;
source comments и remaining reference/options facts сохранены. XML-only документация
не mandatory validation input; XML с actual другой ролью остаётся обязательным.
Document-dependent операции/потребители получают ordinary route/refusal до side
effects, loss отражается в support/help/outcomes и controls. На момент H-001 strong-name unresolved,
S-001/S-002 not-run; implementation/default/activation permissions прежние.

Derived consistency change — H-001: no-choice wording/compare context/dependent
acceptance tests согласованы с частичным решением. Task IDs/counts/dependencies
не изменены. Validator теперь проверяет recorded XML choice отдельно от remaining
strong-name no-choice; report counts обновлены по final check. Арбитражные original
inputs и их hashes не переписаны, статистика independent review не увеличена.

## Relocation — размещение канона и истории

Status: applied

Источник: прямой запрос владельца 2026-10-07 перенести прежнюю specification/reviews/
прочие files в archive и поднять specification v2 на уровень выше, исправить links
и затем commit документацию. Это механическое изменение размещения, не новый P/H
и не пересмотр решений. [Relocation report](relocation-report.md) фиксирует exact scope.

Affected: весь topic package, docs index, root validator, provenance path/hash mapping.
Implementation: 129 historical files → archive; 59 v2 files → topic root;
Markdown references rebased, archive/index и root navigation/report добавлены.
Historical raw JSON snapshots/scripts сохранены; root validator учитывает pre-move
input hashes и after-link-rebase snapshots. Reports/commands/status summaries синхронизированы.
Notes: 35 task IDs/53 dependency edges прежние; на момент relocation U-001 strong-name был открыт, S-001/S-002
not-run. Independent review statistics и implementation/activation permissions прежние.

## H-002 — Решение владельца

Status: applied

Дата: 2026-10-07. Источник: [H-002](human-decisions.md#h-002--ограниченный-strong-name-contract-mvp),
прямой выбор B владельцем. Accepted scope: подтверждённый signing-independent
hydrated profile; signing-dependent/unknown → ordinary load до execution/side effects,
при невозможности перехода → отказ. Emit/signing из hydrated compilation исключены.
Точное извлечение hidden provider state не prerequisite этого среза; remaining
state, semantic/write correctness, references и H-001 сохраняются.

Affected: human-decisions/unresolved, root/epoch READMEs, E0/E1/E2 specs, affected
tasks, spikes/execution/benchmark, revision-blockers, current report summaries,
docs index, validator/validation.json. [Application report](h-002-report.md).
U-001 resolved; S-001/S-002 not-run; task statuses, 35 IDs/53 edges и activation
permissions прежние. Archive не изменён, independent review statistics не увеличены.

H-002 дополнен требованием владельца к явным диагностическим логам: stage,
project/configuration/TFM, operation, signing-dependent/unknown/excluded reason и
ordinary/refusal route. Required S-001/E1 controls отражены в specs и affected tasks.

## Execution update — 2026-10-08

Исторические not-run отметки выше относятся к применению revision/H-002.
Task-00 принят 2026-10-07. S-001/task-08 accepted, round 2, 2026-10-08: supported observed
base-query slice; [review](epoch-0-snapshot-roundtrip/evidence/task-08-review-round-2.md).
Capture-ready не присвоен, S-002 not-run. Requirements и activation permissions
не изменены; текущие task/index summaries и status-aware validator синхронизированы.
