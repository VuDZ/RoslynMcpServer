# E2/task-05 — Mutation matrix для content reuse

- Рекомендуемые модели: GPT-6.1 Sol; альтернативы: GLM 5.3, Grok 4.7.
- Рекомендуемый reasoning: GPT: medium; для альтернатив — применимый режим, если поддерживается.
- Примерная сложность: 3/5; класс `med`.
- Риск ошибки: средний — неверные inputs, fixtures или измерения искажают результат следующего шага.
- Необходимые способности: `реализация_по_контракту`, `проектирование_проверок`.
- Статус: **planned; spec-v2 revised-for-review, 2026-10-07; реализация не начата**.
- Depends on: [E2/task-04](task-04-refreshed-envelope-capture-med.md)
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Закрепить реальные положительные content cases и отрицательные graph/membership cases.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Только tests/report; defects исправляются в owning tasks с последующей полной validation координатором.

## Конкретный результат

- Integration tests и evidence/task-05-mutations.md.

## Проверки

- Source/shared/additional/config/metadata content reuse без DTB
- csproj/import/restore/SDK/new .cs/delete/rename/new ancestor file → ordinary fallback
- roles и dirty owners совпадают с expected
- results сравниваются со fresh ordinary semantic controls.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.

## Уточнения specification v2

Основание: **P-012, P-013, P-014**; [change ledger](../change-ledger.md).

Добавить применимые ancestor .editorconfig/.globalconfig create/delete и linked/explicit discovery boundaries; known-config mutation отдельно. Negative target меняет DefineConstants/AllowUnsafe по existing source bytes, unknown profile fallback. Safe positive metadata update и exact-version replacement negative сравниваются со fresh ordinary result; all-fallback не засчитывается positive E2.

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
