# E0 — Snapshot round-trip

Статус: **spec-v2 / revised-for-review, 2026-10-07**. Production runtime не изменён;
task-00 accepted для docs-only inventory seams и выбора real control, 2026-10-07
([round 2 / validation](evidence/task-00-review-round-2.md)); S-001 accepted для isolated base-query slice, **round 2, 2026-10-08**
([review](evidence/task-08-review-round-2.md)); capture-ready и приёмка эпохи не присвоены.

Общие границы — [README серии](../README.md), [порядок исполнения](../execution.md),
[бенчмарки](../benchmark.md). Эти три документа обязательны для ревью эпохи.

## Результат

Получить один сквозной сценарий обычной MSBuild-загрузки, сохранения переносимого
описания и восстановления в новом процессе через публичные Roslyn API.
Первый semantic-запрос должен вернуть ожидаемый результат. Production load path
в этой эпохе не меняется; результат пригоден для последующей интеграции.

## Поддерживаемый срез

Task-00 выбирает маленький контроль и реальный неизменённый проект/решение
с actual metadata/project references и однозначными loaded instances.
Новый scope не обобщается на все большие решения. Сохраняются publicly supported
parse/compilation options, documents/memberships, directed project refs и metadata
properties. Непредставимое mandatory state и ambiguous TFM дают отказ, не guessing.
Source/generated text, session/ProjectIds и shadow paths не сериализуются.

> **RESOLVED U-001 — H-001/H-002, вариант B, 2026-10-07**
>
> XML documentation бинарных зависимостей исключена по H-001; source comments
> сохраняются. H-002 допускает подтверждённый signing-independent scope без точного
> переноса непубличного strong-name state. Signing-dependent/unknown cases используют
> ordinary load до операции; emit/signing из hydrated compilation исключены.
> Private reflection/silent defaults для remaining state и потеря references запрещены.
> [Принятое решение](../unresolved.md#u-001--fidelity-положительного-e0).

Порядок: U-001 → bounded capability S-001 → принятый executable positive slice.
Task-08 выполняет S-001 в выбранной H-001/H-002 границе fidelity. Schema sketch и inspection могут
предшествовать этой проверке; full capture не становится prerequisite собственного
schema design. Final schema/capture и dependent codec/hydrate не выдаются за
пригодные для real control до successful capability verdict. Negative outcome
даёт blocked reason и возврат scope на решение, а не negative-only E0 acceptance.

Analyzer identities сохраняются, generator output не становится переносимым Document.
Base round-trip не разрешает DLL execution из DTO. В E1 fresh preparation либо
whole-request fallback остаются обязательными; generator-dependent E0 case отмечается
not-supported до принятого соответствующего механизма. References не удаляются ради pass.

Rationale: [ARB-001/002](../archive/arbitration/decision-ledger.md); getter limitations
используются как известное evidence, не исследуются заново.

## Использование прежних результатов

За отправную точку взять [hydrate experiment и его ограничения](../../backlog/workspace-load-cache/epoch-0-feasibility/evidence/task-01-hydrate-host.md)
и существующие WorkspaceLoadCache tests. Не повторять dependency closure research.
Известные ограничения публичного чтения StrongNameProvider/DocumentationProvider
используются без повторного поиска getters. S-001 проверяет actual providers,
допустимые источники состояния и inner-TFM binding выбранного среза в принятой
U-001=B границе; exact strong-name reconstruction не требуется для доказанного
signing-independent scope и не заявляется.
Private reflection и silent defaults запрещены.

## Приёмка

Нужны реальный ordinary capture, round-trip в новом PID, независимый expected
semantic result, bounded codec failures и освобождение candidate ресурсов.
Synthetic DTO-only успех не закрывает эпоху. Изолированная правка существующего
`.cs` проверяет сохранение encoding и linked memberships; production write
boundary этим экспериментом не заменяется.

Бенчмарк: ordinary baseline и isolated restore, load/restore/query/capture timings
по [единому протоколу](../benchmark.md). Если выбранный большой корпус ещё
не поддержан, report сохраняет отказ; поддержанный реальный контроль всё равно нужен.

## Portable roles и executable artifacts

DTO фиксирует raw requested, pre-open loader globals, evaluated instance values
раздельно. Occurrence roles, all owners/memberships и подтверждённые producer
bindings имеют source и однозначное rebinding к новым IDs. Public generated flag
или proven role facts восстанавливают единственную input map; unknown не угадывается
как user/generated по имени или obj path. Generator text/execution provenance не переносится.

E0 передаёт описанное ownership и identity actual DTO/codec/hydrate artifacts.
Инертные production-compatible components допустимы без runtime-entry integration.
E1 использует тот же executable codec либо явно фиксирует перенос/замену и
повторяет bounds/malformed/round-trip checks против фактически поставляемого кода.
Production не зависит от Tests assembly; prototype pass не заменяет final CI.

Production TryApplyChanges остаётся единственным manager reference site. Public
candidate construction без apply допустим, если сохраняет publication/write
invariants; isolated apply calls не переносятся вслепую. Эти ограничения не
предписывают будущие private classes или обязательную двойную реализацию.

## Принятое ограничение XML fidelity

По [H-001](../human-decisions.md) DocumentationProvider metadata references не
обязан точно переноситься и не вызывает unsupported сам по себе. References/aliases/
EmbedInteropTypes и остальные mandatory facts сохраняются. Source-comment parity
загруженных проектов обязательна; external XML text equality не acceptance requirement.
Documentation-dependent операции/потребители получают ordinary route либо явный
отказ до side effects. No silent defaults остаётся для remaining required state;
XML потеря объявлена отдельным ограничением, не точным восстановлением. Strong-name
решение принято по H-002; successful S-001 остаётся prerequisite dependent positive scope.

## Strong-name boundary H-002

[H-002](../human-decisions.md#h-002--ограниченный-strong-name-contract-mvp) закрывает
U-001 выбором B. Hydrated scope сохраняет заявленные semantic results, diagnostics
поддержанных операций и корректность поддержанных writes только в подтверждённом
signing-independent profile. Signing-dependent/unknown request идёт целиком через
ordinary load до зависимой операции/side effects; невозможность перехода даёт отказ.
Emit/signing из hydrated compilation вне первоначального scope. Effective options,
assembly attributes и project bindings учитываются; подписанная внешняя DLL сама
по себе не означает unsupported. Flags/csproj/references не меняются ради pass.
Изменение inputs либо planned write, создающее signing-зависимость/неопределённость,
требует повторного допуска и ordinary route до зависимого результата или persistence.
Required controls: реальный supported semantic/write scope и signing-dependent,
unknown, excluded-emit cases с fallback/refusal до execution/side effects.
S-001 подтверждает конечный профиль; exact hidden-state fidelity не заявляется.
Support/help/outcomes и benchmark compare key отражают H-001/H-002 ограничения.
H-002 bypass/capture skip/fallback/refusal явно логируются: stage, project instance,
configuration/TFM, operation, конкретная signing-dependent/unknown/excluded причина
и ordinary/refusal route. Общего cache miss недостаточно; ordinary failure отделён
от ограничения cache profile, secrets/key contents не выводятся. Обязательны controls
причины/маршрута в S-001 и actual log records на E1 integration.
