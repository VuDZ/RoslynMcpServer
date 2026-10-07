# Arbitration Result

Дата: **2026-10-07**. Статус: **arbitration completed**; исходная specification
не принята и не изменена. Run: `workspace-state-cache-arbitration-2026-10-07`.
Scope: **16/16** findings трёх review catalogs и все их author responses.
Выполненная проверка артефактов: [validation](validation.md).

## Summary

Staged E0→E1→E2 и optional E3 можно сохранить. Требуются точечные contracts для
identity, capture generation, inputs, health и refresh. Один fidelity choice нельзя
вывести из evidence. Два вопроса требуют узкого исполнения, а не новой closure-программы.
Итог — [Decision Ledger](decision-ledger.md), [registry](finding-registry.md) и
[14 proposed changes](proposed-changes.md). Применение P-* — отдельная стадия.

## Statistics

- Original findings: **16**.
- Arbitration clusters: **15**.
- Independently corroborated: **1** (заявленная reviewers независимость, не голосование).
- ACCEPT: **6**.
- ACCEPT WITH MODIFICATION: **6**.
- REJECT: **0**.
- SPIKE: **2**.
- DEFERRED: **0**.
- UNRESOLVED: **1**.
- Proposed changes: **14**, все PROPOSED.

Counts outcomes относятся к clusters; source-level counts есть в ledger.

## Highest-impact decisions

- Strict provider fidelity и обязательный real positive E0 требуют U-001; отказ
  существующего observer не доказывает невозможность любого reconstruction.
- Cold disk matching обязан проверять независимо разрешённые loader globals,
  а не только raw requested или defaults найденного envelope.
- Overlay fallback уже разрешён E1/task-05. Постоянный запрет overlay hit и обязательный
  новый adapter не следуют из требований; missing fresh admission не маскируется hit.
- Post-load stability не связывает graph с прочитанными при evaluation bytes.
- None у BinaryLogger не доказывает отсутствие raw paths; необходим S-002.
- E2 content/metadata refresh требует отдельных наблюдаемых predicates;
  same source role или same DLL path недостаточны.

## Human attention required

[U-001 — fidelity положительного E0](unresolved.md#u-001--fidelity-положительного-e0):
source-backed exact reconstruction, явно ограниченная observable семантика либо
отложенный strict scope. Варианты, риски и последствия находятся в decision block.

## Required experiments

[S-001](spikes.md#s-001--capability-выбранного-real-control) — bounded capability выбранного
real control после U-001. [S-002](spikes.md#s-002--raw-importrestore-evidence) — actual
design-time import/restore evidence до принятия E1 manifest. Оба **not-run**.

## Safely deferred

Новых DEFERRED verdicts нет. Existing optional E3 сохраняет собственный no-go contract.

## Next step

Принять U-001, рассмотреть P-* и затем отдельной стадией обновить spec/tasks с traceability.
S-001/S-002 выполнить к их deadlines. Этот арбитраж не разрешает implementation/activation
и не засчитывает исправления до применения/recheck. Код и tests не менялись; build,
main suite, SourceStructure, AnalyzerLifecycle и benchmarks **not-run**.
