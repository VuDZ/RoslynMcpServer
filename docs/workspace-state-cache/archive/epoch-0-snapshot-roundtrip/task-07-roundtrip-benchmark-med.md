# E0/task-07 — Бенчмарк isolated restore

- Рекомендуемые модели: GPT-6.1 Sol; альтернативы: GLM 5.3, Grok 4.7.
- Рекомендуемый reasoning: GPT: medium; для альтернатив — применимый режим, если поддерживается.
- Примерная сложность: 3/5; класс `med`.
- Риск ошибки: средний — неверные inputs, fixtures или измерения искажают результат следующего шага.
- Необходимые способности: `реализация_по_контракту`, `проектирование_проверок`.
- Статус: **planned; план ready-for-review, 2026-10-07**.
- Depends on: [E0/task-01](task-01-baseline-runner-med.md), [E0/task-06](task-06-real-cross-process-roundtrip-med.md)
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Добавить isolated restore scenario в baseline runner и сопоставить ordinary/restore на одном compare key.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../../code-style.md) целиком.

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
проверки выполняются по [AGENTS.md](../../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.
