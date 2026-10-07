# E1/task-09 — Production restart/failure проверки

- Рекомендуемые модели: GPT-6.1 Sol; альтернативы: GLM 5.3, Grok 4.7.
- Рекомендуемый reasoning: GPT: medium; для альтернатив — применимый режим, если поддерживается.
- Примерная сложность: 3/5; класс `med`.
- Риск ошибки: средний — неверные inputs, fixtures или измерения искажают результат следующего шага.
- Необходимые способности: `реализация_по_контракту`, `проектирование_проверок`.
- Статус: **planned; spec-v2 revised-for-review, 2026-10-07; реализация не начата**.
- Depends on: [E1/task-07](task-07-existing-watcher-attachment-hi.md), [E1/task-08](task-08-hydrated-csharp-write-hi.md)
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Проверить целый production disk load и fallback в новом PID, включая ошибки и изменение inputs.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Тестовый harness/tests; production исправления выявленных дефектов возвращаются соответствующему владельцу адаптера и отражаются в его task/report.

## Конкретный результат

- Integration tests и evidence/task-09-integration.md
- Inventories и independent semantic/write assertions ordinary versus hydrated.

## Проверки

- Real unchanged hit
- source/graph/new file/absent input/binary/version change → miss
- corrupt/truncated store, capture failure, cancellation, startup race
- configuration/platform/TFM, linked .cs и lifecycle controls
- никакие zero-test/skip/timeouts не засчитаны как pass.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.

## Уточнения specification v2

Основание: **P-002, P-003, P-004, P-005, P-006, P-007, P-010, P-012**; [change ledger](../change-ledger.md).

Добавить omitted/explicit Release, different FileSettings, inherited Loaded globals и normalized Any CPU controls. Environment absent→present/changed value при прежних files; props-after-consumption before probe; blocking no-capture; health/coverage/role/owner parity и fresh watcher failure; ancestor config create/delete, explicit configs/linked boundary. Lazy disabled vs explicit enabled; overlay no-fresh-binding actual fallback без cache-induced Unavailable. Все первоначальные controls сохраняются.

## Решение владельца H-001

[H-001](../human-decisions.md): XML documentation provider/text metadata/DLL references
вне MVP equivalence; source comments загруженных проектов и остальные обязательные
metadata/options facts сохраняются. Это объявленное исключение, не silent loss.
Documentation-dependent consumers получают ordinary route либо явный отказ до execution/
side effects. Применимые controls/compare key отражают эту границу; strong-name
решение U-001 всё ещё требуется, execution/review statistics остаются незаполненными.
