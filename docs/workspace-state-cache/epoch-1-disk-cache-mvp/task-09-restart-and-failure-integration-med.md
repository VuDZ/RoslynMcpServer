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
side effects. Применимые controls/compare key отражают эту границу.

## Решение владельца H-002

[H-002](../human-decisions.md#h-002--ограниченный-strong-name-contract-mvp), 2026-10-07:
U-001 решён, выбран B. Поддерживаются только подтверждённые signing-independent
проекты/операции; signing-dependent и unknown cases используют ordinary load до
execution/side effects (при невозможности перехода — явный отказ). Emit/signing из
hydrated compilation вне первоначального scope. Semantic/write correctness и
remaining mandatory state сохраняются; signing flags/csproj/refs не меняются ради pass.
Проверки допуска учитывают effective options, attributes и project bindings;
один symbol query/SignAssembly=false не доказывает независимость. S-001 проверяет
real positive и excluded/unknown controls; runtime policy ещё не реализована.
Execution/review statistics остаются незаполненными.
Применение решения: [H-002 report](../h-002-report.md).

H-002 logging control: проверить явную причину signing-dependent/unknown/excluded,
stage, project instance/configuration/TFM, operation и ordinary/refusal route.
Для isolated S-001 — actual observable reason/route, для E1 — реальные log records;
общий cache miss не закрывает проверку. Ordinary failure не скрывается как успешный
fallback, raw secrets и key contents не попадают в сообщения.
