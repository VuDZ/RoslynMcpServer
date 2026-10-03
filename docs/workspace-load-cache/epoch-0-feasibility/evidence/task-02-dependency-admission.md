# E0/task-02 — dependency-admission experiment

Дата: 2026-10-03. Статус: **accepted for isolated research; positive admission
не доказан**. Исполнитель `/root/dependency_spike`; отдельный Razor witness —
`/root/razor_witness`; coordinator `/root` владеет final build/main suite и status.
Это isolated experiment; production, spec/contract, activation и версии не менялись.

## Результат и граница вывода

Наблюдение imports/Compiler/TaskOutput воспроизводится, однако этого недостаточно
для `supported`. Runner возвращает `unknown` для целого request, когда не доказаны
restore resolution closure, полный identity input set toolset либо analyzer
dependency closure; non-SDK project и escaping glob дают `unsupported`.
Ни одного положительного `CacheCompleteness`, production hit или разрешения
выполнять DLL этот spike не выдаёт. `RevalidateOverlayAsync` выполняет повторную
проверку наблюдённых файлов, затем возвращает null: fresh Complete snapshot и
интеграция с existing gate **not-run**, а не passed по факту null.

Эти отрицательные результаты не разрешают dispatch положительных fixtures
task-03. Исследование обнаруживает конкретные недостающие evidence/contract
решения; оно не переписывает профиль для получения pass.

## Среда и воспроизведение

- Source HEAD на dispatch: `bea9422f3286d8531c05d41a12fe71b9bf6b74d2`, source
  1.5.4; исторический pin `9867318ddb5294ce144bf024b9a61a1a2e3814c3` / 1.3.21
  остаётся отдельным основанием. Current-baseline-audit имеет предыдущий audited
  HEAD `07859db586c68452771c88ba76409bb78e0e289f`, не выдаётся за runtime spike.
- Windows 10.0.26200, x64, `C:\Program Files\dotnet\dotnet.exe`, SDK 10.0.300,
  MSBuild 18.6.3, runtime 10.0.8; Roslyn packages 5.9.0. Каждый fixture имеет
  `global.json` 10.0.300 / rollForward disable и NuGet.Config с пустыми feeds.
- Installed `Microsoft.NETCore.App.Ref`: 10.0.5, 10.0.8. net9 pack отсутствует.
  Missing net9 не заменён net10; ordinary build/restore outcome остаётся failure.
- Running MCP отдельно: 1.5.4.0,
  `E:\Devel\RoslynMcpServer\bin\Release\net10.0\win-x64\publish\RoslynMcpServer.exe`,
  modified 2026-10-01T17:41:09.1475207+03:00; workspace не загружен.
  Его version не доказывает совпадение binary с HEAD.
- До работы dirty: AGENTS.md, README эпохи, packet-task-01, task-00/task-01,
  task-execution.md, пользовательские `.agents/skills/prepare-github-release/`,
  `.cursor/rules/roslyn-mcp.mdc`, `iotester.py`, `scripts/`. Они не правились spike.

Повтор: `run_specific_test` для
`E:/Devel/RoslynMcpServer/RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj`,
className `DependencyEvidenceRunnerTests`, configuration Release,
noRestore true, timeoutSeconds 600. Отдельно `DependencyRazorWitnessTests`.
Focused build делается самим runner. Final build/main suite — coordinator.

Каждый matrix fixture создаёт новый temp root
`%LOCALAPPDATA%/Temp/roslyn-dependency-spike/<scenario>-<guid>/`.
Root имеет `.dependency-spike-owned`; чужой каталог отвергается до subprocess и
первой записи. Restore выполняется один раз отдельно. Затем evaluation
`dotnet msbuild <project> -getProperty:<explicit list> -getItem:<explicit list>`,
ordinary `dotnet build <project> --no-restore` и DTB
`dotnet msbuild <project> -t:Compile -p:DesignTimeBuild=true
-p:SkipCompilerExecution=true -p:ProvideCommandLineArgs=true`, каждый с
`ProjectImports=None` и `Embed`, каждый inner TFM отдельно. Logger argument
передаётся одним элементом ProcessStartInfo.ArgumentList.

После первого failed focused run добавлен **экспериментальный forcing parameter**
`-p:NonExistentFile=<artifact-dir>/never-created-<stage>` во все четыре command
варианта. CoreCompile перечисляет его среди Outputs. В исходном прогоне ordinary
build делал Csc актуальным: DTB имел CoreCompileCount=1, но CompilerCommands=[];
это **не пустой compiler inventory**. Augmented commands отличны от unmodified
spec command; forcing path не сохраняется в portable effective properties.

Child environment очищается; сохраняются только необходимые OS/runtime location
переменные SystemRoot, WINDIR, PATH, TEMP, TMP, USERPROFILE, LOCALAPPDATA, APPDATA,
ProgramFiles, ProgramFiles(x86), DOTNET_ROOT; задаются UI language en-US,
telemetry opt-out, no-logo. Environment/global property bags не экспортируются.
Raw binlogs/diagnostic stdout остаются локальными private artifacts и не добавляются
в Git. `*-command.json` содержит аргументы и exit code, `*-observed.json` —
выбранные imports, compiler commands, TaskOutput и allowlisted context properties;
`fixture-before.json`/`artifacts-manifest.json` — SHA-256/length inventories.
Первоначальные unsanitized-environment raw logs также локальны и не являются
переносимым evidence payload.

## Источники evidence и реальные ограничения

- Positive import: `ProjectImportedEventArgs.ImportedProjectFile` + direct hash.
  `Directory.Build.props` и targets наблюдаются; binlog None достаточен для этих
  paths. У import в export owner **null**: graph import event не приписывается
  root instance без доказанного context mapping.
- Known absent: событие с пустым ImportedProjectFile и literal UnexpandedProject
  принимается только при найденном XML Import с exact `Exists('literal')`.
  Property expansion, сложная condition, false не-Exists или wildcard остаются
  unresolved-negative-import. `ImportIgnored` не используется как proof.
- Region: XML Include/Remove + evaluated DefaultItemExcludes и
  DefaultExcludesInProjectFolder. Структурированные region manifests сохраняют
  includes/excludes/members/hashes, даже когда additional glob пуст. SDK evaluated
  excludes включают `**/.*/**`; для wwwroot/.well-known отдельный web region
  намеренно следует выбранному контракту без dotfile exclusion. Region observation
  не утверждает полноту Remove из всех imported props или условных items.
- Traversal bounded: 100000 entries, depth 64, 256 MiB matching bytes;
  reparse point/нечитаемый путь не дают truncated region. Повторный полноценный
  race-safe region probe/publication protocol здесь не реализован.
- Compiler inventory: public CSharpCommandLineParser разбирает Csc command
  event; учитываются compile/additional/analyzer-config/metadata/analyzer paths.
  Csc сопоставлен с ProjectStarted context; чужой transitive compiler command
  root owner не получает. TaskOutput Analyzer также сопоставлен по consumer path.
  Exact context-to-inner-TFM mapping независимого fresh graph остаётся gate.
- Toolset: import Sdk.props даёт path. Отдельный no-build witness заново читает
  exact global.json и SDK Microsoft.NETCoreSdk.BundledVersions.props; net10
  KnownFrameworkReference выбирает installed Microsoft.NETCore.App.Ref/10.0.8.
  Повторное чтение совпадает; отсутствующий pinned SDK отвергается. Это доказано
  только для default installed pack без overrides/fallback/download; полная SDK
  directory identity и transitive task/resolver reads остаются unproved.
- Custom Target: XML вне реально исполняемого SDK каталога даёт unknown;
  fixture Exec читает отдельный hidden-input.txt и действительно создаёт
  target-observation.txt. Path в Exec не объявлен универсальным source closure.
- Linked source: явный shared/Shared.cs наблюдается в отдельных compiler contexts
  App и Library; external common.props хешируется. Внешний glob unsupported.
  Exact graph edge verification остаётся not-run, а не empty graph success.
- Analyzer: command-line DLL inventory не равен TaskOutput inventory. Явный
  external/Explicit.dll — копия SDK DLL только в owned fixture — остаётся вне
  TaskOutput. Чтение/хеширование не выполняет DLL. Complete provenance из совпавшего
  hash не фабрикуется.

Два конкретных препятствия обнаружены даже у минимального standard fixture:

1. SDK resolver импортирует
   `C:/Program Files/dotnet/sdk-manifests/10.0.100/microsoft.net.workload.mono.toolchain.current/10.0.108/WorkloadManifest.targets`.
   В нём есть Target, а файл вне `sdk/10.0.300`. Буквальное правило выбранного
   профиля даёт custom-target/unknown; каталог SDK не расширен до всего dotnet.
2. Csc содержит 8 analyzer DLL, TaskOutput содержит 6 pack generators. Две DLL
   `Sdks/Microsoft.NET.Sdk/analyzers/Microsoft.CodeAnalysis.NetAnalyzers.dll` и
   `Microsoft.CodeAnalysis.CSharp.NetAnalyzers.dll` отсутствуют в TaskOutput.
   OverlayReady=false здесь требуется ещё до исследования dynamic dependencies.

Standard None/Embed DTB являются разными subprocess; в обоих ProjectInstanceId=2.
Он не используется как portable identity. Повторение числа не означает новую
доказанную сессию; contexts каждого лога сохраняются раздельно.

## Mutation subchecks и V03–V06/V12–V14

- V05: SDK-generated `obj/Debug/net10.0/App.AssemblyInfo.cs` и
  `App.GeneratedMSBuildEditorConfig.editorconfig` наблюдаются compiler parser.
  Каждый файл изменяется и удаляется; file recheck даёт content-changed/unreadable,
  после byte restoration снова совпадает. Это subcheck до возможного hydrate,
  не production miss/hit test.
- V12: absent Local.props реально появляется при неизменных csproj bytes;
  `RecheckObservedFilesAsync` возвращает known-absent-appeared. После удаления
  исходное отсутствие восстановлено.
- V13: Directory.Build.props меняется/удаляется/восстанавливается с тем же probe.
  Независимый SDK/pack no-build witness описан выше. Полная NuGet/restore closure
  не доказана и сохраняет unknown всего request.
- V14: owned Explicit.dll меняется в последнем byte при той же длине; hash probe
  отказывает. Exclusive FileShare.None даёт unreadable, restoration возвращает
  исходный hash. Runtime SDK/pack DLL не меняется. Dependency-DLL closure,
  malformed portable payload, ambiguous consumer и новый session snapshot через
  existing gate **not-run**. Это не заменено unit assert `null`.
- V03: net9;net10 сохраняются как две requested inner keys; net9 prerequisites
  отсутствуют. Positive exact instances/edges/defines comparison **not-run**.
- V06: compiler shared membership observations доступны; positive hydrate/fresh
  equivalence и independent complete graph **not-run**.
- Empty additional region проверяется появлением missing-dir/new.txt: повторный
  inspect меняет ordered membership/content manifest; файл затем удаляется.
- `unsupported`/`unknown` проверяются на уровне целого returned AdmissionEvidence;
  частичный hit, hydrate и analyzer loading не запускаются. Модель не имеет
  доказанной positive ветки — это исследовательское ограничение, не production detector.

Web fixture проверяет inventory `wwwroot/index.txt` и
`wwwroot/.well-known/e0.txt` в structured region; отдельная Web asset byte mutation
не выполнялась. External-asset — явный linked Content `../external/asset.txt`,
не NuGet package static asset. Настоящий NuGet static asset outside project/SDK
**not-run**. Full UTF-8 BOM/UTF-16 decoding matrix, independent graph edges,
production before-hit refusal и corruption/ambiguity integration также **not-run**.
Fixture UTF-8 source с `Привет Ω` сохраняет bytes в initial/final hash inventories;
это не заменяет packet-03 encoding matrix.

## Razor/Web и packet-03

Отдельный fresh MSBuildWorkspace witness принадлежит DependencyRazorWitnessTests.
Полные generated identities/texts, Marker, invariant diagnostics и snapshots
additional/config inputs экспортируются в локальные artifacts. Положительная
эквивалентность с hydrate не выполнялась.

У exact packet-03 fixture `Marker.razor` + `public const string Marker` обнаружен
конфликт generated class/member: CS0542. Fixture не переименован ради pass;
это обязательный finding design owner, positive fixture build не passed.

Fresh scoped CSS witness **passed как negative closure counterexample**:
добавление `App/Marker.razor.css` с `h1 { color: crimson; }` не меняет manifest
выбранных `.cs/.razor/.cshtml/wwwroot` регионов. Все ранее существовавшие fixture
inputs, включая obj, до следующего build/open имеют прежние hashes. Новый fresh
MSBuildWorkspace после build получает другой generated Razor text: элемент h1
дополнен scope attribute `b-6xpslpw42p`; generated editorconfig получает
AdditionalFiles.CssScope. Удаление CSS и повторные build/fresh open восстанавливают
**полный** исходный generated text. Это counterexample negative coverage выбранного
списка регионов, а не рекомендация молча добавить новый glob.

Generated inventory содержит один document, точная identity:
`App/App.csproj|net10.0|<root>/App/obj/Debug/net10.0/Microsoft.CodeAnalysis.Razor.Compiler/Microsoft.NET.Sdk.Razor.SourceGenerators.RazorSourceGenerator/Marker_razor.g.cs|Marker_razor.g.cs`.
Constant `E0Razor.Marker.Marker` имеет значение `E0RazorMarker`. Fresh diagnostics
содержат CS0219 Error по `.editorconfig` и CS0542 Error из exact fixture. Все три
ordinary builds ожидаемо failing по CS0542; `-p:NoWarn=CS0219` в ordinary build
не скрывает fresh workspace diagnostic. Один passing witness test означает,
что **ожидаемое отрицательное поведение наблюдалось**, не positive fixture build.

Razor raw locator:
`C:/Users/VuDZ/AppData/Local/Temp/RoslynMcp-E0-Razor/cfd3a44ec3b94ef382ee29bd6cf865e5/evidence/`.
`baseline.observation.json`, `scoped-css.observation.json`, `restored.observation.json`
содержат FULL generated dictionaries, diagnostics и constant; `*.analyzers.json`
содержат identities/hashes всех DLL, включая три Razor source-generators DLL;
`*.command.json`, stdout/stderr и binlogs разделены по stage.
`artifact-hashes.json` SHA-256
`FD0B367C2EF2C64DB1394F714C10D56D1871D93F043ED21E7CEC8842FD84D4F8`.

## Validation и handoff

Первый focused run: **13 total / 10 passed / 3 failed**; failures выявили отсутствие
Csc после incremental ordinary build. До него compile-only вызов ещё не существующей
test class дал zero matching tests; это не pass. Второй matrix run:
**14 total / 13 passed / 1 failed**: mutation selection ошибочно включал внешний
SDK globalconfig; запись получила UnauthorizedAccessException. Файл не изменён:
recorded/current SHA-256 совпали,
`6888AEB9B88E3AF1D89098A14EDDE33DF3F5CA8C4A4F880D2F2279AA560A493F`.
Исправление ограничивает selection owned fixture root; before-write guards
проверяют canonical prefix, а для Local.props/Explicit.dll exact expected path.

Третий matrix focused run: **14 total / 14 passed / 0 failed**. Razor focused
MCP run: **1 total / 1 passed / 0 failed**. Direct shell Razor test launch прежде
завис и был остановлен; его outcome не засчитан. Последние два exact-path asserts
проверены окончательным coordinator build/main suite.

**Final coordinator validation (2026-10-03):**

- `run_dotnet_build` для RoslynMcpServer.sln, Release, noIncremental=true: exit 0.
  Четыре существующих warnings: CS8603 дважды, CS8601 HostSession, xUnit1031 Wpf test;
  warnings новых файлов нет.
- `run_test_by_filter` для RoslynMcpServer.Tests.csproj, Release,
  `Category!=AnalyzerLifecycle`, noBuild=true, noRestore=true, timeoutSeconds=1200,
  includeFullOutput=true: **1132 total / 1132 passed / 0 failed**, timeout отсутствует.
  Этот suite включает SourceStructure. Отдельное число skips summary не сообщено.
- AnalyzerLifecycle **not-run / not-required**: isolated test-only code,
  lifecycle/load/publication production paths и их build configuration не менялись.

Файлы task-02: DependencyEvidenceRunner.cs, DependencyEvidenceRunnerTests.cs,
DependencyRazorWitnessTests.cs; Models/DependencyCommandResult.cs,
DependencyContextObservation.cs, DependencyImportObservation.cs,
DependencyObservations.cs; этот отчёт и Findings supplement packet-task-03.
Координатор также синхронизировал execution-status bookkeeping в task-02, README
эпохи и design.md; нормативные требования и activation permissions не изменены.
Production C# не менялся; SourceStructure вошёл в полный main Release suite.
AnalyzerLifecycle paths/config не менялись.

Verdicts: experiment — **accepted isolated research**; implementation — **не разрешена**
этим spike; public activation — **не разрешена**; task-03 positive fixtures —
**hold** до design/evidence remediation; next epoch/series complete — **нет**.

## Независимая приёмка

2026-10-03, reviewer `/root/independent_review`, отдельная сессия, не автор
проверяемого кода или design. Авторы: `/root/dependency_spike` и
`/root/razor_witness`; read-only source audit: `/root/evidence_audit`;
координатор final validation/status: `/root`.

Verdict: **ACCEPT — изолированный исследовательский scope E0/task-02**.
Незакрытых must-fix в финальном коде и Findings supplement нет. Проверены
реальные mutation assertions, владение изменяемыми путями, compiler/TaskOutput
attribution, ограничения regions и совпадение хешей standard/Razor artifacts.
Coordinator Release build и main 1132/1132 passed. Проверки whitespace
`git diff --check` и `git diff --no-index --check` семи новых C# файлов прошли.

Приняты experiments, отрицательные findings и воспроизводимое evidence;
dependency closure, новый session-bound snapshot, полный graph/inner-TFM mapping
и positive equivalence этой приёмкой не подтверждаются. Положительные fixtures
task-03, production/public activation и переход к следующей эпохе остаются hold.

## Локальный inventory финального focused matrix run

Base locator: `C:/Users/VuDZ/AppData/Local/Temp/roslyn-dependency-spike/`.
Для каждого child ниже хеш относится к `artifacts/artifacts-manifest.json`;
он содержит hashes commands/stdout/stderr/raw binlogs и fixture bytes. Эти
артефакты не коммитятся, и их наличие после очистки temp не гарантировано.

- `standard-afb755179a774779b18f2b03b25c5214` — `E3339F187A445712D90B8DAE90225464FF9AF70BE6A4312B7BD2BEFC5A5074DE`.
- `absent-7e6cdc14984e4ef6870b0dff1118dca5` — `E451257BDBA1BD4880A5061A3A735ECCF9BE3365B658DBCB109978F2F89C64EE`.
- `custom-target-219b7cb26a6845ff8cefa39c2da37fdf` — `BAA832268669050D1498D1E5F11D671E1A898841B604B38C8DAE2C536FF6E35F`.
- `linked-b80456fb04c64c0e932ec605c8eb27b7` — `5EA35D4202B807BEBA47635D6711C4EE936B2D8945F99B8B721BBD38BC806371`.
- `explicit-analyzer-cdbf6b8cd80247268b97d4df673727a2` — `9B330229553385A1F757E66D308D78E754489102E407EDEC617229746DB3C411`.
- `web-8715707b24e448099371a7de78feecda` — `6340F8CAC1B3926F24EB6721F6496CE81AFC60742A4B60AFB706F4303CA3328B`.
- `external-asset-b018b675dc334590aeb276e91105a678` — `2DE1D10BEBBEE07A891C36F6FB61E721B83955ACF7C5FCFA27893E9798150539`.
- `escaping-glob-3402db3c89e14d54bc3cf6c40f040f3c` — `4716FFDDE0575A17E950D83EB860058165F2F223A701161647C08470861E8EB1`.
- `multi-target-14abf4140cc44add9caee319a85a0c17` — `3423362C56E0462F177DFCECDA261237648899904F1E909EF6181E4E2ADA2457`.
- `empty-a23003c07bba41cd82e58af8f36d7021` — `21D434D9E228DC52E6F1F5AFEE1468E9F3296CAAB00714BBDCFC41E6B5725B25`.
- `missing-reference-9cc328062992436ea2a052a66678fb42` — `AE730D7F01F2103A0C6A0B217FEE87C04762CAB2AF21BF2FC65FDDAF31764C24`.
- `non-sdk-ec1250149ab843a7a33ea0708506d0c6` — `EC6C160323C024A5B337068AE6A89CD743C106F70D02AF532C8F55C7E27A67FD`.
- `resolution-c4a69359e2804d3ca2b4d415cb3e98fa/artifacts/independent-resolution.json`
  — `6DBF57B83661325316F18DC38CCDBC35F269173158E63943AD6783A169D430EA`.
