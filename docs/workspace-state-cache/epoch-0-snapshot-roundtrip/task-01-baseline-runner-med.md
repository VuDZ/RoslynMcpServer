# E0/task-01 — Раннер ordinary baseline

- Рекомендуемые модели: GPT-6.1 Sol; альтернативы: GLM 5.3, Grok 4.7.
- Рекомендуемый reasoning: GPT: medium; для альтернатив — применимый режим, если поддерживается.
- Примерная сложность: 3/5; класс `med`.
- Риск ошибки: средний — неверные inputs, fixtures или измерения искажают результат следующего шага.
- Необходимые способности: `реализация_по_контракту`, `проектирование_проверок`.
- Статус: **accepted, 2026-10-08; round 3: T01-R1-001 закрыт, журнал прогона в `run.txt`; принятый scope — ordinary baseline runner для контроля `RoslynMcpPlugin.csproj`: новый PID, warmup вне медианы, пять успешных measured attempts; production cache и приёмка эпохи не присвоены**.
- Depends on: [E0/task-00](task-00-current-seams-and-controls-med.md)
- Модель, реализовавшая задачу: Grok 4.7.
- Модели, проводившие ревью: Grok 4.7 (три независимые сессии).
- Количество раундов ревью: 3.
- Количество исправлений после ревью: 2.
- Отчёт о реализации, ревью и validation: [baseline](evidence/task-01-baseline.md); [review round 1](evidence/task-01-review-round-1.md); [review round 2](evidence/task-01-review-round-2.md); [accepted round 3](evidence/task-01-review-round-3.md).

## Цель

Подготовить небольшой параметризованный runner для нового PID, ordinary load и первого полезного semantic query по принятому benchmark протоколу.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Runner и его docs; новый runner принимает workspace/query/server явно и не меняет pins архивного baseline_bench.py; без production C#.

## Конкретный результат

- Runner в отдельном scripts/workspace-state-cache/ либо адаптер существующего baseline runner
- Raw attempts и evidence/task-01-baseline.md для контрольного workspace.

## Проверки

- Минимум пять успешных измеряемых попыток после отдельного warmup
- successes/errors/timeouts сохраняются
- restore/publish вне тайминга
- новый PID подтверждается в каждой попытке.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.
