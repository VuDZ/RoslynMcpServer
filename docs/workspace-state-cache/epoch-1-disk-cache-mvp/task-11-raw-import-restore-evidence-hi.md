# E1/task-11 — S-002 — actual import/restore evidence

- Рекомендуемые модели: GPT-6 Astra или GPT-6.1 Sol; альтернатива: Grok 4.7 при подтверждённой пригодности к этому bounded scope.
- Рекомендуемый reasoning: GPT high; для альтернатив — применимый режим с записью фактического значения.
- Примерная сложность: 4/5; класс `hi`.
- Риск ошибки: высокий — ложный supported verdict разрешает недоказанный positive capture.
- Необходимые способности: `семантика_roslyn_msbuild`, `анализ_зависимостей`, `проектирование_проверок`.
- Статус: **accepted, round 3, 2026-10-09 — bounded test-only actual design-time experiment; existing channel partial, supported real manifest insufficient / task-00 blocked; Release build/main 1362/1362 passed**. [Review/validation](evidence/task-11-recheck-validation.md).
- Depends on: [E0/task-07](../epoch-0-snapshot-roundtrip/task-07-roundtrip-benchmark-med.md)
- Decision prerequisite: Принятый применимый E0 scope; результат spike нужен до принятия E1/task-00 и reusable envelope.
- Модель, реализовавшая задачу: Codex, прежний и новый implementer subagents; унаследованная модель сессии, точный runtime model ID/reasoning не подтверждены инструментами.
- Модели, проводившие ревью: Codex, отдельные reviewer subagents; исторический round 1, новый initial round 2 и fresh recheck round 3; точный runtime model ID/reasoning не подтверждены инструментами.
- Количество раундов ревью: 3.
- Количество исправлений после ревью: 2 (T11-R1-001, T11-R2-001), открытых must-fix: 0.
- Отчёт о реализации, ревью и validation: [source evidence](evidence/task-11-raw-evidence.md), [продолжение](evidence/task-11-continuation.md), [round 2](evidence/task-11-review-round-2.md), [round 3](evidence/task-11-review-round-3.md), [final validation](evidence/task-11-recheck-validation.md).

## Цель

Проверить actual MSBuildWorkspace design-time raw events/properties при текущем ProjectImports=None и установить доступность required paths/all-instance bindings.

## Входы и границы

[Spec](spec.md), [карта эпохи](README.md), [spikes](../spikes.md),
[execution](../execution.md) и accepted dependency outputs обязательны.
До C# правки прочитать [code style](../../code-style.md) полностью.

Owned control copy, bounded isolated raw-log analysis до cleanup и report. Current provenance gate/mode не меняются автоматически; второй log/Embed/source collection не выбираются до observed gaps. Обычный CLI build не заменяет этот эксперимент.

## Конкретный результат

- evidence/task-11-raw-evidence.md: versions/events/properties, expected→observed category/path/consumer mapping и completeness boundary.
- Existing channel sufficient/partial/insufficient verdict; отсутствующие/conflicting categories отдельно. Channel/profile decision следует из evidence, не утверждается заранее.

## Проверки

- Nested/conditional/absent/shared/external imports, redirected assets/intermediate, применимые lock/NuGet config inputs проверены по controlled expected inventory.
- Current generic properties не считаются closure без подтверждения; None не считается proof отсутствия paths.
- Finite supported profile имеет один unchanged real positive и negative incomplete→unsupported controls либо manifest readiness blocked.
- Raw path availability не доказывает P-004 consumed-generation binding.

Основание: **P-011**, [change ledger](../change-ledger.md). Experiment outcome
не подменяет full epoch acceptance; negative report не разрешает dependent
positive implementation. Final applicable validation и обновление шапки/README
выполняются по [AGENTS.md](../../../AGENTS.md).
