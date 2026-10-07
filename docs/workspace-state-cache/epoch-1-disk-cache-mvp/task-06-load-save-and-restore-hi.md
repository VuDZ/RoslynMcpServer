# E1/task-06 — Disk lookup и capture в load

- Рекомендуемые модели: GPT-6 Astra или GPT-6.1 Sol; альтернатива: Grok 4.7 после проверки требуемых способностей.
- Рекомендуемый reasoning: GPT: high; для альтернатив — доступный эквивалент с записью фактического режима.
- Примерная сложность: 4/5; класс `hi`.
- Риск ошибки: высокий — неверный graph/binding/publication или cache hit влияет на semantic correctness и записи.
- Необходимые способности: `семантика_roslyn_msbuild`, `анализ_зависимостей`, `согласованность_компонентов`.
- Статус: **planned; spec-v2 revised-for-review, 2026-10-07; реализация не начата**.
- Depends on: [E1/task-01](task-01-text-and-directory-probe-med.md), [E1/task-02](task-02-binary-fast-probe-med.md), [E1/task-03](task-03-atomic-envelope-store-med.md), [E1/task-04](task-04-manager-host-ownership-hi.md), [E1/task-05](task-05-fresh-session-preparation-hi.md)
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Встроить opt-in cache attempt и capture в existing load/prepare path.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Только manager/load tool wiring; host/store/probe adapters готовы; watcher publication
handshake завершается следующей задачей. До его завершения restore attempt
возвращает ordinary fallback с причиной незавершённой integration и не публикует
disk candidate, даже при `useDiskCache=true`.

## Конкретный результат

- Manager load wiring и proposed WorkspaceTools parameters/brief outcome
- evidence/task-06-load.md.

## Проверки

- forceReload bypass RAM+disk
- false default
- request/effective args не смешиваются
- disabled RAM path прежний
- whole-request miss имеет bounded reason
- reusable base capture после пригодного ordinary load без blocking failure, с graph/input generation binding
- cache I/O fault не ломает обычный результат
- reset RAM не стирает диск.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.

## Уточнения specification v2

Основание: **P-002, P-004, P-005, P-006, P-007**; [change ledger](../change-ledger.md).

Disk context проверяет независимо merged pre-open globals и absence, не raw-request shortcut/saved evaluated default. Explicit/ConfigFile policy различена; lazy без opt-in lookup/capture/write disabled, globals работают. Reusable capture только пригодного no-blocking load с graph↔consumed-generation evidence. Unknown significant environment даёт fallback. New dependency/region требует bounded reconfirmation или запрета capture; pre/post hashes не доказывают отсутствие ABA.

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
