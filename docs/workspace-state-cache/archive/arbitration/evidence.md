# Evidence и границы арбитража

Дата: **2026-10-07**. Run: `workspace-state-cache-arbitration-2026-10-07`.
HEAD при разборе: `67564de6c48e6d2eb38c4542fd2ef714910da0b0`.
Арбитр не являлся автором specification, review catalogs или defense в этой сессии;
субагенты не запускались. Точный model ID не утверждается по имени каталога.

## Contract и provenance

Использован файл пользователя
`C:/Users/VuDZ/.codex/attachments/a81ac097-5fd0-437c-be86-a7ff7975ca24/Pasted text.txt`.
Его SHA-256 записан в validation.json. Skill arbitrate-multi-review прочитан как
вспомогательная процедура; при различиях применяется prompt: отдельные ARB files,
SPIKE/DEFERRED и только output directory. Новая specification не создавалась.

Первоначальный [input-manifest.json](input-manifest.json) содержит SHA-256 всех **100**
файлов текущего пакета вне arbitration. Отдельно проверяется совпадение **45** target
files со snapshot review-astra. Эти hashes устанавливают version/immutability,
а не correctness future cache. Ссылки на старые v2 experiments используются как
техническое evidence; v2 normative gates и старые arbitration verdicts не наследуются.

В начале checkout содержал tracked изменение DependencyEvidenceRunner.cs и untracked
helpers/tests, review/defense и прочие пользовательские файлы. Их авторство и acceptance
не присваивались арбитру. Незавершённые новые helpers не использованы как production contract.

## Review corpus

Прочитаны все 16 source findings, result/coverage трёх catalogs, author result,
relations/coverage, ledger и все 16 individual responses. Review markers и defense
marker — completed. Review-astra validation и snapshot использованы для provenance.
Registry сохраняет каждую source identity; defense позиции проверялись по существу,
не считались доказанными автоматически. Независимость reviewers заявлена их coverage;
скрытая история сессий не верифицировалась.

## Normative inputs

Прочитаны общий README, execution, benchmark, review instructions и четыре spec E0–E3.
Точечно прочитаны относящиеся к disputes task contracts:
E0 task-00/02/03/04; E1 task-00/03/04/05/06/07/09;
E2 task-00/03/04/05. Остальные задачи не подвергались новому полному review;
арбитраж разрешает существующий corpus, а не заявляет новый аудит всех 33 задач.
Первичные constraints: working real positive, no silent defaults/private capture,
opt-in default, existing admission/write/ownership, limited profile/fallback,
optional E3. Ретроспективные statements defense о пользовательском intent не
использованы для изобретения отсутствующей нормы.

## Проверенное repository evidence

- [SolutionManager](../../../../Services/Workspace/SolutionManager.cs): EnsureWorkspaceFromConfigAsync,
  LoadAndPrepareAsync precedence и overlay branches, LoadCoreAsync open→watch ordering,
  HasBlockingLoadFailure, TryApplyWorkspaceChanges, watcher loadGraphComplete argument.
- [MsBuildWorkspaceProperties](../../../../Services/Workspace/MsBuildWorkspaceProperties.cs):
  Create, IsSameLoadCache, MatchesPassedLoadArguments; файл прочитан полностью.
- [FileSettings](../../../../Config/RoslynMcpFileSettings.cs): KnownKeys, Configuration/Platform/TFM;
  [WorkspaceTools](../../../../Tools/WorkspaceTools.cs): параметры и descriptions load.
- [Provenance capture](../../../../Services/Analyzers/AnalyzerProvenanceCaptureService.cs):
  ProjectImports=None, OpenAndCaptureAsync/OpenWorkspaceAsync, ReplayOne, ReadProperties,
  cleanup; [capture gate](../../../../Services/Analyzers/AnalyzerProvenanceCaptureGate.cs) полностью.
- [Input-map builder](../../../../Services/Workspace/WorkspaceInputMapBuilder.cs): Build,
  AddDocument/RoleForDocument, CoverageReasons, imports/restore/config/custom-task categories,
  WalkUpFileNames. [WorkspaceInputMap](../../../../Services/Models/WorkspaceInputMap.cs):
  PublishedAsComplete conjunction. [Role reader](../../../../Services/Workspace/RoslynDocumentBuildSideEffect.cs)
  и targeted InputRole uses в [classifier](../../../../Services/Workspace/WorkspaceDiskEventClassifier.cs).
- [WriteBoundaryChecks](../../../../RoslynMcpServer.Tests/SourceStructure/WriteBoundaryChecks.cs):
  полный symbol-reference invariant; это чтение semantics, не C# style review.
- [HydrateHostExperiment](../../../../RoslynMcpServer.Tests/WorkspaceLoadCache/HydrateHostExperiment.cs):
  CaptureAsync, provider refusal, consumed-source comparison, public DocumentInfo creation
  и isolated apply sites; targeted refusal/stale-source tests прочитаны.
- [Исторический hydrate report](../../../backlog/workspace-load-cache/epoch-0-feasibility/evidence/task-01-hydrate-host.md):
  раздел public API limitations. Полный старый feasibility package не анализировался.
- Локальный SDK 10.0.300 Microsoft.Common.CurrentVersion.targets: RAR/SpecificVersion
  context и условный FindDependencies. Версия файла не выдаётся за runtime version MCP.

## Первичные внешние источники

Проверены 2026-10-07; факты отделены от выводов о конкретном support profile:

- [MSBuild environment properties](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-properties?view=visualstudio#environment-properties)
  — environment как evaluation input; ARB-006.
- [BinaryLogger parameters](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-command-line-reference?view=visualstudio#switches-for-loggers)
  — collection project/import source files не тождественна наличию всех raw events;
  ARB-012. Upstream BinaryLogger открыт дополнительно, но installed-version sufficiency
  из него не выводилась; это предмет S-002.
- [Analyzer configuration files](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-files)
  — разная applicability EditorConfig и global AnalyzerConfig; ARB-013.
- [ResolveAssemblyReference](https://learn.microsoft.com/en-us/visualstudio/msbuild/resolveassemblyreference-task?view=visualstudio)
  — exact identity constraints; ARB-015.
- [PortableExecutableReference API](https://learn.microsoft.com/dotnet/api/microsoft.codeanalysis.portableexecutablereference)
  — публичная поверхность как corroboration historical limitation; не новый ordinary runtime capture.

## Ограничения

Новая cache implementation отсутствует; логические interleavings и documented behavior
не выданы за воспроизведённые false hits. S-001/S-002, race/environment/RAR mutations,
benchmarks **not-run**. Полный production tree не перепроверялся. C# style не обсуждался,
C#/test/build configuration не менялись; build, main CI, SourceStructure и
AnalyzerLifecycle **not-run**. Artifact validation проверяет coverage/links/status/
immutability, а не заменяет будущие runtime acceptance tests.

Критически недостающих файлов для adjudication нет. Открытый U-001 и невыполненные
spikes не означают незавершённый arbitration; их completion claims не сделаны.
