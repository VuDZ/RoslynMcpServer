# E1/task-11 — S-002: actual design-time import/restore evidence

Execution update 2026-10-09: bounded partial experiment **accepted, round 3**;
ошибка clean Release CI исправлена, новый focused 3/3 и main 1362/1362 passed.
[Продолжение, independent review и final validation](task-11-recheck-validation.md)
относятся к текущему коду. Ниже сохранены прежние measurements и история.

Дата: **2026-10-09**, Europe/Moscow. Результат эксперимента: **existing channel partial;
insufficient для supported SamplePlugin manifest; E1/task-00 readiness blocked**.
Отдельный положительный ordinary workspace контроль прошёл, но required supported
manifest positive отсутствует. Это завершённый bounded negative/partial experiment,
не приёмка E1 и не разрешение reusable envelope. Final independent review и Release
build/main validation записывает координатор; статус task/index обновляется отдельно.

Исполнитель: отдельный Codex implementer subagent. Точная runtime модель и reasoning
не доступны исполнителю как подтверждённые metadata; рекомендуемая модель из task
не подставляется вместо фактической. Commit не выполнялся. Production код, logger
policy, provenance gates, import policy и activation не менялись.

## Прогон и граница

Исходный HEAD: `da557044333e7c0a80ffa8d7ca25462a22be51cd`.
Dirty `WorkspaceLoadCache` harness и прочие исходные dirty files не менялись и не
используются как accepted evidence. Принятый применимый dependency —
[E0/task-07 bounded SamplePlugin benchmark](../../epoch-0-snapshot-roundtrip/evidence/task-07-benchmark.md).

Сохранённый прогон: `2026-10-09T11:50:24.6277393Z`, PID **29220**.
SDK **10.0.300**, MSBuild product **18.6.3+caa81fa4971f74880cdab61990cb1b11420939ec**,
assembly **15.1.0.0**, Roslyn **5.9.0.0**. AssemblyVersion MSBuild не выдаётся за его product version.
Caller создаёт `BinaryLogger` с `ProjectImports=None`, `Verbosity=Normal`, как current
production capture. Это **caller-supplied** settings: Roslyn переносит `FileName` либо
`Parameters` в remote host, но caller `Verbosity` не переносит. Effective remote
verbosity этим экспериментом не измерена и не объявляется Normal. Значение
caller settings в workspace JSON означает запрос, а не отдельную remote attestation.

Два последовательных `MSBuildWorkspace.OpenProjectAsync`, каждый в отдельном
workspace, на owned temp copy; MSBuild API выполняет actual design-time load.
Real request — неизменённые `samples/RoslynMcpPlugin/RoslynMcpPlugin.csproj`, host
csproj и source bytes, без globals, как E0 контроль. Два public projects,
`RoslynMcpPlugin.SamplePlugin` найден, workspace diagnostics пусты. Controlled
App→Library тоже дал два projects и пустые workspace diagnostics.

В owned real copy сохранены source/project/config files и existing obj restore
artifacts; package storage используется по existing assets paths. Build/restore
real корпуса не запускался. [234 copy hashes](task-11/real-copy-hashes.json) сняты
до open и проверены после open; csproj/references не менялись ради pass. Hashes
проверяют сохранность owned inputs, **не** фактическую consumed generation.

Controlled fixture задаёт independent expected inventory до наблюдения:
[controlled-expected.json](task-11/controlled-expected.json). Он содержит App и Library,
shared Directory.Build.props, shared→nested import, Debug conditional import,
absent conditional import, внешний относительно project root import,
redirected intermediate/assets и индивидуальные lock files. Owned `NuGet.Config`
очищает package sources; `dotnet restore` только готовит эти controlled inputs
до MSBuildWorkspace. Это не evidence-producing CLI build и не substitute design-time run.
Assets, locks и config действительно существуют перед analysis; тест проверяет это.

Bounds: timeout 3 минуты, owned copy не более 2000 файлов / 128 MiB, raw logs не
более 32 MiB, replay не более 100000 events на log. Analysis выполняется до cleanup
owned fixture. SDK installation tree не сканируется.

## Expected → observed paths и bindings

Sanitized targeted replay сохраняет **все** ProjectImported events этого run,
evaluation/start contexts, allowlisted instance/path properties и TaskParameter
с теми же allowlisted names; counts сохраняют типы остальных events без их values.
Generic environment/property dumps и compiler commands не сохраняются в docs.
`<owned>` заменяет temp fixture root, `<user>` — user-profile path.
Полные binlogs остаются только в ignored private TestResults и не входят в deliverable.

- [Real replay](task-11/real/capture.binlog.json): один binlog **124589 bytes**,
  **228** ProjectImported, **1** ProjectEvaluationFinished, **4** ProjectStarted.
  [Workspace](task-11/real/workspace.json) подтверждает ordinary positive.
- [Controlled replay](task-11/controlled/capture.binlog.json): один binlog **86331 bytes**,
  **224** ProjectImported, **1** ProjectEvaluationFinished, **4** ProjectStarted.
  [Workspace](task-11/controlled/workspace.json) подтверждает load health этого контроля.

Required imports: controlled shared/nested/conditional/external paths видны через
`ProjectImportedEventArgs.ImportedProjectFile`. Все принадлежат log-local
`NodeId=1, SubmissionId=0, EvaluationId=4`; `ProjectEvaluationFinished.ProjectFile`
указывает **Library.csproj**, instance properties — Debug / AnyCPU / net10.0.
Importer `ProjectFile` для nested.props — **shared.props**, а для shared.props —
Directory.Build.props. Поэтому этот field нельзя считать project consumer.
Для observed positive paths consumer связывается по evaluation key, затем с
ProjectStarted evaluation 4 / instance 3. Shared imports ожидались для **обоих**
consumers, но root App evaluation 3 не имеет import/evaluation-finished evidence.
Покрытие одного consumer не превращается в all-instance closure.
Дополнительная граница: последующий actual dependent build context 13 / submission 1
имеет **EvaluationId=9**, тогда как imported/evaluation-finished evidence имеет
**EvaluationId=4 / submission 0**. Одинаковые physical ProjectFile и числовой
ProjectInstanceId=3 не доказывают equality этих evaluations. Evaluation 9
также не имеет import inventory. Полноценный binding imported inputs к final
Library/host compilation instance не утверждается даже для dependent project.

Controlled known-absent import `../imports/absent.targets` присутствует как
`UnexpandedProject`, `ImportedProjectFile=null`, с false-condition message и тем же
Library evaluation. Существование и expected candidate известны из owned fixture.
`null` сам по себе не доказывает отсутствие resolved input: для произвольного
condition/glob/path expansion нужна отдельная completeness boundary. Этот case
не разрешает adapter трактовать все null imports как known absence.

Redirected **assets** доступны для обоих consumers через actual
`TaskParameterEventArgs`, `Kind=TaskInput`, `ItemType=ProjectAssetsFile`:
App `intermediate/App/project.assets.json` и Library `intermediate/Library/project.assets.json`.
TaskParameter EvaluationId здесь **-1**; bindings проходят через ProjectStarted
build context: App project context 4 / instance 2 / submission 0, Library project
context 13 / instance 3 / submission 1. Log ordinal + NodeId + ProjectContextId +
ProjectInstanceId + SubmissionId необходимы при общем extractor; path alone недостаточен.
Тест проверяет соответствующие ProjectStarted contexts. Для Library дополнительно
есть evaluation property `ProjectAssetsFile`, `BaseIntermediateOutputPath`,
`IntermediateOutputPath`, `MSBuildProjectExtensionsPath`.

Required **lock inputs**: fixture действительно создал App.lock.json и Library.lock.json.
Library evaluation properties содержат `RestorePackagesWithLockFile=true` и
`NuGetLockFilePath=<owned>/controlled/Library/../locks/Library.lock.json`.
App root evaluation отсутствует; root lock binding этим каналом не подтверждён.
Наличие property или lock bytes после restore не доказывает consumption lock file
во время design-time load. Это отдельный gap для required restore closure.

Required **NuGet configuration**: owned NuGet.Config существует и применялся при
preparation restore, но allowlisted raw evaluation/start properties и task inputs
не сообщают required config inventory. `RestoreConfigFile` отсутствует, даже у
observed Library evaluation. Applicable user/machine/ancestor NuGet configs и
known-absence regions этим raw channel не перечислены. Properties `NuGetPackageRoot`
и `NuGetProjectStyle=PackageReference` не являются config closure. Для real request
restore config inventory также не подтверждён. Assets — доступный отдельный
input, но существование assets не заменяет required config/lock inputs и их bindings.

Real **root plugin imports**: все 228 raw import events относятся только к host
RoslynMcpServer evaluation 4. Root plugin имеет ProjectStarted evaluation 3 /
instance 2, но нет root import events или evaluation-finished properties.
Все ProjectStarted Properties/Globals пусты (PropertyEntryTypes=[]).
Host evaluation видит Directory.Build.props, NuGet generated props/targets и
SDK import paths; это не позволяет восстановить root plugin closure. Host
`MSBuildAllProjects` содержит лишь несколько paths и не совпадает с 228 import
events, поэтому generic property не считается exhaustive inventory.

## Причина и rejected supplement

[Exact installed Roslyn logger seams](task-11/roslyn-logger-seams.json) объясняют
observations: `ProjectBuildManager` создаёт ProjectCollection с empty loggers,
отдельно добавляет logger в BuildParameters перед BeginBuild; `LoadProjectCore`
делает Project evaluation через эту collection. Root evaluation поэтому не
становится complete logged evaluation. Поздняя dependent evaluation внутри build
наблюдается в log. Это explanatory code evidence, не manifest/generation proof.
`None` **не** считается причиной отсутствия всех paths: many actual paths видны.
Sequential overwrite root binlog не подтверждён и не используется как verdict rationale.

После observed gaps отдельно проверен minimal custom `ILogger` lifecycle wrapper
с same filename и с unique per-initialization filename. Actual controlled workspace
opens successful, но `Initialize`/`Shutdown` не вызываются, lifecycle=[] и zero
binlogs. `MSBuildProjectLoader.IsBinaryLogger` распознаёт конкретное full type name
`Microsoft.Build.Logging.BinaryLogger`; произвольный wrapper не переносится в
remote build host. Supplement rejected, код wrapper удалён. Это не adapter success
и не обоснование production logger change. Второй log/Embed/source collection
не выбран автоматически; пригодный source contract остаётся unresolved.

## Incomplete controls и verdict для task-00

[result.json](task-11/result.json) отражает actual incomplete verdict:
root App и root real plugin не имеют instance-bound imported inventory.
Тест выполняет actual assertions по category/path/context и negative transformations
того же observed evidence: удаление import events, удаление evaluation-finished
binding и root-only inventory с dependent imports не дают supported result.
Последний control проверяет, что downstream imports не наследуются root consumer.
`MissingImportConsumers` в тесте — **path-level missing evidence check**: grouping
по physical ProjectFile выявляет root gap, но не выполняет all-instance admission.
Он не доказывает импортную closure dependent evaluation 9; этот отдельный gap
виден в preserved raw contexts и сохраняется как blocker. Это negative evidence
check тестового harness; reusable production adapter не добавлен.

Existing channel **partial**: paths import/absent/redirected assets/часть lock
properties доступны, но all-consumer imports и required restore config/lock
completeness не доказаны. Для finite supported manifest **insufficient**, real
supported manifest отсутствует. Narrow real positive profile из этих facts не
выведен; all-unsupported не закрывает E1.

P-004 consumed-generation binding остаётся **unproven** независимо от raw path
availability. No pre/post equality, copy hashes либо stability после open не
связывают MSBuild-consumed props/assets bytes с будущим manifest generation и не
закрывают ABA. task-00 positive capture и reusable envelope блокированы до actual
пригодного all-instance input source и generation contract.

## Validation и ownership

Три новых test-only C# файла: `RawImportEvidenceCapture`, `RawImportEvidenceFixture`,
`RawImportEvidenceTests` в `RoslynMcpServer.Tests/WorkspaceStateCache`.
Production/test configuration/version bump отсутствуют. Последний focused run:
Roslyn MCP `run_specific_test`, project `RoslynMcpServer.Tests.csproj`, class
`RawImportEvidenceTests`, configuration **Release** — **1 total, 1 passed, 0 failed**.
Это один actual experiment scenario с ordinary positive и negative controls, не zero-test run.
После финальной warning cleanup focused повтор также **1/1 passed**.

Ранние development failures сохранены как история, не засчитаны passes: первый
replay попытался читать Length у закрытого replay stream — исправлено чтением
FileInfo; experimental wrapper дал zero-binlog failures и был удалён; один compile
failure misplaced using исправлен; новый xUnit2031 заменён predicate overload.
`git diff --check` прошёл. Final coordinator Release solution build/main и independent
review — отдельная необходимая validation, ссылку и результат добавляет координатор.
AnalyzerLifecycle не требуется: production load/analyzer/publication и lifecycle
host/config не менялись. SourceStructure будет частью final main suite; structural
limit inventory не менялся. Ни main acceptance, ни task accepted status этот report
сам по себе не присваивает.
