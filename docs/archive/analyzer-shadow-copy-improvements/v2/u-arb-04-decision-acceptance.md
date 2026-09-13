# Приёмка U-ARB-04 — atomic load/prepare decision

Дата: 2026-09-12. Вердикт решения: **решение принимается. Physical load/cache,
optional prepare/gate и публикация — одна линейризуемая операция
`SolutionManager`. Production semantic readers — через сериализованный
accessor.** Последующая реализация
[принята отдельно](u-arb-04-implementation-acceptance.md) в v1.3.14.
Норматив: [u-arb-04-decision.md](u-arb-04-decision.md).
Evidence: [u-arb-04-acceptance.md](u-arb-04-acceptance.md).
Inaccessible не открывался. U-ARB-05 / restart-required / confirmed-only
matcher / write boundary не менять этой реализацией.

Независимая сверка предпосылок (не самоотчёт):

| Claim | Проверка | Статус |
| --- | --- | --- |
| Evidence: published no-overlay compilation грузит/лочит real DLL | Принято ранее; тест `published_semantic_before_enable` | **закрыт** |
| `LoadWorkspace` = `LoadAsync` затем отдельный prepare | `WorkspaceTools.cs`: два await, между ними diagnostics/logging | **закрыт** |
| Два независимых `_workspaceLock` | `LoadAsync` и `ShadowCopyInSolutionAnalyzerReferencesAsync` каждый Wait/Release | **закрыт** |
| `LoadCoreAsync` публикует raw до unlock | `SetPublishedSolution(workspace.CurrentSolution)` перед return | **закрыт** |
| `GetCurrentSolution()` lock-free | getter возвращает `_solution ?? workspace.CurrentSolution` без lock | **закрыт** |
| MCP SDK concurrent dispatch | 1.3.0 `ProcessMessagesCoreAsync`: fire-and-forget `ProcessMessageAsync`, не ждёт предыдущий handler | **закрыт** |

Окно «новая opt-in сессия уже raw, prepare ещё нет» структурно существует.
Fixture-repro concurrent dispatch для выбора политики не обязателен: следствие
вставки измерено, возможность вставки следует из SDK + lock boundaries.

Политика упорядочивания принимается целиком: semantic до enable = честный
no-overlay + restart-required; load/enable первым = только shadow overlay;
промежуточного raw для production reader нет. MVCC / session token не вводить.
Fail-closed при prepare failure. Детерминированный concurrency test — gate
реализации, не этой приёмки решения.

| ID | Sev | Тема | Статус |
| --- | --- | --- | --- |
| Policy | — | Atomic load/prepare + serialized snapshot accessor | **принят** |
| Not shipped | — | Реализация / version bump / schema не начинались | **вне скоупа** |
| Contracts | — | Sticky / restart / matcher / write boundary не трогать | **зафиксировано** |

---

## Что дальше

Реализовать [u-arb-04-atomic-load-prepare.md](u-arb-04-atomic-load-prepare.md)
S1–S4. Не открывать inaccessible. Не ослаблять oracle. E5-S3-1, A4-09…A4-12,
A6-13, F-06, U-ARB-05-1 не чинить в том же шаге.
