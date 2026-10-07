# Author Defense

Дата: **2026-10-07**. Scope: весь review corpus для
`docs/workspace-state-cache` на `67564de6c48e6d2eb38c4542fd2ef714910da0b0`.
Результат авторской защиты, **не arbitration и не acceptance specification**.
Следование приложенному пользователем Multi-Agent Defense prompt; скилл к
структуре/вердиктам результатов не применяется.

## Summary

Обработаны все findings review-astra, review-ds и review-grok. Направление
staged snapshot → disk MVP → content refresh → optional selective reopen
можно сохранить, однако production contracts требуют точечных уточнений.
Нельзя защищать пригодность snapshot только через равные post-load hashes,
known file paths или факт прохождения выбранного symbol query.

Автор принимает большинство выявленных пробелов. Некоторые recommended fixes
сужены: requested-only key недостаточен при current FileSettings/inherited globals;
ProjectImports=None не означает запрет записи import events; materialization
Adhoc candidate не обязана вводить второй TryApplyChanges; E0 не обязательно
Tests-only. Нечитаемые provider states и first-MVP overlay support нельзя
самостоятельно разрешить потерей semantic fidelity или ослаблением admission.

Исходная specification, task/epoch README, reviews и код не менялись.
[Ledger](finding-ledger.md), [relations](relations.md), [coverage](coverage.md)
и [validation](validation.md) фиксируют полный scope и пределы evidence.

## Statistics

- Total findings: **16**.
- ACCEPT: **6**.
- ACCEPT WITH MODIFICATION: **8**.
- REJECT: **0**.
- UNRESOLVED: **2**.
- Source counts: review-astra **3**, review-ds **8**, review-grok **5**.

Здесь нет автоматически объединённых disputes или normative P-* изменений.
Count не является голосованием и не измеряет качество рецензентов. Accepted
finding не считается исправленным: specification ещё не изменена/rechecked.

## Accepted concerns

- review-astra/F-001: evaluated graph должен иметь evidence соответствия
  capture input generation, не только post-load стабильности.
- review-astra/F-002: independently validated environment policy нужна уже для
  обычной property expansion в новом PID.
- review-astra/F-003: metadata refresh требует observable resolution-preserving
  predicate; одинаковый path сам по себе недостаточен.
- review-ds/F-008: восстановление role/provenance и all-memberships mappings
  должно явно соответствовать ordinary input map.
- review-grok/F-003: new ancestor analyzer configs должны иметь explicit
  discovery/known-absence probes.
- review-grok/F-004: source content-only eligibility нельзя вывести только из
  роли known source; нужен finite supported target/import policy.

## Partially accepted concerns

- review-ds/F-001 и review-grok/F-005: key/parameter matrix неполны. Автор
  предлагает различить raw requested, merged host globals, evaluated defaults
  и mode admission, вместо blanket requested/effective shortcut.
- review-ds/F-003: early gate/blocked contingency нужны; новый повторный spike
  missing getters не заменяет fidelity decision и не должен создать dependency cycle.
- review-ds/F-004: capture eligibility/portable health/coverage нужны; current
  Unknown не превращается в Complete просто от пустых LastDiagnostics, и не все
  transient diagnostic/session states должны переноситься.
- review-ds/F-005: lazy entry disk policy нужно описать; default false не требует
  автоматически нового config key. Explicit-only opt-in — авторское предложение,
  не утверждённый product выбор.
- review-ds/F-006: location/reuse actual E0 artifact надо закрепить; обязательный
  Tests-only codec и неизбежная другая E1 реализация не следуют из текста.
- review-ds/F-007: single TryApplyChanges invariant обязателен; не всякое public
  candidate construction обязано вызывать TryApplyChanges.
- review-grok/F-002: dependency evidence source отсутствует как contract. Однако
  None запрещает collection files, а не доказывает отсутствие path events;
  current replay хранит generic property dictionary. Нужна проверка реальных
  raw events, не заранее обязательный второй log или source embedding.

## Rejected concerns

Полных REJECT нет. Отклонённые части восьми частично принятых findings указаны
в их отдельных ответах; это не превращает underlying defect в rejected.

## Unresolved

**review-grok/F-001 — fidelity E0/E1.** Подтверждены public getter limitations
и отказ existing strict capture для stock references. Нужно решение владельца:
сужается ли observable XML documentation/strong-name contract, с какими
unsupported write/emit/query cases, либо нужен дополнительный source-backed
constructor/effective-input capture. Нельзя выдать null/default substitution
или negative-only harness за успешный real round-trip. Blocker касается текущей
заявленной strict positive E0, а не доказанной невозможности всех snapshot подходов.

**review-ds/F-002 — overlay hit первого MVP.** Current fresh provenance требует
ordinary MSBuild open. Task-05 предусматривает будущий adapter или fallback,
но готового disk-only adapter нет. Нужно решить, входит ли overlay hit в E1.
Автор рекомендует сначала explicit mode-specific ordinary fallback с отдельным
контролем/benchmark; permanent exclusion или новый portable binder здесь не утверждены.

Кроме этих двух primary UNRESOLVED, accepted concerns оставляют конкретные
design choices для следующей стадии: environment policy, raw import evidence
channel, точный content-only profile и config-driven opt-in coverage. Признание
defect не утверждает одну из этих реализаций. Они перечислены в соответствующих
Proposed response, без окончательной классификации SPIKE/DEFERRED.

## Cross-review observations

Representability и key concerns имеют очевидный overlap, но framing/corrections
различаются. Import discovery и graph/input generation, health и roles,
binary resolution и source-dependent options дополняют друг друга. Полные
identities и отличия сохранены в [relations.md](relations.md); окончательной
кластеризации не выполнялось. Независимое совпадение не заменяет code evidence.

## Recommended arbitration focus

1. Принять fidelity choice для реального E0 и не вернуть всю v2 closure программу.
2. Разделить cached base, requested/merged globals и fresh mode admission;
   определить overlay и lazy opt-in scope без изменения default по умолчанию.
3. Требовать observable source/generation binding для initial capture и практический
   import/environment/content-only support predicate вместо формулировок «stable»/«known».
4. Сузить metadata refresh до resolution-preserving cases либо honest fallback.
5. Закрепить перенос health/roles и единый executable codec/write boundary с
   небольшими tasks; не подменять эти уточнения общими обещаниями final CI.

Это приоритеты для арбитра, не финальные verdicts или требования новой версии.
