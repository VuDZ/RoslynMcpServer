# E2/task-01 — Обновление existing .cs при restart

- Рекомендуемые модели: GPT-6.1 Sol; альтернативы: GLM 5.3, Grok 4.7.
- Рекомендуемый reasoning: GPT: medium; для альтернатив — применимый режим, если поддерживается.
- Примерная сложность: 3/5; класс `med`.
- Риск ошибки: средний — неверные inputs, fixtures или измерения искажают результат следующего шага.
- Необходимые способности: `реализация_по_контракту`, `проектирование_проверок`.
- Статус: **planned; spec-v2 revised-for-review, 2026-10-07; реализация не начата**.
- Depends on: [E2/task-00](task-00-startup-diff-roles-hi.md)
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Применить новые source texts ко всем existing memberships восстановленной base без DTB.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Переиспользовать WorkspaceDocumentDiskSync/reconcile/read policy; не интерпретировать globs и не добавлять/удалять Documents.

## Конкретный результат

- Startup source refresh adapter, focused tests и evidence/task-01-source.md.

## Проверки

- Один physical read, актуальная encoding policy, shared source одинаков во всех проектах
- changed-during-read вызывает retry/miss
- no Open/DTB witness
- semantic assertions сверены со fresh ordinary load.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.

## Уточнения specification v2

Основание: **P-013**; [change ledger](../change-ledger.md).

Positive source refresh выполняется только в admitted content-only profile; unknown/non-admitted evaluation dependency даёт ordinary fallback. Не анализировать произвольные task bodies и не доверять имени SDK вместо evidence. Supported safe positive case сохраняется.
