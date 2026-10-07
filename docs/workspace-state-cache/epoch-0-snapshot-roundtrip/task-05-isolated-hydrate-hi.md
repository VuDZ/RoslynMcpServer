# E0/task-05 — Восстановление base host

- Рекомендуемые модели: GPT-6 Astra или GPT-6.1 Sol; альтернатива: Grok 4.7 после проверки требуемых способностей.
- Рекомендуемый reasoning: GPT: high; для альтернатив — доступный эквивалент с записью фактического режима.
- Примерная сложность: 4/5; класс `hi`.
- Риск ошибки: высокий — неверный graph/binding/publication или cache hit влияет на semantic correctness и записи.
- Необходимые способности: `семантика_roslyn_msbuild`, `анализ_зависимостей`, `согласованность_компонентов`.
- Статус: **planned; план ready-for-review, 2026-10-07**.
- Depends on: [E0/task-03](task-03-ordinary-capture-med.md), [E0/task-04](task-04-snapshot-codec-low.md)
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Восстановить isolated AdhocWorkspace с новыми Roslyn IDs, текущими source bytes и свежими metadata references.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Hydrate и isolated host ownership; не менять production SolutionManager или write boundary; использовать существующий эксперимент как отправную точку, не копировать его strict admission research целиком.

## Конкретный результат

- Hydrate adapter/session owner и focused resource/identity tests
- evidence/task-05-hydrate.md.

## Проверки

- Нет OpenSolution/OpenProject/DTB в hydrate-owned path
- порядок проектов не меняет edges/memberships
- linked physical path читается один раз
- cancellation/failure dispose host и references
- analyzer execution не разрешается DTO.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.
