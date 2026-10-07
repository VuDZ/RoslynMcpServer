# Порядок исполнения workspace state cache

Статус: **draft / ready-for-review, 2026-10-07**.

## Размер задачи и решения

Task — один проверяемый adapter/helper/scenario. Schema, capture, codec, store,
probe, manager integration, watcher attachment и write integration разделены.
Внутри task допустимы связанные focused tests и evidence, обязательная final
validation координатора. Если появились два независимых результата или несколько
новых архитектурных выборов, остановить расширение task и разрезать scope.

Рутинные implementation решения исполнитель принимает сам в пределах spec.
Не вводить обязательные подробные implementation packets на каждый DTO/helper.
Неопределённый существенный contract фиксируется отдельной небольшой поправкой
spec с evidence и возвращается на ревью; task не становится новым task-02.1.
Предложенные пути новых cache компонентов — `Services/Workspace/StateCache/`
и `RoslynMcpServer.Tests/WorkspaceStateCache/`; окончательные имена определяет
исполнитель соответствующей небольшой задачи. Existing эксперимент остаётся
источником опыта, production код не зависит от Tests assembly.

## Исходное состояние

Перед implementation проверить HEAD, dirty inventory, source/runtime версии
и применимые existing seams. [Исторический аудит](../../backlog/workspace-load-cache/epoch-0-feasibility/current-baseline-audit.md)
не заменяет актуальный код. [Production manager](../../../Services/Workspace/SolutionManager.cs),
[input map](../../../Services/Workspace/WorkspaceInputMapBuilder.cs),
[watcher starter](../../../Services/Workspace/WorkspaceDiskWatcherStarter.cs),
[source sync](../../../Services/Workspace/WorkspaceDocumentDiskSync.cs),
[non-CSharp sync](../../../Services/Workspace/WorkspaceNonCSharpDiskSync.cs),
[write boundary](../../../Services/Workspace/WorkspaceWriteBoundary.cs) используются повторно.

## Зависимости и интеграция

Depends on указывает принятые outputs, не просто наличие файлов.
Plan review-ready не разрешает implementation автоматически. Сначала принять
план текущей эпохи вместе с общими документами; затем выполнять его tasks.
Следующая эпоха требует принятого применимого scope предыдущей и final validation.
Public rollout/default не выводятся из принятия draft или isolated experiment.

Общие SolutionManager/analyzer/publication files принадлежат одному активному
исполнителю. Параллельные задачи допустимы только с независимым scope и готовым
потребляемым contract. Координатор интегрирует результаты и владеет final tests,
status bookkeeping и commits. Этот документ не назначает subagents автоматически.

## Ревью

План эпохи проверяется как spec + tasks + common README/benchmark/execution;
участники записываются в README эпохи. Task review проверяет actual diff, tests
и evidence. Приёмка реализации эпохи проверяет совокупный результат и benchmark.
Сессия независимого рецензента не является автором проверяемого scope; допускается
та же модель в новой сессии. Findings возвращаются исполнителю, исправления
перепроверяются. Авторская consistency check не считается независимым review round.

Точная модель исполнителя, модели-рецензенты, раунды и fixed findings ведутся
по [общему стандарту](../../README.md#учёт-задач-ревью-и-статистики).
Рекомендации GPT/Grok/GLM/DeepSeek не означают подтверждённую взаимозаменяемость;
проверяется пригодность к конкретному scope. Reasoning задаётся при dispatch,
а не применяется автоматически из Markdown. `xhi` рекомендует двух рецензентов.

## Validation и завершение

Обязательны [AGENTS.md](../../../AGENTS.md) и полный [code style](../../code-style.md)
до первой C# правки. During implementation — focused tests. После production
C# изменений — весь SourceStructure. Перед code/test acceptance или commit —
Release build solution и полный `Category!=AnalyzerLifecycle` main suite по
[CI workflow](../../../.github/workflows/test-suite.yml). AnalyzerLifecycle дополнительно
при изменениях analyzer/shadow-copy/load/prepare/publication либо его harness/config;
`ROSLYN_MCP_ANALYZER_LIFECYCLE=1`. Roslyn MCP runners предпочтительны.
Не ослаблять exact structural limit inventory; failures/skips/timeouts/not-run
и отсутствующую среду записывать явно. Zero-test run не pass.

Статусы: planned → in-progress → review → accepted; blocked/deferred содержат
причину. Accepted фиксируется только после применимых review/validation.
До финального ответа/commit координатор обновляет task, README эпохи, сводку,
reports и execution-status summaries: дата, принятый scope и evidence обязательны.
Functional/performance outcomes указываются отдельно; неудачный benchmark
не выдаётся за готовую оптимизацию. No-go selective spike оставляет последующие
задачи deferred и явно сохраняет прежний production fallback.

Docs-only подготовка плана требует consistency/dependency/link/diff checks;
C# build/test suites без code/config изменений не требуются. План может быть
закоммичен как ready-for-review с указанием ещё не проведённого независимого ревью.
