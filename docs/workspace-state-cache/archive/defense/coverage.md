# Defense Coverage

Дата: **2026-10-07**. Основание — приложенный Multi-Agent Defense prompt,
с последующим прямым указанием пользователя использовать именно prompt, не skill.
Output directory: `docs/workspace-state-cache/defense/`.

## Reviews processed

- [review-astra](../review-astra/result.md): все 3 finding files, result, coverage,
  validation.md, validation.json, target-snapshot.json и `.status=completed`.
  Catalog name не подтверждает model ID; сам reviewer сообщает exact model unknown.
- [review-ds](../review-ds/result.md): все 8 finding files, result, coverage,
  `.status=completed`. Точное имя модели в этих артефактах не заявлено;
  оно не выведено из суффикса каталога.
- [review-grok](../review-grok/result.md): все 5 finding files, result, coverage,
  `.status=completed`. Заявленная reviewer model — Grok 4.7.

Все доступные `review-*` непосредственно в target package обнаружены; других
каталогов review этой specification в текущем inventory нет. Historical review
отложенной программы не включается как четвёртый review текущего proposal.
Предположение о независимости corpus основано на заявлениях coverage reviewers;
их скрытую историю сессий эта защита не проверяет.

## Findings processed

- review-astra/F-001–F-003: **3/3**.
- review-ds/F-001–F-008: **8/8**.
- review-grok/F-001–F-005: **5/5**.
- Всего **16/16**, по одному author position и response file.

Полная bijection — [finding-ledger.md](finding-ledger.md), machine-readable
inventory — [positions.json](positions.json). Source IDs не менялись.

## Evidence inspected

Proposal: README, execution/benchmark/preparation-report/review instructions;
E0–E3 specs, reviewed task contracts/карты в авторском контексте подготовки
пакета, отдельно перечитанные targets спорных claims. Текущий пакет совпадает
с **45** SHA-256 исходных файлов snapshot review-astra; это проверка version
provenance, не тест correctness будущего cache.

Production code, targeted участки:

- `Services/Workspace/SolutionManager.cs`: LoadAndPrepareAsync precedence,
  ordinary open→watch ordering, HasBlockingLoadFailure, lazy entry, existing apply wrapper.
- `Services/Workspace/MsBuildWorkspaceProperties.cs`: Create, IsSameLoadCache,
  MatchesPassedLoadArguments.
- `Services/Analyzers/AnalyzerProvenanceCaptureService.cs`: BinaryLogger parameters,
  OpenAndCaptureAsync, BuildSnapshot, ReplayOne, ReadProperties, temporary log cleanup.
- `Services/Analyzers/AnalyzerProvenanceCaptureGate.cs`: missing/foreign/status admission.
- `Services/Workspace/WorkspaceInputMapBuilder.cs`: roles, regions, walk-up,
  categories/coverage; `Services/Models/WorkspaceInputMap.cs`: PublishedAsComplete.
- `Services/Workspace/RoslynDocumentBuildSideEffect.cs`, disk classifier и tool
  load surface; `Config/RoslynMcpFileSettings.cs`: KnownKeys/properties.
- `RoslynMcpServer.Tests/SourceStructure/WriteBoundaryChecks.cs`: exact reference-site invariant.

Experiment/history:

- `HydrateHostExperiment.cs`: CaptureAsync provider refusals/consumed characters,
  CreateBaseSessionAsync/DocumentInfo roles и isolated TryApplyChanges,
  CaptureCompilationOptions StrongNameProvider refusal.
- `HydrateHostExperimentTests.cs`: stale source/public option refusals и их setup.
- `WorkspaceObservationReader.cs`: stated limited XML-sidecar evidence; этот файл
  untracked, использован только как текущий explanatory comment, не как accepted API.
- [Historical hydrate evidence](../../../backlog/workspace-load-cache/epoch-0-feasibility/evidence/task-01-hydrate-host.md):
  конкретные public API limits и различие synthetic/ordinary positive outcome.
- Локальный SDK 10.0.300 `Microsoft.Common.CurrentVersion.targets`:
  RAR и conditional `_FindDependencies`.

Primary external sources реально открыты 2026-10-07:

- [MSBuild environment properties](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-properties?view=visualstudio#environment-properties).
- [ResolveAssemblyReference](https://learn.microsoft.com/en-us/visualstudio/msbuild/resolveassemblyreference-task?view=visualstudio).
- [Analyzer configuration files](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-files).
- [MSBuild command-line BinaryLogger parameters](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-command-line-reference?view=visualstudio).
- [Upstream BinaryLogger.cs](https://source.dot.net/Microsoft.Build/Logging/BinaryLogger/BinaryLogger.cs.html).

Conversation-derived evidence: владелец выбрал постепенный MVP/file-directory
diff/current watchers, допустил fast binary heuristic, попросил небольшие tasks
и reuse исследований. Отдельного разрешения терять XML-doc/signing fidelity,
требовать overlay disk hit или включать lazy cache default не было. Это
contemporaneous context данной авторской сессии, материализованный здесь;
не post-hoc оправдание новой архитектуры.

## Limitations

- Новый cache ещё не реализован. Race/environment/SpecificVersion/config/target
  scenarios в этой защите не запускались; statements различают logical contract
  counterexample и наблюдавшийся run.
- Current concrete StrongNameProvider каждого future real control не исследован;
  compiler API limitations проверены по code/experiment, не новым live capture.
- Не снимался installed-version raw binlog inventory, поэтому upstream event
  behavior не выдан за complete 18.6.3 import/restore evidence.
- Полный production tree не анализировался семантически; SHA-protection source
  files не является code review. C# style не проверялся, C# не менялся.
- Build, main CI, SourceStructure, AnalyzerLifecycle и benchmarks not-run:
  defense-only stage, без production/test/build configuration правок.
- Legacy v2 normative gates и прежние review/арбитражи не наследуются новой серией.
- Exact author model ID недоступен; `—` сохранено, название модели не угадывалось.

## Missing inputs

Критически необходимых review/spec files нет среди отсутствующих: все 16 findings
доступны. Подлежащие выбору fidelity/mode/profile contracts и непроведённые dynamic
experiments — открытые design/evidence вопросы, а не причина пропустить finding.
Source attachment SHA и неизменность inputs сохранены в [input-manifest.json](input-manifest.json).
