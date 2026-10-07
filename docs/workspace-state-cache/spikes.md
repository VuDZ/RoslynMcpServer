# Открытые эксперименты specification v2

Обновлено: **2026-10-08**. S-001 **accepted** для isolated base-query slice, round 2;
capture-ready не присвоен; S-002 **not-run**.
[Результат S-001 и validation](epoch-0-snapshot-roundtrip/evidence/task-08-capability.md),
[review round 2](epoch-0-snapshot-roundtrip/evidence/task-08-review-round-2.md).
U-001 решён по H-001/H-002 (B), 2026-10-07. Deadlines и требования арбитража сохраняются.
[Исходное решение](archive/arbitration/spikes.md) содержит rationale insufficiency.

## S-001 — Capability выбранного real control

Основание: [P-001](change-ledger.md#p-001), [ARB-002](archive/arbitration/findings/ARB-002.md).
Отдельный bounded scope: [E0/task-08](epoch-0-snapshot-roundtrip/task-08-real-control-capability-hi.md).
Decision prerequisite: [U-001](unresolved.md#u-001--fidelity-положительного-e0) **resolved / B**, 2026-10-07.

Проверить достижимость ordinary capture/minimal public reconstruction неизменённого
реального контроля в выбранной fidelity границе. Зафиксировать versions/revision/
globals/query и U-001 outcome; фактические providers, metadata/project references,
instance bindings и источник каждого mandatory state. Не удалять refs/переписывать
csproj ради pass. Сравнить заранее выбранные observable results с ordinary baseline;
по выбранному B обязательно проверить ordinary fallback signing-dependent/unknown
cases и исключение emit/signing из hydrated compilation до execution/side effects.
При невозможности ordinary route — явный отказ. Нужны actual admission evidence
и объявленная reconstruction policy; успешный symbol query не доказывает весь scope.

Достаточный положительный результат: один real positive capability verdict с
однозначными instances и traceable required state, без silent defaults/private
reflection. Минимальный isolated harness не требует полного нового schema/capture/
codec/store. Известные отсутствующие getters не исследуются повторно; SDK/restore
closure audit не добавляется. Inspection не заменяет cross-process task-06.

Возможные исходы: supported slice с evidence; partial только внутри выбранного
U-001 либо возврат scope владельцу; unsupported → positive E0 blocked. Synthetic
codec/hydrate pass и negative-only result не закрывают real-positive gate.

**Deadline:** после U-001, до dependent positive codec/hydrate; final executable
schema/capture опираются на подтверждённый slice. Schema sketch/inspection допустимы
раньше; готовый full capture не prerequisite собственного schema design.

## S-002 — Raw import/restore evidence

Основание: [P-011](change-ledger.md#p-011), [ARB-012](archive/arbitration/findings/ARB-012.md).
Отдельный bounded scope: [E1/task-11](epoch-1-disk-cache-mvp/task-11-raw-import-restore-evidence-hi.md).

На owned контрольной копии проверить actual ordinary MSBuildWorkspace design-time
raw log при текущем `ProjectImports=None`, до cleanup. Зафиксировать versions,
events/properties/evaluation contexts; сопоставить expected inputs с source/path/
all-consumer instances. Нужны nested/conditional/absent/shared/external imports,
redirected assets/intermediate и применимые lock/NuGet configuration inputs.
Generic ProjectStarted properties/обычный CLI build/current map/analyzer snapshot
не считаются доказанной closure; None не доказывает отсутствие raw paths.

У finite profile каждый required category имеет actual source, instance binding,
completeness boundary; missing/conflicting evidence → incomplete/unsupported.
Нужен неизменённый реальный positive request и negative incomplete controls.
Path availability не доказывает consumed-generation binding P-004.

Возможные исходы: существующий канал достаточен → зафиксировать verified extraction
при прежних None/provenance gates; partial → перечислить gaps и обосновать narrow
profile либо дополнительный канал; нет any real positive → E1 manifest blocked.
Второй log/Embed/source collection и их стоимость не назначаются заранее.

**Deadline:** до принятия E1/task-00 manifest adapter и reusable disk envelope.
Изолированный E0 round-trip не зависит от S-002. All-unsupported не означает E1 success.

## Прежний optional E3

[Selective reopen spike E3/task-00](epoch-3-project-refresh/task-00-single-project-reopen-spike-hi.md)
сохраняет прежний no-go contract. Он не новый результат арбитража и не prerequisite E1/E2.

## Уточнение S-001 после H-001/H-002

[U-001 решён владельцем](human-decisions.md): XML исключение H-001 и strong-name B по H-002.
S-001 не требует extraction/equality исходного metadata DocumentationProvider.
Он должен подтвердить supported results/source comments и объявленные external XML
limitations с documentation-dependent отказом/fallback. Remaining mandatory state,
metadata properties и real positive требования сохраняются; isolated spike accepted
для base-query slice, round 2, production eligibility не присвоена.
Точный перенос непубличного strong-name state не prerequisite signing-independent
slice. [H-002 report](h-002-report.md) фиксирует применение решения, не запуск spike.
