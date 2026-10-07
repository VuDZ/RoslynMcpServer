# E1/task-04 — Минимальный host ownership seam

- Рекомендуемые модели: GPT-6 Astra или GPT-6.1 Sol; альтернатива: Grok 4.7 после проверки требуемых способностей.
- Рекомендуемый reasoning: GPT: high; для альтернатив — доступный эквивалент с записью фактического режима.
- Примерная сложность: 4/5; класс `hi`.
- Риск ошибки: высокий — неверный graph/binding/publication или cache hit влияет на semantic correctness и записи.
- Необходимые способности: `семантика_roslyn_msbuild`, `анализ_зависимостей`, `согласованность_компонентов`.
- Статус: **planned; план ready-for-review, 2026-10-07**.
- Depends on: [E0/task-07](../epoch-0-snapshot-roundtrip/task-07-roundtrip-benchmark-med.md)
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Разделить manager-owned workspace host и MSBuild-only open/diagnostics операции, сохранив ordinary поведение.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../code-style.md) целиком.

SolutionManager host field/open/diagnostics/resource seams и targeted tests; не добавлять disk lookup, новые watcher policies или portable provenance.

## Конкретный результат

- Минимальная host abstraction/adapter и изменения ownership в SolutionManager
- evidence/task-04-host.md с ordinary-path regressions.

## Проверки

- Один manager lock, отсутствие повторного under-lock acquire
- обычный RAM matcher/omitted args/buildArgs/sticky overlay/reset сохранены
- same-key candidate failure не dispose активный host до замены
- lazy texts/metadata остаются доступными до завершения читателей
- ordinary regression tests проходят.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.
