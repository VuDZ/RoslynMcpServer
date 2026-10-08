# E0/task-04 — final validation и acceptance

Дата: **2026-10-08**, Europe/Moscow. Координатор: Codex (GPT-6; точный runtime
model ID недоступен). Итог: **accepted** для bounded schema-v1 codec в принятом
S-001 профиле, независимое [ревью round 3](task-04-review-round-3.md).
T04-R1-001/002/003 закрыты; подтверждено **3** исправления после ревью.
Codec/tests находятся в HEAD `81512861b5131785cc77de424a4068b8a9c1da4b`.
Hydrate, cross-process round-trip, benchmark и приёмка эпохи этим отчётом не принимаются.

Координатор выполнил Roslyn MCP runners по текущему рабочему дереву, Release:

- `run_dotnet_build`, `RoslynMcpServer.sln`, `noIncremental=true`: **passed**.
- `run_specific_test`, `WorkspaceSnapshotCodecTests`, `noBuild=true`,
  `noRestore=true`: **25/25 passed**.
- `run_test_by_filter`, `FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure`,
  `noBuild=true`, `noRestore=true`: **123/123 passed**.
- Первый полный `Category!=AnalyzerLifecycle`, main Tests project,
  `noBuild=true`, `noRestore=true`, timeout 300 seconds: **1322/1323 passed**,
  **1 failed**. Existing
  `WorkspaceNonCSharpDiskSyncTests.Saved_additional_file_and_editorconfig_reach_the_published_snapshot`
  не нашёл `severity = error` в опубликованном editorconfig. Причина не установлена;
  ранее тот же тест падал в [S-001 validation](task-08-capability.md), с другим assertion.
- Повтор всего `WorkspaceNonCSharpDiskSyncTests` без правок: **4/4 passed**.
- Повтор **полного** main suite с теми же параметрами без C# правок:
  **1323/1323 passed**, 0 failed; passed совпадает с total, skips не наблюдались.

Рецензент отдельно выполнил codec **25/25** с pre-test build; source не менялся.
Первый main failure сохранён как failure, а не включён в successful outcome.
Tool outputs доступны в истории исполнения; retained TRX этим отчётом не заявляется.
Посторонние dirty WorkspaceLoadCache/tooling files не принимались и не коммитятся,
хотя полная suite выполнялась по текущему рабочему дереву.

AnalyzerLifecycle **not-run / not required** для codec: инертная serialization
не меняет analyzer/shadow-copy/load/prepare/publication paths, lifecycle host
или test/build configuration. Structural limit inventory не ослаблялся.
Version / AssemblyVersion / FileVersion остаются **1.5.5**.
Task/index/implementation report синхронизированы координатором; public activation
и readiness всей эпохи не присвоены.
