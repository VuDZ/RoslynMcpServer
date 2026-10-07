# E0/task-08 — S-001 — capability реального контроля

- Рекомендуемые модели: GPT-6 Astra или GPT-6.1 Sol; альтернатива: Grok 4.7 при подтверждённой пригодности к этому bounded scope.
- Рекомендуемый reasoning: GPT high; для альтернатив — применимый режим с записью фактического значения.
- Примерная сложность: 4/5; класс `hi`.
- Риск ошибки: высокий — ложный supported verdict разрешает недоказанный positive capture.
- Необходимые способности: `семантика_roslyn_msbuild`, `анализ_зависимостей`, `проектирование_проверок`.
- Статус: **planned / experiment not-run; spec-v2 revised-for-review, 2026-10-07**.
- Depends on: [E0/task-00](task-00-current-seams-and-controls-med.md)
- Decision prerequisite: [U-001 выбран владельцем](../unresolved.md#u-001--fidelity-положительного-e0); выполнен 2026-10-07 по H-001/H-002 (B); task-00 и experiment остаются planned/not-run.
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

Проверить bounded ordinary capture/minimal public reconstruction выбранного неизменённого real control в accepted U-001 fidelity границе.

## Входы и границы

[Spec](spec.md), [карта эпохи](README.md), [spikes](../spikes.md),
[execution](../execution.md) и accepted dependency outputs обязательны.
До C# правки прочитать [code style](../../code-style.md) полностью.

Минимальный isolated harness/inspection и report; без production codec/store, повторного поиска отсутствующих getters, полного SDK/restore closure audit или изменения control csproj/refs.

## Конкретный результат

- evidence/task-08-capability.md: версии/control revision/globals/query/U-001 outcome, actual provider/reference types и instance bindings.
- Supported/partial/unsupported verdict с traceable source каждого mandatory state; XML исключение H-001 проверено; strong-name exclusions H-002 и ordinary fallback для signing-dependent/unknown cases проверяются обязательно.

## Проверки

- Один actual real positive с metadata/project references, однозначными instances и заранее выбранными observable results либо honest blocked reason.
- Minimal prototype не требует полного нового capture/schema/codec, schema sketch может предшествовать.
- No silent defaults/private reflection/ref removal; unsupported scope не превращается в negative-only E0 success.
- Full cross-process round-trip по-прежнему проверяет task-06.

Основание: **P-001**, [change ledger](../change-ledger.md). Experiment outcome
не подменяет full epoch acceptance; negative report не разрешает dependent
positive implementation. Final applicable validation и обновление шапки/README
выполняются по [AGENTS.md](../../../AGENTS.md).

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
