# Task-02.1 — source-backed contracts и фактические controls

Дата: **2026-10-04**. Автор: `/root/dependency_remediation`. HEAD:
`d3c8d5241c1c1c5d95042ce75bc057b747284cf1`. Это проверенный source audit и частичные
selection controls, **не complete dependency closure, не Stage A acceptance**.
Owner choices A021-01/02/03 и D021-04/05 приняты; повторного approval они не требуют.
Write scope предварительно перечислен в [packet](implementation-packet-task-02.1.md).

## S021-01. Связь установленных bytes с exact source

Установленные SDK 10.0.300 resolver, manifest reader, Build.Tasks и SDK NetAnalyzers
имеют ProductVersion `10.0.300-servicing.26230.102+caa81fa4971f74880cdab61990cb1b11420939ec`;
Microsoft.Build — `18.6.3+caa81fa4971f74880cdab61990cb1b11420939ec`.
Primary source — [точный VMR commit](https://github.com/dotnet/dotnet/commit/caa81fa4971f74880cdab61990cb1b11420939ec),
а не соседний release tag. ProductVersion связывает заявленный revision; сам по
себе он не доказывает reproducible build. SHA-256 установленных binaries:

- WorkloadMSBuildSdkResolver: `822A3D03854E840088732E1E05760416E9D2AB0382CC40B493A623D7BB52DC25`;
- WorkloadManifestReader: `BE8372EB0AF5BC4A6ECAE341BA72122074B64B55457730392F720C827CFEE6A9`;
- Microsoft.NET.Build.Tasks: `1245836CB67F585CA0E1B8B0F7FFAED7158C34D3C6150009BC8324E541E85B7F`;
- Microsoft.CodeAnalysis.NetAnalyzers: `ED90BD01BEB19CA761FFEBCA25074E0AF0DA1EF1DC6957CC678CFE72239F670D`;
- Microsoft.Build: `D4D8ED40AEC5A6490D5A456303FE13B39F906CE6689A588A850BACF2742BC72A`.

Private raw root: `%LOCALAPPDATA%/Temp/task021-source-audit-681e7dbdf0894c70b930c7fa0fe0cbaf/`.
`source-manifest.json` сохраняет 49 exact source URLs, Git blob identities и локальные
SHA-256; hash manifest `6EC075DEB18EBD2F59E7DCBF7DB3BC4B363178FF12F3D997191EE7FF68E98B1A`.
Public source downloads только читались; upstream code не выполнялся.
Reviewer выявил collision двух InternalsVisibleToTests.cs artifact basenames; оба повторно
скачаны в component-prefixed unique paths с проверкой исходных SHA-256. Повторный
полный manifest recheck:49/49 hashes совпали,49distinct paths. Ошибка locator исправлена;
алгоритмические source files и выполненные selection outputs не изменились.

## S021-02. Workload manifest selection: конечная проверенная ветвь

Исходники exact revision:
[SdkDirectoryWorkloadManifestProvider](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/sdk/src/Resolvers/Microsoft.NET.Sdk.WorkloadManifestReader/SdkDirectoryWorkloadManifestProvider.cs),
[CachingWorkloadResolver](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/sdk/src/Resolvers/Microsoft.NET.Sdk.WorkloadMSBuildSdkResolver/CachingWorkloadResolver.cs),
[WorkloadInstallType](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/sdk/src/Resolvers/Microsoft.NET.Sdk.WorkloadManifestReader/WorkloadInstallType.cs),
[WorkloadSet](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/sdk/src/Resolvers/Microsoft.NET.Sdk.WorkloadManifestReader/WorkloadSet.cs).

Узкий control требует exact global.json SDK10.0.300/rollForward:disable, отсутствия
sdk.workloadVersion, workload environment overrides и userlocal marker, наличия MSI
marker, отсутствия installstate, workloadsets и primary sdk-manifests/10.0.300.
Он читает KnownWorkloadManifests.txt, membership более низких numeric feature bands,
каждый known ID directory, все numeric version directories и положительные/отрицательные
WorkloadManifest.json probes. Выбирается последний существующий manifest version
в наибольшем меньшем band; targets probe сохраняется и при отсутствии. Нет
разрешения произвольного NuGet/reference fallback: это штатный resolver algorithm.

Конкретные selectors текущей установки:

- `metadata/workloads/10.0.300/installertype/msi` существует;
- `metadata/workloads/10.0.300/userlocal` отсутствует;
- `%ProgramData%/dotnet/workloads/X64/10.0.300/InstallState/default.json` отсутствует;
- `sdk-manifests/10.0.300` и workloadsets в bands10.0.300/8.0.100 отсутствуют;
- selected manifests находятся в band10.0.100; known samsung.net.sdk.tizen не найден;
- отсутствуют DOTNETSDK_WORKLOAD_MANIFEST_ROOTS, DOTNETSDK_WORKLOAD_MANIFEST_IGNORE_DEFAULT_ROOTS,
  DOTNETSDK_WORKLOAD_PACK_ROOTS, DOTNET_CLI_HOME, MSBuildEnableWorkloadResolver в среде probe.

Запущен `probe-workload-selection.ps1 -OutputRoot <raw-root>`: exit0,
**16 selected manifests, 16 targets, 172 file/directory/absence probes, 0 target path
различий** с retained standard DTB None; повторная проверка файлов:0failures.
Это не test всего resolver. Directory memberships записаны, но script не делает
вторую membership enumeration; atomic/stability proof этим прогоном не заявлен.
Глубина/reparse ancestor/bounds и process environment discovery должны быть
строже в reusable implementation. Ambient probe environment не подменяет очищенную
среду existing discovery child process.

Control raw `workload-selection-probe.json` SHA-256:
`AF842B60501F7B2B12872D6ABECF14AB7238805EF3C44891CD62BED165DA2E86`;
script SHA-256 `F1BEEAB7B07CCBA55F0E8D5AAFC4602B2860B43C1A64463AF027009A3CB9CF27`.
Observed comparator: private standard-a33f58cb360a4ef8b42f9937dd0b1e0a/artifacts/
App-net10.0-dtb-None-observed.json. Compare был независимым выбором из current disk,
а не сравнением saved inventory с самим собой.

## S021-03. AutoImport и localization: установленный дополнительный input contract

[WorkloadResolver](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/sdk/src/Resolvers/Microsoft.NET.Sdk.WorkloadManifestReader/WorkloadResolver.cs)
Create читает NETCoreSdkRuntimeIdentifierChain.txt; userlocal и pack-root override
меняют порядок roots. GetInstalledWorkloadPacksOfKind(Sdk) сначала загружает и
компонует все selected manifests, проверяет dependsOn versions, duplicate pack/
workload IDs и redirects. Затем pack alias разрешается по порядку RID chain;
первый существующий `<root>/packs/<resolved-id>/<version>` выбирается в roots order.
CachingWorkloadResolver проверяет `<pack>/Sdk/AutoImport.props`, выдаёт distinct paths.
Значит отрицательные pack directory и AutoImport probes обязательны даже когда
в текущем обычном net10 проекте нет workload references.

[Localization reader](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/sdk/src/Resolvers/Microsoft.NET.Sdk.WorkloadManifestReader/WorkloadManifestReader.Localization.cs)
пробует localize/WorkloadManifest.<CurrentUICulture.Name>.json и цепочку Parent,
читает первый найденный. Discovery runner задаёт DOTNET_CLI_UI_LANGUAGE=en-US,
но reuse обязан явно сохранить свой culture contract и соответствующие negative
probes. S021-02 ещё не реализует этот алгоритм, composition или transitive XML
imports. Поэтому workload **полный closure остаётся unproven**, несмотря на0diff.

## S021-04. Framework pack reference selection: два выполненных controls

Точный источник:
[ProcessFrameworkReferences](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/sdk/src/Tasks/Microsoft.NET.Build.Tasks/ProcessFrameworkReferences.cs),
[ResolveTargetingPackAssets](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/sdk/src/Tasks/Microsoft.NET.Build.Tasks/ResolveTargetingPackAssets.cs).

Узкая ветвь: одна default Microsoft.NETCore.App KnownFrameworkReference из exact
SDK BundledVersions; net9.0 либо net10.0, installed default targeting pack, без
TargetingPackVersion override, package/download/root overrides. FrameworkList.xml
selects ReferencedByDefault!=false; managed files — ref/<tfm>/<AssemblyName>.dll,
analyzers — Type=Analyzer/Language=cs по Path. Normal reference path обязан
существовать; fallback path branch не используется и не разрешается. Хешируются
FrameworkList, выбранные DLL, PlatformManifest/PackageOverrides presence/bytes.

Запущен `probe-framework-selection.ps1 -OutputRoot <raw-root>`: exit0.
SDK BundledVersions hash `B7704392EEF04078CC5544B944A0E44350273B677F8CFDDEB34309F54A97874D`.

- net9.0: Microsoft.NETCore.App.Ref9.0.16, **164 references+6 pack analyzers**,
  independent path set comparison:0reference differences/0analyzer differences.
- net10.0: Microsoft.NETCore.App.Ref10.0.8, **167 references+6 pack analyzers**,
  independent path set comparison:0reference differences/0analyzer differences.

Comparator — отдельные exact inner instances existing MultiTarget evidence
oracle455eba496bd6496dac029b95ff1b63dc/evidence/admission/admission.json.
Raw `framework-selection-probe.json` SHA-256
`AFEFE7E45224192A6691B983B321F7251826EA5DA4B8E977C1C88EF7932D503A`;
script SHA-256 `ECCDB446BCD677BC7A140ED8D61863EFD2BB28566D848134C34AC7591D6F7B76`.

Это доказанный выбор paths в ограниченной ветви, **не restore closure**: NuGet
config hierarchy, assets/dgspec/generated imports, target parameter provenance,
metadata aliases/EmbedInteropTypes и все task inputs пока не закрыты.

## S021-05. Analyzer dependencies: конкретные несовпадения вместо trust shortcut

Read-only helper `/root/dependency_remediation/analyzer_source_audit` связал pack
binary informational revisions с exact primary source: net10 — dotnet/dotnet
`94ea82652cdd4e0f8046b5bd5becbd11461482ca`, net9 — dotnet/runtime
`a1e6809fb8318884882ceff057000654f558738a`. Raw root:
`%LOCALAPPDATA%/Temp/task021-analyzer-sources-b6c2dec58a51401b85ac2fa9018df2db/`.
`all-analyzer-identity-map.json`, `assembly-refs.json`, `external-memberrefs.json`
и `all-saved-source-hashes.json` сохраняют binaries/refs/source locators.

Шесть pack main DLL недостаточны: фактический inventory каждого pack — **71file**:
6main+65satellites (13 cultures×5 resource names). LibraryImport generator читает
build_property.LibraryImportGenerator_UseMarshalType и
build_property.LibraryImportGenerator_GenerateForwarders. Regex explicit culture
обращается к CultureInfo.GetCultureInfo; resource diagnostics — к ResourceManager/
CurrentUICulture/parent satellite probes. Host globalization switches, ICU/NLS
native selection не доказаны disk-only inventory этих71files.

Текущий isolated OwnedAnalyzerLoader принимает exact Name/Version/Culture/token
из TPA runtime и трёх Roslyn assemblies. Это не production host-catalog policy.
Реальные requests: SDK Roslyn3.11; JSON4.4; net9interops4.8; net10interops4.14,
тогда как host5.9. Requested netstandard2.0 vs host2.1; Composition1.0.31/7/9
отсутствует в trusted-set. Значит текущий loader не пригоден для positive control
без explicit reviewed adapter contract и реальных совместимость/identity tests.

SDK NetAnalyzers PE содержит реальные File.OpenRead/File.WriteAllText/Directory.Exists
и reflection MemberRefs. Helper локализует их guards в exact pinned source. Поиск
отдельных call names не является proof отсутствия остальных внешних чтений.

## Следующий конечный scope и текущие gates

Допустим limited implementation independent graph observations, framework/workload
selection и no-build recheck, если executable packet перечисляет exact types,
ownership, equality, limits, ошибки и tests. Эти controls не убирают unconditional
restore/toolset/analyzer unproven reasons, не создают Complete snapshot и не
разрешают task04 stages2–7. Они не закрывают M021-01/M021-10.

Missing proof остаётся конкретным: AutoImport/composition/localization+transitive
XML imports, restore task input closure, analyzer external reads/runtime dependency
contract и compatible isolated loader; exact binlog-to-inner association и fresh
base binding также ещё не реализованы. R02/R05 могут улучшаться независимо, но
current packet не должен обещать полный graph/import proof там, где сохраняется
unresolved evaluation. Razor/Web остаются negative по принятому D021-05.


### S021-05a. Завершённый read-only helper audit

142pack files manifest SHA-256
`9B793E98DB02F5C364E3AE0442DBF9BBE47F777AE0A9B9CF6134356D9DD6C8C3`;
14main DLL demanded-to-actual identity map
`D48DA86185F4503C8CEC25B95014462CF1D1F4D957553B99811A9395FCF91B3B`;
900exact SDK source/project files manifest
`92FEAFC4D82CA55B66F7D4BA9A9EBC9A6D44087C83549554008362AA1BE15DB4`.
Source tree1164entries, truncated=false; 239pack/runtime sources сохранены отдельно.

IL связывает подозревавшиеся File/Directory calls SDK analyzer с utility
[RulesetToEditorconfig.Converter](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/sdk/src/Microsoft.CodeAnalysis.NetAnalyzers/src/Utilities/Compiler/RulesetToEditorconfigConverter.cs).
В остальных сохранённых production sources его вызовов не найдено. Поэтому это
**не доказанный analyzer-entrypoint I/O blocker**, но исключение utility из reachable
callback graph ещё требует proof. Нельзя считать grep отрицательный результат
exhaustive closure.

[CodeMetricsAnalyzer](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/sdk/src/Microsoft.CodeAnalysis.NetAnalyzers/src/Microsoft.CodeAnalysis.NetAnalyzers/Microsoft.CodeQuality.Analyzers/Maintainability/CodeMetricsAnalyzer.cs)
выбирает первый AdditionalFiles с exact case-sensitive basename CodeMetricsConfig.txt
и вызывает supplied GetTextOrEmpty: directory discovery здесь отсутствует. Поэтому
ordered AdditionalFiles — реальный вход, и set-only equality недостаточна для этого
callback, если разрешены дублирующиеся basenames.

Reflection сузилась до host light-up: SyntaxTreeCategorizedAnalyzerConfigOptions
поля _backing/_analyzerOptions; AnalyzerConfigOptionsProviderExtensions поле _treeDict;
OperationWrapperHelper три named IOperation interfaces в typeof(SyntaxNode).Assembly;
NullableSyntaxAnnotationEx fixed Workspace type/двух полей; DiagnosticExtensions
host properties/method. [LightupHelpers](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/sdk/src/Microsoft.CodeAnalysis.NetAnalyzers/src/Utilities/Compiler/Lightup/LightupHelpers.cs)
компилирует expressions для этих host methods. Installed Activator call принадлежит
generic BoundedCache<TValue>.CreateDefaultValue (`new TValue()`), не arbitrary path.
Actual host identities/member shapes поэтому должны быть частью future contract.

Один текущий OwnedAnalyzerLoader на snapshot создаёт дополнительный mixed-TFM seam:
net9/net10 mains имеют одинаковые simple names с различными versions. Один ALC
не обеспечивает две независимые версии на simple name. Для positive MultiTarget
нужны проверенные отдельные consumer/closure loaders и lifetime либо доказанная
унификация; partial L021-1 этого не меняет и positive overlay не открывает.

Consumer-specific узкий positive contract может отвергать explicit Regex culture,
Interop/JSON attribute candidates и неизвестные additional/config values, но само
отсутствие attribute-name strings не proof: нужны semantic symbol identities,
полный исходный compilation/analyzer inputs и unreachable callback evidence.
Для неизменённых ProjectGraph fixtures generators не обязаны выдавать source;
SDK diagnostic analyzers всё равно остаются reachable. Positive generator witness
с invariant regex — отдельный разрешённый owned experiment после exact packet,
не task04 pipeline и не изменение текущих golden files.


### S021-03a. Выполненный AutoImport control

После S021-02 выполнен private `probe-workload-autoimport.ps1 -OutputRoot <raw-root>`:
exit0,16manifests,292pack definitions,86workload definitions,89SDK packs после RID
alias selection,28существующих SDK pack directories,12AutoImport.props,5selected
localization catalogs,165additional input probes,0file recheck failures. Проверены
manifest depends-on numeric version requirements и duplicate IDs; redirect branch
отказывается. en-US/en localization chain фиксирована как research assumption,
соответствующая задаваемому DOTNET_CLI_UI_LANGUAGE; actual child CurrentUICulture
отдельно не наблюдалась. Это ограничение control, а не доказанный culture mapping.

Независимо выбранные12AutoImport.props сравнены с12retained standard DTB None imports:
0path differences. Raw SHA-256:

- workload-autoimport-probe.json: `53DE75A23FF5532C110842543B0B5D760238A261C1C992E8B0103709560B14C6`;
- probe-workload-autoimport.ps1: `DDF4EDE51F3A15847487B0ECEF6C3E0E015241F752E58B17D8B756CC7E23414F`;
- workload-autoimport-comparison.json: `3EED54BCEB23ED05E173F212EDE62E9A0094F8FE5B6B3DD8E99D754F5F9D04D8`.

Все лежат в ранее указанном source audit root. Membership/ancestor stability и
transitive XML conditions/imports не доказываются этим script. Код L021-1 не
включает workload selector; scope accepted packet не расширен.

### S021-05b. Уточнение consumer execution surface

Helper проверил exact Roslyn source revision35d9211b841e7613c1d2f8f5af6d628ace696c4c,
совпадающую с установленным Roslyn ProductVersion. AnalyzerFileReference.GetGenerators
сначала metadata-only строит GeneratorAttribute map и при отсутствии generators
для language возвращается до GetAssembly. SDK pair имеет0GeneratorAttribute.
SolutionCompilationState_SourceGenerators/GeneratorDriverInitializationCache используют
GetGenerators(language), AdditionalDocuments, ParseOptions и AnalyzerConfigOptionsProvider.
Текущий isolated observer вызывает GetCompilation/GetSourceGeneratedDocuments,
а затем намеренно отказывает при любых AnalyzerReferences. DiagnosticAnalyzer
callbacks SDK pair не следует приписывать этому operation graph.

Это пригодный факт для будущего consumer-specific adapter, **не полная analyzer
closure**: SDK pair остаётся в compiler/DTO/provenance/metadata inventory; будущий
WithAnalyzers/GetAnalyzerDiagnostics scope потребует нового callback closure.
Raw roslyn-AnalyzerFileReference.cs SHA-256
`B3F65D98A65649BF536265F67AC0D2F2A34BEE40968C3C12AF20647F53F59AD6`;
roslyn-workspace-source-hashes.json содержит exact source locators. Code L021-1
этот fact не использует для открытия admission или изменения observer.

Pack source audit теперь нашёл четыре именованных global option booleans: две
LibraryImport options, build_property.EnableComHosting и
build_property.EnableGeneratedComInterfaceComImportInterop. Missing/unparseable
часть guards должна воспроизводиться точно; весь GlobalOptions остаётся входом.

Regex named-culture branch требует одновременно effective IgnoreCase и nonempty
cultureName, а CultureInvariant conflict диагностируется до lookup. Invariant ASCII
case без этих guards исключает конкретный CultureInfo.GetCultureInfo path, но
StringWriter constructor generator читает CurrentCulture. Полная native/resource/
host culture closure этим не доказана. Pack/runtime source manifest v2:259files,
SHA-256 `8401D25E02BDDF39BC46F02E91964F8AA872784C9E81B9FCC4B9D66BF2496037`.

## S021-06. Exact ProjectGraph restore research

Создана marker-owned temp копия **неизменённых** ProjectGraph literals. App/Lib
csproj и двух .cs SHA-256 совпали с historical main comparison4/4. Global pin и
clear-feeds NuGet.config имеют exact builder bytes. Repo/retained fixtures не
менялись; research marker и отдельный culture.proj являются harness augmentation.
`dotnet restore <App.csproj> --configfile <NuGet.config> -v:diag` вне measured paths
завершился exit0 с той же очищенной environment allowlist, что discovery runner.

Raw root внутри source-audit:
`projectgraph-owned-836a42ef5bea4f11b20c831a165dd90a/`; stdout/stderr/command.json и
оба generated restore outputs сохранены. `projectgraph-owned-restore-audit.json`
SHA-256 `DF3D77F02F66010399E66E0F6983FCF1DEC1DDE49B08DCD38252EC51FDD05049`.

Actual schema assetsVersion4: App содержит только Lib/1.0.0 type=project;
Lib libraries пусты. Обе project.restore.configFilePaths содержат ровно owned
NuGet.config; packageFolders всё ещё указывает default user NuGet directory.
Наличие packageFolders не доказывает package filesystem reads; package library
на этом запросе отсутствует. Сохранены assets/dgspec/generated nuget imports,
ancestor positive/negative .git, NuGet.config casing, Directory.Build props/targets,
Directory.Packages.props/global.json probes. Historical fixture obj удалены existing
cleanup; прежняя попытка чтения помечена NotRun, не empty inventory.

Exact installed NuGet.ProjectModel/NuGet.Configuration ProductVersion
7.6.0-rc.23102+caa81fa...; MSBuild.Tasks.Core18.6.3+caa81fa...;
Microsoft.Build.Tasks.Git10.0.300+caa81fa... — тот же pinned VMR revision.
`restore-task-binary-identity.json` сохраняет hashes. Дополнительные7exact source
files имеют unique path-based locators в restore-source-manifest.json.

[ResolvePackageAssets](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/sdk/src/Tasks/Microsoft.NET.Build.Tasks/ResolvePackageAssets.cs)
читает binary cache (format12, settingsSHA256) либо assets. Cache freshness использует
mtime относительно assets, malformed/settings mismatch rebuilds cache. No-build
resolver должен независимо разобрать assets/selection и проверить cache-derived
observed outputs, а не доверять hash/cache timestamp. DesignTimeBuild может получить
empty target при отсутствующем TFM; это отказ для нашего profile, не proven empty.
[LockFileCache](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/sdk/src/Tasks/Microsoft.NET.Build.Tasks/LockFileCache.cs)
имеет build-lifetime in-memory cache; fresh discovery начинается в новом child.
[NuGetPackageResolver](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/sdk/src/Tasks/Microsoft.NET.Build.Tasks/NuGetPackageResolver.cs)
создаёт ordered package-folder resolver; отсутствие package library ещё требуется
связать с недостижимостью package asset resolution loops.

Actual standard DTB RAR task parameters: FindDependencies/FindSatellites/
FindRelatedFiles/FindSerializationAssemblies=false. Это существенно сужает reads;
SearchPaths, StateFile и compiler metadata comparison всё ещё требуют контракта.
GetFrameworkPath и SourceLink LocateRepository реально запускаются, поэтому
наблюдённые SDK/pack файлы не являются полным toolset-input proof.

Отдельная property-only culture probe завершилась exit1/MSB4185: стандартная
MSBuild allowlist не разрешает CultureInfo.CurrentCulture getter. Никакого
MSBUILDENABLEALLPROPERTYFUNCTIONS или другого gate bypass не включалось. Это
честный negative research result, не culture observation. Explicit ambient
CurrentCulture/CurrentUICulture для future host contract пока не захвачены.

### S021-06a. Граница restore control и SourceLink no-repository branch

Explicit `--configfile` — augmentation research control. Один configFilePaths в
его assets не доказывает, что implicit restore первоначального capture request
выбирает только тот же config. Для admission нужны independently выбранные
implicit hierarchy/selector inputs либо доказанная equivalence этого exact request.
Совпадение package-free outputs не заменяет эту часть proof.

Exact `Microsoft.Build.Tasks.Git.dll` ProductVersion10.0.300+caa81fa..., AssemblyVersion
10.0.14.23102, token31bf3856ad364e35, SHA-256
`D4581D3E582EC1FD1F09F5545C5388BBC0A8AFFA365B71B897900A5C345250BF`.
[LocateRepository](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/sourcelink/src/Microsoft.Build.Tasks.Git/LocateRepository.cs#L53)
передаёт initial `MSBuildProjectDirectory` и null RepositoryId.
[GitRepository.TryFindRepository](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/sourcelink/src/Microsoft.Build.Tasks.Git/GitDataReader/GitRepository.cs#L359)
идёт от initial directory до filesystem root: проверяет `.git` как directory и
file, существование самого directory, затем `HEAD` для bare repository. Отсутствие
только `.git` недостаточно. При absent HEAD `IsGitDirectory` возвращает false до
чтения commondir. [RepositoryTask](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/sourcelink/src/Microsoft.Build.Tasks.Git/RepositoryTask.cs#L115)
при false завершается до repository-cache, GitEnvironment.Create/OpenRepository,
gitconfig/SSH/PATH/git.exe/network discovery. NoWarnOnMissingInfo=true дополнительно
исключает localized missing-repository warning; это supplied input, не assumption.

Owned App/Lib control:18rows/10unique ancestor directories, каждый существует,
`.git` file/directory и `HEAD` file отсутствуют. Эти exact positive/negative probes
должны входить в future independent no-build re-resolution и mutation witnesses.
Это source-backed branch contract; actual probe implementer пока не добавлен.

Translation task не является no-op: supplied Hosts.ItemSpec всегда разбирается
как URI authority, RepositoryUrl проходит Uri.TryCreate. При пустом URL provider
URL override не вызывается. SourceRoots перечисляются: сначала SourceControl,
для git root — также ScmRepositoryUrl. Finite inputs включают Hosts, RepositoryUrl,
IsSingleProvider, ordered SourceRoots metadata; provider-count task читает supplied
ProviderTargets. Default valid hosts github.com/gitlab.com/bitbucket.org и
visualstudio.com/vsts.me/dev.azure.com не требуют network. Actual Hosts/SourceRoots
DTB parameter values ещё не сопоставлены, поэтому категория не объявлена complete.

Raw helper directory:
`C:/Users/VuDZ/AppData/Local/Temp/task021-analyzer-sources-b6c2dec58a51401b85ac2fa9018df2db/`.

- sourcelink-owned-no-repository-probes.json SHA-256 `9145D9F9D939043CB5C683B6BA8FECCFFD06566481E48FA4BA6B249E77A223E4`;
- sourcelink-source-hashes.json:86pinned files, SHA-256 `A1427350C26C51F6CECB13EE59688FD91F1EC631DDF24839F99F32E067ACB6E8`;
- sourcelink-installed-binary-identity.json SHA-256 `AA3C8F9ACC9FFA87674ECBB66D94A0067E8B0D11B237921A8870088E4ACE8DFD`;
- sourcelink-installed-targets.json сохраняет hashes реально установленных props/targets.

Source bytes получены по exact primary HTTPS revision; web renderer cache misses
не трактуются как отсутствие source. Assembly execution/helper builds не выполнялись.

### S021-06b. Implicit restore и GetFrameworkPath selection

Повторный owned control использовал exact ProjectGraph literals и обычный
`dotnet restore <App.csproj> -v:diag`, без explicit --configfile. Exit0. Raw root:
`projectgraph-owned-f15f523afdac4f01bbd8142a9ddb949a/`; aggregate
projectgraph-implicit-restore-audit.json SHA-256
`D0E63E4B07CDBFAF463ABCB06FFD433328DF4D02CFE70E5F892583859552D01E`.
Оба assets выбирают4configs: owned NuGet.Config, user NuGet.Config и два machine
Microsoft.VisualStudio.FallbackLocation.config/Offline.config. packageFolders
содержит user root и VS Shared NuGetPackages; restore.sources содержит SDK
library-packs. App имеет только project Lib, Lib libraries пусты. Configured fallback
не равен фактическому fallback asset selection: последнюю запрещает approved scope,
а недостижимость package loops и required existence probes доказываются отдельно.

Observed standard DTB RAR SearchPaths ровно CandidateAssemblyFiles,
HintPathFromItem, TargetFrameworkDirectory, RawFileName;167framework inputs
ExternallyResolved=true, HintPath не выведен как произвольный search root.
FindDependencies/FindDependenciesOfExternallyResolvedReferences/FindSatellites/
FindRelatedFiles/FindSerializationAssemblies=false, Silent=true; StateFile всё равно
obj/Debug/net10.0/App.csproj.AssemblyReference.cache. Cache validation/metadata
resolution остаются отдельными guards. Task output path match не заменяет их.

GetFrameworkPath в _SetEmbeddedWin32ManifestProperties запрашивает только
FrameworkVersion40Path; Execute сам no-op. Exact source
[ToolLocationHelper](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/msbuild/src/Utilities/ToolLocationHelper.cs#L1699)
переходит в [FrameworkLocationHelper](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/msbuild/src/Framework/Utilities/FrameworkLocationHelper.cs#L1419).
Metadata-only IL audit actual SDK Microsoft.Build.Framework.dll подтверждает:
FEATURE_WIN32_REGISTRY CheckForFrameworkInstallation call здесь не compiled.
Current architecture, absent COMPLUS_INSTALLROOT/COMPLUS_VERSION из cleared child
environment, observed runtime10.0.8 и Windows prefix v4.0 приводят к одному
Directory.Exists(runtimeParent), затем GetDirectories(runtimeParent,"v4.0*").
Actual parent Microsoft.NETCore.App существует, matching directories0 => null
до candidate MSBuild.exe/Microsoft.Build.dll checks. Именно эта bounded negative
membership, current runtime identity/path и COMPLUS absence нужны fresh resolver.
Это не разрешение доверять runtime directory целиком. Subsequent SDK target
Exists(default.win32manifest) при пустом prefix тоже требует request-drive probe;
research captured C:/default.win32manifest absent, не universal absence для любого cwd.

Raw getframework-path-negative-probes.json SHA-256
`5EB384C719B70E9C0E722B32AD035AC6691DCEF31C99101349F88DC5D1474F9D`;
framework-location-source.json SHA-256
`599594BB8909BD0E0A065D9FBA52E81F736383C883AFB2C8CBB81A5AC40C2143`;
getframework-helper-source.json SHA-256
`717AF976642A47C49A41C48FBDDAA90405B59492BD6B3F1CCA393B828A3D5B96`.
Первая guessed Shared/FrameworkLocationHelper URL вернула404; exact Framework/Utilities
path найден по pinned tree, другой branch/tag не использовался.

### S021-03b. Startup UI-culture source вместо assumption

Exact SDK [Program.InitializeProcess](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/sdk/src/Cli/dotnet/Program.cs#L437)
вызывает [UILanguageOverride.Setup](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/sdk/src/Cli/Microsoft.DotNet.Cli.Utils/UILanguageOverride.cs#L19).
Valid DOTNET_CLI_UI_LANGUAGE=en-US задаёт DefaultThreadCurrentUICulture=en-US;
VSLANG/PreferredUILang выставляются только если отсутствовали. CurrentCulture при
этом не меняется. MSBuild SetConsoleUI повторно выбирает external UI override или
current console fallback; для en-US terminal encoding fallback также en-US.
[EncodingUtilities](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/msbuild/src/Framework/EncodingUtilities.cs#L267)
действительно читает Windows CurrentBuildNumber registry и OS version для UTF8
support. Независимо от этих branches итоговый UI-language candidate остаётся en-US
при доказанном preceding CLI initialization; это не основание скрыть platform
read или считать общий CurrentCulture установленным. In-proc CLI forwarding и
передача culture в build nodes всё ещё должны входить в executable contract.

Raw culture-startup-source-manifest.json SHA-256
`9C170C7D6A9535D44FC9D5A181E87131E5E200B77329F9658946E3200E6058C6`;
encoding-culture-source.json SHA-256
`34F89636F0953325B51C999BA0B5B9DE08009BEE253EBDFE61FD7AE7E90942DE`;
culture-framework-binary-identities.json (4installed binaries) SHA-256
`73A1DC8A5706EC5DE9B1EE0E33E6208D460E40226D1C5FC7E700671BCB84B46F`.
Текущий autoimport research всё ещё selection-only: его прежний en-US assumption
стал source-backed startup candidate, но all-input closure/host culture contract
этим автоматически не завершены.

### S021-06c. Доказанный package-free selection guard

Для actual f15f assets helper exact source audit доказал: WriteAnalyzers/WriteItems/
WriteDebugItems/WriteFrameworkAssemblies проверяют IsPackage до resolver и filesystem
asset operations. Единственный App Lib имеет type=project, Lib libraries пусты.
WritePackageDependenciesDesignTime допускает только Package/Unresolved, поэтому
Project пропускается. Direct Lib присутствует в dependency group и не выдаётся
как transitive project. .NETCoreApp major>=3 исключает shim apphost branch.
RuntimeIdentifier и ExpectedPlatformPackages должны быть captured пустыми для
соответствующих ранних exits; одних csproj literals недостаточно. Эти guards
исключают main/fallback package-file selection, включая .sha512/.nupkg.metadata,
resource locale/PDB/XML probes. Configured fallback остаётся лишь directory-exists
input этого request; actual fallback asset selection по-прежнему запрещён.

Cold CacheWriter создаёт resolver из lockFile.PackageFolders до target matching:
Directory.Exists user root и каждого fallback вызываются даже при0packages.
Absent user root допускается; absent fallback throws PackagingException. Actual
оба существуют. VersionFolderPathResolver constructor только сохраняет root/case;
Windows case-insensitive path branch исключает Unix filesystem case probe.

Warm PKGA cache нельзя игнорировать: mtime>assets mtime, version12 и settings SHA
могут позволить не читать assets/resolver вообще; settings hash не включает assets
content и DesignTimeBuild. BuildEngine registered LockFileCache также может скрыть
повторное чтение. No-build closure обязан самостоятельно разобрать assets и
сопоставить cache-derived outputs либо доказать cold-state provenance. Valid header
с corrupt body не обещает rebuild. Current owned obj не содержит *.assets.cache;
это не доказывает warm state будущего build/DTB.

JsonPackageSpecReader.ReadMSBuildMetadata безусловно читает
NUGET_ENABLE_EXPERIMENTAL_MACROS и NuGetEnvironment UserSettingsDirectory; actual
NuGet.Common IL подтверждает Windows/CoreCLR APPDATA branch. Cleared child runner
сохраняет APPDATA и исключает macro env; current audit shell observation отдельно
не выдаётся за child observation. URI classification PackageSource не выполняет
network/source read. Полный parser/host primitive contract ещё исследуется.

Raw helper subdirectory resolvepackage-bc326a38213d418e86a634627b739747:

- all-source-manifest.json SHA-256 `7062820C76011E847F41C473BD1EB5D1172ACE2EC0BB774548BBB53A1F75D9E8`;
- actual-implicit-assets-inputs.json SHA-256 `244CB77A3BBFF8E5FA8D2033E12D7BE1404561C1D0E6EC08567B232875C57935`;
- selected-framework-nuget-il.json SHA-256 `D01DC3CE16C291412609A5062A645A8AC140AD2D9890472C08E9A65B054A240C`.

### S021-07. Конечный import frontier, пока без независимого condition proof

Retained standard App net10 DTB None содержит219import events:142positive и77
negative. Диагностический standard-dtb-import-frontier.json хранит exact original
observation hash, positive file identities/hashes и все negative unexpanded imports
с evaluated conditions. SHA-256 `8B063F9187B01ADBE585A334E12624C7A17EDC55171A6244F750B04B8FFC4C18`.
Это конечная поверхность аудита, не independent evaluator: прочитать evaluated
condition из diagnostic Message недостаточно для admission. Source-backed contracts
ещё нужны для user/SDK ImportBefore/After wildcards, ancestor Directory.Build/
Packages props lookup, custom before/after extension paths, PublishProfile,
workload negative target switches и explicit SDK analyzer disabling import absence.
Historical cleanup не превращает unavailable positive import в negative observation.

### S021-08. Точный SDK-evaluated producer двух DLL

[Microsoft.NET.Sdk.Analyzers.targets](https://github.com/dotnet/dotnet/blob/caa81fa4971f74880cdab61990cb1b11420939ec/src/sdk/src/Tasks/Microsoft.NET.Build.Tasks/targets/Microsoft.NET.Sdk.Analyzers.targets#L123)
содержит ItemGroup Condition=EnableNETAnalyzers и два применимых C# item definitions:
CSharp.NetAnalyzers (дополнительный Language=C#) и общий NetAnalyzers. Пути строятся
от MSBuildThisFileDirectory к соседнему analyzers directory; оба metadata
IsImplicitlyDefined=true. VB item при Language=C# неприменим. Это defining producer,
а не TaskOutput. Installed bytes SHA-256
`E1C018A1131AFC08C590B7D699600112E2A524656EC1CEC47E1B15824B08C803`;
pinned source SHA-256 `D5442AD54DBC954440FB344ECEB5AE8B3C61CF76971C1B6943E14BEFCD00098E`.
Normalized LF text совпал целиком; raw source/installed hashes различаются из-за
line endings. Future evidence сохраняет actual installed hash, не подменяет source hash.
Raw sdk-analyzer-producer-source.json сохраняет сравнение.

Standard DTB actual AnalysisLevel=latest, EffectiveAnalysisLevel=10.0,
EnableNETAnalyzers=true. Global config target выбирает
analyzers/build/config/analysislevel_10_default.globalconfig посредством точных
RulesVersion/AnalysisMode/WarningsAsErrors guards и Exists. Imported
Microsoft.CodeAnalysis.NetAnalyzers.props дополнительно проверяет отсутствие
DisableNETAnalyzersForNuGetPackage.props; его негативный filesystem selector
обязателен. Raw sdk-analyzer-producer-inputs.json с3installed XML inputs SHA-256
`67732E2D614A985CBB76ECC8A7EE597EAC5050E227DBCC234428C72EB1571899`.

Этот producer contract закрывает происхождение конкретных item definitions, но
не доказывает отсутствие последующего Analyzer Include/Remove/Update из остальных
imports/targets или task mutation. Exact consumer evaluation association, guards,
all-input condition proof и equality с compiler8/DTO8 остаются обязательны. Новые
TaskContext IDs для этих двух DLL не создаются.

### S021-06d. Parser success contract до явно названных host services

Helper завершил finite source-visible chain для exact f15f assets sections:
LockFileFormat.Read(All) → JsonUtility.LoadJson → linked NuGet.Shared.Utf8JsonStreamReader
→ fixed Utf8JsonStreamLockFileConverter/TargetV4/TargetLibrary/Library/DependencyGroup/
PackageFolders converters. Reader читает только переданный assets FileStream через
16KiB pooled buffer; serializer reflection/plugin dispatch отсутствует. Frameworks
net10.0 и семь compatibility imports выбираются из fixed common-framework constants;
DefaultFrameworkNameProvider eagerly создаёт built-in mappings без внешней
provider registration. Project version1.0.0,272prune references с10distinct upper
ranges проходят fixed NuGetVersion/VersionRange parser. runtimeIdentifierGraphPath
сохраняется строкой; RuntimeGraph создаётся из двух empty enumerables, RID файл
parser не открывает. PackageSpec GetTargetFramework использует exact alias lookup,
а не GetNearest fallback. Sources становятся PackageSource/Uri metadata без fetch.

Source-visible external semantic reads success branch: supplied assets stream,
NUGET_ENABLE_EXPERIMENTAL_MACROS, Windows OS predicate и cached UserSettingsDirectory
через APPDATA на cache miss. Все эти values должны быть captured/revalidated.
Invalid JSON/TFM/version/logging resource branches не объявляются covered success;
изменение bytes/branch выводит request за exact proof и требует refusal.

Предложенный explicit host boundary, пока не accepted positive packet: actual
runtime10.0.8 CoreLib/System.Text.Json identities; Utf8JsonReader fixed token APIs,
FileStream.Read/Dispose, ArrayPool<byte>.Shared/Rent/Return, strings/collections,
Version/Enum parsers, Lazy/Monitor, environment/OS query primitives. ArrayPool не
объявляется «pure allocation»: EventSource initialization и Gen2 GC trim callback,
TickCount/memory pressure/ProcessorCount и два DOTNET_SYSTEM_BUFFERS_SHAREDARRAYPOOL_*
settings входят в runtime service semantics. На уровне audited NuGet source они
не выбирают дополнительный semantic package/config input; будущий host contract
обязан явно закрепить эту границу и runtime identity, а не доверять installation.

Raw parser-contract-7fe1c73403524ad99f9a1f5853618538 под helper resolvepackage root:
all-sources-v1.json SHA-256 `45E8E332EA9A82D260FB3E0C526D85A2E5DEE90285434364BF6667E458DD65B0`;
installed-parser-host-identities.json SHA-256 `467BA9BF74EF69D94A7500AB5E7F2D55BAA54500027268C2AE02B8738B343238`.
Actual CoreLib SHA-256 `5B6700E2802AFDB2CC4863B096760A66442E89FD6EE5EDB718C697AE5CF33DDC`;
System.Text.Json SHA-256 `D196448CA8C8DD3A4F0360F8F90602ED6B0044245B4613D52F0B3391B9D897B5`,
runtime ProductVersion source revision94ea82652cdd4e0f8046b5bd5becbd11461482ca.

### S021-09. Actual ProjectGraph task frontier и простые task guards

Standard source audit не подменяет actual ProjectGraph. Retained prior coordinator
main root f8973e6e03e242c4b8668a0b42d3b461/evidence/admission содержит
instance-0-App-net10.0-dtb-None.stdout.txt и instance-1-Lib-net10.0-dtb-None.stdout.txt.
App:45task calls/33distinct; Lib:40/29. Sourceaudit projectgraph-dtb-task-frontier.json
SHA-256 `5B84CFAF8397AC6522F5D9EBBC17B87AF3EB37A8396B172E2AE3DF49288B5E44`.
App дополнительно вызывает AssignProjectConfiguration, CombineTargetFrameworkInfoProperties,
CombineXmlElements, GetReferenceNearestTargetFrameworkTask. Observed nearestnet10.0
совпадает с единственным Lib target; supplied default fallbackTFMs неточные target
не выбирают автоматически. Actual selection source guard ещё требуется.

12exact pinned simple-task sources сохранены в projectgraph-simple-task-sources.json,
SHA-256 `F0281A7AA764991670160C934E942E0D77FAA272A24524B584DAB9836EB3B832`.
Проверенные локальные contracts:

- FindAppConfigFile сканирует supplied PrimaryList/SecondaryList в обратном порядке,
  сначала whole ItemSpec, затем basename app.config; сам не открывает filesystem.
  Empty lists исключают matching/resource logging branch. Источник этих lists
  (default None/Content regions) всё равно должен быть доказан.
- GetPackageDirectory при empty Items возвращается до resolver. Для5framework
  items с уже заданным PackageDirectory пропускает package lookup; resolver перед
  циклом всё же создаётся, поэтому PackageFolders existence остаётся input.
- SetRidAgnosticValueForProjects при HasSingleTargetFramework=true возвращает item
  без изменения. CombineTargetFrameworkInfoProperties и CombineXmlElements работают
  с supplied generated XML/metadata; actual valid XML не содержит DTD/external IDs.
- Hash использует ordered ItemSpec+U+2028, UTF8 и SHA256, metadata не участвует;
  IgnoreCase переключает ToUpperInvariant. Это input-string hash, не чтение файлов
  по ItemSpec. Runtime crypto/ArrayPool остаются explicit host services.
- ResolveAppHosts сначала фильтрует supplied KnownAppHostPacks по exact framework;
  empty AppHostRuntimeIdentifier и empty PackAsToolShimRuntimeIdentifiers исключают
  GetHostItem/runtimegraph/pack-directory branches. Значения должны быть captured
  как task parameters, не только угаданы из library OutputType.

Remaining существенные task guards: ResolvePackageFileConflicts platform inputs,
RAR cache/identity; graph configuration/nearest-framework source; Csc/GenerateMSBuildEditorConfig/
WriteLinesToFile exact read/write surfaces и tool/runtime host mappings. Эти notes
не удаляют ни один current unproven reason.
