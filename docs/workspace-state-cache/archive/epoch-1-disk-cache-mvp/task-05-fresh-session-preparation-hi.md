# E1/task-05 — Preparation новой hydrated сессии

- Рекомендуемые модели: GPT-6 Astra или GPT-6.1 Sol; альтернатива: Grok 4.7 после проверки требуемых способностей.
- Рекомендуемый reasoning: GPT: high; для альтернатив — доступный эквивалент с записью фактического режима.
- Примерная сложность: 4/5; класс `hi`.
- Риск ошибки: высокий — неверный graph/binding/publication или cache hit влияет на semantic correctness и записи.
- Необходимые способности: `семантика_roslyn_msbuild`, `анализ_зависимостей`, `согласованность_компонентов`.
- Статус: **planned; план ready-for-review, 2026-10-07**.
- Depends on: [E1/task-00](task-00-input-manifest-hi.md), [E1/task-04](task-04-manager-host-ownership-hi.md)
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Подключить восстановленную base к текущим analyzer/generator preparation и admission с новой session.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../../code-style.md) целиком.

Только adapter к existing Services/Analyzers и manager prepare seam; полный SDK/analyzer dynamic closure audit не входит; gate semantics не ослабляются.

## Конкретный результат

- Scoped fresh binding adapter либо явный support/fallback classifier
- evidence/task-05-preparation.md с ordinary/hydrated controls.

## Проверки

- DTO не становится Complete provenance простой сменой ID
- Banned/Unavailable и restart-required сохраняются
- поддержанный generator работает с актуальными bindings или request целиком fallback
- references не удаляются ради hit
- sticky overlay matrix проходит
- требуется жизнеспособный real positive E1 control.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.
