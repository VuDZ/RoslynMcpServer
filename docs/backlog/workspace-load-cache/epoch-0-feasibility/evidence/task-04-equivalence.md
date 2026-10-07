# E0/task-04 — independent fresh MSBuild equivalence

Дата: 2026-10-04. Статус: **blocked for positive equivalence; isolated negative
oracle scope accepted**. Этот отчёт фиксирует выполнение isolated oracle;
production readiness и public activation не утверждаются.

## Scope и исполнители

- Coordinator: `/root`; orchestration/models/tests: `/root/oracle_runner`;
  observation reader: `/root/observation_reader`.
- Независимый reviewer: `/root/independent_review`; не автор design этой эпохи
  и не автор проверяемых файлов. Review **ACCEPT для ограниченного scope**;
  финальная coordinator validation выполнена ниже.
- Source HEAD на dispatch: `d3c8d5241c1c1c5d95042ce75bc057b747284cf1`.
  Исторический pin `9867318ddb5294ce144bf024b9a61a1a2e3814c3` / 1.3.21
  сохраняется отдельно. Source package version 1.5.4.
- Allowlist: семь новых файлов packet-04, этот evidence report и разрешённое
  task/index/packet execution-status bookkeeping. Общие host/DTO/fixtures,
  dependency runner, production, config и нормативные contracts не меняются.

## Предпосылки и граница результата

[Task-02](task-02-dependency-admission.md) принята только как isolated research:
restore/toolset/analyzer dependency closure не доказаны, свежий Complete overlay
не создаётся. Даже обычный SDK request получает unknown. Workload manifest
с target находится вне SDK, compiler analyzer inventory не совпадает с TaskOutput.
[Task-03](task-03-fixtures.md) принята с 13 layouts и отрицательными admission
ожиданиями; это не разрешение положительного hydrate.

Положительные V01–V06 остаются **not-run**, пока task-02 не предоставит
supported evidence. Rejected generator проверяет admission; отсутствие hydrate
и двух generated sets не является эквивалентностью. После установки владельцем
SDK 9.0.314 / net9 reference pack 9.0.16 MultiTarget проходит negative admission.
Razor golden сохраняет исходный конфликт CS0542 и
counterexample scoped CSS, а не подменяется упрощённой fixture.

## Среда

Windows 10.0.26200, win-x64; `dotnet --info`: SDK 10.0.300,
MSBuild 18.6.3+caa81fa49, runtime 10.0.8. После установки владельцем:
Installed SDKs 9.0.314/10.0.201/10.0.300; NETCore reference packs
9.0.16/10.0.5/10.0.8. Roslyn packages 5.9.0.
Дополнительная проверка по вопросу владельца: локальный
`sdk/10.0.300/Microsoft.NETCoreSdk.BundledVersions.props`, KnownFrameworkReference
для net9.0, запрашивает `Microsoft.NETCore.App.Ref` версии **9.0.16**.
Установка SDK 9 без нужного pack не доказывает готовность V03; используемый
fixture SDK остаётся 10.0.300. Эта prerequisite-проверка не устраняет closure/API
blockers положительной equivalence.
Fixture global.json pin 10.0.300 / rollForward disable; внешние feeds отключены.
Запрос CIM hardware inventory получил Access denied; RAM/CPU заново не измерены.

## Реализованный срез

Семь новых C# файлов packet-04: `IndependentWorkspaceOracle.cs`,
`WorkspaceObservationReader.cs`, `IndependentWorkspaceOracleTests.cs`,
`Models/WorkspaceObservation.cs`, `Models/OracleDifference.cs`,
`Models/OracleComparison.cs`, `Models/ScenarioOutcome.cs` под
`RoslynMcpServer.Tests/WorkspaceLoadCache/`.

CompareAsync выполняет SDK/prerequisite check, restore вне измеряемых стадий,
реальный dependency InspectAsync, проверку ожидаемых disposition/reasons и
ошибок evidence stages; возвращает fixture в исходное состояние и сохраняет
отдельные `comparison.json` / `positive-scope.json`. Общие closure reasons
без project/import/compiler observations не превращают ошибку в pass.
Encodings/Razor допускают только отдельно проверенные ожидаемые compiler
diagnostics. Failed cleanup и artifact persistence возвращают Failed; отмена
сохраняет token, ошибка cleanup при отмене сохраняет обе причины в aggregate.

Discovery запускает ordinary/DTB builds через task-02, и они могут выполнять
analyzers/generators в дочерних процессах. Отказ останавливает **candidate-owned**
capture/hydrate/oracle и загрузку DLL в candidate; процесс-wide отсутствие
выполнения DLL не заявлено. Счётчики discovery builds не приписываются capture,
hydrate или oracle. Для этих неисполненных стадий строго
`(0,0,0,null,false,[])`: это unobserved, а не доказанный zero-DTB.

OpenOrdinaryAsync создаёт новый MSBuildWorkspace через принятый OpenFreshAsync,
без DTO или graph предыдущей сессии. Отдельный контроль открывает две реальные
fresh ordinary sessions, проверяет разные SessionId/workspace/solution IDs,
nonzero complete DTB counters и фактический `Lib.Value.Number=42`.

Reader материализует graph, project properties и closed option vectors,
all-memberships, characters/encoding/bytes, references с aliases/EmbedInteropTypes
и hashes, constants/navigation, config diagnostic behavior и sorted diagnostic
multiset. Comparator сравнивает все 11 категорий; null/missing/empty различаются,
diagnostic multiplicity сохраняется. Independently authored expectation ловит
общую ошибку обеих сторон. Прямые reader tests используют явно synthetic Adhoc
capability с representable options; это не observed admission и не hydrate proof.

Нормализация: fixture-root paths относительные с `/`, instance
`project.csproj|innerTFM`, membership `instance|role|path`; SDK/pack/external paths
остаются абсолютными. Folders/name/source kind — значения membership, reference
properties не удаляются. Generated scaffold требует однозначную public identity
assembly/type/hint; generated texts остаются только ephemeral evidence.

## V01–V06 и дополнительная матрица

После установки SDK 9 focused run: 42/42 новых harness tests passed.
Фактические attempts: **negative-admission 13 Passed / 0 Failed / 0 NotRun; positive-equivalence
0 Passed / 0 Failed / 13 NotRun**. Test pass означает проверку честного outcome,
а не положительную эквивалентность. До установки: negative 12 Passed / 1 NotRun;
историческая validation и post-install evidence приведены ниже.

- **V01 / ProjectGraph:** negative admission Passed (unknown); graph/options/
  references/navigation/diagnostics equality A→B→C NotRun. Отдельный real ordinary
  control проверяет константу 42, но не сравнение с hydrate.
- **V02 / Encodings:** negative admission Passed; ожидаемый CS0219 не считается
  successful fixture build. Полная UTF-8 BOM/no-BOM/UTF-16 fresh-vs-hydrate и
  persisted round-trip bytes NotRun. Synthetic reader проверяет UTF-8 BOM,
  `Привет Ω`, options/config/constant/navigation; он не заменяет V02.
- **V03 / MultiTarget:** после установки reference pack negative admission Passed
  (unknown); Inspect содержит instances net9.0/net10.0 и compiler inputs обоих
  TFM. До установки negative admission был NotRun до Inspect. Positive exact
  two-inner mapping/defines NotRun. Reader
  отвергает неоднозначное связывание одного project path с двумя instances.
- **V04 / RazorGenerator:** negative admission Passed с observed compiler inputs
  и ожидаемыми CS0219/CS0542; positive full generated set/texts/diagnostics и marker
  comparison NotRun. Exact fixture и failing golden не исправлялись ради pass.
- **V05 / GeneratedObj:** negative admission Passed; новое доказательство
  post-capture missing/changed obj → pre-hydrate refusal NotRun. Ранее принятые
  task-02 mutation subchecks сохраняются как отдельное evidence.
- **V06 / LinkedImport:** negative admission Passed; hydrate updates обеих
  memberships и external import invalidation в этом oracle NotRun. Synthetic
  comparator не подменяет реальную linked graph проверку.
- **V23:** real fresh ordinary controls Passed в ограниченном scope двух
  независимых ordinary sessions; capture-vs-hydrate distinction и production
  RAM/disk/force/reset/false→true policies NotRun.
- AbsentImport, CustomTarget, ExplicitCompile, WebAssets, ExternalAsset,
  MissingReference, EmptyProject: negative admission Passed; соответствующие
  positive/capability/mutation assertions NotRun. MissingReference отказался по
  общим unknown closure reasons, а не доказывает специальный missing-edge detector.
  ExplicitCompile не доказывает новый writer capability; он только admission row.

## Невыполненные обязательства и конкретные limits

Packet-04 stages 2–7 — capture A, codec round-trip, hydrate B, independent oracle C
и closure stabilization, write-back/reconcile, reverse insertion и DTO/analyzer
tampering witnesses — **не реализованы/не исполнены этим runner**. Supported
disposition не имеет пути к positive Passed: без design-owned remediation он
возвращает Failed при конфликте ожиданий либо NotRun. Task-01 smoke witnesses
не перенесены в отчёт как выполненные task-04 проверки.

Reader — limited scaffold, не полный oracle completeness proof:

- точное multi-inner связывание по одному project path отсутствует; first-match
  и Project.Name inference запрещены;
- DesktopStrongNameProvider search paths недоступны через public getter;
  непредставимые option services дают явный failure;
- у Project.GetSourceGeneratorDiagnosticsAsync нет accessible public member
  в Roslyn 5.9.0: compile probe получил CS1061. Reader консервативно отвергает
  analyzer/generator-bearing observation; available generated documents не
  доказывают полноту generator/load diagnostics;
- PortableExecutableReference.DocumentationProvider также недоступен как public
  getter (CS1061). Conventional adjacent XML sidecar path/hash — наблюдение
  файловой системы, **не identity реально выбранного documentation provider**.

Недоказанные API seams возвращаются design/dependency owner. Production paths
и нормативный профиль ради устранения этих ограничений не изменялись.

## Независимое review

Reviewer `/root/independent_review` отделён от author sessions. R04-01 — формат
diagnostic IDs (JSON objects и workspace arrays); R04-02 — failed evidence stages
не дают negative pass; R04-03 — cleanup/persistence ошибки сохраняются в outcome;
R04-04 — обязательный стиль expanded argument lists. Исправления возвращены тем
же исполнителям. **R04-01–R04-04 закрыты** той же независимой review session
после повторной проверки. **ACCEPT** относится только к negative-admission
harness, comparator/reader scaffold, fresh ordinary controls и direct synthetic
reader capabilities. Ни полного oracle completeness, ни positive equivalence
review не принимает.

## Validation

Первый compile probe: exit 1, CS1061 inaccessible generator diagnostic member;
новый nullable warning CS8620 в generic expectation comparator. Исправлены
публичным ограничением reader и корректным nullable generic type, без private API.
Development MCP `run_test_by_filter`, `FullyQualifiedName~IndependentWorkspaceOracleTests`,
Release, noBuild=false, noRestore=true, timeoutSeconds=1200, includeFullOutput=true:
**42 total / 42 passed / 0 failed**.

## Финальная coordinator validation и приёмка (2026-10-04)

До установки SDK 9, после всех функциональных и стилевых правок coordinator
`/root` выполнил:

- MCP `run_dotnet_build`, workspacePath `E:/Devel/RoslynMcpServer/RoslynMcpServer.sln`,
  configuration Release, noIncremental=true: **exit 0**. Четыре baseline warnings:
  CS8603 дважды в NuGetFallbackAssemblyResolver, CS8601 в lifecycle HostSession,
  xUnit1031 в WpfTemporaryProjectCallbackTests. Новых warnings нет.
- MCP `run_test_by_filter`, workspacePath
  `E:/Devel/RoslynMcpServer/RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj`,
  configuration Release, filter `FullyQualifiedName~IndependentWorkspaceOracleTests`,
  noBuild=true, noRestore=true, timeoutSeconds=1200, includeFullOutput=true:
  **42 total / 42 passed / 0 failed / 0 skipped**, timeout отсутствует.
- Тем же MCP runner и final binaries, filter `Category!=AnalyzerLifecycle`,
  остальные параметры те же: **1222 total / 1222 passed / 0 failed / 0 skipped**,
  timeout отсутствует. Это вся main CI suite, включая SourceStructure,
  prior dependency/fixture/host tests и новые 42 cases; не zero-test run.
- `git diff --check` и `git diff --no-index --check -- NUL <new-file>` для каждого
  из семи новых файлов не дали whitespace diagnostics. No-index exit 1 означает
  существование diff; LF→CRLF warnings не являются whitespace findings.
- AnalyzerLifecycle — **not-run/not-required**: isolated test-only edits;
  production lifecycle/load/publication paths, lifecycle host и test/build
  configuration не менялись.

**Принятый scope:** семь C# файлов packet-04, negative attempts (до установки
12 Passed/1 NotRun; после установки 13 Passed/0 NotRun),
comparator/reader scaffold с прямыми synthetic tests, real ordinary independence
control, evidence и task/index/packet bookkeeping. Task-04 overall **blocked**
для positive equivalence: dependency closure и public observation contracts
выше требуют remediation до реализации/запуска packet stages 2–7.

## Post-install validation (2026-10-04)

Владелец установил SDK 9.0.314. Coordinator проверил установленный
`Microsoft.NETCore.App.Ref/9.0.16` и повторил validation без изменения C#:

- MCP focused filter `FullyQualifiedName~IndependentWorkspaceOracleTests`:
  **42 total / 42 passed / 0 failed / 0 skipped**.
- MCP solution `run_dotnet_build`, Release, noIncremental=true: **exit 0**;
  те же четыре baseline warnings, новых warnings нет.
- MCP main filter `Category!=AnalyzerLifecycle` после этого build:
  **1222 total / 1222 passed / 0 failed / 0 skipped**.
- Оба test runs: Release, noBuild=true, noRestore=true, timeoutSeconds=1200,
  includeFullOutput=true; timeout отсутствует. SDK runner и fixtures — 10.0.300.
  AnalyzerLifecycle по прежней границе scope not-run/not-required.

Фактические final main attempts: **negative 13 Passed / 0 Failed / 0 NotRun;
positive 0 Passed / 0 Failed / 13 NotRun**. Установка закрыла только prerequisite
V03; task-04 сохраняет blocked status для positive equivalence.

Тот же независимый reviewer `/root/independent_review` проверил post-install
focused V03 в `d727c7e38af2457baa7a47cb920783ad/evidence/`: SDK 10.0.300,
точный global.json pin/hash, оба inner compiler contexts net9.0/net10.0,
None/Embed DTB commands с `E0_NINE`/`E0_TEN` и reference packs 9.0.16/10.0.8.
Restore, evaluation и восемь discovery commands имеют exit 0.
Disposition unknown и closure reasons сохранены; capture/hydrate/oracle artifacts
отсутствуют, counters incomplete/unobserved. Review не нашёл замечаний к новому
environmental outcome. Наличие двух compiler contexts не является fresh/hydrate
равенством options/defines или разрешением positive dispatch.

Final main raw evidence:
`C:/Users/VuDZ/AppData/Local/Temp/independent-workspace-oracle/task04-postinstall-summary-4ab39db59b3c4e96aaa153fcab518e2c/`:

- `attempts.json`, SHA-256
  `D2EFBB1C2CBA0915992F1BC615B66AB52EE5B53ECD00C57BC3297385CF13FB6F`;
- `artifact-manifest.json`, SHA-256
  `D5E6E27E944D904F4C013B7B2E9C633DA1FE92C999329528B041AFDB2FABBA83`;
- `installed-sdks.txt`, SHA-256
  `B9373BA280CBD0C6097844FD4079428F75FC03021A81A669E5DA583D33862F0F`;
- `reference-packs.txt`, SHA-256
  `7B3ED278D6A38F0AAB22D163A82997DD3AA9FD1F4AE97F3216DC95528DAD38E7`.

Final main V03:
`C:/Users/VuDZ/AppData/Local/Temp/independent-workspace-oracle/934e78d44591443884fda5a224636c1f/evidence/comparison.json`,
SHA-256 `AA46A61199FCB9D1F861611692B4C86E522DB272D822F91EBD5CFB95D7BAB91B`.
Manifest фиксирует paths/hashes/lengths всех retained artifacts 13 final rows;
private logs остаются вне Git. Исторические locators ниже сохранены отдельно.

## Raw evidence и hashes

Исторические main-suite attempts до установки SDK 9, outcomes/counters и точные locators сохранены в
`C:/Users/VuDZ/AppData/Local/Temp/independent-workspace-oracle/task04-summary-1af5ffa51e084cf2aee3c75d2d4e6e34/`:

- `attempts.json`, SHA-256
  `B09746332428CC88307D1A703D06BF9BE7D1D53D10CF1AACE8F8BCF1886F52F0`;
- `artifact-manifest.json`, SHA-256
  `E2E154DE744635ED2D618239057738226DC2EF696E95411931B8F2ED4EB28B18`:
  hashes/lengths всех retained command/stdout/stderr/observed/admission/binlog
  artifacts выбранного final attempt каждого scenario. Эти private raw logs
  не добавляются в Git. Fixture roots удалены только после Restore/Verify;
  artifact directories находятся вне fixture roots и остаются доступными.

V01–V06 до установки, `evidence/comparison.json` locators относительно
`C:/Users/VuDZ/AppData/Local/Temp/independent-workspace-oracle/`:

- V01: `8257882681f54e4f9714759cc173227e`;
- V02: `52cc1d83b7914665a12d3310f4b29e5a`;
- V03: `fc451969a69b4a549fc080ced18348d5`;
- V04: `3086153b74ca4edcab4dd4237e1fc985`;
- V05: `ecb31b8f5b1441ae8221907042c037a4`;
- V06: `bbdc7496ea204c77baf764db7022895c`.

Остальные семь rows и SHA-256 comparison/positive-scope файлов — в attempts.json.
Во всех rows capture/hydrate/oracle counters unobserved/incomplete, contexts=null.
Полные Differences этих отказавшихся rows пусты: semantic comparison не запускался.
Они не являются нулём unexplained semantic differences при положительной приёмке.

Real ordinary control:
`C:/Users/VuDZ/AppData/Local/Temp/independent-workspace-oracle/e539ca5386264defb09dc5baf80e3e0d/ordinary-controls/sessions.json`.
FirstSession `7170f8e0-6cf5-4213-b9ca-6bb16e3621d8`,
SecondSession `d52a0d7c-6d5d-4bf5-a516-c424bef5740d`.
Второй measured ordinary control: OpenInvocations=1, ProjectStarts=4,
CoreCompileTargets=2, DesignTimeContexts=4, Complete=true; raw/replay binlog
locators записаны в sessions.json. Оба controls проверены тестом, SessionId
не используется в portable equivalence keys.

## Отдельные verdicts и следующий шаг

- **Experiment allowed: yes**, только isolated negative harness/scaffold,
  существующие ordinary controls и synthetic capabilities в принятом scope.
- **Implementation allowed: no** для production hydrate/cache на этом evidence;
  production lifecycle matrix не пройдена этим заданием.
- **Public activation allowed: no**, положительная эквивалентность, complete
  dependency/overlay и performance gates не закрыты.
- **Next epoch allowed: no** на основании этой task; required feasibility
  equivalence остаётся blocked. Metadata O7 сохраняет отдельный verdict и не
  становится cache go от этих результатов.
- **Series complete: no**; epoch-0 и O1–O8 этим scope не завершены.

Следующий технический шаг — design/dependency remediation конкретных limits
task-02 и public oracle APIs, обновление затронутых packets владельцем,
затем реализация и независимая проверка stages 2–7 на реально supported fixtures.
Нормативные contracts и activation permissions при status bookkeeping не менялись.
