# E0/task-04 — Bounded snapshot codec

- Рекомендуемые модели: GPT-6 Luna; альтернативы: DeepSeek 4.1 Flash, GLM 5.3.
- Рекомендуемый reasoning: GPT: medium; для альтернатив — применимый режим, если поддерживается.
- Примерная сложность: 2/5; класс `low`.
- Риск ошибки: высокий — ошибка bounds/schema validation допускает некорректный payload или неограниченную allocation.
- Необходимые способности: `реализация_по_контракту`, `проектирование_проверок`.
- Статус: **accepted, round 3, 2026-10-08 — bounded schema-v1 codec в принятом S-001 профиле**. [Ревью round 3](evidence/task-04-review-round-3.md), [final validation](evidence/task-04-validation.md).
- Depends on: [E0/task-02](task-02-snapshot-schema-hi.md), [E0/task-08 — S-001](task-08-real-control-capability-hi.md)
- Модель, реализовавшая задачу: Grok 4.7.
- Модели, проводившие ревью: round 1 — Codex (GPT-6 Sol), независимая сессия; round 2 и round 3 — Codex (GPT-6; точный runtime model ID недоступен), независимые сессии.
- Количество раундов ревью: 3.
- Количество исправлений после ревью: 3; T04-R1-001/002 подтверждены round 2, T04-R1-003 подтверждён round 3.
- Отчёт о реализации, ревью и validation: [codec](evidence/task-04-codec.md), [review round 1](evidence/task-04-review-round-1.md), [review round 2](evidence/task-04-review-round-2.md), [review round 3](evidence/task-04-review-round-3.md), [final validation](evidence/task-04-validation.md).

## Цель

Реализовать encode/decode минимального DTO по зафиксированной schema.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Только serialization/validation; без cache directory, locking, file scan и manager интеграции.

## Конкретный результат

- Codec и focused malformed/round-trip tests
- evidence/task-04-codec.md.

## Проверки

- Unknown schema, truncated data, duplicate keys, invalid options, over-limit payload и checksum failure дают отказ до hydrate
- aggregate bounds проверяются
- round-trip переносимого DTO проходит.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.

## Уточнения specification v2

Основание: **P-001, P-008**; [change ledger](../change-ledger.md).

Dependent codec для positive control начинается после successful S-001 и final schema. Acceptance evidence указывает executable artifact; E1 либо использует тот же codec, либо явно фиксирует перенос/замену и проверяет bounds/malformed/round-trip против поставляемой версии.
