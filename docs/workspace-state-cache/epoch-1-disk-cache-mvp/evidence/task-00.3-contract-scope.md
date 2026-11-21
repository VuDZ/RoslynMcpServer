# E1/task-00.3 — audit границы freshness на подготовленных restore artifacts

Дата: **2026-10-09**, Europe/Moscow. Результат исследования: **not-viable как
достаточный маршрут разблокировки task-00 на имеющемся evidence**. Один draft
ниже отделяет ordinary load от будущего NuGet restore, но ни одна restore-only
категория пока не получила доказанного exclusion для реального supported request.
Исследование не доказывает принципиальную невозможность такого профиля.

Scope: docs-only contract audit текущего кода, локальных SDK targets и accepted
bounded experiments. Новых ordinary/restore mutation runs, C# controls и production
правок нет. Исполнитель — Codex implementer subagent; точный runtime model
ID/reasoning не подтверждены tools. Рекомендации моделей task не являются runtime
metadata. HEAD при audit: `fc72dc80d330874ae5160950e972a8c8b4d67874`.
Посторонние dirty файлы сохранены. Commit не выполнялся. Independent review,
final docs validation и статус task/index записывает координатор отдельно.

## Основание и уровень доказательств

Нормативные inputs прочитаны: [task-00.3](../task-00.3-manifest-contract-scope-hi.md),
[E1 spec](../spec.md), [E1 index](../README.md), [series README](../../README.md),
[execution](../../execution.md), [task-00](../task-00-input-manifest-hi.md),
[change ledger, P-004/P-011](../../change-ledger.md),
[H-001/H-002](../../human-decisions.md). Outputs Depends on:
[E0/task-07 benchmark](../../epoch-0-snapshot-roundtrip/evidence/task-07-benchmark.md)
и [accepted round 2 / validation](../../epoch-0-snapshot-roundtrip/evidence/task-07-review-round-2.md),
[S-002 source](task-11-raw-evidence.md) и
[accepted recheck / validation](task-11-recheck-validation.md).
Дополнительное принятое evidence:
[task-00.1 source](task-00.1-supplemental-source.md),
[validation](task-00.1-validation.md), [actual controls](task-00.1/controls.json).
Unaccepted dirty WorkspaceLoadCache и task-00-manifest.md не используются как
доказательство manifest readiness.

Обозначения evidence ниже: **observed** — сохранённый accepted run;
**code** — прочитанный текущий caller/локальный target;
**inference** — проверяемый вывод о возможном поведении;
**missing** — нужное сравнение ещё не выполнено. Отсутствие event/property не
становится доказательством отсутствия consumption. Последовательность «assets
существуют → configs/locks не нужны» не используется.

### Проверенные источники текущего execution path

- **C1** — [SolutionManager.cs](../../../../Services/Workspace/SolutionManager.cs), строка 2070:
  ordinary workspace создаётся с current loader globals и передаётся в
  OpenAndCaptureAsync. [AnalyzerProvenanceCaptureService.cs](../../../../Services/Analyzers/AnalyzerProvenanceCaptureService.cs), строка 180,
  создаёт caller BinaryLogger None/Normal и вызывает OpenProjectAsync/OpenSolutionAsync
  (строки 256–285); отдельного NuGet restore caller здесь нет. Это code evidence
  о вызовах данного пути, не доказательство невозможности restore/config reads
  внутри любых импортированных targets/tasks. SHA256 файлов соответственно
  `A4A5FA65CBC8BB8B1EBAD7CA6CDD5BC86701107B25B375685B97B375A389FD32` и
  `9E82D44091E21F7C06C74D460504B28ED7990AC74552DBE6364CB15749406205`.
- **C2** — [WorkspaceInputMapBuilder.cs](../../../../Services/Workspace/WorkspaceInputMapBuilder.cs), строка 467,
  сохраняет unknown coverage imports/restore/custom tasks/walk-up;
  CreateCategories, строки 537–563, явно не выводит restore closure из bin/obj.
  SHA256 `6746EB2E84C2E4C3EC09B61D0EFA5B0EC71C12100934E5464BFABE62DB28501C`.
- **L1** — локальный `C:/Program Files/dotnet/sdk/10.0.300/Current/Microsoft.Common.props`,
  строки 19–74: Directory.Build.props discovery; BaseIntermediateOutputPath и
  MSBuildProjectExtensionsPath; wildcard import `$(MSBuildProjectFile).*.props`.
  SHA256 `A09E6E94A2B8E4FBB8A186DB7DC67F0ACD15DFBA6AE9861B2FC6B5CC29B11D3D`.
- **L2** — локальный `C:/Program Files/dotnet/sdk/10.0.300/Microsoft.Common.targets`,
  extension targets import и Directory.Build.targets discovery (строки 24–64).
  SHA256 `7B9B0AE85BB340547E7A7A849953EC3FAB2717D10C7AC6E914747E42A3BCDB86`.
- **L3** — локальный `C:/Program Files/dotnet/sdk/10.0.300/Sdks/Microsoft.NET.Sdk/targets/Microsoft.PackageDependencyResolution.targets`,
  строки 23–24, 230–313: нормализованный ProjectAssetsFile, ResolvePackageAssets
  с DesignTimeBuild condition; outputs включают compile references, analyzers,
  content и transitive project references. В task также передаются TFM/RID,
  PackageReference и прочие settings. Assets bytes сами по себе не определяют
  весь результат. SHA256 `CB0A2CF6EA590FBD3DFBF333D32FC7EE1DB3344908AB04DC31288C5591C5136C`.
- **L4** — локальный `C:/Program Files/dotnet/sdk/10.0.300/NuGet.targets`,
  строки 771–805: _GetRestoreSettings/GetRestoreSettingsTask с RestoreConfigFile,
  RestoreRootConfigDirectory, RestoreSolutionDirectory и startup context;
  строки 951–952, 978–979 передают lock path/mode в restore graph.
  SHA256 `21959FD793F42067D37B63283693DE2ECF83E31940FD1EF0B7843581C7CCD0C7`.
- **L5** — actual generated files реального контроля:
  `samples/RoslynMcpPlugin/obj/RoslynMcpPlugin.csproj.nuget.g.props` задаёт
  ProjectAssetsFile/NuGetPackageRoot и imports buildTransitive props;
  `.nuget.g.targets` импортирует buildTransitive targets, включая configuration
  binder/usersecrets/analyzers dependencies. SHA256 соответственно
  `5F36415F9CC574961B6264AA9A05E055E8C86B9BB15F08032F6AA5928135C061` и
  `23D9EFD8FA99EB4EB1AF94DF513D30F1FACC49F0D121AEE74532AF023B14A382`.
  Это текущее local inspection, не новые isolated execution measurements и не
  гарантия сохранности generated files на другой машине.

Локальный `dotnet --info` сообщил SDK 10.0.300 / commit caa81fa497, host 10.0.8,
но завершился с access-denied при чтении Service Control Manager. Полный успешный
runtime inspection этим не заявляется. Accepted S-002/task-00.1 runs отдельно
зафиксировали SDK 10.0.300, MSBuild product 18.6.3, Roslyn 5.9.0.0.
Installed task binary internals в этом audit не декомпилировались.

Primary documentation проверена online 2026-10-09:

- **W1** — [NuGet restore target](https://learn.microsoft.com/en-us/nuget/reference/msbuild-targets):
  restore создаёт assets и generated props/targets; restore и build evaluation
  имеют различные globals. Это объясняет разделение стадий, не equality instances.
- **W2** — [PackageReference locks](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#locking-dependencies):
  lock определяет restore closure/locked-mode; наличие lock может включать его
  usage даже без explicit opt-in; индивидуальный NuGetLockFilePath поддерживается.
- **W3** — [NuGet configuration hierarchy](https://learn.microsoft.com/en-us/nuget/consume-packages/configuring-nuget-behavior):
  applicability зависит от project/solution/tooling context, user/machine и
  ancestor settings; explicit config и environment expansion меняют resolution.
- **W4** — [package props/targets](https://learn.microsoft.com/en-us/nuget/concepts/msbuild-props-and-targets):
  generated NuGet imports подключают package build files, в том числе conditional
  multi-targeting imports; имя «restore artifact» не исключает evaluation role.
- **W5** — [ResolvePackageAssets upstream source](https://github.com/dotnet/sdk/blob/main/src/Tasks/Microsoft.NET.Build.Tasks/ResolvePackageAssets.cs)
  и [LockFileCache](https://github.com/dotnet/sdk/blob/main/src/Tasks/Microsoft.NET.Build.Tasks/LockFileCache.cs)
  показывают чтение ProjectAssetsFile как NuGet LockFile object. Здесь «LockFile»
  не означает `packages.lock.json`. Это current upstream main, не аттестация
  binary version 10.0.300 и не exhaustive audit всех SDK tasks.

## Current → proposed: один точечный draft

**Current C0.** Series «Индекс входов» и task-00 требуют actual restore inputs:
assets, применимые lock/config files, импортированные nuget.g.props/targets,
actual imports и positive/absent discovery. P-011 связывает каждую category/path
со всеми consumer instances; P-004 отдельно связывает consumed graph generation.
Это specification guarantee; production disk manifest adapter ещё отсутствует.
Реальный operational outcome сейчас — task-00 blocked, cache hit не разрешён.

**Proposed D1 (не применён).** «Профиль `ordinary-prepared-restore` гарантирует
поддержанную semantic/write equivalence со fresh ordinary design-time load на
текущих уже подготовленных artifacts, при independently current loader context.
Профиль не выполняет NuGet restore и не обещает эквивалентность будущему restore,
актуальность package closure относительно изменённых feeds/configs/locks или
успех locked-mode restore. Для конкретного file/category exclusion требуется
traceable доказательство, что во всех final consumer instances этого finite
profile файл влияет только на предшествующий/будущий restore и не читается, не
участвует в conditions/discovery и не меняет заявленные ordinary options,
references, memberships, diagnostics, semantics и write admission. Unknown
applicability/consumption означает unsupported whole-request ordinary route.
Assets, imported generated props/targets, package build files и binaries,
project/package declarations, SDK/environment/resolution и применимые
source/config/membership/absence regions сохраняют прежние gates».

**Proposed D2 (следствие D1).** «Правка, появление или удаление **доказанного
restore-only** NuGet config/lock при неизменных всех remaining ordinary inputs
сама по себе не вызывает disk miss. Если файл имеет ещё mandatory роль либо
exclusion не доказан, применяется прежний probe/discovery и miss/fallback.
После нового restore любое обнаруженное изменение mandatory artifacts,
dependencies или контекста даёт whole-request ordinary load; invalid/missing
artifacts и unknown graph health не допускают reusable capture. Побайтово и
контекстно неизменный результат нового restore не обязан давать miss только
по факту запуска restore. Это не proof P-004».

Это одно narrowing proposal: qualified exclusion **restore-only config/lock**,
без исключения всех NuGet files/obj или изменения ordinary route. Условие proof
не считается выполненным. Сейчас множество доказанно исключённых categories
для real control **пусто**. Исполнитель рекомендует **не применять draft сейчас**:
он не даёт supported real hit и не решает source/generation blockers.

Потенциальная польза после отдельного proof — меньше misses на restore-policy
изменениях, не влияющих на current ordinary load. Риск — принять config/lock за
restore-only при hidden/custom task read или resolver dependency и выдать stale
options/references/membership либо некорректный write. Ограничение future restore
должно быть явно в help/profile/outcome. D1 не исключает freshness current ordinary
semantic result и не заменяет неизвестность допущением.

## Category audit: inclusion, exclusion и outcomes

Каждый verdict ниже относится к конкретной category; `eligible unchanged`
обозначает лишь гипотетический исход D1 после **всех** gates. Никакой runtime
disk hit в этом audit не наблюдался. «Current miss» означает требование текущего
контракта для обнаруженной mutation, а не результат реализованного disk path.

### R1 — NuGet config: explicit, ancestor, user/machine, appearance/deletion

Source: L4/W3; observed S-002 не дал config inventory; task-00.1 читает лишь
restore-time `assets.project.restore.configFilePaths`. Новый App/NuGet.Config
там отсутствует, удалённый config всё ещё указан. Это наблюдённый stale provenance,
не доказанная current applicability и не доказанное ordinary non-consumption.
Точки project-vs-solution, user/machine additional-config directories и explicit
RestoreConfigFile нельзя восстановить одним saved assets list.

Current: known applicable config content/create/delete → miss; unknown discovery
→ unsupported. Proposed: **conservatively tracked candidate, exclusion unproven**.
Только доказанный restore-only config мог бы получить eligible unchanged до
restore; actual profile сегодня fallback. После restore changed assets/imports/
resolution → miss; unchanged artifacts не доказывают unchanged ordinary context.
NuGet config, также прочитанный target, AdditionalFile либо импортируемый XML,
сохраняет mandatory роль. Environment expansion W3 включена в environment gate.
Missing: owned config source/path-policy mutation с прежними artifacts → fresh
ordinary inventories/options/references/diagnostics/semantics; затем separate
restore и повторное сравнение. Actual config-mutation equivalence run отсутствует.

### R2 — packages.lock.json, individual locks, lock mode

Source: L4/W2. S-002 controlled locks существуют, но только Library evaluation
сообщает lock path/options; root consumption не доказан. task-00.1 supplemental
properties дают оба expected paths в отдельной evaluation, не final-instance proof.
W5 SDK LockFile object нельзя подменять package lock file.

Current: applicable lock content/create/delete → miss; unknown policy/path →
unsupported. Proposed: **conservatively tracked candidate, exclusion unproven**.
Qualified restore-only lock mutation до restore могла бы дать eligible unchanged;
actual verdict сегодня fallback. Locked-mode success/failure после restore явно
вне D1; при failed restore reuse допустим лишь если остальные ordinary eligibility
доказаны, чего этот audit не устанавливает. Любой changed mandatory artifact
после restore → miss, invalid/missing artifacts → fallback/no-capture.
Появление default или индивидуального lock не игнорируется по отсутствию flag.
Missing: lock content/create/delete/locked-mode case с сохранёнными artifacts,
fresh ordinary parity и отдельными success/failure restore outcomes.

### R3 — project.assets.json и resolution state

Source: L3/W5; observed S-002 TaskInput.ProjectAssetsFile имеет redirected paths
App/Library с build contexts; task-00.1 подтверждает paths отдельной evaluation.
В task inputs L3 есть non-assets settings и outputs разных roles.
Verdict: **include**, одинаково current/proposed. Изменение/удаление assets,
изменение actual path/context, applicable dependency либо required package binary
→ miss/fallback до результата; нового restore ждать не надо. Appearance вместо
absent assets требует ordinary load/нового admission. Assets existence не health
proof; reuse с mismatched TFM/RID не допускается по одному valid JSON.
Missing: all-final-instance consumed assets bytes binding/P-004, полный SDK task
dependency scope/cache correctness. SDK assets cache/output и другие intermediate
inputs нельзя автоматически исключать: meaningful read/role должен быть доказан,
unknown input даёт fallback. Новое restore не делает старый envelope valid.

### R4 — generated nuget.g.props/targets и package build imports

Source: L1/L2/L5/W4; observed S-002 dependent imports включает NuGet generated
files; task-00.1 видит root+referenced imports собственной evaluation.
Verdict: **include**, current/proposed. Это ordinary evaluation inputs, хотя
производятся restore. Content/create/delete/rename generated import или imported
package props/targets → miss; изменения relevant package task binaries → binary
gate/fallback. После restore new imports/memberships требуют нового ordinary load.
L1/L2 wildcard means новые project extensions могут изменить graph при прежнем
assets: нужен bounded region/pattern inventory, не hashes только known files.
Unknown conditional task input/completeness → unsupported. Generated filename и
stale artifact provenance не доказывают safe omission или consumed generation.

### R5 — PackageReference/PackageVersion/Directory.Packages.props, project graph

Source: W1/W2, L3 PackageReferences input, current project declarations и accepted
E0 real plugin→host graph. Verdict: **include**, current/proposed, даже до restore.
Project/package/import declaration mutation → miss; stale assets не «замораживают»
current options/TFM/conditions/project edges. Actual imported Directory.Packages.props
имеет evaluation role; standard potential ancestor appearance/deletion и explicit
import paths сохраняются. После restore changed package paths/content/refs → miss.
Resolved metadata/project-reference inventories и aliases/embed properties должны
совпадать с ordinary, не только число DLL. Unknown outer/inner TFM, metadata-only
edge или final instance context → fallback; same csproj path не instance identity.

### R6 — ordinary imports, SDK tasks/targets/resolvers и environment

Source: C1/L1–L4, S-002 missing root evaluation и поздняя dependent evaluation 9,
task-00.1 `ReferencePath=[]` при hundreds ordinary refs. Verdict: **include**
actual imports + discovery + context, current/proposed. Import/SDK/resolution/
schema/MSBuild/Roslyn incompatibility → miss/incompatible/fallback; significant
environment absent→present/changed value → miss/fallback under same capture/lookup
policy. Full SDK installation scan не добавляется. D1 не разрешает controlled
environment, меняющий ordinary semantics, или saved globals как current oracle.
Trace category→source→path→**all final instances** остаётся обязательным.
Unknown resolver/task dependency → unsupported. NuGet restore provenance не заменяет
ordinary SDK task inputs. Environment/settings changes, влияющие лишь на будущий
restore, могли бы попасть в exclusion только с отдельным proof; blanket allowlist
не предлагается. Missing: finite final context attestation/compatible environment
policy; upstream code/targets inspection не даёт этот executable admission.

### R7 — redirected obj/intermediate и generated source/target outputs

Source: L1/L3, observed S-002 redirected `intermediate/App|Library` assets;
task-00.1 explicit obj/External Compile и три ordinary-only generated sources
на real owner (AssemblyAttributes/AssemblyInfo/GlobalUsings).
Verdict: **include actual mandatory paths/roles/producers**, current/proposed;
никакого broad obj pruning для explicit dependencies. Path redirection property
mutation → miss; relevant output/input bytes/create/delete или changing producer
bindings → ordinary route. Generated sources не становятся UserInput по расширению,
а restore-only category не поглощает source generation. Changed generated props/
targets из R4 mandatory и до, и после restore. Missing: target-produced options,
references, memberships и generation binding. Observed retention conflict с
existing generated file не доказывает конкретный forced regeneration failure;
такой run не выполнялся и не объявляется.

### R8 — source/additional/analyzer configs, links, shared owners и absent regions

Source: C2, E0 accepted linked write scope; task-00.1 observed new source/editorconfig
ordinary consumption и external/explicit obj membership; current P-012.
Verdict: **include**, current/proposed. Known bytes/content change → miss;
new/deleted source/config/import in applicable region → membership/input miss.
Все project consumers/shared memberships сохраняются; linked physical file
принадлежит всем owners. Explicit external input проверяется точечно, external
glob нуждается в собственной completeness boundary; unknown → fallback.
`.editorconfig`/`.globalconfig` applicability не объединяется с NuGet hierarchy;
source-linked и project config boundaries проверяются отдельно. Repo root не
универсальная граница walk-up. False-condition import с null raw path не является
полным known-absence inventory. Ни D1, ни config/lock exclusion не снимают эти
presence/deletion gates.

## False-hit проверки и оставшиеся blockers

Сопоставление actual evidence выполнено по заранее ограниченным observations,
не по одному source hash или successful symbol query:

- **F1 — options.** task-00.1 custom ReadLinesFromFile input меняет ordinary define,
  отсутствуя в supplemental evaluation; assets unchanged не спасают parity.
  D1 должен отказывать unknown task input; этот observed negative остаётся.
- **F2 — references.** Supplemental ReferencePath пуст; real ordinary inventories
  226/236, controlled 167/167. Это missing target resolution, не proof отсутствия
  refs и не разрешение копировать saved refs без current closure.
- **F3 — memberships.** Три generated ordinary-only source на real owner и newly
  created source/config означают явный gap; stable known-source hashes не closure.
- **F4 — instances.** S-002 imports принадлежат dependent evaluation 4; root 3
  не покрыт и final dependent 9/submission 1 не связан. task-00.1 own IDs не
  заменяют эти final contexts; narrowing config/lock не меняет этого факта.
- **F5 — generation.** Accepted task-00.1 `ConsumedBWithEqualAHashes=true` и
  `PostConsumptionAbaHashesEqual=true`; file retention не удерживает появление
  absent input. D1 сохраняет P-004; pre/post probes и existing handles не решение.
- **F6 — restore-only assumption.** L5 package targets импортируются обычной
  evaluation; config/lock может иметь custom secondary role. Отсутствие таких
  events в bounded binlog не доказывает absence. Этот scenario — inference/risk,
  новый config/lock consumption control не выполнялся.

**Что draft потенциально убирает из S-002 required closure:** только требование
считать доказанный restore-only config/lock ordinary freshness input. Ни одного
такого exclusion proof для real request нет. **Что не закрывает:** current
applicability proof для различения roles, root/all-final imports, unknown targets,
SDK/environment/current globals, redirected artifact completeness, target-produced
graph и P-004. Task-00.1 supplies independent evaluation paths, не этот binding.
E0 accepted roundtrip/performance остаётся фактом bounded isolated scope; E1
production hit и его input-probe/useful costs из него не выводятся.

H-001 external XML-only documentation exclusion и H-002 signing-independent
profile сохранены без расширения. XML с другой mandatory ролью включается;
source comments сохраняются. Signing-dependent/unknown/excluded emit и
documentation-dependent requests сохраняют ordinary/refusal route до dependent
result/side effects, explicit H-002 logs и current write/admission/preflight.
Overlay-on без fresh binding сохраняет whole-request ordinary fallback;
force/cache opt-in/lazy defaults и cache I/O failure policy прежние. Supported
`.cs` writes не получают bypass, unsupported mutations отвергаются до persistence.

## Рекомендация владельцу, affected work и validation

**Не применять D1/D2 как решение task-00 на текущем evidence.** Семантически
разделение prepared ordinary load и future restore полезно; его operational
viability для disk capture пока **not-viable / insufficient evidence**. Сужение
не превращает missing imports или P-004 в solved и не допускает false hit.
Принять можно именно docs-only audit с этим отрицательным выводом; применение
draft требует отдельного выбора владельца и applicable review.

Если владелец позднее выберет proof этого draft, затрагиваются contracts
series input index/E1 spec, task-00 category adapter, task-01 text/region probes,
task-06 capture/admission/outcomes, task-09 mutation/health/writes и task-10 compare
profile/benchmark. Это список consequences, не edits/dependencies/activation
в данном запуске. Task-11/task-00.1 results сохраняются; переработка всех эпох,
arbitrary task analysis и BuildHost fork не предлагаются.

Минимальные **будущие, not-run** controls для решения restore-only question:

1. Owned real copy + controlled PackageReference project: заранее фиксировать
   all project/instance/options/reference/alias/embed/document/additional/config
   inventories, diagnostics и выбранные semantic/write assertions. Mutate
   applicable explicit/ancestor/user-context NuGet config при прежних artifacts;
   compare с fresh ordinary load до restore. Secondary-role config negative
   обязан давать fallback; одних успешных queries недостаточно.
2. Lock content/create/delete + индивидуальный path/locked-mode policy с прежними
   artifacts; fresh ordinary comparison. Затем отдельный restore, success/failure
   disclosure, exact artifacts/refs/membership changes и повторный ordinary baseline.
   Test oracle не выбирается из наблюдённого результата.
3. Config create/delete не из saved assets inventory, linked external boundary,
   redirected extensions/assets, new wildcard generated props/targets и package
   build file mutation. Project/package/import/SDK/environment changes проверяются
   независимо; each mandatory mutation требует miss/ordinary parity.
4. Отдельно final-instance input source и P-004 controls: consumed props/assets B
   с A до первого post-load probe; namespace appearance/ABA и generated output
   ownership. Без positive real consumed-generation mechanism reuse запрещён
   даже если config/lock parity пройдёт.

Эти controls не выполнены данным docs-only audit и не объявляются prerequisite
для принятия его отрицательного результата. Нужен independent review actual
draft/traceability; final docs validation: local links, consistency current→proposed,
dependency/status non-change и diff whitespace. Build/main/SourceStructure/lifecycle
**not-run / not required для этого docs-only scope** по execution/AGENTS.md.
Если будущий proof добавит code/tests, координатор выполняет полную применимую
Release validation; прежние task-00.1/S-002 passes не засчитываются за новые controls.
