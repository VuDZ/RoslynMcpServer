# E0/task-05 — Isolated base host hydrate

Дата: **2026-10-08**, Europe/Moscow.
Исполнитель: Codex (GPT-6; точный runtime model ID недоступен).
Статус реализации: **accepted, round 2, 2026-10-08 — isolated schema-v1 Debug base-query hydrate adapter/session owner, H-001/H-002**. [Review round 2](task-05-review-round-2.md), [final validation](task-05-validation.md).
T05-R1-001 подтверждён исправленным round 2; подтверждённых исправлений после ревью **1**. Приёмка всей эпохи не присвоена.
Dependencies task-03/task-04/task-08 приняты до начала правок; prerequisite acceptance
зафиксирован координатором в `c78764d` (codec code — `8151286`).
Version / AssemblyVersion / FileVersion не менялись: **1.5.5 / 1.5.5.0 / 1.5.5.0**.
Коммит исполнителем не выполнялся.

## Actual artifact и ownership

Инертные production-compatible компоненты находятся в `Services/Workspace/StateCache/`:

- `IsolatedWorkspaceHydrator` — public-API candidate construction для schema **1** /
  profile **s001-single-target-debug-base-query-h001-h002-v1**.
- `IsolatedHydratedSession` — единственный owner нового `AdhocWorkspace` и всех
  image-backed `Metadata` ресурсов; disposal идемпотентен.
- `Models/HydratedInputOccurrence` — одно occurrence с новыми owner/document/producer IDs,
  role и provenance strings. `InputMap` session сохраняет каждое DTO occurrence,
  включая analyzer identity, linked owners и разные document kinds одного пути.
- `SnapshotOptionFacts` — узко добавлены public reconstruction helpers к принятому
  `/debug+` witness; существующий validator остался в actual `WorkspaceSnapshotCodec`.

Production `SolutionManager`, load entry, write boundary, analyzer/shadow-copy
preparation, watchers, csproj и package versions не менялись. Нового production
`TryApplyChanges` site нет. Production не зависит от Tests assembly. E1 может
использовать эти actual artifacts с обязательными fresh admission/publication gates;
перенос/замена потребует повторить checks против поставляемого кода.

## Construction и поддержанный срез

Hydrator выполняет actual codec encode/decode перед I/O. Это один schema validator
и defensive copy изменяемых DTO arrays перед await, не cache-validity proof.
Этот дополнительный codec round-trip входит в actual restore cost; ускорение
здесь не измерялось. Все project IDs создаются до construction. Новые document IDs
различают memberships. Directed references создаются через portable keys; порядок
projects не влияет на edges и memberships.

Current bytes каждого физического document path читаются один раз и разделяются
между memberships; каждый membership получает свой публичный encoding/checksum
policy. Text не берётся из DTO. UTF-8 с/без BOM и UTF-16 LE с BOM проверены;
null encoding остаётся null и не создаёт implicit persistence policy. Checksums
вычисляются заново для current text. Source comments сохраняются. DTO checksum
не используется как fresh-input admission либо как current bytes hash.

Metadata читается с диска в новые owned images. References получают сохранённые
kind/aliases/EmbedInteropTypes и path; resource image может разделяться memberships
одного path/kind. Public metadata reader проверяет image до return: Roslyn
`CreateFromImage` сам по себе ленив и принимал invalid bytes до первого binding.
Отказ после такого allocation освобождает и invalid image, и предыдущие images.
Для assembly с внешними metadata modules применяется явный отказ: у byte-image
owner нет module-file resolver, поэтому недостающая module state не заменяется
default. Отдельный real multi-module control не запускался; этот profile не
объявляет такую поддержку. Cyclic refs и script source также явно исключены.

Candidate `Solution` построен публичными `AddProject`/`WithProject*`; owner workspace
`CurrentSolution` остаётся пустым, publication/apply не выполняется. Сохраняются
project paths/name/assembly/output/output-ref/default namespace/CompilationOutputInfo,
parse symbols/features и compilation options/resolvers. Новый host получает свой
metadata service resolver и analyzer-config SyntaxTreeOptionsProvider. На real
control public scalar options сравниваются с ordinary через Equals с отдельно
объявленными identity normalizations; original resolver/provider в actual candidate
не передаются. Private reflection отсутствует.

Base compilations строятся вручную по dependency order через `CSharpCompilation.Create`,
с current source, metadata и directed compilation refs. `Project.GetCompilationAsync`
не вызывается: analyzer/generator execution не разрешено DTO. AnalyzerFileReference
сохраняет original identity и получает throwing loader; explicit GetAssembly отказывает.
Это finite base compiler query, не готовый generator-aware production workspace.

Role/RoleSource и ProducerSource переносятся без переинтерпретации по имени/path.
Unknown остаётся Unknown. Generated flag и producer подтверждённые portable facts
восстанавливаются с новыми IDs. Output и уникально связанный metadata producer
проверены отдельно. Occurrence presence вычисляется заново через current File.Exists;
old capture presence не становится fresh validity/admission evidence.

## H-001 / H-002

H-001 явно исключает external metadata XML: используется DocumentationProvider.Default.
Metadata references и их обязательные properties сохраняются. Source XML comment
реального `WorkspaceSnapshot` присутствует после hydrate. Documentation-dependent
consumers/queries, emit/signing и arbitrary analyzer/generator execution вне
поддержанного среза; ordinary routing/log integration принадлежит E1.

H-002 сохраняет объявленную empty-search-path StrongNameProvider policy только для
принятого signing-independent base-query profile. Fresh compilation effective options,
assembly attributes и directed project bindings проверяются до возврата session.
Изменение current source на AssemblyKeyFile либо unresolved attribute вызывает
контекстный refusal до результата, cleanup проходит. Exception включает stage,
project, configuration/TFM, operation, конкретную reason и route=refusal; secrets/key
contents не печатаются. Это isolated refusal, не запуск whole-request ordinary fallback
и не evidence production log records. Подписанные внешние DLL не удаляются.

## Focused validation

Roslyn MCP `run_specific_test`, project `RoslynMcpServer.Tests`, class
`IsolatedWorkspaceHydratorTests`, Release, noRestore=true, timeoutSeconds=360,
includeFullOutput=true. До independent round 1 focused run: **21/21 passed, 0 failed / 0 skipped**; после исправления — **27/27** (см. supplement ниже).

Behavioral controls: real ordinary public-loader capture → actual codec → hydrate
и independent expected SamplePlugin/IRoslynMcpPlugin / Newtonsoft JToken / source XML;
new IDs; reversed project ordering; edge aliases/EmbedInteropTypes; every input-map
occurrence/owner/role/producer; once-read linked source/additional memberships;
current changed text/constant/checksum; four encoding cases; replaced metadata identity;
missing source/metadata; pre-cancellation/invalid snapshot before resources;
cancellation after first and second image; allocation failure; invalid second image;
actual metadata disposal; idempotent session disposal; denied analyzer DLL loading.

SourceSetAnalysis / ReachabilityAnalysis проверяет hydrate-owned transitive calls:
нет MSBuildWorkspace OpenProjectAsync/OpenSolutionAsync, MSBuildProjectLoader
LoadProjectInfoAsync, Process.Start или Workspace.TryApplyChanges. Exact inventory:
три intentional `TargetOutsideTheScope` limits — `_readBytes` ← `readBytes`,
`_metadataCreated` ← `metadataCreated`, `_workspaceDisposed` ← `workspaceDisposed`;
тест утверждает полные identity strings с конкретным semantic call site, не лишь count.
Arbitrary supplied resource callbacks остаются unproved; default owned code не
исполняет их. Analysis limitations для virtual/interface dispatch остаются как
документировано в `ReachabilityAnalysis`.

Host cleanup witness сочетает behavioral completion notification с semantic
SourceSetAnalysis assertions: actual Workspace.Dispose находится в try до notification,
Metadata.Dispose вызывается для каждого owned image в finally, и это верно для failure
и normal session paths. Callback сам по себе не объявлен доказательством Dispose.
`AdhocWorkspace.AddProject` допускается после Dispose, поэтому исключение этого
метода не используется как cleanup oracle. Actual image access после disposal
даёт ObjectDisposedException. Private Roslyn reflection не используется.

Все intermediate outcomes сохранены честно:

1. **5/16 passed, 11 failed** — неверные test-oracle предположения о числе linked
   IDs (additional membership тоже входит в GetDocumentIdsWithFilePath), исключении
   AddProject после Workspace.Dispose; также ImmutableArray equality и receiver
   conditional delegate invocation в structural witness.
2. **12/16 passed, 4 failed** — ImmutableArray assertions сравнивали instance identity
   при одинаковом содержимом; conditional receiver находился вне Invocation subtree.
3. **16/16 passed** после исправления этих assertions.
4. **18/21 passed, 3 failed** — новый malformed-image control выявил lazy metadata
   validation; остальные два failure: test fixture с null output-ref и ошибочный
   AST ancestor выбор Block вместо ForEach.
5. **19/21 passed, 2 failed** — слишком широкий локальный text replacement изменил
   expected image count прежнего one-image cancellation control; возвращён exact 1.
6. **21/21 passed** после исправлений, final focused result.

Последний build warnings report после option-flow annotation: только unchanged
CS8603 в NuGetFallbackAssemblyResolver и xUnit1031 в WpfTemporaryProjectCallbackTests.
Final validation координатора завершена: Release solution build passed, hydrate **27/27**,
codec **25/25**, весь SourceStructure **123/123**, complete main **1350/1350**.
[Полные результаты и acceptance](task-05-validation.md). AnalyzerLifecycle **not-run / not required**:
production load/prepare/publication и lifecycle host не изменены, isolated adapter инертен.

New PID, persistent envelope store, disk .cs edit, production writes/activation,
benchmark / speedup / peak memory / Open/DTB runtime counters — **not-run/not-measured**;
это task-06/task-07/E1 scope. Structural owned-path witness не выдаётся за runtime
DTB measurement произвольных callbacks.

## Исправление после independent review round 1

T05-R1-001 / P2 принят исполнителем. Default equality record `SnapshotProjectKey`
различала casing path, хотя actual codec на Windows принимает такие target/owner/
producer keys как один physical project instance. Новый focused control сначала
воспроизвёл дефект **0/6 passed, 6 failed**: actual codec encode/decode проходил,
hydrate получал KeyNotFoundException на target, owner или producer rebinding.
Это deliberate pre-fix red regression run, а не незавершённая final validation.

Исправление сохраняет единственную codec identity: internal `ProjectKeyIdentity`
выдаёт уже существующий comparer без изменения schema validation. Hydrate ids и
immutable returned `Session.Projects` явно используют его. Configuration/Platform/
TargetFramework остаются case-sensitive. Path casing следует OS InputPathCanon.Comparer.
Дополнительного comparer с копией правил нет; accepted payload не ужесточён.

Шесть regression cases независимо меняют casing Target, occurrence.Owner и
occurrence.Producer, каждый в двух project order. Контроль подтверждает actual codec
acceptance до hydrate, edge/fresh document+producer IDs и returned Projects lookup
по alternate path casing. Metadata producer и public generated producer покрыты.
На case-sensitive ОС предусмотрены codec rejection и distinct returned map lookup;
эта ветка локально на Windows не запускалась и не заявляется отдельным OS pass.

После исправления Roslyn MCP Release, noRestore=true:

- `IsolatedWorkspaceHydratorTests`: **27/27 passed, 0 failed / 0 skipped**.
- `WorkspaceSnapshotCodecTests`, noBuild=true после той же компиляции: **25/25 passed**.
- `git diff --check`: passed (только LF→CRLF warnings).

Review round 1 coordinator evidence остаётся
[task-05-validation-round-1.json](task-05-validation-round-1.json): Release build,
focused **21/21**, SourceStructure **123/123**, main **1344/1344** до исправления.
Это историческая validation review code, не final acceptance исправленного code.
Final code freeze принят координатором после independent [round 2 ACCEPT](task-05-review-round-2.md)
и повторной [Release solution/SourceStructure/main validation](task-05-validation.md). Exact structural callback-limit inventory
остаётся тем же: 3 identities, каждый TargetOutsideTheScope.
