# E1/task-11 — независимое ревью прежних изменений, round 2

Дата: **2026-10-09**, Europe/Moscow. Рецензент: отдельный Codex reviewer subagent
`initial_review`, унаследованная модель сессии; точный runtime model ID/reasoning
не подтверждены инструментами. Reviewer работал только на чтение, build/tests
не запускал. HEAD: `da557044333e7c0a80ffa8d7ca25462a22be51cd`.

Итог исходного ревью: **MUST-FIX**. Существующий bounded partial experiment
сохраняется; полного отката не требуется. Прежний round 1 и его validation
сохранены как история, их acceptance не подменяет этот recheck.

## T11-R2-001 — clean Release CI prerequisite

`RawImportEvidenceFixture.cs:40–43` в проверенной исходной версии без проверки
перечислял `obj/Debug/net10.0`. CI делает restore и только Release build/test,
поэтому Debug directory может отсутствовать. Constructor выбрасывает
`DirectoryNotFoundException` до actual design-time experiment. Severity: **P1**.

Требуемое исправление: копировать необязательные generated directories только
при их существовании и проверить clean Release-only boundary. Исполнитель
продолжения добавил guard и два single-configuration regression cases;
закрытие finding подтверждает [fresh round 3](task-11-review-round-3.md).

## Проверенные границы

Прочитаны task/spec/README/spikes/execution, applicable accepted E0/task-07
outputs и полный code style до C# style review. Проверены три test-only
`RawImportEvidence*.cs`, targeted replay, expected inventory и source report.
Посторонний dirty WorkspaceLoadCache/tooling scope исключён.

Raw JSON подтверждает missing root evaluation 3 и отсутствие import inventory
для later dependent evaluation 9/submission 1. Report корректно ограничивает
path-level `MissingImportConsumers`, required config/lock closure и P-004 proof.
None не объявлен доказательством отсутствия paths. Channel **partial**, finite
supported real manifest **insufficient / blocked**; ordinary useful positive
не выдан за manifest positive. Дополнительных must-fix findings нет.

Final acceptance и Release build/main validation остаются за координатором:
[итоговый recheck/validation](task-11-recheck-validation.md).
