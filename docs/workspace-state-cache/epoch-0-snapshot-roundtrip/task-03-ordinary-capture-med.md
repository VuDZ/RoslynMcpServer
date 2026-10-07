# E0/task-03 — Capture ordinary workspace

- Рекомендуемые модели: GPT-6.1 Sol; альтернативы: GLM 5.3, Grok 4.7.
- Рекомендуемый reasoning: GPT: medium; для альтернатив — применимый режим, если поддерживается.
- Примерная сложность: 3/5; класс `med`.
- Риск ошибки: средний — неверные inputs, fixtures или измерения искажают результат следующего шага.
- Необходимые способности: `реализация_по_контракту`, `проектирование_проверок`.
- Статус: **planned; план ready-for-review, 2026-10-07**.
- Depends on: [E0/task-02](task-02-snapshot-schema-hi.md)
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Снять DTO с реального успешно загруженного base solution согласно schema.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Capture и его tests; existing ordinary load вызывается как источник, production manager path не переписывается; не исследовать все MSBuild dependency categories.

## Конкретный результат

- Capture adapter и focused capture tests
- evidence/task-03-capture.md с actual inventory выбранного контроля и причинами неподдержанных cases.

## Проверки

- Сохраняются options/reference aliases/EmbedInteropTypes и все memberships
- analyzer identity не берётся из shadow path
- stock metadata reference и options проверяются на реальном ordinary capture
- silent defaults запрещены.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.
