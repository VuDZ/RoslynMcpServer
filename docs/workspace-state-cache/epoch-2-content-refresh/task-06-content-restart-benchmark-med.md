# E2/task-06 — Бенчмарк edit/build → restart

- Рекомендуемые модели: GPT-6.1 Sol; альтернативы: GLM 5.3, Grok 4.7.
- Рекомендуемый reasoning: GPT: medium; для альтернатив — применимый режим, если поддерживается.
- Примерная сложность: 3/5; класс `med`.
- Риск ошибки: средний — неверные inputs, fixtures или измерения искажают результат следующего шага.
- Необходимые способности: `реализация_по_контракту`, `проектирование_проверок`.
- Статус: **planned; spec-v2 revised-for-review, 2026-10-07; реализация не начата**.
- Depends on: [E2/task-05](task-05-content-and-graph-mutations-med.md)
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Измерить single/batch source edit и no-op/changed build перед restart.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Runner scenarios/report; mutations выполняются только на owned копии контроля и очищаются; реальный пользовательский checkout не меняется.

## Конкретный результат

- evidence/task-06-benchmark.md с raw input differences и timings
- Материал для отдельной приёмки E2.

## Проверки

- Content-hit и graph-miss разделены
- build side effects перечислены по реальным изменившимся inputs
- ordinary/hydrated useful results совпадают
- bytes/time sample binary policy сохранены в compare key
- no-op build не считается автоматически unchanged.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.

## Уточнения specification v2

Основание: **P-002, P-014**; [change ledger](../change-ledger.md).

Metadata performance scopes явно указывают resolution eligibility и actual hit/fallback; no-op build не означает неизменность resolution. XML exclusion H-001 включён в compare key; strong-name B по H-002 и actual ordinary fallback входят в compare key. Первоначальные single/batch/edit/build сценарии сохраняются.

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
