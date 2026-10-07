# Coverage — review-ds

Ревьюер: одна сессия, независимый adversarial review пакета
`docs/workspace-state-cache/`. Дата: 2026-10-07. Каталог результатов:
`docs/workspace-state-cache/review-ds/`.

## Reviewed target

Пакет `docs/workspace-state-cache/` целиком, как один проверяемый proposal:

- `README.md` серии (цель, маршрут, индекс входов, бинарная политика, поведение MVP,
  статистика, подготовка пакета);
- `execution.md` (размер задачи, исходное состояние, зависимости, ревью, validation);
- `benchmark.md` (контролы, прогон, метрики, сценарии эпох, решение по результатам);
- `preparation-report.md` (scope подготовки, авторская проверка, открытые вопросы, пределы);
- `review/README.md` (инструкции рецензенту — входной документ, не результат ревью);
- четыре канонические spec: `epoch-0-snapshot-roundtrip/spec.md`,
  `epoch-1-disk-cache-mvp/spec.md`, `epoch-2-content-refresh/spec.md`,
  `epoch-3-project-refresh/spec.md`;
- четыре execution README эпох (E0–E3): статусы, карты задач, зависимости, раздельные
  plan/task/implementation-счётчики;
- все **33** task-файла: E0 task-00…07, E1 task-00…10, E2 task-00…06, E3 task-00…06 —
  шапки (модели, reasoning, сложность, риск, способности, статус, Depends on),
  «Цель», «Входы и границы», «Конкретный результат», «Проверки».

## Context inspected

- `AGENTS.md`, `docs/code-style.md` (только как обязательный контекст процесса; правила
  стиля в findings не применялись — предмет ревью не код).
- Production seams, на которые ссылается пакет:
  - `Services/Workspace/SolutionManager.cs` (skeleton целиком + чтение ключевых участков:
    895-961 `LoadAndPrepareAsync` overlay-ветка, 1380-1474 publication helpers,
    1940-2149 `LoadCoreAsync`/`HasBlockingLoadFailure`/flush, 2486-2558 watcher start/stop
    и callbacks);
  - `Services/Workspace/WorkspaceWriteBoundary.cs`, `WorkspaceFilePersistence.cs`,
    `SourceTextEncoding.cs`, `MsBuildWorkspaceProperties.cs`,
    `WorkspaceDiskWatcherStarter.cs`, `WorkspaceInputMapBuilder.cs` (1-499),
    `RoslynDocumentBuildSideEffect.cs`;
  - `Services/Analyzers/AnalyzerProvenanceCaptureService.cs`,
    `AnalyzerProvenanceCaptureGate.cs`, `AnalyzerExecutionGate.cs`;
  - `Services/Workspace/WorkspaceDiskEventClassifier.cs` (использование `InputRole`);
  - `Tools/WorkspaceTools.cs` (`load_workspace`, `reset_workspace`, early-return и ответ);
  - `Config/RoslynMcpFileSettings.cs` (конфигурационная поверхность: только
    `workspace-path`);
  - `RoslynMcpServer.Tests/SourceStructure/WriteBoundaryChecks.cs` (структурный контракт
    единственного `Workspace.TryApplyChanges`);
  - `RoslynMcpServer.csproj`, `Directory.Build.props` (TFM/SDK/пакеты; отсутствие
    `SignAssembly` / `GenerateDocumentationFile`).
- Исторические документы, на которые пакет ссылается как на источник опыта:
  `docs/backlog/workspace-load-cache/epoch-0-feasibility/evidence/task-01-hydrate-host.md`
  (включая раздел «Конкретные ограничения public API») и
  `docs/backlog/workspace-load-cache/btcpay-candidate.md` (только статус/факты о
  невалидности кандидата для прежнего профиля). Использовались как evidence о
  технических ограничениях, не как действующие requirements.

## Context not inspected

- Другие каталоги ревью: `docs/workspace-state-cache/review-astra/` **не читался**
  (contents сознательно не открывались; известно только наличие каталога и имён файлов из
  общего листинга дерева). Результаты других reviewers, defence и arbitration не
  использовались как evidence.
- Любые `review-response/` и `_archive/arbitration/` материалы legacy-программы
  (`docs/backlog/workspace-load-cache/_archive/…`) не читались.
- Остальные legacy-документы v2 (`epoch-1-workspace-lifecycle`, `epoch-2-conservative-disk-cache`,
  `epoch-3-live-consistency`, `epoch-4-measured-optimizations`, `TRACEABILITY-v2.md`,
  `UNRESOLVED-v2.md`, `POST-ARBITRATION-ISSUES.md`, `cache-contract.md`, `verification.md`)
  не изучались: они не нормативны для нового MVP, а их O1–O8/gates по README серии не
  наследуются. Их отсутствие в evidence не влияет на findings.
- Прочие каталоги `docs/` (`mcp-tool-surface-evolution`, `test-improvements`,
  `test-runner-selection`, `archive`, `github-releases`), `Plugins/`, `Hosting/`,
  `Diagnostics/` — не использовались.
- `RoslynMcpServer.Tests/WorkspaceLoadCache/` (существующие тесты эксперимента) —
  прочитан только по упоминанию в отчёте эксперимента; сами тесты не анализировались.

## Source/code verification

Проверено по коду (не по тексту плана):

- Контракт сопоставления загрузок: `MatchesPassedLoadArguments` против `IsSameLoadCache`
  и предупреждение в XML-doc; состояние `Loaded*` живёт в процессе → F-001.
- Analyzer admission: provenance-снимок создаётся только внутри MSBuild-open
  (`OpenAndCaptureAsync`), gate отвергает `null`, `LoadAndPrepareAsync` уходит в
  `Unavailable` при `shadowCopyInSolutionAnalyzers=true` → F-002.
- Существующий load path всегда выполняет binlog-provenance capture в рамках ordinary
  загрузки (`LoadCoreAsync:2045-2078`) — учтено при оценке F-002/F-004.
- Вердикт загрузки и coverage: `LastDiagnostics` → `HasBlockingLoadFailure` →
  `loadGraphComplete` в `StartDiskWatcherUnderLock` и `CoverageReasons` → F-004.
- Роли входов: `RoleForDocument` + private-reflection `IsGenerated` + использование
  `InputRole` в `WorkspaceDiskEventClassifier` → F-008.
- Структурный write-boundary контракт `SourceStructure/WriteBoundaryChecks.cs` → F-007.
- Конфигурационная поверхность `RoslynMcpFileSettings` (нет ключа кэша) и
  `EnsureWorkspaceFromConfigAsync` → F-005.
- Разделение путей `Services/Workspace/StateCache/` vs `RoslynMcpServer.Tests/WorkspaceStateCache/`
  и формулировка E1/task-03 «использовать E0 codec» → F-006.

Nothing was executed: C# не собирался, тесты не запускались, MCP-сервер не
перезапускался, workspace через `load_workspace` не загружался. Все выводы — чтение
исходников, документации и истории эксперимента.

## Coverage limitations

- Representability ограничения проверены только по отчёту эксперимента
  (`task-01-hydrate-host.md`), а не воспроизведены на текущем Roslyn 5.9.0 / текущем
  HEAD: положительный ordinary capture реального контроля в этой сессии не снимался.
  Отсюда `Confidence: medium` у F-003.
- Поведение реального MSBuild-графа для выбранного контроля (есть ли подпись сборки,
  XML-doc рядом с зависимостями, in-solution analyzer projects, multi-TFM) не измерялось:
  контроль в E0/task-00 ещё не выбран. Это влияет и на F-002 (overlay), и на F-003.
- Массовые участки `SolutionManager.cs` (≈3000 строк) читались выборочно по skeleton и
  ключевым участкам; полный анализ всех MSBuild-специфичных веток (shadow-copy
  publication plan, reconciliation, write freshness) не выполнялся.
- `WorkspaceInputMapBuilder.cs` прочитан частично (строки 1-499 из 757); выводы F-008
  опираются на участки ролей и категорий, остальная часть (categories/search roots) не
  проверялась.
- Нормативные документы вне пакета (`docs/ARCHITECTURE.md`, `docs/README.md#учёт-задач-ревью-и-статистики`)
  системно не сверялись; статистика/учёт в findings не оценивались.
- Спецификации E2 и E3 проверены на согласованность с README/execution/benchmark и
  существующими механизмами, но отдельных findings по ним нет: их требования либо
  покрыты задачами, либо явно объявлены fallback-ом. Отсутствие findings не означает
  проверки каждого сценария E2/E3 — это предел покрытия.
- Оценка стоимости реализации (размер refactor host seam, трудоёмкость) не проверялась
  измерениями; F-007 говорит только о структурном контракте.
