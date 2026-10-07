# Decision Ledger

Дата: **2026-10-07**. Run: `workspace-state-cache-arbitration-2026-10-07`.
Все 16 source findings покрыты 15 clusters. Verdict относится к замечанию;
P-* остаются PROPOSED до отдельной стадии изменения/принятия specification.

| ARB | Sources | Decision | Changes | Summary |
|-----|---------|----------|---------|---------|
| [ARB-001](findings/ARB-001.md) | review-grok/F-001 | UNRESOLVED | —; U-001 | Нельзя выбрать потерю provider fidelity за владельца |
| [ARB-002](findings/ARB-002.md) | review-ds/F-003 | SPIKE | P-001; S-001 | После fidelity choice проверить concrete real capability |
| [ARB-003](findings/ARB-003.md) | review-ds/F-001, review-grok/F-005 | ACCEPT WITH MODIFICATION | P-002 | Независимые loader globals и mode admission, не raw-request shortcut |
| [ARB-004](findings/ARB-004.md) | review-ds/F-002 | ACCEPT WITH MODIFICATION | P-003 | Existing fallback достаточен; нужен explicit mode control |
| [ARB-005](findings/ARB-005.md) | review-astra/F-001 | ACCEPT | P-004 | Связать original graph с generation manifest |
| [ARB-006](findings/ARB-006.md) | review-astra/F-002 | ACCEPT | P-005 | Проверять environment нового PID |
| [ARB-007](findings/ARB-007.md) | review-ds/F-004 | ACCEPT WITH MODIFICATION | P-006 | Portable health и capture eligibility, не копия всей session |
| [ARB-008](findings/ARB-008.md) | review-ds/F-005 | ACCEPT WITH MODIFICATION | P-007 | Lazy без opt-in остаётся disabled; новый config key не обязателен |
| [ARB-009](findings/ARB-009.md) | review-ds/F-006 | ACCEPT WITH MODIFICATION | P-008 | Evidence проверяет actual shipped codec; Tests-only не предписан |
| [ARB-010](findings/ARB-010.md) | review-ds/F-007 | ACCEPT WITH MODIFICATION | P-009 | Один apply site; public candidate construction допустим |
| [ARB-011](findings/ARB-011.md) | review-ds/F-008 | ACCEPT | P-010 | Сохранить occurrence roles/owners без догадок |
| [ARB-012](findings/ARB-012.md) | review-grok/F-002 | SPIKE | P-011; S-002 | Проверить raw evidence до выбора нового log channel |
| [ARB-013](findings/ARB-013.md) | review-grok/F-003 | ACCEPT | P-012 | Explicit discovery/absence применимых ancestor configs |
| [ARB-014](findings/ARB-014.md) | review-grok/F-004 | ACCEPT | P-013 | Finite observable content-only predicate |
| [ARB-015](findings/ARB-015.md) | review-astra/F-003 | ACCEPT | P-014 | Metadata refresh только при сохранении resolution |

## Accepted changes

Шесть ACCEPT и шесть ACCEPT WITH MODIFICATION clusters дают P-002–P-010 и P-012–P-014.
Это согласованные arbitration outcomes, а не уже применённые specification fixes.
P-001/P-011 фиксируют открытые эксперименты и временные ограничения SPIKE outcomes.
Полный [change set](proposed-changes.md) содержит **14** P с rationale, affected areas,
dependencies, implementation freedom и проверяемыми последствиями.

## Rejected findings

Полных REJECT нет. Отклонены отдельные предпосылки/corrections:

- Requested-only equality достаточна при разных FileSettings — ARB-003.
- Отдельный overlay storage key сам создаёт fresh admission — ARB-003/004.
- Fallback не предусмотрен task-05; overlay hit безусловно обязателен — ARB-004.
- Пустой LastDiagnostics автоматически делает PublishedAsComplete=true — ARB-007.
- У lazy entry нет load globals; нужен обязательный новый config key — ARB-008.
- E0 обязательно Tests-only, E1 обязательно другая реализация — ARB-009.
- Всякое initial construction обязано вызвать manager apply wrapper — ARB-010.
- ProjectImports=None доказывает отсутствие raw import path events — ARB-012.

Эти опровержения не стирают underlying contract gaps.

## Spikes

- [S-001](spikes.md#s-001--capability-выбранного-real-control): после U-001, до dependent codec/hydrate.
- [S-002](spikes.md#s-002--raw-importrestore-evidence): до принятия E1 manifest source.

Оба not-run. Известные API limits не исследуются повторно; raw-log sufficiency не угадывается.

## Deferred decisions

[None](deferred.md). Existing optional E3 не превращён в новый prerequisite.

## Unresolved decisions

- [U-001](unresolved.md#u-001--fidelity-положительного-e0): fidelity либо source-backed
  reconstruction; варианты, риски и последствия описаны полностью.

Author UNRESOLVED по overlay не сохранён автоматически: explicit adapter-or-fallback
в E1/task-05 уже даёт допустимый минимальный outcome (ARB-004).

## Cross-finding consistency

1. U-001 предшествует S-001; gate не требует готового capture до собственного schema.
   S-001 не заменяет full E0 round-trip и не разрешает negative-only acceptance.
2. P-002 globals, P-005 environment, P-011 dependency paths и P-004 generation binding
   независимы: исправление одного не доказывает остальные. SDK fingerprint не объявляет
   каждый SDK file immutable и не возвращает отложенный full closure scope.
3. P-003 fresh admission совместим с shared clean base и split keys. Никакой P не
   разрешает session/provenance/shadow paths из DTO или обход реального ordinary ban.
4. P-006 health, P-010 roles и P-012 discovery — отдельные dimensions единственной map.
   Ни один не превращает whole-graph Unknown в Complete; новый blanket live-read ban не вводится.
5. P-013 и P-014 относятся к разным eligibility predicates E2. Negative fallback
   не снимает обязательные positive content/metadata cases. E1 остаётся whole-request fallback.
6. P-008 разрешает reuse actual production-compatible artifact; P-009 сохраняет single
   apply site независимо от его расположения. Prototype evidence не заменяет final CI.
7. Default false и P-007 совместимы; optional future config opt-in/overlay adapter
   не сделаны скрытыми prerequisites. Benchmark modes остаются раздельными.
8. Ни один outcome не меняет mvp-fast blind spot, public activation permissions,
   status исходных 33 tasks или scope optional E3. P не применены автоматически.

## Accounting

Cluster counts: ACCEPT **6**, ACCEPT WITH MODIFICATION **6**, REJECT **0**, SPIKE **2**,
DEFERRED **0**, UNRESOLVED **1**. Source-level counts: **6/7/0/2/0/1** соответственно.
Разница вызвана объединением двух identity findings, не потерей source.
Corroborated clusters **1**; новых A-NEW findings **0**; самостоятельных defense-originated
findings **0**. Полноту доказывает [registry](finding-registry.md) и [validation](validation.md).
