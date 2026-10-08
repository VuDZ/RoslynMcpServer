# E0/task-02 — независимое ревью, round 1

Дата: **2026-10-08**, Europe/Moscow. Рецензент: Codex (GPT-6; точный runtime
model ID недоступен). Отдельная reviewer-сессия, не автор проверяемого scope.
Итог: **ACCEPT** для version-1 descriptor schema и её живого same-process
ordinary consumer в конечном Debug/base-query профиле S-001/H-001/H-002.
Подтверждённых must-fix findings: **0**, исправлений после независимого ревью: **0**.
Приёмка задачи и запись финальных build/test результатов остаются координатору.

## Проверенный scope и основания

Прочитаны [task-02](../task-02-snapshot-schema-hi.md), [spec](../spec.md),
[README эпохи](../README.md), [README серии](../../README.md),
[execution](../../execution.md), [benchmark](../../benchmark.md),
[task-00 controls](task-00-controls.md), [task-00 acceptance](task-00-review-round-2.md),
[S-001 capability](task-08-capability.md), [S-001 acceptance](task-08-review-round-2.md),
[H-001/H-002](../../human-decisions.md) и [implementation evidence](task-02-schema.md).
[Code style](../../../code-style.md) прочитан целиком до style review.
Применимые `.editorconfig` в репозитории не найдены. Проверены только новые файлы;
прежний dirty scope WorkspaceLoadCache и посторонние untracked файлы не принимались.

Проверены все 18 новых DTO под `Services/Workspace/StateCache/Models/` и
[RealControlSchemaCapture](../../../../RoslynMcpServer.Tests/WorkspaceStateCache/RealControlSchemaCapture.cs),
[SnapshotOptionsCapture](../../../../RoslynMcpServer.Tests/WorkspaceStateCache/SnapshotOptionsCapture.cs),
[SnapshotSchemaTests](../../../../RoslynMcpServer.Tests/WorkspaceStateCache/SnapshotSchemaTests.cs).
Read-only сопоставление с существующими `WorkspaceInputMapBuilder`,
`RealControlReconstruction` и `CapabilityOperationGate` подтверждает границы
применяемых conservative roles, public factories и H-002 operation witness.

SHA256 проверенных итоговых файлов:

- RealControlSchemaCapture.cs: `1AD5C2E2A043572198BCAE85E52FCE4E9502E458E6F955F1EE33A328AA81EC43`.
- SnapshotOptionsCapture.cs: `FB03D91833232F08BBAC3299A82126B68D5D062ECF89E08BC57ADE38C66A088D`.
- SnapshotSchemaTests.cs: `85B3C0EF2381E15622E1438233E9DA5EE5048544696656A1F3F7B0BC0F587FE7`.
- SnapshotCompilationOptions.cs: `4637D7ADE944B0D1B372F2172F68B19FA7EBAFFC4C01E06141D6374B2BFF0E1C`.

## Проверка требований по коду

- Raw requested, pre-open loader globals и evaluated values — отдельные DTO
  facts. Null сохраняет absence; public evaluation с теми же globals и
  compilation attributes служат конечным binding witness выбранного single-target
  контроля. Missing/multitarget/ambiguous state не заменяется guessed defaults.
- Stable instance key состоит из canonical path/configuration/platform/TFM.
  Directed references используют ключи без Roslyn IDs и сохраняют aliases и
  EmbedInteropTypes. Duplicate loaded path и duplicate owner/kind/path membership
  дают explicit unsupported.
- Parse properties/features и compilation scalar state реально попадают в DTO.
  Обратная сборка использует public constructors/With* и Roslyn Equals. Finite
  `/debug+` witness обозначен явно; `/debug-` negative control проверяет отказ
  parser-owned state, которое нельзя воспроизвести этим witness. Resolvers и
  comparer имеют закрытые identities и отказ при неизвестных случаях.
- Normalization StrongNameProvider, MetadataReferenceResolver и
  SyntaxTreeOptionsProvider в Equals не скрывает произвольные defaults: первые
  два имеют H-002/public-host factory boundary, tree provider допускается только
  после null public descriptor в fresh Project и соответствует объявленной
  analyzer-config construction policy. Custom descriptor provider отказывается.
  Nullable ScriptClassName теперь сохраняет реальное nullable public значение.
- Все source/additional/config memberships сохраняют paths/name/folders/kind,
  public generated flag и encoding/checksum metadata. Текст используется только
  для проверки disk equality и не входит в payload. Occurrences удерживают всех
  owners и отдельные роли на общем path. Suspicious names при false flag остаются
  Unknown; producer для metadata требует unique matching output и directed edge.
  Synthetic linked/additional и ambiguous producer controls проверяют именно эти
  ветви, без заявления AdditionalFile real-positive.
- Analyzer paths имеют original pre-overlay descriptor source; callsite сначала
  использует public MSBuildProjectLoader и AdhocWorkspace.AddProjects, без shadow
  preparation. Caller witness здесь фиксирует реальное происхождение этого
  isolated inventory. Нет утверждения о production admission произвольного
  переданного Solution. Non-file/unproven analyzer state имеет explicit outcome.
- DTO содержат primitive/string/array/DTO fields. Compilation, SyntaxTree,
  ProjectId/DocumentId, sessions, operation contexts, source/generated text и
  shadow paths не сериализуются. H-001 binary XML exclusion записан явно;
  source comments остаются в file-backed source contract. Наличие analyzer
  identity не разрешает cached DLL execution.

Persisted `TestResults/task-02/schema-observation.json` сверён с evidence:
plugin/server — 5/220 source, 226/236 metadata, 13/13 analyzers, 2/2 configs;
Debug/AnyCPU/net10.0; directed plugin → server. Occurrence totals — 219 UserInput,
6 Unknown, 4 AnalyzerConfig, 462 MetadataDependency, 26 AnalyzerDependency,
4 Output, 2 EvaluationInput. Дополнительные 18 server sources — новые DTO;
report не приписывает текущему дереву исторический task-08 source inventory.

## Validation и пределы verdict

Рецензент выполнил code/evidence/inventory consistency review и `git diff --check`
(exit 0; единственное предупреждение относится к прежнему dirty
DependencyEvidenceRunner.cs). Тесты независимо повторно не запускались:
координатор владеет обязательной final validation. Исполнитель сообщил и
implementation evidence фиксирует focused **7/7 passed**, координатор сообщил
успешный final Release solution build после code freeze; эти сообщения не
заменяют отдельную запись окончательного SourceStructure/main CI результата.

ACCEPT относится к task-02 artifact и минимальному живому schema consumer.
Reusable capture/input-generation binding task-03, codec/store, новый PID/hydrate,
imports/restore/walk-up/region completeness, benchmark, production writes,
runtime fallback/logging и activation не проверялись и этим ревью не принимаются.
Нет нового вопроса, требующего изменения normative specification. Коммит не делался.
