# E1/task-08 — Правка .cs на hydrated host

- Рекомендуемые модели: GPT-6 Astra или GPT-6.1 Sol; альтернатива: Grok 4.7 после проверки требуемых способностей.
- Рекомендуемый reasoning: GPT: high; для альтернатив — доступный эквивалент с записью фактического режима.
- Примерная сложность: 4/5; класс `hi`.
- Риск ошибки: высокий — неверный graph/binding/publication или cache hit влияет на semantic correctness и записи.
- Необходимые способности: `семантика_roslyn_msbuild`, `анализ_зависимостей`, `согласованность_компонентов`.
- Статус: **planned; план ready-for-review, 2026-10-07**.
- Depends on: [E1/task-07](task-07-existing-watcher-attachment-hi.md)
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Провести existing .cs edit через текущую manager write boundary на Adhoc host.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Existing .cs persistence/apply seams и tests; add/remove/rename/project-file write capabilities в этой задаче не реализуются.

## Конкретный результат

- Минимальный host-specific apply adapter и targeted encoding/stale-write tests
- evidence/task-08-write.md.

## Проверки

- Preflight/exact inverse/session/base stamps обязательны
- same physical file согласован во всех memberships
- encoding и partial persistence не подменяются rollback
- unsupported operation отвергнута до bytes
- shadow refs никогда не записываются в csproj.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.
