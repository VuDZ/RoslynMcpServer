# E0/task-05 — Восстановление base host

- Рекомендуемые модели: GPT-6 Astra или GPT-6.1 Sol; альтернатива: Grok 4.7 после проверки требуемых способностей.
- Рекомендуемый reasoning: GPT: high; для альтернатив — доступный эквивалент с записью фактического режима.
- Примерная сложность: 4/5; класс `hi`.
- Риск ошибки: высокий — неверный graph/binding/publication или cache hit влияет на semantic correctness и записи.
- Необходимые способности: `семантика_roslyn_msbuild`, `анализ_зависимостей`, `согласованность_компонентов`.
- Статус: **accepted, round 2, 2026-10-08 — isolated schema-v1 Debug base-query hydrate adapter/session owner, H-001/H-002**. Round 3 (2026-10-08) подтвердил ACCEPT без новых must-fix. [Review round 3](evidence/task-05-review-round-3.md), [review round 2](evidence/task-05-review-round-2.md), [final validation](evidence/task-05-validation.md).
- Внешняя приёмка: **passed, 2026-10-08**, подтверждена владельцем. [Общий отчёт task-04/task-05](evidence/task-04-05-external-acceptance.md).
- Depends on: [E0/task-03](task-03-ordinary-capture-med.md), [E0/task-04](task-04-snapshot-codec-low.md), [E0/task-08 — S-001](task-08-real-control-capability-hi.md)
- Модель, реализовавшая задачу: Codex (GPT-6 Sol), subagent; reasoning унаследован от координатора, отдельный runtime режим не подтверждён.
- Модели, проводившие ревью: round 1 и round 2 — Codex (GPT-6 Sol), две независимые сессии; round 3 — Grok 4.7, отдельная сессия.
- Количество раундов ревью: 3.
- Количество исправлений после ревью: 1; T05-R1-001 подтверждён round 2; round 3 новых исправлений не потребовал.
- Отчёт о реализации, ревью и validation: [hydrate](evidence/task-05-hydrate.md), [review round 1](evidence/task-05-review-round-1.md), [review round 2](evidence/task-05-review-round-2.md), [review round 3](evidence/task-05-review-round-3.md), [final validation](evidence/task-05-validation.md), [runner outputs](evidence/task-05-validation.json).

## Цель

Восстановить isolated AdhocWorkspace с новыми Roslyn IDs, текущими source bytes и свежими metadata references.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Hydrate и isolated host ownership; не менять production SolutionManager или write boundary; использовать существующий эксперимент как отправную точку, не копировать его strict admission research целиком.

## Конкретный результат

- Hydrate adapter/session owner и focused resource/identity tests
- evidence/task-05-hydrate.md.

## Проверки

- Нет OpenSolution/OpenProject/DTB в hydrate-owned path
- порядок проектов не меняет edges/memberships
- linked physical path читается один раз
- cancellation/failure dispose host и references
- analyzer execution не разрешается DTO.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.

## Уточнения specification v2

Основание: **P-001, P-008, P-009, P-010**; [change ledger](../change-ledger.md).

Hydrate positive scope зависит от U-001/S-001. Восстановить role/owners/producer facts с новыми IDs, Unknown не становится UserInput. Ownership/reuse artifact зафиксированы. Public construction допустим; isolated TryApplyChanges нельзя перенести как второй production reference site.

## Решение владельца H-001

[H-001](../human-decisions.md): XML documentation provider/text metadata/DLL references
вне MVP equivalence; source comments загруженных проектов и остальные обязательные
metadata/options facts сохраняются. Это объявленное исключение, не silent loss.
Documentation-dependent consumers получают ordinary route либо явный отказ до execution/
side effects. Применимые controls/compare key отражают эту границу.

## Решение владельца H-002

[H-002](../human-decisions.md#h-002--ограниченный-strong-name-contract-mvp), 2026-10-07:
U-001 решён, выбран B. Поддерживаются только подтверждённые signing-independent
проекты/операции; signing-dependent и unknown cases используют ordinary load до
execution/side effects (при невозможности перехода — явный отказ). Emit/signing из
hydrated compilation вне первоначального scope. Semantic/write correctness и
remaining mandatory state сохраняются; signing flags/csproj/refs не меняются ради pass.
Проверки допуска учитывают effective options, attributes и project bindings;
один symbol query/SignAssembly=false не доказывает независимость. S-001 проверяет
real positive и excluded/unknown controls; runtime policy ещё не реализована.
Execution/review statistics остаются незаполненными.
Применение решения: [H-002 report](../h-002-report.md).
