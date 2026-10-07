# E3/task-04 — Целая publication selective candidate

- Рекомендуемые модели: GPT-6 Astra; альтернативы: GPT-6.1 Sol, Grok 4.7 при подтверждённой пригодности к concurrency/binding scope.
- Рекомендуемый reasoning: GPT: high; для альтернатив — доступный эквивалент с записью фактического режима.
- Примерная сложность: 5/5; класс `xhi`.
- Риск ошибки: высокий — неверный graph/binding/publication или cache hit влияет на semantic correctness и записи.
- Необходимые способности: `анализ_конкурентности`, `семантика_roslyn_msbuild`, `управление_ресурсами`.
- Статус: **planned; план ready-for-review, 2026-10-07**.
- Depends on: [E3/task-02](task-02-project-reopen-adapter-hi.md), [E3/task-03](task-03-membership-change-routing-med.md)
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Собрать retained+reloaded graph и безопасно опубликовать его через manager lifecycle.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../../code-style.md) целиком.

Только manager merge/binding/publication/ownership seams; не переписывать write/analyzer policies или store.

## Конкретный результат

- Merge/publication integration с targeted revision/cancellation tests
- evidence/task-04-publication.md.

## Проверки

- Directed refs и IDs rebound однозначно
- fresh analyzer/session admission, preserved non-CSharp layer и write contexts
- final probe/watch revision race detected
- old same-key remains until success
- cancelled/failed merge не публикуется
- lazy reader resources живут до завершения использования.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.
