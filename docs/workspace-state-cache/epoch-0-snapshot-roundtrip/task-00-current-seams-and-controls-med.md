# E0/task-00 — Актуальные seams и контролы

- Рекомендуемые модели: GPT-6.1 Sol; альтернативы: GLM 5.3, Grok 4.7.
- Рекомендуемый reasoning: GPT: medium; для альтернатив — применимый режим, если поддерживается.
- Примерная сложность: 3/5; класс `med`.
- Риск ошибки: средний — неверные inputs, fixtures или измерения искажают результат следующего шага.
- Необходимые способности: `реализация_по_контракту`, `проектирование_проверок`.
- Статус: **accepted, 2026-10-07; round 2: оба замечания закрыты; принятый scope — docs-only inventory seams и выбор real control; на дату этой приёмки S-001 not-run, capture-ready не присвоен**.
- Depends on: нет.
- Модель, реализовавшая задачу: Grok 4.7.
- Модели, проводившие ревью: Codex (GPT-6 Astra).
- Количество раундов ревью: 2.
- Количество исправлений после ревью: 2.
- Отчёт о реализации, ревью и validation: [implementation evidence](evidence/task-00-controls.md); [review round 1](evidence/task-00-review-round-1.md); [accepted round 2 / validation](evidence/task-00-review-round-2.md).



## Цель

Зафиксировать короткий inventory текущего load/prepare/write/input-map пути и выбрать маленький контроль плюс реальный неизменённый проект для round-trip.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Только docs текущей эпохи; читать SolutionManager, WorkspaceInputMapBuilder и existing WorkspaceLoadCache experiment; не выполнять полный SDK/restore closure audit.

## Конкретный результат

- evidence/task-00-controls.md: HEAD/source version, dirty inventory, actual host, workspace path/properties, SDK, ожидаемый символ и поддерживаемый начальный срез
- Список применимых результатов старого hydrate experiment и конкретных representability gaps.



## Проверки

- Контроль содержит реальные metadata/project references и заранее проверяемый символ
- generator-dependent scope записан отдельно, отсутствие поддержки не скрыто
- найденные пробелы не превращаются в пачку новых исследований.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.

## Уточнения specification v2

Основание: **P-001**; [change ledger](../change-ledger.md).

Зафиксировать actual real control для bounded S-001, известные provider/API refusals и принятый U-001=B по H-001/H-002. Task остаётся коротким inventory/selection; executable S-001 выполняет отдельный task-08. Недоказанный positive control не объявляется capture-ready.
