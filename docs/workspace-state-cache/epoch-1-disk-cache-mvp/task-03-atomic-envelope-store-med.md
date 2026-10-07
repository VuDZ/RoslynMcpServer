# E1/task-03 — Минимальный атомарный store

- Рекомендуемые модели: GPT-6.1 Sol; альтернативы: GLM 5.3, Grok 4.7.
- Рекомендуемый reasoning: GPT: medium; для альтернатив — применимый режим, если поддерживается.
- Примерная сложность: 3/5; класс `med`.
- Риск ошибки: высокий — partial envelope или пропущенная integrity/bounds проверка может разрешить неверный hydrate.
- Необходимые способности: `реализация_по_контракту`, `проектирование_проверок`.
- Статус: **planned; spec-v2 revised-for-review, 2026-10-07; реализация не начата**.
- Depends on: [E1/task-00](task-00-input-manifest-hi.md)
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Сохранять и читать versioned snapshot+manifest envelope по request key в user-private cache directory.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Store и codec integration; использовать E0 codec; без многопоколенного каталога, GC scheduler и большого multiprocess ownership протокола.

## Конкретный результат

- Store reader/writer с bounded integrity checks и focused concurrent/fault tests
- evidence/task-03-store.md.

## Проверки

- Unique temp + atomic replace
- читатель видит целый old/new envelope
- corruption/schema mismatch даёт miss
- multiwriter не создаёт partial envelope
- cache payload не исполняет команды/не задаёт write path
- capture/write failure не ломает ordinary load.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.

## Уточнения specification v2

Основание: **P-006, P-008**; [change ledger](../change-ledger.md).

Envelope хранит bounded portable graph-health/coverage facts и diagnostic origin без старых transient watcher/execution/session states. Blocking ordinary load не создаёт usable envelope. Store проверяет actual shipped codec artifact, а перенос/rewrite явно фиксируются и проходят bounds/malformed/round-trip suite.
