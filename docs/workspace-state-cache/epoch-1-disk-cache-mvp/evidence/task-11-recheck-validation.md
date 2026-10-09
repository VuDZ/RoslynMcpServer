# E1/task-11 — итог продолжения, review и final validation

Дата: **2026-10-09**, Europe/Moscow. Координатор: Codex; точный runtime model
ID/reasoning не подтверждены инструментами. HEAD до и после работы:
`da557044333e7c0a80ffa8d7ca25462a22be51cd`. **Коммитов нет**.

Итог: **accepted, round 3 — bounded test-only S-002 partial/negative experiment**.
Существующий результат сохранён; исправлена ошибка подготовки owned copy на
clean Release CI. Required supported real manifest **не доказан**; E1/task-00
implementation и reusable envelope остаются **blocked**. Production readiness,
приёмка E1 и activation не объявляются.

## Ревью и изменение

Прежний [round 1](task-11-review-round-1.md) и [validation](task-11-validation.md)
сохранены как история. Новый независимый [round 2](task-11-review-round-2.md)
выявил **T11-R2-001**: обязательное чтение отсутствующего Debug obj directory
ломало experiment в clean Release CI. Единственный исполнитель продолжения
добавил guard отсутствующих generated directories и два regression cases,
Debug-only и Release-only. [Fresh round 3](task-11-review-round-3.md): **ACCEPT**,
T11-R2-001 **CLOSED**, открытых must-fix **0**.

История task-11: **3** independent review rounds, **2** подтверждённых исправления
(историческое T11-R1-001 и текущее T11-R2-001). Исполнитель и оба новых reviewer —
отдельные Codex subagents, унаследованная модель сессии; рекомендуемая модель
task не подставлялась как фактическая. Координатор C# не редактировал.

## Final validation координатора

После последних C# изменений выполнены Roslyn MCP runners:

- `run_dotnet_build`, решение `RoslynMcpServer.sln`, Release,
  noIncremental=true: **Build succeeded**.
- `run_specific_test`, основной tests project, `RawImportEvidenceTests`, Release,
  noBuild=true, noRestore=true: **3 total / 3 passed / 0 failed / 0 skipped**.
  Два sparse constructor regression cases и actual design-time experiment.
- `run_test_by_filter`, основной tests project, Release,
  `Category!=AnalyzerLifecycle`, noBuild=true, noRestore=true, timeout 1200 s:
  **1362 total / 1362 passed / 0 failed / 0 skipped**. Total=Passed; zero-test
  или partial/unparsed run не засчитан. Полный SourceStructure включён.
- AnalyzerLifecycle: **not-run / not required**. Изменения ограничены isolated
  test-only harness; production analyzer/shadow-copy/load/prepare/publication,
  lifecycle host и configuration не менялись. Structural limits не изменены.

[Неизменённые ответы MCP](task-11-recheck/validation-tools.json) включают initial
build/focused и final build/focused/main. Первоначальный focused 1/1 не засчитан
как validation нового fix. MCP runner не создаёт TRX; skip accounting основан
на parsed total/pass summary, отдельный Assert-NoSkippedTests не запускался.
Существующие warnings: CS8603 в NuGetFallbackAssemblyResolver, CS8601 в HostSession,
xUnit1031 в WpfTemporaryProjectCallbackTests. Новых warnings в owned scope нет.

## Новый actual evidence run

Focused run final code: PID **70504**, real open **2026-10-09T12:02:21.254014Z**,
SDK 10.0.300 / MSBuild product 18.6.3 / Roslyn 5.9.0.0. Caller None/Normal
сохранён; remote verbosity не измерена. [Workspace JSON](task-11-recheck/focused/real/workspace.json),
[controlled replay](task-11-recheck/focused/controlled/capture.binlog.json),
[real replay](task-11-recheck/focused/real/capture.binlog.json),
[expected inventory](task-11-recheck/focused/controlled-expected.json),
[234 unchanged-copy hashes](task-11-recheck/focused/real-copy-hashes.json),
[result](task-11-recheck/focused/result.json). Temp/user paths sanitized; полные
binlogs остаются в ignored `TestResults/task-11-8e4522b089cb44c7b343c728ec48635d`.
Прежние measurements не переписаны. Анализ выполнялся до cleanup owned fixture.

Ordinary useful positive: два projects, пустые workspace diagnostics,
SamplePlugin symbol найден. Imports 224/228 относятся к dependent evaluation 4;
root evaluation 3 и later dependent evaluation 9/submission 1 не покрыты.
Required restore config/lock closure отсутствует. Result **partial / blocked**,
RealSupportedManifest=false, P-004 **unproven**. Raw paths/copy hashes не являются
consumed-generation proof. Второй log/Embed/source collection не выбран.

## Ownership и статусы

Test-only code: три `RawImportEvidence*.cs`; продолжение изменило Fixture и Tests,
Capture сохранён. Reviewed identities приведены в round 3; повторно сверены
после validation. Production, версия 1.5.6 и configuration не менялись.
Посторонние исходные dirty files, включая WorkspaceLoadCache и task-00-manifest.md,
сохранены с прежними SHA256. Task-00 status обновлён только как dependency blocker,
его DTO/adapter не реализован и не принят.

Task-11, E1 index, series execution/spike summaries синхронизированы на этот
accepted scope с ссылками на review/validation. Requirements и activation gates
сохранены. Локальные links/JSON, source identities, dirty preservation и
`git diff --check` проверены; результаты находятся в
[bookkeeping checks](task-11-recheck/checks.json).
