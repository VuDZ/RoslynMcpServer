# E3/task-00 — Узкий spike project reopen

- Рекомендуемые модели: GPT-6 Astra или GPT-6.1 Sol; альтернатива: Grok 4.7 после проверки требуемых способностей.
- Рекомендуемый reasoning: GPT: high; для альтернатив — доступный эквивалент с записью фактического режима.
- Примерная сложность: 4/5; класс `hi`.
- Риск ошибки: высокий — неверный graph/binding/publication или cache hit влияет на semantic correctness и записи.
- Необходимые способности: `семантика_roslyn_msbuild`, `анализ_зависимостей`, `согласованность_компонентов`.
- Статус: **planned; план ready-for-review, 2026-10-07**.
- Depends on: [E2/task-06](../epoch-2-content-refresh/task-06-content-restart-benchmark-med.md)
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Проверить один проект с зависимостью: штатный MSBuild reopen изменённого проекта и перенос результата в hydrated graph.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Isolated test harness и docs; не менять production manager и не исследовать все типы решений; один сценарий с текущими references/TFM/analyzer rules.

## Конкретный результат

- Один исполняемый prototype и evidence/task-00-reopen.md
- supported либо deferred verdict с конкретными binding/resource limitations и срезом следующих задач.

## Проверки

- Реально изменённый csproj/props меняет semantic result
- retained project и references корректны
- никаких fabricated provenance/session bindings
- no-go честно останавливает downstream selective tasks, оставляя E2 fallback.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.
