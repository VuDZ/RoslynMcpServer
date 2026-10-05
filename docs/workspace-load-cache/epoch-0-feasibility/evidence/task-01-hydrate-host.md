# E0/task-01 — hydrate host experiment

Дата: 2026-10-03 (Europe/Moscow). Статус: **accepted for E0/task-01 isolated
experiment; final validation passed**.
CI prerequisite correction: **validated locally, 2026-10-05**; новый запуск
исправленного workflow на GitHub ещё не выполнен (см. supplement ниже).
Изолированный эксперимент по [task-01](../task-01-hydrate-host-spike-Sol.md)
и [принятому packet](../design/implementation-packet-task-01.md).

## Provenance и границы

- HEAD: `a7eaf9a7bb4d3c5b5fbb53c149abe88759c06251`; production baseline
  совпадает с audited code task-00: последний commit изменяет только документы.
- Historical pin: `9867318ddb5294ce144bf024b9a61a1a2e3814c3`, source 1.3.21.
- SDK: 10.0.300; MSBuild: 18.6.3+caa81fa49; runtime: 10.0.8;
  Windows 10.0.26200, win-x64. Roslyn package pin: 5.9.0.
- Running MCP: 1.5.4.0, binary
  `E:\Devel\RoslynMcpServer\bin\Release\net10.0\win-x64\publish\RoslynMcpServer.exe`,
  modified 2026-10-01T17:41:09.1475207+03:00; workspace не загружен.
  Версия процесса не доказывает идентичность текущему HEAD.
- Исходные пользовательские untracked файлы `.agents/skills/prepare-github-release/`,
  `.cursor/rules/roslyn-mcp.mdc`, `iotester.py`, `scripts/` не меняются.
- Только новые файлы allowlist в `RoslynMcpServer.Tests/WorkspaceLoadCache/`
  и этот отчёт. Production, csproj, solution, normative contract и packets
  остаются read-only. README эпохи вне allowlist: статус task-01 фиксируется здесь.

## Владение и проверка

- `/root/host`: HydrateHostExperiment, ExperimentSession, ExperimentAnalyzerAdmission.
- `/root/support`: immutable models, bounded SemanticSnapshotCodec, DtbEventCounter.
- `/root/fixtures`: behavioral fixtures в HydrateHostExperimentTests.
- `/root`: координация, bounded codec (budget/nullable-flow fixes), итоговая сборка
  и main CI suite, evidence.
- `/root/independent_review`: отдельный read-only reviewer, не автор реализации.
  Final host/session/codec/counter/models/fixtures/report reviewed;
  earlier must-fix findings устранены, новых must-fix нет. Verdict:
  **accepted for E0/task-01 isolated experiment**. Reviewer не менял файлы
  и не запускал test processes; final validation принадлежит координатору.

Diff состоит из 20 новых C# файлов из exact allowlist packet и этого отчёта.
Существующие tracked files не изменены (`git diff --name-only` пуст).
Модели вынесены по одному типу в 14 файлов `Models/`; остальные шесть — host,
session, analyzer delegate, codec, counter и fixtures. Проверка whitespace
`git diff --no-index --check -- NUL <each-new-file>` и `git diff --check` прошла.
Git сообщает только conversion warning LF → CRLF, без whitespace errors.

## Capability и behavioral evidence

Все positive результаты ниже относятся к synthetic fixtures task-01. Fixture
создаёт два настоящих SDK-проекта и linked source под уникальным temp root.
DTO получает публично наблюдаемые options/documents из metadata-free Adhoc capture,
затем fixture явно добавляет известный CoreLib reference. Admission/region/graph
evidence задана fixture независимо от DTO; это не observed production
`sdk-project-v1` capture и не полный fresh-MSBuild semantic oracle.

- **V01/V02, read/hydrate:** encode → decode → новый AdhocWorkspace с новыми
  Roslyn IDs и новым session ID; известные options, project edges, metadata
  aliases и linked memberships сохраняются. Recapture принадлежащих host
  metadata references даёт тот же encoded value payload. Следующий semantic call
  проверяет compilation, constant values и разрешение project reference.
- **V07, writer:** существующие `WorkspaceWriteBoundary.Preflight` и
  `WorkspaceFilePersistence.WriteTextAsync` используются без production refactor.
  Изменение текста, SDK-glob add, rename и remove проверены следующими semantic
  calls и обычной disk build после каждого успешного шага. Сохранённые `.cs`
  содержат ожидаемые bytes, исходные csproj bytes неизменны.
- **Encoding:** non-ASCII linked edit сохраняет точные preamble и payload в
  UTF-8 без BOM, UTF-8 с BOM и UTF-16 LE с BOM; все memberships одного physical
  path получают одинаковый текст/encoding. Null encoding допускает read, но
  write отвергается без guessing. Lossy encoder fallback отвергается до первого
  byte; конфликтующие linked edits не могут сохранять разные тексты одного файла.
- **Refusal:** null/stale session, stale base/context, unknown analyzer,
  explicit Compile при disabled default items и external linked add/remove
  отвергаются до persistence. Test сравнивает disk/project bytes до и после.
  Existing external linked source допускает text edit при валидном evidence.
- **Partial/cancel:** второй файл с incompatible sharing lock даёт
  PartialPersistence с только первым committed path. Cancellation до I/O даёт
  пустой SavedPaths; после первого committed write сохраняет этот path и
  сообщает Cancelled/unapplied state. Rollback нескольких файлов не заявлен.
- **Reconcile:** bytes читаются для каждого physical path и обновляют все
  compile/additional/analyzer-config memberships, включая explicit generated
  obj inputs, без writer, csproj edits или DTB. Unreadable либо пропавший linked
  input оставляет прежний published snapshot; прямой reconcile не воссоздаёт
  удалённый файл. Удаление, успешно выполненное writer, учитывается по его
  persisted candidate.
- **Codec:** closed schemas/keys/enums/options, duplicate identities и default
  arrays проверяются. Decode ограничен 16 MiB; Encode проверяет cumulative text
  lower bound и serializes через bounded stream, не создавая unbounded payload.
  Tests включают malformed DTO и individually-valid values с aggregate overflow.
- **Admission:** exact analyzer и dependency inventories и hashes сравниваются
  с независимым evidence до workspace materialization, в том числе перед empty
  shortcut. Removed/extra/duplicate/unbound DLL, отсутствующая dependency,
  AnalyzerInventoryComplete=false и invalid profile/evidence отвергаются до
  delegate. Файлы, known absence, graph, regions и их manifests revalidate;
  eager source/metadata reads принадлежат session и повторно сверяют hashes.
  Stale source text при capture отвергается, не связывается с новым hash bytes.
- **Ownership:** session владеет workspace/metadata/collectible loader resources
  и operation lock; unsuccessful candidate освобождается. Dispose запрашивает
  unload, но немедленный CLR unload не заявлен. Production manager lock,
  watcher session и shadow loader не используются.

## DTB evidence и предел structural witness

Обычный open — negative witness: actual BinaryLogger передаётся Roslyn loader,
original logs находятся в отдельном `ordinary-<guid>` stage directory. Counter
replays original raw logs в отдельный tee output; тест сравнивает неизменность
original hashes, distinct paths и ненулевые Open/ProjectStart/CoreCompile/context
observations. Это наблюдение состоявшегося ordinary open, не empty counter pass.
Unattached, corrupt, cancelled и duplicate replay оставляют Complete=false;
OCE сохраняет переданный cancellation token.

`Hydrate_transitive_calls_have_no_open_or_process_with_exact_external_admission_limit`
использует `SourceSetAnalysis`/symbol reachability по production scope с experiment
files. Для `CreateBaseSessionAsync` reachable OpenProjectAsync/OpenSolutionAsync/
Process.Start отсутствуют и анализ IsDecided. Для полного `HydrateAsync` сохранён
точный inventory **одного** `TargetOutsideTheScope` limit: параметр
`analyzerAdmission` → вызов `_analyzerAdmission` в `AdmitAnalyzersAsync`, без
assignment внутри declared scope. Test проверяет kind и полный identity/text
с `SourceSetAnalysis.Describe` текущего invocation; неожиданный limit не игнорируется.

Behavioral empty-inventory witness закрывает этот branch и показывает
Complete=true/нулевые DTB counters для hydrate-owned closed path. Arbitrary
injected delegate помечает coverage unknown: отсутствие Open в reachable known
code не выдаётся за полное доказательство отсутствия Open/process у delegate.
Положительная DLL/generator execution в этом task **not-run**; нет принятого
original DLL admission или его session ID, который можно было бы честно записать.
Это остаётся task-02…04.

Binlogs, temporary SDK projects и byte witnesses создаются заново в каждом run,
проверяются в lifetime fixture и удаляются `Fixture.Dispose` только под его
проверенным уникальным temp root. Они не сохранены как checked-in artifacts.

## Validation

Промежуточные runs координатора через Roslyn MCP:

1. Release solution build: failed, три API/typing ошибки; затем исправлены.
2. Первый focused runner: pre-test build failed на недоступном публичном
   `PortableExecutableReference.DocumentationProvider`; tests not-run.
3. Следующий focused runner: 45 total / 2 passed / 43 failed — фикстуре не хватало
   MSBuild registration до JIT logger. Подготовка исправлена.
4. После регистрации: 45 total / 43 passed / 2 failed. Отдельно выявлены
   игнорирование custom ILogger обычным MSBuild loader и неверная постановка
   теста failure retention при законном удалении source.

Final code validation координатора (после всех C# fixes):

5. `run_specific_test`: workspacePath =
   `E:\Devel\RoslynMcpServer\RoslynMcpServer.Tests\RoslynMcpServer.Tests.csproj`,
   className = `RoslynMcpServer.Tests.WorkspaceLoadCache.HydrateHostExperimentTests`,
   configuration = Release, includeFullOutput = true; pre-test build включён.
   **53 total / 53 passed / 0 failed / 0 skipped**, timeout отсутствует.
6. `run_dotnet_build`: workspacePath =
   `E:\Devel\RoslynMcpServer\RoslynMcpServer.sln`, configuration = Release,
   noIncremental = true. Effective command:
   `dotnet build -v:minimal -p:Configuration="Release" --no-incremental`.
   **Exit 0, build succeeded**. Четыре warnings в existing files:
   NuGetFallbackAssemblyResolver.cs:35/53 CS8603; HostSession.cs:1177 CS8601;
   WpfTemporaryProjectCallbackTests.cs:195 xUnit1031. В новых файлах warnings нет.
7. `run_test_by_filter`: тот же Tests csproj, filter =
   `Category!=AnalyzerLifecycle`, configuration = Release, noBuild = true,
   noRestore = true, timeoutSeconds = 1200, includeFullOutput = true.
   Первый полный run: **1117 total / 1116 passed / 1 failed / 0 skipped**.
   Existing `PluginAssemblyLoadTests.Missing_private_dependency_skips_the_plugin_instead_of_binding_the_host_copy`
   упал в ShadowCopyPlan: при copying не найден временный `NEWTONSOFT.JSON.DLL.tmp`.
   Причина исчезновения файла не установлена; этот run не засчитан как pass.
8. `run_specific_test` для этого exact class/method с Release/noBuild/noRestore,
   timeoutSeconds = 300, includeFullOutput = true: **1/1 passed**, 0 skipped.
9. Повторный **полный** `run_test_by_filter` с теми же параметрами пункта 7,
   без изменения C# между сборкой и обоими main runs:
   **1117 total / 1117 passed / 0 failed / 0 skipped**, timeout отсутствует.

Main suite включает весь `RoslynMcpServer.Tests/SourceStructure` и существующие
write/encoding/shared-source/non-CSharp regressions. Runs выполнялись через
64-bit MCP dotnet host с SDK 10.0.300; runner удаляет inherited SDK resolver
environment overrides, DOTNET_CLI_UI_LANGUAGE=en-US. Runner outputs сохранены
в tool history этого исполнения; этот отчёт — transcription параметров и
результатов. Отдельный retained TRX runner не создавал, его наличие не заявляется.

Приёмка координатора 2026-10-03 повторила тот же Release build (exit 0, `--no-incremental`),
`HydrateHostExperimentTests` **53/53** и полный main suite
**1117/1117** (`Category!=AnalyzerLifecycle`, `noBuild`). Замечаний по spec нет.

AnalyzerLifecycle **not-run / not required**: новые isolated test files не меняют
production lifecycle, workspace load/prepare/publication paths, lifecycle host
или shared test/build configuration. Это не positive lifecycle evidence.

## Конкретные ограничения public API

- `DesktopStrongNameProvider.KeyFileSearchPaths` не является публичным getter в
  установленном Roslyn 5.9.0. Capture возвращает `unsupported-options`, не подменяет
  provider значением null/default и не читает private state через reflection.
- У stock `PortableExecutableReference` нет публичного getter для
  DocumentationProvider. Capture такого reference возвращает явный отказ.
  Hydrate может создать собственный private reference с известным provider из
  заранее заданного DTO и затем точно recapture его; это synthetic capability,
  не доказательство capture обычной MSBuild-сессии.
- Roslyn 5.9.0 MSBuild loader распознаёт logger по точному типу BinaryLogger,
  поэтому custom counter не получает обычные MSBuild события. Источник package
  nuspec — commit `35d9211b841e7613c1d2f8f5af6d628ace696c4c`;
  [IsBinaryLogger и LoadInfoAsync](https://github.com/dotnet/roslyn/blob/35d9211b841e7613c1d2f8f5af6d628ace696c4c/src/Workspaces/MSBuild/Core/MSBuild/MSBuildProjectLoader.cs#L233).
  Harness исправлен на actual original BinaryLogger и отдельный counter replay;
  final ordinary negative witness прошёл.
- Capture не может однозначно bind несколько same-path inner-TFM instances из
  ordinary Roslyn project inventory; ambiguity отвергается. DTO/hydrate используют
  точные value InstanceKey, но positive ordinary multi-TFM capture здесь не доказан.

Эти findings возвращаются design owner как remaining representability delta.
Положительный ordinary-capture/full fresh-MSBuild equivalence этим task не доказан.

## Decision authorities

- Experiment allowed: yes, в пределах accepted task-00 packet; scoped capability
  implemented/validated и принята независимым reviewer.
- Implementation allowed: только isolated experiment; production lifecycle/cache — no.
- Public activation allowed: no.
- Next epoch allowed: no; E0/task-02…06 ещё не приняты.
- Series complete: no.

Synthetic fixture evidence не доказывает observed `sdk-project-v1` completeness.
Полный fresh-MSBuild semantic oracle и генераторы остаются task-02…04.
Performance не измеряется без workload/budget из U-ARB-03.
Production cache-store, watcher reconciliation, cross-process cache hit,
полный generator semantic equivalence и production activation этим spike не
реализованы и не разрешены. Public API representability findings выше требуют
решения design owner; successful synthetic tests их не устраняют.

## CI SDK fix — 2026-10-05

Scope: исправлена установка SDK в `.github/workflows/test-suite.yml` для обеих
jobs (`unit`, `lifecycle`): exact `10.0.300` плюс latest `10.0.x`. Статус:
**validated locally**. Scope исходного isolated experiment и activation permissions
не изменяются.

[CI run 37148534996](https://github.com/VuDZ/RoslynMcpServer/actions/runs/37148534996)
на commit `bea9422f3286d8531c05d41a12fe71b9bf6b74d2` завершился шестью failures
в `HydrateHostExperimentTests.Fixture.BuildAsync()`. В полном выводе присутствуют
SDK `10.0.303` и `10.0.401`, но отсутствует `10.0.300`; CLI сообщает
`Requested SDK version: 10.0.300` и `A compatible .NET SDK was not found.`
Прежний setup `10.0.x` устанавливал latest SDK, а fixture намеренно использует
exact pin с `rollForward: disable`. Установка exact SDK закрывает prerequisite,
сохраняя target framework `net10.0` и воспроизводимость экспериментов.

Повторная локальная validation текущего рабочего дерева (включая существовавшие
пользовательские изменения): Windows x64, SDK `10.0.300`, Release.

- `run_dotnet_build` для `RoslynMcpServer.sln`: exit 0; прежние warnings
  CS8603 (2), CS8601 (1), xUnit1031 (1).
- `run_specific_test`, class `HydrateHostExperimentTests`, `noBuild=true`:
  **53/53 passed**, включая все шесть ранее падавших случаев.
- `run_test_by_filter`, `Category!=AnalyzerLifecycle`, main test project,
  `noBuild=true`: **1264/1264 passed**.
- Полный `Category=AnalyzerLifecycle`, `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`,
  `--no-build --no-restore`: **13/13 passed**, failed 0, skipped 0.
  TRX: `TestResults/hydrate-sdk-ci-full/hydrate-sdk-lifecycle-full.trx`.
  Запуск выполнен вне sandbox: первоначальный sandbox-прогон получил failure
  подключения к MSBuild BuildHost pipe и был остановлен. Этот failure не считается
  pass; затем отдельно прошёл тот же тест (1/1) и полный lifecycle-набор (13/13).

Изменённый workflow ещё не запускался на GitHub; локальная validation не является
доказательством выполнения setup-dotnet на hosted runner. Production C# и fixture
pins в этой правке не изменены.
