# E1/task-09 — Production restart/failure проверки

- Рекомендуемые модели: GPT-6.1 Sol; альтернативы: GLM 5.3, Grok 4.7.
- Рекомендуемый reasoning: GPT: medium; для альтернатив — применимый режим, если поддерживается.
- Примерная сложность: 3/5; класс `med`.
- Риск ошибки: средний — неверные inputs, fixtures или измерения искажают результат следующего шага.
- Необходимые способности: `реализация_по_контракту`, `проектирование_проверок`.
- Статус: **planned; план ready-for-review, 2026-10-07**.
- Depends on: [E1/task-07](task-07-existing-watcher-attachment-hi.md), [E1/task-08](task-08-hydrated-csharp-write-hi.md)
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Проверить целый production disk load и fallback в новом PID, включая ошибки и изменение inputs.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../../code-style.md) целиком.

Тестовый harness/tests; production исправления выявленных дефектов возвращаются соответствующему владельцу адаптера и отражаются в его task/report.

## Конкретный результат

- Integration tests и evidence/task-09-integration.md
- Inventories и independent semantic/write assertions ordinary versus hydrated.

## Проверки

- Real unchanged hit
- source/graph/new file/absent input/binary/version change → miss
- corrupt/truncated store, capture failure, cancellation, startup race
- configuration/platform/TFM, linked .cs и lifecycle controls
- никакие zero-test/skip/timeouts не засчитаны как pass.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.
