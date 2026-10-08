# E0/task-03 — Capture ordinary workspace

- Рекомендуемые модели: GPT-6.1 Sol; альтернативы: GLM 5.3, Grok 4.7.
- Рекомендуемый reasoning: GPT: medium; для альтернатив — применимый режим, если поддерживается.
- Примерная сложность: 3/5; класс `med`.
- Риск ошибки: средний — неверные inputs, fixtures или измерения искажают результат следующего шага.
- Необходимые способности: `реализация_по_контракту`, `проектирование_проверок`.
- Статус: **accepted, 2026-10-08; round 1, без must-fix замечаний ревью; после приёмки закрыто расхождение descriptor и live parse options / document identity; принятый scope — ordinary capture schema v1 с уже загруженного контроля RoslynMcpPlugin в Debug/base-query профиле S-001; codec и hydrate не входят в scope этой приёмки; приёмка всей эпохи не выполнена**.
- Depends on: [E0/task-02](task-02-snapshot-schema-hi.md), [E0/task-08 — S-001](task-08-real-control-capability-hi.md)
- Модель, реализовавшая задачу: Grok 4.7.
- Модели, проводившие ревью: Grok 4.7, независимая сессия.
- Количество раундов ревью: 1.
- Количество исправлений после ревью: 0.
- Отчёт о реализации, ревью и validation: [capture](evidence/task-03-capture.md); [review round 1](evidence/task-03-review-round-1.md); [final validation](evidence/task-03-validation.md).

## Цель

Снять DTO с реального успешно загруженного base solution согласно schema.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Capture и его tests; existing ordinary load вызывается как источник, production manager path не переписывается; не исследовать все MSBuild dependency categories.

## Конкретный результат

- Capture adapter и focused capture tests
- evidence/task-03-capture.md с actual inventory выбранного контроля и причинами неподдержанных cases.

## Проверки

- Сохраняются options/reference aliases/EmbedInteropTypes и все memberships
- analyzer identity не берётся из shadow path
- stock metadata reference и options проверяются на реальном ordinary capture
- silent defaults запрещены.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.

## Уточнения specification v2

Основание: **P-001, P-010**; [change ledger](../change-ledger.md).

Применить accepted U-001 contract и successful S-001 slice, без silent defaults/удаления refs. Capture role facts имеет traceable source и portable all-occurrence binding; path-under-obj/suffix не доказывает producer.

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
Execution/review statistics этого task заполнены в шапке после независимого ревью и final validation.
Применение решения: [H-002 report](../h-002-report.md).
