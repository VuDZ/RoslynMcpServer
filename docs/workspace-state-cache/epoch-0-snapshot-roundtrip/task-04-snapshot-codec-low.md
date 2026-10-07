# E0/task-04 — Bounded snapshot codec

- Рекомендуемые модели: GPT-6 Luna; альтернативы: DeepSeek 4.1 Flash, GLM 5.3.
- Рекомендуемый reasoning: GPT: medium; для альтернатив — применимый режим, если поддерживается.
- Примерная сложность: 2/5; класс `low`.
- Риск ошибки: высокий — ошибка bounds/schema validation допускает некорректный payload или неограниченную allocation.
- Необходимые способности: `реализация_по_контракту`, `проектирование_проверок`.
- Статус: **planned; spec-v2 revised-for-review, 2026-10-07; реализация не начата**.
- Depends on: [E0/task-02](task-02-snapshot-schema-hi.md), [E0/task-08 — S-001](task-08-real-control-capability-hi.md)
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

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
