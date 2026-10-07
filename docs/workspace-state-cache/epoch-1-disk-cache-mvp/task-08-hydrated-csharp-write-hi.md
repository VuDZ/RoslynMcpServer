# E1/task-08 — Правка .cs на hydrated host

- Рекомендуемые модели: GPT-6 Astra или GPT-6.1 Sol; альтернатива: Grok 4.7 после проверки требуемых способностей.
- Рекомендуемый reasoning: GPT: high; для альтернатив — доступный эквивалент с записью фактического режима.
- Примерная сложность: 4/5; класс `hi`.
- Риск ошибки: высокий — неверный graph/binding/publication или cache hit влияет на semantic correctness и записи.
- Необходимые способности: `семантика_roslyn_msbuild`, `анализ_зависимостей`, `согласованность_компонентов`.
- Статус: **planned; spec-v2 revised-for-review, 2026-10-07; реализация не начата**.
- Depends on: [E1/task-07](task-07-existing-watcher-attachment-hi.md)
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Провести existing .cs edit через текущую manager write boundary на Adhoc host.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Existing .cs persistence/apply seams и tests; add/remove/rename/project-file write capabilities в этой задаче не реализуются.

## Конкретный результат

- Минимальный host-specific apply adapter и targeted encoding/stale-write tests
- evidence/task-08-write.md.

## Проверки

- Preflight/exact inverse/session/base stamps обязательны
- same physical file согласован во всех memberships
- encoding и partial persistence не подменяются rollback
- unsupported operation отвергнута до bytes
- shadow refs никогда не записываются в csproj.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.

## Уточнения specification v2

Основание: **P-009**; [change ledger](../change-ledger.md).

Production apply остаётся единственным existing wrapper site; public candidate construction не служит bypass для actual writes. Unsupported операции и принятые fidelity exclusions H-001/H-002 проходят ordinary fallback до side effects; при невозможности перехода — явный отказ. Signing-dependent/unknown writes не исполняются на hydrated state.

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
