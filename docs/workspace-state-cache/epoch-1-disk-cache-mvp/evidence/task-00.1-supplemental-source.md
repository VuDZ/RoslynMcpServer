# E1/task-00.1 — публичный supplemental collector

Дата: **2026-10-09**, Europe/Moscow. Итог: **partial source / insufficient reusable
capture**. Рекомендация — сохранить task-00 blocked: all-final-instance binding,
current restore applicability и P-004 не доказаны. Это завершённое bounded
исследование одного collector и retention-гипотезы; production adapter, store,
watcher, activation и изменение требований не входят в результат.

Исполнитель — Codex, текущая сессия; точный runtime model ID/reasoning не
подтверждены инструментами. Исходный HEAD `fc72dc80d330874ae5160950e972a8c8b4d67874`.
Existing dirty WorkspaceLoadCache и соседние docs не использовались как accepted
implementation. Основания: [E0/task-07](../../epoch-0-snapshot-roundtrip/evidence/task-07-benchmark.md),
[accepted S-002](task-11-recheck-validation.md), [spec](../spec.md),
[execution](../../execution.md). Source/ref/csproj реального контроля не менялись.

## Один сценарий и заранее выбранные наблюдения

Test-only [collector](../../../../RoslynMcpServer.Tests/WorkspaceStateCache/SupplementalManifestCollector.cs)
создаёт отдельный публичный `ProjectCollection`, по одному `Project` для двух
заранее назначенных consumers. Это не graph discovery algorithm для произвольных
requests. [Тест](../../../../RoslynMcpServer.Tests/WorkspaceStateCache/SupplementalManifestTests.cs)
переиспользует owned fixture S-002: unchanged SamplePlugin→RoslynMcpServer и
controlled App→Library с independent [expected inventory](task-00.1/expected.json).
Для каждого — ordinary open → supplemental evaluation → fresh ordinary open.
Caller request globals отсутствуют; кандидат явно назначает DesignTimeBuild,
BuildingInsideVisualStudio, SkipCompilerExecution, ProvideCommandLineArgs=true и
BuildProjectReferences=false. Эти globals — гипотеза контекста, не remote attestation.

До сравнения выбраны project paths/edges, все ordinary documents и text hashes,
additional/analyzer configs, defines/language version, output/optimization/platform,
unsafe/deterministic/nullable options, metadata paths/aliases/embed и project refs.
Семантический independent assertion: SamplePlugin либо AppControl symbol найден;
ordinary diagnostics пусты, public graph ровно два заранее ожидаемых проекта.
Два ordinary snapshots равны по выбранным observations; полнота остальных Roslyn
options/diagnostics и exact semantic equivalence этим не заявляются. Supplemental
evaluation не используется как oracle ordinary graph.

Focused actual run: PID **58760**, UTC **2026-10-09T14:39:44** (17:39:44 Europe/Moscow),
SDK **10.0.300**, MSBuild product **18.6.3+caa81fa4971f74880cdab61990cb1b11420939ec**,
Roslyn **5.9.0.0**. [Real JSON](task-00.1/real.json),
[controlled JSON](task-00.1/controlled.json), [controls](task-00.1/controls.json),
[unchanged-copy identities](task-00.1/real-copy-hashes.json). Temp/user paths sanitized.
Generic environment, property dumps, credentials и commands с secrets не сохранены.

## Source → category → path → owners

- **Root/referenced imports:** `Project.ImportsIncludingDuplicates` →
  `ResolvedImport.ImportedProject.FullPath`, с включённым
  `RecordDuplicateButNotCircularImports`. Consumer — containing candidate Project,
  identity — collection-local `LastEvaluationId` + project path + globals + evaluated
  Configuration/Platform/TFM. Controlled shared/nested/conditional/external imports
  найдены у **обоих** owners по independent expected inventory. Root и referenced
  candidate имеют IDs 3/6, не заимствуют друг у друга импортированный path.
  Real plugin/host имеют **150/151** entries. Это восполняет source gap root S-002
  для отдельной evaluation, но не связывает эти IDs с final BuildHost instances
  S-002 (включая позднюю dependent evaluation).
- **Project files:** explicit independent consumer paths, собственные owners;
  root XML не входит в Imports и должен учитываться отдельно. Same physical path
  и successful repeat open не доказывают same evaluation/consumption.
- **Source membership:** evaluated `Compile` → absolute path у каждого candidate.
  Controlled explicit `obj/Explicit.cs` и external `Explicit.cs` сохранены и
  видны в ordinary Documents. Generic фильтр obj не применяется. Real evaluation
  имеет ноль candidate-only Compile и по **три** ordinary-only generated sources
  у каждого owner: AssemblyAttributes, AssemblyInfo, GlobalUsings. Это фактический
  **membership mismatch**, не допустимая потеря.
- **Options/references:** allowlisted evaluated properties и `ProjectReference`
  дают source facts; candidate-only defines в исходных controls пусты. Это только
  одна сторона comparison: generated TFM defines и target-produced options не
  восстанавливаются автоматически. `ReferencePath` пуст у всех candidates;
  real ordinary имеет **226/236** metadata refs (controlled **167/167**). Поэтому
  target execution/actual resolved options и references остаются отдельным gap.
- **Assets:** evaluated `ProjectAssetsFile`, отдельно для каждого owner.
  Controlled redirected `intermediate/App|Library/project.assets.json` совпадают
  с independent expected inventory. Missing assets вызывает IOException;
  path property/existence не доказывают actual ordinary consumed bytes.
- **Locks:** evaluated `NuGetLockFilePath`, RestorePackagesWithLockFile/LockedMode;
  controlled индивидуальные locks существуют и совпадают с expected paths.
  Property не доказывает design-time lock consumption или полный current lock policy.
- **NuGet configs:** публичный JSON assets `project.restore.configFilePaths`
  содержит restore-time paths отдельно у каждого owner; controlled NuGet.Config
  найден. Новый `App/NuGet.Config` отсутствует в старом assets inventory;
  удалённый прежний config всё ещё указан. Это **stale restore provenance**, не
  current applicability. Текущий NuGet Settings API не запускался; его discovery
  могло бы дать current paths при заданном root/machine context, но не binding к
  прежнему restore или ordinary consumption. Completeness user/machine/ancestor/
  explicit config и known absence **не доказана**, presence assets её не заменяет.
- **Environment/SDK:** SDK/MSBuild paths/version взяты из allowlisted candidate
  properties. Ordinary remote environment/globals/SDK resolver results не attested;
  inherited caller process не считается доказательством их equality. Arbitrary
  environment-dependent conditions, TFM/reference metadata propagation, resolver
  inputs и multiple final instances одного path остаются unsupported для capture.
- **Absent/regions/custom:** false Exists import не входит в resolved Imports.
  Fixture знает его заранее; collector не умеет перечислять все potential imports,
  ancestor configs, glob regions или task File inputs. Controlled ReadLinesFromFile
  перед CoreCompile читает external custom.txt и меняет actual ordinary defines,
  но path/define отсутствуют в evaluation inventory. Универсальная task модель
  намеренно не добавлялась. Такой request требует whole-request ordinary route.

Публичный contract [Project.Imports](https://learn.microsoft.com/en-us/dotnet/api/microsoft.build.evaluation.project.imports)
ограничен реально импортированными files, исключая false-condition imports/root.
[Duplicate imports API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.build.evaluation.project.importsincludingduplicates)
сохраняет повторные relationships, но only-first evaluation не создаёт связь с
другим consumer. [NuGet Settings source](https://source.dot.net/NuGet.Configuration/Settings/Settings.cs.html)
описывает отдельное settings discovery, а
[configuration hierarchy](https://learn.microsoft.com/en-us/nuget/consume-packages/configuring-nuget-behavior)
объясняет зависимость current applicability от контекста. Это API основания
границ; installed-version observations находятся в JSON, не выводятся из latest source.

## P-004: минимальный механизм до consumption

Гипотеза: discovery → удержание existing inputs `FileAccess.Read/FileShare.Read`
→ fresh ordinary open. Проверен минимальный механизм, а не production capture:

1. Удерживаемый Directory.Build.props допускает ordinary чтение; запись и deletion
   получают IOException. Это ограниченный Windows protection existing path.
2. При удерживаемом props появляется ранее absent conditional import;
   новый ordinary open фактически видит NEW_INPUT define. Existing-file handles
   **не удерживают namespace/absence**. Также создаются source membership и
   `.editorconfig`; fresh ordinary graph включает оба. Новый ancestor NuGet.Config
   возможен и отсутствует в сохранённом assets inventory.
3. A hash → props B → ordinary consumption B → restore A **до первого post-load
   probe** даёт equal A pre/post hashes при CONSUMED_B в ordinary options. Отдельно
   A ordinary consumption → B mutation → A возврат до probe также даёт equality.
   Оба являются counterexamples к hash-only history proof; usable capture не создаётся.
4. Existing generated AssemblyInfo.cs можно удержать, но попытка writer получает
   IOException. Это actual file-sharing конфликт; forced ordinary regeneration
   под lock не запускалась и её конкретный failure outcome не утверждается.
   Разрешить writes означает потерять byte retention; запретить все writes без
   проверки ordinary compatibility нельзя. Generated target outputs требуют
   отдельной доказанной границы, которой этот collector не даёт.
5. Удаление config оставляет старый assets path; missing assets и zero path budget
   дают actual exceptions. IO/budget exceptions этого test-only probe не являются
   production fallback adapter. Unknown/custom/absence/generation gaps означают
   отказ от создания reusable capture; успешный ordinary request сохраняется.

В prototype **нет executable reusable capture/admission/store path**.
`UsableCapture=false` в evidence — отрицательный итог эксперимента, не тест
работоспособности production gate. Нельзя объявить весь набор failure controls
проверкой cache fallback, которого ещё нет. Newly discovered paths/regions не
подтверждаются post-load hash задним числом; bounded retry не устраняет namespace
ABA автоматически. Sufficient P-004 mechanism не выбран.

## Overhead и bounds

Один focused SamplePlugin measurement, без percentile/median claims:
ordinary open + выбранные observations **857.85 ms**, supplemental evaluations
**232.43 ms**. Supplemental timer включает import/item/allowlisted property и assets
JSON reading; не включает полноценную generation protection, повторный ordinary
open, snapshot envelope, binary identities, watcher, SDK scan и production response.
Warm in-process source overhead нельзя подставлять в new-PID E1 useful benchmark.

Owned copy bounds fixture: 2000 files / 128 MiB; candidate per-project
imports+Compile bound 4096, assets 16 MiB; scenario CancellationToken 3 минуты,
runner external timeout 240 s. Public evaluation синхронная и не принимает этот
token: это **не universal interruptible evaluation budget**. Arbitrary tasks не
исполняются collector; controlled ordinary custom target исполняется только в
owned negative fixture. При исключении test fails без portable capture. Общее
число configs/refs/projects, JSON depth и namespace exploration не bounded adapter
contract; пока есть только два фиксированных consumers.

## Решение и validation

**Source partial**: самостоятельный public API даёт root+referenced imported
inventory и полезные per-evaluation paths. **Insufficient** для manifest текущего
task-00 contract: final-instance context, restore applicability, target-produced
graph state и consumed generation остаются недоказанными. Реальный ordinary positive
есть; supported real manifest/capture positive **нет**. Task-00 остаётся blocked;
выбрать этот collector как production producer сейчас нельзя.

Independent review и final validation: [отчёт](task-00.1-validation.md).
Исследование может быть принято как bounded partial/negative outcome; это не
acceptance task-00 adapter, reusable envelope, всей E1 или public activation.
