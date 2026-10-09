# E0/task-07 — Бенчмарк isolated restore

- Рекомендуемые модели: GPT-6.1 Sol; альтернативы: GLM 5.3, Grok 4.7.
- Рекомендуемый reasoning: GPT: medium; для альтернатив — применимый режим, если поддерживается.
- Примерная сложность: 3/5; класс `med`.
- Риск ошибки: средний — неверные inputs, fixtures или измерения искажают результат следующего шага.
- Необходимые способности: `реализация_по_контракту`, `проектирование_проверок`.
- Статус: **accepted, round 2, 2026-10-09 — bounded SamplePlugin ordinary/isolated restore benchmark runner, child admission, раздельные timings/memory/raw evidence и предложение E1; T07-R1-001/002/003 закрыты; повторные Release build/main и положительный прогон прошли; production readiness и приёмка эпохи не объявлены**. [Отчёт](evidence/task-07-benchmark.md), [review round 2 / validation](evidence/task-07-review-round-2.md).
- Depends on: [E0/task-01](task-01-baseline-runner-med.md), [E0/task-06](task-06-real-cross-process-roundtrip-med.md)
- Модель, реализовавшая задачу: Grok 4.7, текущая сессия, без subagents.
- Модели, проводившие ревью: Codex (GPT-6 Sol), независимая сессия, без subagents.
- Количество раундов ревью: 2.
- Количество исправлений после ревью: 3 (T07-R1-001, T07-R1-002, T07-R1-003), подтверждены независимым recheck round 2; открытых must-fix findings: 0.
- Отчёт о реализации, ревью и validation: [benchmark](evidence/task-07-benchmark.md), [исходный summary](evidence/task-07/summary.json), [review round 1](evidence/task-07-review-round-1.md), [accepted round 2 / validation](evidence/task-07-review-round-2.md), [новый положительный прогон](evidence/task-07-review-round-2/positive/summary.json).

## Цель

Добавить isolated restore scenario в baseline runner и сопоставить ordinary/restore на одном compare key.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Runner extension и report; не интегрировать production cache и не подгонять контроль под положительный результат.

## Конкретный результат

- evidence/task-07-benchmark.md с raw attempt links, medians/stages, расходом памяти и limits
- Предложение scope E1, основанное на работающем real round-trip.

## Проверки

- Раздельные capture/read/decode/hydrate/query timings
- новый PID
- unsupported large target отражён отдельно
- report не объявляет production readiness и сопровождает E0 acceptance.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.
