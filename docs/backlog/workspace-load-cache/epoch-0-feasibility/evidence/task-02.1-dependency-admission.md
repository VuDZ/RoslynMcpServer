# E0/task-02.1 — dependency admission remediation: Stage A evidence

Дата: **2026-10-04**. Текущее состояние: **Stage A finite proof в работе; remediation не accepted**. Автор документов/audit: `/root/dependency_remediation`.
Координатор `/root` выполнил validation и владеет task/index bookkeeping.
Независимый reviewer `/root/independent_review`: **ACCEPT historical investigative scope, L021-1 packet/code и final limited validation (2026-10-04)**: Release build exit0, focused21/21, main1228/1228; reparse subcase NotRun. Завершённый
Stage A, full remediation и prerequisite task-04 не приняты.

## Исторический результат первого investigative scope

Свежие измерения подтверждают два препятствия действующего контракта:
Target-bearing workload manifest находится вне SDK directory, а две SDK NetAnalyzers
DLL отсутствуют в genuine TaskOutput Analyzer. Также не доказаны конечные restore,
toolset и executable analyzer closure. Required unchanged supported graph и real
fresh Complete overlay не получены; их результат **NotRun**, не Passed по null.

Подготовлены [D021-01..05 и принятые owner choices A021-01..03](../design/task-02.1-decisions.md)
и [investigative implementation packet](../design/implementation-packet-task-02.1.md)
с exact audited declarations, ownership, allowlist/denylist и contracts.
Positive implementation packet остаётся blocked: конечные selectors/schema и
новые graph/provenance declarations не утверждены. Stage A целиком не accepted.
C# edits отсутствуют; все новые результаты используют текущие tests и read-only
аудит. Этот historical прогон предшествовал owner approval. Затем владелец принял A021-01/02/03 и D021-04/05; normative amendments внесены, finite proof и executable packet исследуются отдельно ниже.

## HEAD, dirty inventory и среда

HEAD на dispatch и в audit: `d3c8d5241c1c1c5d95042ce75bc057b747284cf1`.
Preexisting modified: README эпохи, packet-task-04, task-02 и task-04.
Preexisting untracked: task-02.1 specification; evidence/task-04-equivalence.md;
IndependentWorkspaceOracle.cs, IndependentWorkspaceOracleTests.cs,
WorkspaceObservationReader.cs; Models/OracleComparison.cs, OracleDifference.cs,
ScenarioOutcome.cs, WorkspaceObservation.cs; `.agents/skills/prepare-github-release/`,
`.cursor/rules/roslyn-mcp.mdc`, `.zcodeignore`, `iotester.py`, `scripts/`.
Полный inventory сохранён в `dirty.txt` raw root ниже. Этот diff не присваивается
task-02.1. Implementer изменил только три новых docs и gate supplements packets
01/03/04; coordinator отдельно обновляет status task-02.1/README.

Windows 10.0.26200/x64; host `C:/Program Files/dotnet/dotnet.exe`.
Свежий dotnet --info: SDK **10.0.300**, MSBuild **18.6.3+caa81fa49**, runtime **10.0.8**.
Установлены SDK 9.0.314/10.0.201/10.0.300 и NETCore reference packs
9.0.16/10.0.5/10.0.8. AssemblyName metadata тестовой Release Microsoft.CodeAnalysis.dll:
**5.9.0.0**; это чтение metadata, не загрузка analyzer для исполнения.
global.json fixtures — 10.0.300/rollForward disable. SDK 9 установлен владельцем
до задачи; task-02.1 ничего не устанавливает и не меняет toolset/config.

## Свежие команды и raw evidence

Основной private raw root:
`C:/Users/VuDZ/AppData/Local/Temp/task021-stage-a-0441efd9449a435d8fc0dddb8b9a8890/`.

- `stage-a-probe.ps1` — воспроизводимый read-only audit с exact args, SHA-256
  installed XML/current code/project/global.json, dotnet --info/list-sdks.
  Запуск shell exit **0**. `probe-result.json`: EvaluationExitCode=0,
  AllAuditedBytesUnchanged=true, UTC `2026-10-04T14:44:40.1633809Z`.
- Query: `dotnet msbuild <standard-root>/App/App.csproj -nologo
  -getProperty:MSBuildEnableWorkloadResolver,EnableNETAnalyzers,NETCoreSdkVersion,MSBuildToolsPath,TargetFramework,TargetFrameworks,Configuration,Platform,__DisableWorkloadResolverSentinelPath`.
  Working directory: retained standard fixture; exact command/stdout/stderr
  сохранены в `unchanged-evaluation-*`. Без forcing/resolver/analyzer overrides,
  restore и target execution. Оба flags true, SDK10.0.300, TFMnet10.0.
- `standard-fresh-audit.json`: compiler inventory 8 DLL, genuine TaskOutput 6;
  точные недостающие NetAnalyzers paths, 16 workload imports и XML Target counts.
  SHA-256 `95DBEAF1C61032950EB5BEA29D59F874FC42F38D920697BF057E4882AAED5F74`.
- `collect-fresh-evidence.ps1`: собирает ровно 13 comparison.json свежего main
  после UTC14:44 и их positive-scope.json; script exit **0**.
  `main-fixtures.json`, SHA-256
  `A5B9743CA589FE614BA5ED712A64305F5570107049049EB3FE884014D3687D8A`.
- `main-fixture-artifacts.json`: paths/lengths/SHA-256 всех retained artifacts
  этих 13 rows (commands, input/region/admission/observed JSON, binlogs).
  SHA-256 `265603E5480D0D738219F20F375D1F6727EDB438A63B47F9F0D6AD478DA77C70`.
- `main-dependency-attempts.json` перечисляет 12 свежих task-02 matrix scenarios.
  `main-dependency-artifacts.json` сохраняет их raw artifacts и свежий Razor witness;
  SHA-256 `39409B9EE508C200E13AE067292A4A24646603080B303341EE5B70057B3B315E`.
- `coordinator-validation.json` — полные возвращённые MCP отчёты build/focused/main,
  SHA-256 `A9C856AEA6BDB39A01728E954B17D5DC282DD3514A70BF63BFD696E9845D7C6C`.
- `artifact-manifest.json` — hashes/lengths файлов audit root на момент сбора,
  SHA-256 `FF9144F08D8CD8A83B51355AAFF938D4163E3C9C12066A0E2E417CA6A15C4A80`.

Raw binlogs/private logs не коммитятся; они не portable payload и могут исчезнуть
при очистке temp. Они не объявляются секрет-free environment export. Санитизированные
наблюдения сохраняют только существующий allowlist. Новый portable payload task-02.1 не создавался; source/generated texts не
экспортировались в него. Existing tests создавали owned fixtures/private witness
texts; full Razor texts сохраняются только в private witness evidence.

При source audit один rg-вызов включал отсутствующий installed Microsoft.Build.xml
и вернул exit1; package XML найден. Это недоступный путь read-only поиска,
не failed build и не подтверждение доступности какого-либо нового graph API.
Новых API compile probes не выполнялось.

## Validation координатора

Свежие прогоны на неизменённом C#:

- MCP `run_dotnet_build`: workspacePath
  `E:/Devel/RoslynMcpServer/RoslynMcpServer.sln`, configuration Release,
  noIncremental=true; **build exit0**. Baseline warnings: CS8603 в
  NuGetFallbackAssemblyResolver:35/53, CS8601 HostSession:1177,
  xUnit1031 WpfTemporaryProjectCallbackTests:195. Всего четыре; новые code changes
  отсутствуют.
- MCP `run_test_by_filter`: workspacePath
  `E:/Devel/RoslynMcpServer/RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj`,
  configuration Release, filter `FullyQualifiedName~WorkspaceLoadCache.Dependency`,
  noBuild=true, noRestore=true, timeoutSeconds=300, includeFullOutput=true:
  **15 total / 15 passed / 0 failed**.
- Тот же runner/project/config, filter `Category!=AnalyzerLifecycle`,
  noBuild=true, noRestore=true, timeoutSeconds=1200, includeFullOutput=true:
  **1222 total / 1222 passed / 0 failed**. Включены SourceStructure,
  dependency/fixture/host/oracle tests. Это baseline validation, не positive
  acceptance task-02.1. Tests runner не напечатал отдельного skip count или
  numeric process exit; статус returned passed, total=passed, timeout не сообщён.
  Отдельный TRX здесь не заявлен.
- AnalyzerLifecycle **NotRun / not-required**: docs-only, lifecycle/shadow-copy/
  workspace publication production paths, host и test/build config не менялись.
  Дополнительный SourceStructure-only run не требуется без production edits;
  эти tests вошли в main.

Старые forcing arguments `-p:NonExistentFile=<artifact>/never-created-<stage>`
в runner сохранены и явно записаны в command.json. Это augmentation, а не
неизменённая spec DTB command. Новая property query не использует forcing;
эквивалентность augmented/unaugmented compiler inventory этим не доказана.

## M021-01..12 — actual outcome отдельно от test pass

Во всех нижеследующих admitted candidates AnalyzerInventoryComplete=false,
OverlayReady=false; complete category coverage отсутствует. Existing tests
проверяют корректность отрицательного результата и ограниченные subchecks.
Зелёный reporting test не делает весь M021 выполненным.

1. **M021-01 / positive control:** ProjectGraph negative-admission **Passed**,
   disposition unknown. Два observed selected instances, ExpectedEdges=0;
   требуемая реальная edge/whole-graph closure не получена. Supported control
   **NotRun**, owner D021-01/02/03/04. Raw row ProjectGraph ниже.
2. **M021-02 / V03:** MultiTarget negative-admission **Passed**, unknown.
   Свежие восемь None/Embed build/DTB observations содержат соответственно
   E0_NINE + pack9.0.16 и E0_TEN + pack10.0.8; SDK10 pin сохранён. Отсутствующий
   SDK prerequisite не подменяется. Полная explicit/absent request-property
   и independent exact graph/edge/rebinding матрица **NotRun**. Raw MultiTarget.
3. **M021-03 / V05:** existing standard test повторил change/delete/restore
   owned App.AssemblyInfo.cs и GeneratedMSBuildEditorConfig, file probe обнаруживает
   content-changed/unreadable, restoration возвращает совпадение. Subcheck **Passed**;
   production admission producer proof/pre-hydrate miss **NotRun**. Raw standard
   `standard-46f3cfc9236a42e295e980019727bfbc/artifacts/`; GeneratedObj row unknown.
4. **M021-04 / V06:** existing linked test подтвердил две compile memberships
   shared.cs; imports наблюдаются, unknown. Escaping-glob negative **Passed** /
   unsupported. Полный edge/inner proof и import mutation обеих consumers
   **NotRun** в task-02.1. Raw linked-c2c30917ddca40da978ae7b41e231e69 и
   escaping-glob-a525c91bd71d4ffa87bb8ce0106a642c плюс LinkedImport row.
5. **M021-05 / V12:** literal Local.props appearance при неизменном csproj
   даёт known-absent-appeared, removal восстанавливает отсутствие: subcheck
   **Passed**. Expanded Exists/owning evaluation и ancestor input appearance
   **NotRun**; unresolved-negative-import сохраняется. Raw
   absent-cde575310c0446f19ef6f7f037e68713 и AbsentImport row.
6. **M021-06 / V13:** standard Directory.Build.props change/delete/restore и
   existing exact-pin/default-pack no-build check **Passed**. Это не configs/assets/
   full SDK/pack resolution closure. Same-size/same-mtime всей категории и fresh
   mismatch matrix **NotRun**. Installed files только читались; global config не
   изменялся. Raw main dependency artifacts и read-only stage-a probe.
7. **M021-07 / regions:** empty additional region получает новый member и другой
   manifest, Web hidden .well-known входит в inventory: subchecks **Passed**.
   Scoped CSS negative witness **Passed**, полнота текущих regions опровергнута.
   Imported Remove/conditions, Web byte mutation, non-member/race/reparse/
   unreadable/bounds полная матрица **NotRun** в remediation. Raw standard
   region-mutation, WebAssets и Razor witness ниже. Limits не ослаблены.
8. **M021-08 / analyzer inventory:** actual SDK/pack 8/6 mismatch и explicit
   non-TaskOutput DLL refusal наблюдаются; negative tests **Passed**. Inventory
   complete=false, overlay=false. Omitted-all/extra/dependency tampering existing
   host tests в main — synthetic seam checks; с independently complete real
   evidence вся требуемая матрица **NotRun**. Raw standard audit и
   explicit-analyzer-907e134dfd5e4883a2cc1c7699f13481.
9. **M021-09 / V14:** explicit owned main DLL изменена при той же длине и
   восстановлена; hash mismatch и exclusive-lock unreadability обнаружены:
   subchecks **Passed**. Same-mtime не утверждается; private/transitive dependency,
   missing/malformed payload и stale execution complete matrix **NotRun**.
   Runtime SDK/pack DLL не модифицировались. Raw explicit-analyzer выше.
10. **M021-10 / fresh overlay:** existing runner returns null, negative assertion
    **Passed**; два real fresh Complete snapshots/gate/rebinding **NotRun**.
    Нового no-DTB/no-process structural proof для несуществующего positive
    revalidation нет. Existing task-01 synthetic no-DTB tests не подменяют его.
    Evidence: audited RevalidateOverlayAsync, false flags всех rows.
11. **M021-11 / negative request:** custom hidden-input target, non-SDK, escaping
    glob и linked external Content negative tests **Passed**; custom/linked asset
    unknown, non-SDK/escaping unsupported. MissingReference и EmptyProject unknown
    не доказывают отдельный missing-edge/soft-warning/empty-complete detector.
    Real NuGet static asset, missing-root и весь требуемый soft-warning/empty
    detector matrix **NotRun** в remediation. Raw main-dependency-attempts.json
    и соответствующие oracle rows.
12. **M021-12 / V04 + integration:** Razor/Web negative admission **Passed**;
    Razor ordinary discovery build имеет expected CS0542/CS0219, это не successful
    build. Scoped CSS меняет generated text; восстановление возвращает полный
    baseline. Admission unknown, inventory/overlay false, positive generator
    equivalence **NotRun**. Fixtures/goldens/expectations не изменены. Reader
    public API limits task-04 (generator/load diagnostics, documentation-provider
    identity, representability и multi-inner binding) сохраняются отдельным hold.

Fresh Razor witness:
`C:/Users/VuDZ/AppData/Local/Temp/RoslynMcp-E0-Razor/76657ab4f7f34f6eabd5029a2875255f/evidence/`.
Baseline/scoped/restored observations и commands входят в main-dependency-artifacts.
Пути task-02 scenarios выше относительны `%LOCALAPPDATA%/Temp/roslyn-dependency-spike/`.
Их fresh mutation assertions находятся в existing DependencyEvidenceRunnerTests;
новых assertions task-02.1 нет.

## Handoff каждой из 13 task-03 fixtures

Формат каждой строки: admission / analyzer inventory / fresh overlay / positive
dispatch. **Для всех 13: unknown; incomplete(false); refused(false); hold.**
Фактический отрицательный experiment **Passed**, positive **NotRun** в каждой
строке. Это четыре независимых состояния, не следствие одного disposition bool.
ExpectedEdges пуст даже у ProjectGraph/LinkedImport и не считается complete graph.
Raw locator каждой строки: следующий child под
`C:/Users/VuDZ/AppData/Local/Temp/independent-workspace-oracle/`, затем
`evidence/comparison.json`; рядом positive-scope.json и admission artifacts.

- ProjectGraph: `ced67a0bd5f34120b6db666d1528f007`; 2 observed instances, 0 edges;
  graph positive hold, D021-01/02/03/04.
- Encodings: `6e1958f42b334235974599c7d1e77eba`; 1 instance, 0 edges;
  ожидаемый CS0219 не превращается в successful ordinary build/equivalence.
- MultiTarget: `455eba496bd6496dac029b95ff1b63dc`; 2 instances, 0 edges;
  prerequisites доступны, complete exact mapping/explicit-absent matrix hold.
- RazorGenerator: `d3d9e147070645f0b1670ba8c2247f38`; 1 instance, 0 edges;
  дополнительно CS0542 golden/CSS и oracle API hold.
- GeneratedObj: `b8a96824ecba48899f401c096e73a131`; 1 instance, 0 edges;
  положительный producer closure hold.
- LinkedImport: `a5b6ef18d0484a0aa4f495bbf8f5df2f`; 2 instances, 0 edges;
  membership observations не закрывают graph/import closure.
- AbsentImport: `d86a94de32b44691828d0846efe73c1d`; 1 instance, 0 edges;
  literal absence не закрывает unresolved conditions.
- CustomTarget: `11e623172b1541c19fd09f2d19f20054`; 1 instance, 0 edges;
  собственный Target сохраняет unknown независимо от SDK proposals.
- ExplicitCompile: `f4358c3fb0384051be3eb15983529142`; 1 instance, 0 edges;
  admission row не доказывает writer add/remove/rename capability.
- WebAssets: `843164a405d14213993d51f60b4deca4`; 1 instance, 0 edges;
  hidden asset inventory не даёт full Web coverage.
- ExternalAsset: `22d6ca9158ee44689ddf1de0a08967a4`; 1 instance, 0 edges;
  linked external Content refused; настоящий NuGet static asset не исследован.
- MissingReference: `746ad183d749428a8b067ba42cf6995b`; 1 instance, 0 edges;
  unknown по общим причинам не доказывает missing-edge detector.
- EmptyProject: `1d85f45a44994b9d9ec4ac5f3211ed4b`; 1 instance, 0 edges;
  законная пустота user source не равна independently complete empty inventory.

Итого actual experiments: negative **13 Passed / 0 Failed / 0 NotRun**;
positive **0 Passed / 0 Failed / 13 NotRun**. Numeric enums в JSON:
ScenarioOutcome Passed=0, Failed=1, NotRun=2. Во всех comparison counters
capture/hydrate/oracle unobserved/incomplete, DesignTimeContexts=null;
их нули не zero-DTB proof. Stages 2–7 task-04 не выполнялись.

## Trace R021 и оставшиеся owners

R021-01/03/04/06/07/08 → D021-01/02/03, current Inspect/Revalidate/inspector,
M021-01/06/08/09/10: blocker доказан, обязательные positive implementations
не выполнены. R021-02 → D021-04, empty edges/path-only owners, M021-01/02/04/11:
точные новые models/signatures deferred до replacement packet. R021-05 →
D021-03/05, current region/negative-import code, M021-03/05/07: partial subchecks,
complete bounded coverage не доказана. R021-09 → unchanged fixtures/oracle,
M021-12/13-row handoff: новых supported expectations нет, edits не требуются.
Владелец normative spec уже принял A021-01/02, strict D021-03 и D021-04/05; design owner
после этого выпускает full positive packet. Implementer не выбирает их за владельца.

## Независимое review — 2026-10-04

Автор `/root/dependency_remediation` и reviewer `/root/independent_review` — разные
сессии. Reviewer не писал design/код этой задачи и не запускал собственные tests.
Принят только factual audit, proposals D021-01..05, investigative gate packet,
evidence, supplements packets01/03/04 и согласованный blocked bookkeeping.
Privacy wording уточнён тем же implementer и перепроверен тем же reviewer;
открытых must-fix нет.

Reviewer независимо проверил все 13 comparison/positive-scope JSON, восемь
MultiTarget compiler observations, 498 fixture artifacts, 454 dependency/witness
artifacts и 18 audit manifest entries по hashes/lengths. Все шесть напечатанных
root hashes совпали; audited API declarations сверены с source. Coordinator
validation record подтверждён. Полный Stage A/positive implementation packet,
M021-01 supported control, M021-10 fresh Complete overlay и prerequisite task-04
остаются blocked/NotRun. Этот ACCEPT не меняет normative decisions или пять
verdicts ниже.

## Раздельные verdicts

- **Experiment allowed: yes**, только уже разрешённое isolated negative research
  и read-only audit. Его review acceptance не означает accepted remediation.
- **Implementation allowed: no** для dependent positive C# Stage B и production;
  positive packet/closure proof не готовы.
- **Public activation allowed: no**; opt-in/default=false и прежние gates сохранены.
- **Task-04 dependency prerequisite: blocked**; oracle API readiness отдельно blocked.
- **Next epoch allowed: no** по этому результату; **series complete: no**.

A021-01/02/03 и D021-04/05 приняты владельцем 2026-10-04; повторное approval не требуется. Принятие само по
себе не создаёт supported control или fresh overlay: потребуются источник полного
closure, согласованный packet, реализация, M021 matrix, independent review и final
validation. Наличие этого отрицательного отчёта не меняет статус на accepted.

## Продолжение после owner decisions — 2026-10-04

Owner approvals A021-01/02/03 и D021-04/05 получены. Current нормативные границы
согласованы в spec/cache-contract/UNRESOLVED/design/packets. Выбран strict finite
source-backed proof; installed-code trust не выбран. Razor/Web пока negative.
Прежний independent ACCEPT относится только к historical investigative version,
не принимает новые amendments/source contracts автоматически.

Продолжающийся [source audit](../design/task-02.1-source-contracts.md) фиксирует
exact binaries/revisions и исполнимые конечные input contracts. На этапе первоначального source audit C# не менялся; текущий limited implementation/validation описан ниже. Исторические 1222/1222
и 13 negative rows выше не выдаются за validation будущей implementation.
## Продолжение после owner choices: source controls — 2026-10-04

Owner choices A021-01/02/03 и D021-04/05 приняты; historical limited investigation
выше не является текущим отсутствием approval. Точный source audit, актуальные
boundaries и private raw hashes:
[task-02.1-source-contracts.md](../design/task-02.1-source-contracts.md).
Новые source-backed disk-only research scripts (оба exit0) воспроизвели selected
16workload manifests/targets с172probes и0target path differences, а также exact
pack reference paths net9.0 (164references+6analyzers)/net10.0 (167+6), оба0diff.
Selection-only controls не снимают restore/toolset/analyzer closure holds и не
дают supported/Complete/OverlayReady. Это новые исследовательские прогоны, не
повторные .NET tests; baseline counts15/1222 выше относятся прежнему unchanged code.

Private source audit root:
`%LOCALAPPDATA%/Temp/task021-source-audit-681e7dbdf0894c70b930c7fa0fe0cbaf/`.
`installed-sdk-binary-identity.json` SHA-256
`74223711539A84AB7097DB1AF72375FA94AC7BFA4EC01B8F7C70DC020295D8CD` сохраняет
actual paths/ProductVersion/FileVersion/SHA256 пяти SDK/MSBuild binaries;
source revision совпадает с informational product version, но reproducible build
не проверялся. Source scripts не исполняли downloaded code или analyzer DLL.

L021-1 partial executable packet принят independent reviewer после R021L-01/02/03 fixes;7new C# files+runner реализованы. Первая coordinator Release build:exit0, четыре прежних предупреждения, новых нет; focused tests/code review в работе. M021-01/10 positive controls, full R021-02/03/04/05/06/07/08 и
13fixtures positive handoff остаются невыполненными; никакие historical negative
rows не переименовываются в remediation acceptance. Razor/Web negative scope
сохранён, task04 supported pipeline stages2–7 — NotRun.



### L021-1 первая code validation — 2026-10-04

Координатор: Release solution build exit0, четыре прежних предупреждения, новых нет.
Focused FullyQualifiedName~WorkspaceLoadCache.Dependency:21total/19passed/2failed.
Новые Graph_discovers_transitive_instances_without_authorizing_admission и
Graph_preserves_inner_frameworks_and_refuses_missing_or_ambiguous_reference упали
из-за helper WriteProject: mixed slash path сравнивался exact string с canonical
path из graph. Raw graph commands/JSON подтверждают реально полученные child и
net9/net10 instances; fixture helper должен возвращать Path.GetFullPath. Assertions
не ослабляются. Private roots: dependency-partial-proof-4254d67f2c45480c83b02f9694fd3e52
и dependency-partial-proof-9ec7745ce7e4481fbf7cdada67a9e7e5 под local temp.

Independent code review отдельно нашёл R021C-01 explicit root TargetFramework
semantics, R021C-02 общий read/enumeration budget и XML rereads, R021C-03 mandatory
expanded-argument formatting. Это реальные review fixes; первый suite не accepted.
Signature delta C02 проходит same-reviewer до изменения соответствующего API.

### Limited code review после fixes — 2026-10-04

Same reviewer `/root/independent_review`: **ACCEPT L021-1 code**, R021C-01/02/03
закрыты. Это только7newfiles+runner partial implementation; full remediation не
принята. Root explicit TFM теперь передаётся в первую evaluation отдельно от
absent transitive cache; общий budget расходуется внутри probes, XML разбирается
из тех же прочитанных bytes; aggregate regressions и canonical test paths исправлены.
Reparse fact сохраняет отдельный owned reparse-witness.json, где Passed/NotRun
показывает actual branch; Passing fact не подменяет unavailable subcase.

Coordinator first build/focused raw report:
`task021-stage-a-0441efd9449a435d8fc0dddb8b9a8890/coordinator-l021-first-validation.json`,
SHA-256 `C7C1A50D06924B0E5747A15BD61CEADC9F88FAC350E971CFF08DE518298BFED6`.
Вторая Release solution build exit0, те же4предупреждения; focused/main final
ещё ожидаются. Все изменения versions/runtime/csproj/production отсутствуют.

### L021-1 повторная execution validation, ещё не final acceptance

Coordinator corrected build exit0; focused21/21Passed. Reparse subcase отдельно
NotRun: owned reparse-witness.json из dependency-partial-proof-8366fff1de844dca9346e2bef1b215bd
содержит IOException. Дополнительный owned diagnosis установил Win32error1314 /
HResult-2147023582, required privilege; no-elevation junction creation успешно,
Directory|ReparsePoint attributes1040. Test-only junction setup delta проходит review.

Первый full main после corrections:1228total/1227Passed/1Failed,
WorkspaceNonCSharpDiskSyncTests.Saved_additional_file_and_editorconfig_reach_the_published_snapshot:
ожидался новый editorconfig severity=error, получен старый текст. Эти production/
test paths не менялись task-02.1. Отдельный повтор affected class без правок:4/4Passed.
Причина отказа не доказана, failure не объявляется исправленным или preexisting flaky;
coordinator повторит main после final junction diff.

Raw corrected validation record в phase1 temp root:
coordinator-l021-corrected-validation.json SHA-256
`959E8F13162DC0E3F84F2FD60EFCFCCA6E75BE69D85C7C3197E86136CD6675FB`.

### Junction delta и ограничение MCP execution environment

Tiny test-only packet delta принят тем же reviewer, реализован и передан coordinator
с frozen C#. Третья Release build через MCP:exit0, те же4предупреждения. Третий
focused MCP run:21total/9Passed/12Failed; несколько existing runner и graph tests
получили Process.Start(dotnet) Win32 AccessDenied. Это не pass/unknown admission.
Новый reparse-witness.json в dependency-partial-proof-e95011db224a4db8ab78967d8c150ac4
содержит NotRun: symlink Win321314, затем absolute Windows PowerShell Process.Start
тоже AccessDenied. Junction child не стартовал и reparse assertions не исполнялись.
Причина общего process-launch отказа пока не доказана; coordinator проверяет тот
же Release/no-build/no-restore filter через доступный alternate runner, без
изменения глобальной security policy. Final validation/acceptance ещё pending.

P021-2 candidate outline в implementation packet перечисляет точные remaining
proof guards и truthful SDK-evaluated envelope seam. Это design investigation,
не accepted positive executable packet. Новые SourceLink `.git`+`HEAD` contracts
и implicit-vs-explicit NuGet config observations сохранены в source-contracts.
Реальный supported control/fresh Complete overlay всё ещё NotRun; L021-1 остаётся
частичной реализацией со всеми permanent unproven reasons и readiness=false.

Coordinator fallback CLI run того же focused21:9Passed/12Failed/0Skipped exit1;
TRX сохранён в phase1 temp root l021-cli-validation/dependency-focused.trx.
Diagnostic run20 без одного setup fact на том же compiled binary:20/20Passed.
Это correlation, не установленная причинность/диагноз security policy. По решению
координатора PowerShell fallback полностью удалён из теста; никакие глобальные
настройки/права не менялись. R021C-04 reviewer lifetime finding устраняется удалением
started-child code. Current symbolic-link-only witness сохраняет exact error detail
и honest NotRun, все assertions при доступном link сохранены. Final build/focused21/
main после revert ещё ожидаются. Reparse subcase не объявляется Passed.

### Final limited L021-1 acceptance — 2026-10-04

После удаления process fallback coordinator выполнил final Release solution build:
exit0, те же4прежних предупреждения, новых нет. Focused
`FullyQualifiedName~WorkspaceLoadCache.Dependency`:21/21Passed. Полный main
`Category!=AnalyzerLifecycle`:1228/1228Passed. Same reviewer C01–C04 CLOSED;
C04 закрыт удалением process path. **ACCEPT только L021-1 partial implementation**.
Reparse subcase отдельно NotRun/Win321314; доступность этой branch не утверждается.
SourceStructure входит в main; separate production check NotRequired (production
C# не менялся). AnalyzerLifecycle NotApplicable к L021-1: host/loader/publication/
configuration не менялись. Earlier failures и diagnostically isolated20 сохраняются.

Final frozen8file hashes и полные MCP reports:
phase1 temp root coordinator-l021-final-validation.json SHA-256
`8E16EB05CDA1A322715AF138C29ED1FDCA10E4D162BE8EED8EBDEBCF5DA161BC`.
Attempted junction/focused failures+diagnostic report:
coordinator-l021-junction-diagnostics.json SHA-256
`D86C4B2655A9CA85626086719855DFCCDE72085B01F9C363AD48EB318FC1C537`;
CLI l021-cli-validation/dependency-focused.trx SHA-256
`67BBE99AE4974041F55278904A2AEBFB3E425723A51581C39E6A74CE2B18D81A`.

Owner также принял A021-06 truthful typed-envelope binding location. Exact R08
normative paragraph и spec/cache-contract/packet01/02.1 синхронизированы. Это новый
разрешённый adapter seam, не implementation/admission acceptance. Finite source
contracts и full positive executable packet продолжаются; M021-01/10 positive
controls, real fresh Complete overlay, required generator и task04 positive stages
не выполнены. Unresolved categories сохраняют unknown/false, Razor/Web negative.
