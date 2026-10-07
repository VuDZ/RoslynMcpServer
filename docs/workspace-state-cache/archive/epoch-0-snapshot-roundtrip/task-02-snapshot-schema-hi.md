# E0/task-02 — Минимальная переносимая schema

- Рекомендуемые модели: GPT-6 Astra или GPT-6.1 Sol; альтернатива: Grok 4.7 после проверки требуемых способностей.
- Рекомендуемый reasoning: GPT: high; для альтернатив — доступный эквивалент с записью фактического режима.
- Примерная сложность: 4/5; класс `hi`.
- Риск ошибки: высокий — неверный graph/binding/publication или cache hit влияет на semantic correctness и записи.
- Необходимые способности: `семантика_roslyn_msbuild`, `анализ_зависимостей`, `согласованность_компонентов`.
- Статус: **planned; план ready-for-review, 2026-10-07**.
- Depends on: [E0/task-00](task-00-current-seams-and-controls-med.md)
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Описать единственную versioned DTO schema для начального capture/hydrate без Roslyn object serialization.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../../code-style.md) целиком.

Новые DTO файлы под Services/Workspace/StateCache или isolated prototype с единым согласованным назначением; codec/store/manager не реализуются в этой задаче.

## Конкретный результат

- evidence/task-02-schema.md и DTO declarations: request/effective properties, stable project-instance keys, directed refs, options, document roles/paths/encoding, reference properties и portable analyzer identities
- Явные limits и причины unsupported для начального среза.

## Проверки

- Нет source/generated text, Compilation/SyntaxTree, переносимых ProjectId/session/operation contexts или shadow paths
- ambiguity и unavailable public option getters имеют явный outcome
- схема имеет живой real-capture потребитель.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.
