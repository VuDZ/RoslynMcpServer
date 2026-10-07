# E3/task-02 — Adapter selective MSBuild reopen

- Рекомендуемые модели: GPT-6 Astra или GPT-6.1 Sol; альтернатива: Grok 4.7 после проверки требуемых способностей.
- Рекомендуемый reasoning: GPT: high; для альтернатив — доступный эквивалент с записью фактического режима.
- Примерная сложность: 4/5; класс `hi`.
- Риск ошибки: высокий — неверный graph/binding/publication или cache hit влияет на semantic correctness и записи.
- Необходимые способности: `семантика_roslyn_msbuild`, `анализ_зависимостей`, `согласованность_компонентов`.
- Статус: **planned; план ready-for-review, 2026-10-07**.
- Depends on: [E3/task-00](task-00-single-project-reopen-spike-hi.md), [E3/task-01](task-01-dirty-project-dependency-map-med.md)
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Открыть только dirty project instances через принятый в spike MSBuild path и снять candidate result.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../../code-style.md) целиком.

Только reopen/capture/resources; manager publication и membership orchestration в следующих задачах.

## Конкретный результат

- Project reload adapter/capture tests и evidence/task-02-adapter.md.

## Проверки

- Фактические Open/DTB observations показывают scope с учётом loader transitive work
- effective config/platform/TFM сохранены
- new refs/options/analyzer capture однозначны
- failed/cancelled workspace dispose
- unsupported result → full fallback.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.
