# E0/task-02 — Минимальная переносимая schema

- Рекомендуемые модели: GPT-6 Astra или GPT-6.1 Sol; альтернатива: Grok 4.7 после проверки требуемых способностей.
- Рекомендуемый reasoning: GPT: high; для альтернатив — доступный эквивалент с записью фактического режима.
- Примерная сложность: 4/5; класс `hi`.
- Риск ошибки: высокий — неверный graph/binding/publication или cache hit влияет на semantic correctness и записи.
- Необходимые способности: `семантика_roslyn_msbuild`, `анализ_зависимостей`, `согласованность_компонентов`.
- Статус: **accepted, 2026-10-08; round 1, без must-fix замечаний; принятый scope — schema v1 и живой isolated ordinary consumer для S-001 Debug/base-query профиля; reusable capture и codec/hydrate не входят в scope этой приёмки; приёмка всей эпохи не выполнена**.
- Depends on: [E0/task-00](task-00-current-seams-and-controls-med.md), [E0/task-08 — S-001](task-08-real-control-capability-hi.md)
- Модель, реализовавшая задачу: Codex (GPT-6 Astra).
- Модели, проводившие ревью: Codex (GPT-6 Astra), независимая сессия.
- Количество раундов ревью: 1.
- Количество исправлений после ревью: 0.
- Отчёт о реализации, ревью и validation: [schema](evidence/task-02-schema.md); [review round 1](evidence/task-02-review-round-1.md); [final validation / acceptance](evidence/task-02-validation.md).

## Цель

Описать единственную versioned DTO schema для начального capture/hydrate без Roslyn object serialization.

## Входы и границы

[Спецификация эпохи](spec.md), [README](README.md),
[правила исполнения](../execution.md) и outputs Depends on обязательны.
Перед C# правкой прочитать [code style](../../code-style.md) целиком.

Новые DTO файлы под Services/Workspace/StateCache или isolated prototype с единым согласованным назначением; codec/store/manager не реализуются в этой задаче.

## Конкретный результат

- evidence/task-02-schema.md и DTO declarations: request/effective properties, stable project-instance keys, directed refs, options, document roles/paths/encoding, reference properties и portable analyzer identities
- Явные limits и причины unsupported для начального среза.

## Проверки

- Нет source/generated text, Compilation/SyntaxTree, переносимых ProjectId/session/operation contexts или shadow paths
- ambiguity и unavailable public option getters имеют явный outcome
- схема имеет живой real-capture потребитель.

Это focused минимум; применимые Release build/main/SourceStructure/lifecycle
проверки выполняются по [AGENTS.md](../../../AGENTS.md) и execution rules.
Дата, принятый scope, evidence и фактические модели обновляются в шапке и
README эпохи после ревью/validation. Новый существенный вопрос возвращается
в spec точечной поправкой, а не превращает эту задачу в большой decision packet.

## Уточнения specification v2

Основание: **P-001, P-002, P-008, P-010**; [change ledger](../change-ledger.md).

Schema sketch до U-001/S-001 допустим; final executable scope требует их результатов. Три уровня properties описываются отдельно. Artifact ownership/location/version и E1 reuse route фиксируются без обязательного Tests-only размещения. Occurrence roles/all owners и подтверждённые producer bindings имеют source; Unknown сохраняется, generator text/execution provenance не переносится.

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
Execution/review statistics этого task заполнены в шапке после независимого ревью и final validation.
Применение решения: [H-002 report](../h-002-report.md).
