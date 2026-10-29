# Task-06: Windows preflight и исправления независимого ревью

## Главное

- **Completed, 2026-10-29:** независимый раунд 2 принял сокращённый preflight scope, F01–F05 подтверждены; task-01 R10-F01 подтверждён раундом 11. База `7882c161ada2fa11df93287047ab90e6a6693b23`, ветка `research/workspace-host-process`. Исполнитель дополнений — GPT Sol-6.1 (`gpt-6.1-sol`). Production 1.5.18 не менялся.
- Выполнены недостающие Windows наблюдения: настоящая пара MCP Codex root/subagent, CLI negative другого checkout, SID/FS/ACL facts, environment-зависимый MSBuild counterexample, три тёплых identity-прогона на C: LocalAppData. [Полный результат](../epoch-0-design-and-spike/evidence/task-06-environment-and-deployment-preflight.md).
- Все пять findings task-06 приняты. F01 исправлен в benchmark с red/green checks; F02–F05 уточняют provenance, cache, публикацию и актуальные статусы. Повторное review подтвердило все пять; task-06: 2 раунда / 5 исправлений.
- Дополнительно принято task-01 R10-F01: `readOnly` metadata не доказывает отсутствие эффектов explicit/lazy load/prepare/MSBuild. Уточнены design/reset evidence и regressions task-02, owner choice global retire сохранён.
- Windows Release solution build успешен, 3 прежних warnings; полный main filter **1432/1432 passed**. Standalone spike Release build — 0 warnings/errors; пять checks green. Prelaunch proof обеих ОС остаётся unverified до task-02; shared activation не разрешена, O-01 не выбран.

## Findings и ответы

Источники: [task-06 round 1](../review-task-06-2026-10-29/result.md),
[task-01 round 10](../review-task-01-2026-10-29/result.md).

| ID | Ответ / изменение | Проверка |
|---|---|---|
| Task-06 F01, major | ACCEPT. Удалялся переданный scratch, включая чужие данные. Теперь `identity-run-UUID` под scratch parent; parent/его файлы сохраняются. Source и parent не включают друг друга, output вне source и только CreateNew. Cleanup проверяет свой parent/reparse. | Пять поведенческих red failures на исходном коде → пять green на исправленном; [red](../epoch-0-design-and-spike/evidence/task-06-data/identity-checks-red.json), [green](../epoch-0-design-and-spike/evidence/task-06-data/identity-checks-green.json). |
| Task-06 F02, major | ACCEPT. Собственный host image не доказывает provenance proxy, исполняемого до managed lock. Report/design/task-02 сохраняют unverified на обеих ОС, обязательный gate **до shared admission/activation**. Убрано достаточное условие «осталась только Windows pair». Scope cut владельца сохранён. | Независимая source/doc перепроверка; механизм не объявлен выполненным. |
| Task-06 F03, major | ACCEPT. Windows выпускает только warm; Linux — cold-advised с проверкой return, без обещания residency. Старый Linux raw не переименован; исходный method ограничен advisory без return/residency proof. | Windows samples все warm, green fixture проверяет это; новая Linux advice-ветка здесь не перезапущена, ограничение записано. |
| Task-06 F04, minor | ACCEPT. Flush — измеренная операция, atomic rename/crash durability/ENOSPC/partial launch benchmark не проверяет. Это task-02, как и guarded initialize. | Проверка кода benchmark и границ отчёта. |
| Task-06 F05, minor | ACCEPT. Устранены актуальные planned/waiting-owner summaries, task/index/report синхронизированы по фактической стадии; исторические срезы сохранены как история. | Links/status/diff checks; окончательные counters после review. |
| Task-01 R10-F01, major | ACCEPT. Классификация по фактической стадии: отмена чистого чтения, budget/drain и явный исход/неопределённость для effectful load/prepare и writes. Предшествовавшие эффекты не скрываются, replay нет. Metadata не является proof. | Контрпримеры текущего кода в независимом review; task-02 получает reset во время explicit readOnly list/graph load и lazy semantic load. Production реализация не менялась. |

## Метод и воспроизводимость

Исходные Linux JSON из `7882c161` сохранены без изменений. Windows raw, XML/argv,
SID/ACL facts и версия живого MCP 1.5.16.0 находятся в
[task-06-data](../epoch-0-design-and-spike/evidence/task-06-data/).
Identity измерял FDD output 1.5.18. Windows pair — реальные root/subagent контексты
одного Codex app-server; negative control — явно CLI, не третий чат. Сравнение
полной среды Windows не выполнялось; same executable path не full image proof.

```powershell
dotnet build spikes/PipeRelay/PipeRelay.csproj -c Release
dotnet spikes/PipeRelay/bin/Release/net10.0/PipeRelay.dll identity-checks artifacts/workspace-host-process/task-06-checks
dotnet spikes/PipeRelay/bin/Release/net10.0/PipeRelay.dll identity-bench bin/Release/net10.0 <fresh-parent-on-C> <new-out.json> 3 logs win-x64 win-arm64 linux-x64 linux-arm64 linux-musl-x64 osx-arm64
```

Identity scratch создаётся под свежим LocalAppData probe на томе C:; benchmark
удаляет только свой child. Затем удалён только пустой probe parent после проверки
абсолютного parent/UUID. Production roots не создавались, ACL не менялись.
Новых MCP после измерения не оставлено: subagent PID 54104 остановил себя,
negative PID 57824 вышел по stdin EOF; исходные 45500/42596 сохранены.

## Validation и приёмка

- Release spike: 0 warnings/errors; `identity-checks` red exit 1 / 5 failures, green exit 0 / 5 passed.
- Final Release solution build через MCP: success, 0 errors, прежние CS8603 (`NuGetFallbackAssemblyResolver`), CS8601 (`HostSession`), xUnit1031 (`WpfTemporaryProjectCallbackTests`); [raw](../epoch-0-design-and-spike/evidence/task-06-data/final-build-mcp.json). На предварительной сборке были также MSB3061 из-за старого MCP, в финальной их нет.
- Final Windows main filter `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`, Release, noBuild/noRestore: **1432/1432 passed**, 0 failed; [raw](../epoch-0-design-and-spike/evidence/task-06-data/final-main-mcp.json). Linux исходного результата: 1428/1428; новой Linux validation после Windows fixes нет.
- Production C#/lifecycle paths не менялись: SourceStructure/AnalyzerLifecycle отдельно не требовались. Main suite включает обычные SourceStructure tests.
- Markdown/anchors: 26 файлов, 678 локальных ссылок, broken links/anchors 0; 54 unique tools / S01–S42 без потерь, DAG 7 задач ацикличен. Три Linux raw неизменны; 12 Windows JSON валидны. `git diff --check` — PASS.
- [Task-06 round 2](../review-task-06-2026-10-29/result.md) и [task-01 round 11](../review-task-01-2026-10-29/result.md) завершены: F01–F05 и R10-F01 закрыты, новых must-fix нет. Task-06 completed в сокращённом scope, 2/5; task-01 in progress, 11/15; сумма задач E0 20 раундов / 45 исправлений. O-01, task-01 и эпоха этим не принимаются.
