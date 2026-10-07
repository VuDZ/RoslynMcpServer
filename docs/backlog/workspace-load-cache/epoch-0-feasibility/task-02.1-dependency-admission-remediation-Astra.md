# E0/task-02.1 — dependency-admission-remediation

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **5/5**.
- Риск ошибки: **высокий** — ложный supported разрешает hydrate или выполнение
  analyzer DLL при неполном inventory и непроверенной identity.
- Необходимые способности: `архитектурное_мышление`, `анализ_зависимостей`,
  `семантика_roslyn_msbuild`, `независимая_проверка`.
- Статус: **in-progress (Stage A finite proof, 2026-10-04)**.
  Выполнены аудит sources и fresh negative observations; historical investigation
  и limited executable L021-1 приняты независимым reviewer и координатором.
  Accepted scope L021-1: семь новых isolated C# файлов и runner, independent
  graph observations, framework selection и bounded no-build recheck; closure
  reasons и unknown/false/false сохранены. Final Release build exit 0,
  focused 21/21 и main 1228/1228 passed. Reparse subcase NotRun (Win32 1314);
  прежние failures и удалённый subprocess fallback сохранены в evidence.
  Владелец утвердил A021-01/02/03, D021-04/05 и A021-06 typed envelope;
  поправка binding representation R021-08 внесена. Finite proof и полный
  positive packet остаются в работе. Supported control, fresh Complete overlay
  и полная приёмка remediation не выполнены.
  [Validation, review и ограничения](evidence/task-02.1-dependency-admission.md).
- Depends on: [task-00](task-00-current-baseline-and-execution-contract-Astra.md),
  [task-01](task-01-hydrate-host-spike-Sol.md),
  [task-02](task-02-dependency-admission-spike-Astra.md),
  [task-03](task-03-oracle-fixtures-Luna.md).
- Входное evidence: [task-02](evidence/task-02-dependency-admission.md),
  [task-04 после установки SDK 9](evidence/task-04-equivalence.md#post-install-validation-2026-10-04).
- Выходной потребитель: **возобновление положительного scope task-04**.

## Цель и граница результата

Доработать isolated dependency experiment task-02 до доказанного admission
для конкретных неизменённых SDK requests и до реально нового session-bound
analyzer overlay. Устранить препятствия зависимости, из-за которых task-04
не может начать capture/hydrate/fresh comparison. Отрицательные observations
task-02 сохраняются; переписывать весь spike или объявлять его прежнюю приёмку
positive admission нельзя.

Обязательны [spec эпохи](spec.md), [design](design.md),
[общие правила](../task-execution.md), [verification](../verification.md),
packets [task-01](design/implementation-packet-task-01.md),
[task-03](design/implementation-packet-task-03.md) и
[task-04](design/implementation-packet-task-04.md).
Эта задача не меняет выбранные AdhocWorkspace, writer только .cs,
opt-in/default=false и activation permissions.

Task-02.1 **не выполняет положительные стадии 2–7 task-04** и не доказывает
fresh/hydrate equivalence. Полный reader, generator/load diagnostics,
documentation-provider identity и representability options остаются
task-04/design. Готовность dependency layer и готовность oracle API отражаются
двумя отдельными пунктами handoff; закрытие первого не закрывает второй.

## Текущая отправная точка

Проверить заново HEAD, dirty inventory и фактический код перед implementation.
На дату этой спецификации:

- `DependencyEvidenceRunner.InspectAsync` безусловно добавляет
  `restore-resolution-closure-unproven`, `toolset-identity-input-set-unproven`,
  `analyzer-dependency-closure-unproven`; positive ветки нет.
- `ExpectedEdges` всегда пуст; imports имеют owner=null; transitive compiler
  observations не заменяют inspection каждого exact inner instance.
- `AnalyzerInventoryComplete=false`, `OverlayReady=false`.
  `RevalidateOverlayAsync` вызывает file recheck, не использует возвращённые
  failures и всегда возвращает null. Это безопасный отказ, но не успешный overlay.
- В standard fixture compiler содержит восемь analyzer DLL, TaskOutput — шесть
  pack generators. Две SDK NetAnalyzers DLL не имеют доказанной TaskOutput
  identity. WorkloadManifest.targets находится вне `sdk/10.0.300` и содержит
  Target; текущее конкретное правило даёт custom-target/unknown.
- Literal `Exists` покрыт частично; property/condition expansion оставляет
  `unresolved-negative-import`. Regions не доказывают imported Remove и
  условные memberships. Scoped CSS даёт сохранённый Razor counterexample.
- SDK pin — 10.0.300 / rollForward disable; Roslyn 5.9.0, MSBuild 18.6.3.
  Установлены SDK 9.0.314 и reference pack 9.0.16. Task-04 теперь наблюдает
  compiler contexts net9.0/net10.0, но disposition остаётся unknown.
- Task-03 expectations и task-04 negative tests явно ожидают unknown/reasons.
  Их зелёный результат нельзя сохранить удалением новых positive assertions.

## Этап A — design решения и implementation packet

До первой C# правки подготовить `design/implementation-packet-task-02.1.md`:
audited types/files/signatures, полный allowlist/denylist, ownership,
cancellation/error/lifetime contracts, sources/limits и exact integration edits.
Эта спецификация **не является заполненным implementation packet**.

Решения записать в `design/task-02.1-decisions.md`: ID, evidence, варианты,
выбранное правило, нормативный статус и affected contracts:

1. **D021-01 / workload imports.** Target вне SDK при текущем профиле остаётся
   unknown. Нельзя объявлять весь dotnet directory SDK, игнорировать импорт
   по имени Microsoft или убирать workload resolver только ради pass.
   Определить, возможен ли требуемый positive request без изменения профиля.
   Новая граница toolset/profile требует конкретной поправки и принятого решения
   владельца normative spec. До этого request остаётся unknown.
2. **D021-02 / SDK analyzer provenance.** Compiler DLL без TaskOutput запрещена
   для overlay текущим контрактом. Нельзя синтезировать TaskOutput из compiler
   path/hash или исключить NetAnalyzers из inventory. Получить реальный полный
   источник в принятом контракте либо согласовать изменение provenance правила
   с владельцем и обновить affected contracts до implementation.
3. **D021-03 / closure proof.** Для restore, toolset и executable analyzer
   dependencies определить конечные источники, discovery и fresh no-build
   revalidation. Список наблюдённых файлов, directory name и AssemblyRef metadata
   сами по себе не доказывают полноту произвольных dynamic reads.
4. **D021-04 / graph и binding.** Определить public evidence для request roots,
   transitive edges, exact inner-TFM mapping и rebinding в новый baseSolution.
   Недостающие модели producer описывает exact declarations/schema changes
   и обновляет affected packets до dispatch; потребитель не додумывает поля.
5. **D021-05 / Razor/Web.** Разрешить scoped CSS counterexample и external
   static-assets coverage относительно принятого списка regions. Не добавлять
   glob молча. CS0542 exact Razor golden — отдельный fixture/design finding;
   два пустых generated sets не становятся положительным результатом.

Изменение нормативного профиля/provenance не разрешено одним выполнением
task-02.1. Незаданные решения возвращаются владельцу с proposed поправкой.
Если решение не принято, status blocked/deferred с причиной; исследовательский
отчёт допустим, но prerequisite task-04 не закрыт.

## Этап B — обязательные доработки

### R021-01. Admission по каждой обязательной категории

Реализовать positive ветку только после закрытия каждой обязательной категории
целого request. Убрать unproven reason лишь вместе с проверяемым proof категории.
Хранить source/version/owner/raw locator и причины отказа. Missing, partial,
failed или ambiguous evidence не становится empty complete inventory.
`supported`, `AnalyzerInventoryComplete` и `OverlayReady` имеют отдельные
условия; одно значение не выводится автоматически из другого.

### R021-02. Полный graph и exact instances

Независимо обнаружить выбранные roots и необходимые transitive projects,
inner TFMs и edges с aliases/EmbedInteropTypes. Переданные roots не являются
graph proof. Проверять missing reference, soft warning и легально пустой project
раздельно. Связать imports, compiler inputs и TaskOutput с exact instances;
не использовать Project.Name, первый path match или runtime ProjectInstanceId
как portable identity. Контексты разных binlogs имеют разные log associations.

### R021-03. Restore и reference closure

Установить реальные inputs выбора assets/package/reference: применимые NuGet
configs, assets/generated restore imports, package/pack contents и resolution
inputs по D021-03. Сверить независимо разрешённые references с compiler paths
и properties; не сравнивать saved fingerprint только с собой. Определить bounded
режим для fallback/overrides/download и ambiguity; неподдержанный режим отказывает
целому request. Restore — вне измеряемых capture/hydrate стадий. Revalidation
перед reuse не запускает restore/build. XML sidecars учитывать как file inputs,
не как доказательство выбранного Roslyn documentation provider.

### R021-04. Toolset/SDK/pack identity

Определить admitted identity input set SDK/tasks/resolvers по принятому решению;
различать SDK, MSBuild runtime и reference packs. Независимо повторно разрешать
global.json и фактические directories без нового DTB. Проверять содержимое,
выбор версии, path и дополнительные resolution inputs; изменение значимого
ненаблюдённого файла не должно сохранять positive readiness. Workload import
решается только по D021-01. Installed SDK/packs и global configs не изменяются.

### R021-05. Imports, absence и membership regions

Разрешить bounded supported subset property expansion/conditions/Exists
с owning evaluation и исходным import. Неразрешённая condition остаётся unknown;
ImportIgnored не доказывает отсутствие. Сохранить negative inputs, включая
absence directory/ancestor config там, где оно влияет на resolution. Построить
regions по project и реально импортированным Include/Remove/excludes/conditions,
включая пустые regions и dotfiles Web. Явные linked inputs и все memberships
учитываются; escaping glob не расширяет профиль. Truncated traversal не complete.
Повторный probe обнаруживает изменение во время чтения и отказывает attempt.

### R021-06. Compiler/analyzer inventory и provenance

Для каждого instance независимо получить полный compiler inventory, включая
доказанный empty analyzer set. Проверить source/additional/config/metadata inputs,
SDK-generated obj compile/config и producers. Сопоставить каждую analyzer DLL
с действительным разрешённым provenance источником и consumer context.
Доказать equality всех analyzer/dependency inventories evidence и DTO до
empty-analyzer shortcut. Explicit analyzer вне TaskOutput сохраняет отказ,
пока правило не изменено отдельным принятым нормативным решением. Incremental
пропуск Csc не empty inventory; forcing parameter старого spike записывается
как augmentation, его влияние проверяется отдельно.

### R021-07. Analyzer executable dependency closure

Закрыть main DLL, допустимые private/transitive DLL, host contracts и способы
resolution по D021-03. PE/AssemblyRef inspection допустим без выполнения DLL,
но existing AnalyzerPrivateDependencyInspector — refusal detector main-only,
не универсальный discovery source. Dynamic loading/native/resource/config inputs
вне доказанного subset дают отказ. Зафиксировать versions/paths/content hashes
и uncertainty; не получать closure загрузкой DLL из DTO. Razor SDK dependencies
и pack generators проходят ту же проверку полноты.

### R021-08. Реальный fresh session-bound overlay

Реализовать `RevalidateOverlayAsync` через тот же runner, который получил
independent portable facts. Проверить результат file recheck и заново разрешить
SDK/pack/dependencies без Open*, DTB, subprocess, restore или исполнения DLL.
Rebind consumer path/inner TFM к новым ProjectId `baseSolution` однозначно.
Создать новый isolated admission envelope с новым nonempty LoadSessionId,
текущим toolset, fresh `AnalyzerProvenanceSnapshot` реальных TaskOutput/context
observations и полными independent consumer bindings для объединения genuine
TaskOutput и SDK-evaluated sources. Production snapshot сохраняет native
TaskOutput/binding смысл без fabricated task IDs, SourceProjectId или Confirmed.
Existing suitability gate должен принять его actual same-session Complete
capture; isolated host отдельно и обязательно проверяет полную typed-source
coverage и fresh consumer bindings всех compiler/DTO DLL перед DLL loading.
Это уточнение A021-06 принято владельцем 2026-10-04; production models/gate и
R021-07/M021-08/M021-10 не ослабляются.
Saved snapshot и `with { LoadSessionId = ... }` не являются fresh provenance.

Wrong session, missing/changed/unreadable dependency, incomplete coverage,
ambiguous binding, malformed payload или ложный caller flag дают null/refusal
до DLL loading. Отказ overlay даёт base-only session по existing host contract;
required generator positive result тогда failed/not-run. Cancellation сохраняет
token; I/O/evidence failures сохраняют bounded reason/artifact, programming
misuse остаётся contract error. Ресурсы закрываются при отказе/отмене.

### R021-09. Интеграция потребителей

После подтверждения admission обновить **только доказанные** expectations
task-03; сохранить negative cases, layouts/bytes/goldens и причины оставшихся
отказов. Для исправления Razor golden требуется отдельное design решение,
а не переименование fixture ради green run.

Перед final validation согласованно обновить tests task-02/task-03/task-04,
которые сейчас требуют unknown. Новая supported строка до реализации positive
pipeline task-04 должна честно записывать positive NotRun с причиной отсутствия
pipeline, а не Failed из-за устаревшего unknown expectation и не Passed по
synthetic comparison. Positive branch task-04 здесь не реализуется; negative
harness не ослабляется для сохранения зелёного main suite.

Каждый shared файл имеет одного integration owner; affected model/codec/host
contracts и packets обновляются **до** consumer edits. Exact разрешённый
integration diff task-04 описать в packet-02.1 и дополнении packet-04.

## Границы файлов и исполнения

Основной audited scope producer: `DependencyEvidenceRunner.cs`,
`DependencyEvidenceRunnerTests.cs`, `DependencyRazorWitnessTests.cs` и
`Models/Dependency*.cs` под `RoslynMcpServer.Tests/WorkspaceLoadCache/`.
Потенциальные shared edits `Models/AdmissionEvidence.cs`, evidence/instance/edge/
analyzer models, `OracleFixtureBuilder*.cs`, `IndependentWorkspaceOracle*.cs`
и tests разрешаются только exact allowlist и ownership из нового packet.
Wildcard здесь — inventory исследования, **не allowlist**. Новые types/files
producer перечисляет полностью. Проверяется совместимость task-01/03/04.

Production `Services/`, `Tools/`, SolutionManager, csproj/solution, package
versions и runtime configuration не правятся. Existing provenance gate и
metadata inspection APIs потребляются из isolated tests. Нормативные spec/profile
меняет владелец только по принятому решению этапа A. Fixture mutations — только
в owned temp roots с canonical boundaries, restore/verify и cleanup; installed
toolset/global configs только читаются. Raw binlogs private; portable payload
не содержит environment bags/secrets, source/generated texts, старых
ProjectId/LoadSessionId или shadow paths.

## Обязательная матрица проверки

Для каждой строки разделить test pass и actual experiment outcome; указать
category/disposition, coverage, OverlayReady и raw evidence:

- **M021-01 / positive control:** реальный неизменённый ProjectGraph request:
  supported whole graph, nonempty instances/edges, independently complete
  inventories. Все closure categories проверены; successful build не admission.
- **M021-02 / V03:** net9.0/net10.0, explicit/absent TFM/effective properties,
  exact contexts/edges, `E0_NINE`/`E0_TEN`, packs 9.0.16/10.0.8 и SDK10 pin.
  Missing prerequisite — NotRun, не замена TFM или SDK.
- **M021-03 / V05:** obj compile/config changed/deleted при unchanged csproj;
  producer/input recheck отказывает; restoration восстанавливает outcome.
- **M021-04 / V06:** linked .cs у двух consumers и external import; обе
  memberships покрыты, изменённый import отказывает. Escaping glob — negative.
- **M021-05 / V12:** literal и принятая expanded Exists condition; появление
  absent import/ancestor input. Unresolved condition остаётся negative.
- **M021-06 / V13:** configs/assets/restore choice/SDK/toolset/pack changes,
  включая same-size/same-mtime bytes; fresh resolution mismatch отказывает.
- **M021-07 / regions:** imported Remove, empty region с новым member, Web hidden
  asset, non-member control, race/reparse/unreadable/bounds. Scoped CSS имеет
  отдельный closed либо negative outcome, не false completeness.
- **M021-08 / analyzer inventory:** реальные SDK/pack analyzers и разрешённый
  generator; explicit non-provenance DLL, omitted-all DTO analyzers, extra DLL,
  incomplete inventory и ambiguous consumer — refusal до loading.
- **M021-09 / V14:** main/private dependency changed same path, missing,
  unreadable/malformed; old hash/AssemblyVersion не авторизует stale execution.
- **M021-10 / fresh overlay:** два новых hydrated base sessions; snapshot каждого
  имеет его SessionId и новые bindings, gate принимает real Complete snapshot.
  Old/wrong-session snapshot не принимается. No-DTB/no-process revalidation
  подтверждена instrumentation/structural analysis; synthetic Complete не pass.
- **M021-11 / negative request:** custom target с hidden input, non-SDK,
  external package static asset вне профиля, missing root/reference, soft warning,
  legally empty project. Whole-request refusal и empty complete различаются.
- **M021-12 / V04 и integration:** Razor/Web отдельно admission, overlay,
  ordinary compiler/generator outcome и unresolved oracle limits. Consumer
  tests отражают новое disposition без ложной positive equivalence.

Task-02.1 нельзя закрыть только отрицательной матрицей. Если base positive
control или реальный fresh Complete overlay не достигнуты, prerequisite
remediation остаётся blocked, даже если reporting tests зелёные. Для Razor/Web
или MultiTarget допустим отдельный negative scope по normative verification;
он не разрешает положительный dispatch этих rows.

## Validation, review и приёмка

До C# edits исполнитель читает `docs/code-style.md` целиком; coordinator передаёт
это требование каждому C# author/style reviewer. Focused tests — новая матрица
и затронутые dependency/host/fixture/oracle tests. Перед acceptance coordinator
строит solution и запускает полный main Release suite `Category!=AnalyzerLifecycle`,
предпочтительно MCP runners. После fixes main suite повторяется. Zero tests,
skips/failures/timeouts/unavailable environment записываются явно. SourceStructure
обязателен отдельно после production C# edits; здесь они запрещены.
AnalyzerLifecycle добавляется при затронутом lifecycle/load/prepare/publication
path или его test/build configuration с `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`;
applicability обосновать.

Независимый reviewer — новая author-independent session, не автор design/кода
task-02.1. Must-fix возвращаются тому же implementer и перепроверяются тем же
reviewer. Coordinator владеет final validation, integration и status bookkeeping.

Полная приёмка remediation требует принятых design решений и filled packet,
real supported control, real fresh Complete overlay, negative mutation checks,
согласованных consumer expectations, independent ACCEPT и final suite.
Историческая isolated-research приёмка task-02 не переписывается.

## Outputs и handoff в task-04

1. `design/task-02.1-decisions.md`, filled
   `design/implementation-packet-task-02.1.md` и affected packets.
2. Isolated code/test diff exact allowlist; trace R021/M021 → code/assertion/
   actual outcome/evidence. Unresolved rows имеют причину и владельца.
3. `evidence/task-02.1-dependency-admission.md`: HEAD/dirty, SDK/runtime/Roslyn,
   commands/exit codes, prerequisites, input/region/artifact hashes, private raw
   locators, focused/final counts, review identities и limits.
4. Матрица каждой task-03 fixture: admission доказан или отказ; analyzer inventory
   complete; fresh overlay ready или отказ; task-04 positive dispatch permitted
   либо hold. Disposition не заменяет остальные колонки.
5. Синхронизированные status/date/scope/evidence links в этом task, README и
   affected summaries. Task-04 остаётся blocked до собственных API remediation
   и stages 2–7; task-02.1 не помечает её accepted.

Handoff отдельно выдаёт experiment, implementation, public activation,
next epoch и series complete verdicts. Успешная task-02.1 разрешает только
доказанный isolated downstream scope. Production/public activation и переход
эпохи не открываются admission или overlay экспериментом.
