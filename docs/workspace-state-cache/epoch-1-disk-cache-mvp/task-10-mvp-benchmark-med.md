# E1/task-10 — Бенчмарк первого MVP

- Рекомендуемые модели: GPT-6.1 Sol; альтернативы: GLM 5.3, Grok 4.7.
- Рекомендуемый reasoning: GPT: medium; для альтернатив — применимый режим, если поддерживается.
- Примерная сложность: 3/5; класс `med`.
- Риск ошибки: средний — неверные inputs, fixtures или измерения искажают результат следующего шага.
- Необходимые способности: `реализация_по_контракту`, `проектирование_проверок`.
- Статус: **planned; план ready-for-review, 2026-10-07**.
- Depends on: [E1/task-09](task-09-restart-and-failure-integration-med.md), [E0/task-01](../epoch-0-snapshot-roundtrip/task-01-baseline-runner-med.md)
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Измерить end-to-end unchanged restart и miss overhead через production load entry.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Runner extension и report; product docs/version/default меняются только отдельным принятым выпуском, не benchmark задачей.

## Конкретный результат

- evidence/task-10-benchmark.md с raw runs, compare keys и выводом по functional/performance scope
- Материал для отдельной приёмки E1 в README.

## Проверки

- ordinary forced, absent-cache, unchanged hit, source miss, corrupt-cache fallback
- timings scan/binary/decode/hydrate/prepare/capture/write/query, bytes read и working set
- фиксированный поддержанный real control быстрее по useful median
- fallback target не выдан за cache speedup.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.
