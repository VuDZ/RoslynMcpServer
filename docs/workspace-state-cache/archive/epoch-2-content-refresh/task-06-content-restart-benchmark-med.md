# E2/task-06 — Бенчмарк edit/build → restart

- Рекомендуемые модели: GPT-6.1 Sol; альтернативы: GLM 5.3, Grok 4.7.
- Рекомендуемый reasoning: GPT: medium; для альтернатив — применимый режим, если поддерживается.
- Примерная сложность: 3/5; класс `med`.
- Риск ошибки: средний — неверные inputs, fixtures или измерения искажают результат следующего шага.
- Необходимые способности: `реализация_по_контракту`, `проектирование_проверок`.
- Статус: **planned; план ready-for-review, 2026-10-07**.
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
Перед C# правкой прочитать [code style](../../../code-style.md) целиком.

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
проверки выполняются по [AGENTS.md](../../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.
