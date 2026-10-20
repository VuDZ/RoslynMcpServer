# D/E0/task-04 — Параллельный MSBuildProjectLoader: no-go

Дата: **2026-10-10**. Исполнитель: **Codex (GPT-6)**. Статус: **spike реализован,
матрица замеров завершена; формальная приёмка code/test changes ожидается из-за
незелёного единого main CI в текущей рабочей копии**.

**Вердикт эксперимента — no-go.** Лучший режим с N ≤ 4 — round-robin, N=4:
27,636 с против baseline 41,711 с, ускорение 1,51×. Порог — не больше
20,856 с (50 % baseline). Кроме того, один из трёх прогонов потерял 11 generated
documents у FancyZonesEditor. Память не стала препятствием: медиана пика
BuildHost у этого режима — 2,114 GB, ниже 4 GB.

Принятый scope production: **не принят и не активирован**. Выполненный scope:
отдельный консольный spike, два исключения в корневом csproj, исключение spike
из области SourceStructure, замеры и evidence. Путь загрузки сервера, версия,
CHANGELOG, solution и CI workflow не изменены. E1 не реализована; завершение
этого эксперимента не означает завершения E0.

## Код и воспроизводимость

- [Консольный проект](../../../../spikes/ParallelLoad/ParallelLoad.csproj), net10.0,
  вне RoslynMcpServer.sln; [инструкция запуска](../../../../spikes/ParallelLoad/README.md).
- Roslyn 5.9.0, Microsoft.Build.Locator 1.11.2; Microsoft.Build/Framework/StringTools
  18.6.3 исключены из runtime, как в сервере. System.Management 10.0.8 используется
  только монитором для определения командной строки новых dotnet BuildHost.
- Global properties перенесены из
  [MsBuildWorkspaceProperties](../../../../Services/Workspace/MsBuildWorkspaceProperties.cs):
  `Configuration=Debug`, `Platform=x64`, без TargetFramework override.
- Регистрация MSBuild повторяет
  [MsBuildBootstrapper](../../../../Diagnostics/MsBuildBootstrapper.cs): сначала
  SDK по global.json целевого решения, затем выбор совместимого экземпляра и
  fallback сервера. Копии GlobalJsonSdkReader и MsBuildInstanceSelector находятся
  внутри spike, ссылки на сборку сервера нет. У Files pin 10.0.102 отсутствует
  среди установленных SDK; применяется тот же fallback, что у сервера.
- Все build/run spike выполнены через MCP `run_dotnet_build` / `run_dotnet_run`
  с явным путём проекта. MCP принимает проект вне загруженного solution;
  shell fallback для spike не потребовался. `run_dotnet_run` собирает и запускает
  Debug, поэтому основная матрица относится к Debug-бинарнику spike; отдельно
  проверена Release-сборка проекта.
- Аргументы каждого прогона: `--solution <bench>/clones/{powertoys,files}/<solution>.slnx`
  и `--output <repo>/artifacts/parallel-load/<solution>-<mode>-<run>.json`.
  Для parallel добавляются `--workers N --strategy round-robin|cluster`.
  Run 0 — прогрев; run 1–3 — измеренные новые процессы.
- [SHA-256 исходников](task-04-parallel-loader/source-sha256.json),
  [окружение](task-04-parallel-loader/environment.json),
  [сводка с деталями каждого прогона](task-04-parallel-loader/comparison.json).
  Все **44** основных снимка `*.json.gz` (32 PowerToys и 12 Files, около 34 MB),
  предварительные прогоны и TRX в репозиторий не входят: при приёмке 2026-10-10
  они перенесены в неверсионируемый `<repo>/artifacts/parallel-load/task-04-evidence/`
  на машине замера. Содержимого исходных документов в снимках нет.

## Окружение и подготовка

AMD Ryzen 7 9800X3D, 8 ядер / 16 логических процессоров; Windows 11 Pro
10.0.26200; 93,7 GB RAM. SDK: 9.0.314, 10.0.201, 10.0.300, 10.0.401.
~~MSBuild зарегистрирован из VS 2026 Enterprise
`C:/Program Files/Microsoft Visual Studio/18/Enterprise/MSBuild/Current/Bin/amd64`.~~
**Исправление 2026-10-10 ([task-05](task-05-profile-guided-partition.md)):** поле
`Registration` baseline-снимков обоих решений указывает `<SDK>/10.0.401`; MSBuild
из VS используется только BuildHost-net472 (vcxproj). Точное значение
регистрации есть в каждом снимке. Опрос SecurityCenter2
обнаружил Kaspersky (`productState=266240`) и Windows Defender; настройки
антивируса не менялись, Kaspersky не выключался. Начальная отметка машины:
2026-10-10 00:59:06 по Москве. Имена машины и профиля в evidence обезличены.

Использованы существующие checkout вне репозитория в
`E:/Devel/roslyn-mcp-bench/clones`. HEAD проверен перед замером:

- PowerToys: `7b6789c576b304c7e1eda8ce571cc1b0afd280ab`, PowerToys.slnx;
- Files: `1994d9a4269f5144c70bc67d6a48de4a20616912`, Files.slnx.

Перед серией выполнен restore обоих решений, оба exit code 0:

```text
<VS>/MSBuild/Current/Bin/amd64/MSBuild.exe <bench>/clones/powertoys/PowerToys.slnx -t:restore -p:Platform=x64 -p:Configuration=Debug -verbosity:minimal
<VS>/MSBuild/Current/Bin/amd64/MSBuild.exe <bench>/clones/files/Files.slnx -t:restore -p:Platform=x64 -p:Configuration=Debug -verbosity:minimal
```

Решения не собирались. Файловый cache ОС не сбрасывался. Design-time build
во время open обновляет общие obj; это принципиально для найденных гонок.
Прогрев исключён из всех медиан. Комбинации выполнялись последовательно,
а параллельность была только внутри одного замера.

## Измеряемый интервал и сбор метрик

Wall — от запуска групп загрузки до готового слитого Solution в AdhocWorkspace;
baseline тоже включает AddSolution. Bootstrap, создание MEF workspace,
XML-группировка и запись slnf выполняются до этого интервала; сериализация
снимка, завершение монитора и cleanup — после. GroupMs измеряет отдельный
LoadSolutionInfoAsync; MergeMs включает перепривязку ссылок и AddSolution.
Все интервалы измерены Stopwatch. Это время open с материализацией Solution,
а не время `dotnet run` вместе со сборкой spike.

Монитор — отдельный процесс spike, исключённый из дерева CPU измеряемого
процесса. Он использует Toolhelp32Snapshot, GetProcessTimes и
GetProcessMemoryInfo; dotnet BuildHost определяется по командной строке через
WMI на отдельных потоках. Период — 100 мс. Для каждого PID учитывается время
создания, CPU сохраняется монотонно, дерево потомков расширяется до неподвижной
точки. Busy cores = (CPU spike + CPU его потомков) / wall.

Максимальный фактический интервал **во всех 44 основных прогонах — менее 1 с**;
на измеренных baseline/binlog — не больше 343 мс, в parallel — не больше
171 мс. Ошибок монитора нет. Пик памяти — максимальная наблюдённая сумма working
set всех одновременно живых BuildHost. CPU коротких потомков, завершившихся
между опросами, остаётся нижней оценкой; мгновенный пик между опросами не измерен.

Предварительная реализация опрашивала WMI в процессе загрузчика, затем
синхронно во внешнем процессе. При N=4/8 встречались интервалы 1,4–2,1 с.
Эти прогоны **не входят** в таблицы: они сохранены вне репозитория в
`<repo>/artifacts/parallel-load/task-04-evidence/preliminary/`. Финальные parallel-прогоны
повторены после исправления. Baseline/binlog во внешнем процессе уже соблюдали
интервал; их wall и CPU root имеют ту же границу, CPU/working set берутся из
тех же Windows counters. Оператор метрик не является загрузчиком решения.

## Медианы: PowerToys

Для каждой строки: один прогрев и n=3 измерения. GB = 10⁹ байт. Ускорение
вычислено относительно baseline; одинаковые ключи проектов считаются повторно
загруженными, если они присутствовали более чем в одной группе.

| Режим | wall, с | ускорение | повторных проектов | пик BuildHost, GB | busy cores | эквивалентность |
|---|---:|---:|---:|---:|---:|---|
| baseline | 41,711 | 1,00× | 0 | 1,137 | 1,40 | identical |
| baseline-binlog | 41,807 | 1,00× | 0 | 1,142 | 1,49 | identical |
| round-robin N=2 | 33,660 | 1,24× | 82 | 1,634 | 2,77 | identical |
| round-robin N=4 | 27,636 | 1,51× | 175 | 2,114 | 4,50 | unexplained |
| round-robin N=8 | 24,718 | 1,69× | 278 | 3,231 | 8,07 | unexplained |
| cluster N=2 | 40,308 | 1,03× | 2 | 1,115 | 1,50 | identical |
| cluster N=4 | 40,367 | 1,03× | 2 | 1,116 | 1,53 | identical |
| cluster N=8 | 40,385 | 1,03× | 2 | 1,116 | 1,56 | identical |

Уникальных ключей во всех прогонах — 248. Сумма проектов по группам:
248 / 330 / 423 / 526 для baseline / round-robin 2 / 4 / 8; для cluster — 250.
Медианы MergeMs — 167–203 мс. Число обнаруженных BuildHost: baseline 2,
round-robin 4/8/16, cluster 4/6/7. N загрузчиков не означает ровно N процессов:
в PowerToys загрузчик может запустить и netcore, и net472 BuildHost.

Медиана количества событий diagnostics: baseline/binlog 3570,
round-robin 4637/5518/6283, cluster 3579. В сравнении используется **множество**
(kind, message); повторные одинаковые сообщения не являются расхождением.

Cluster сохраняет результат, но основной компонент удерживает почти всю
работу в одной группе: в N=2 она возвращает 241 проект, вторая — 9. Разница
wall групп составляет примерно 40 с против 3 с. Связные компоненты строятся
только по буквальным ProjectReference из XML csproj, без evaluation;
условия и свойства не вычисляются, ссылки из Directory.Build.* не видны.
Разложение по числу проектов не учитывает реальную стоимость design-time build.

## Стоимость binlog

`median(baseline-binlog) − median(baseline)` = **+95 мс (+0,23 %)**.
В каждом прогоне записаны два binlog, суммарно около 8,2 MB;
`Verbosity=Normal`, `ProjectImports=None`. Replay отсутствует в замере.

Диапазон измеренных binlog-прогонов — 38,716–41,837 с; baseline —
41,471–41,919 с. Эта разница медиан — наблюдение данного протокола с n=3,
а не точная причинная оценка 95 мс: cache ОС, обновляемые obj, антивирус и
последовательный порядок комбинаций дают вариативность времени.

## Эквивалентность и причины расхождений

Два baseline сначала сравнивались друг с другом. У PowerToys и Files
**0 различий**, третий baseline также совпал. Ни одна дельта parallel не
списана на natural variance. Проверены все поля из задания: ключи,
language/output/default namespace, documents/additional/analyzer config как
множества и порядки, metadata/analyzer/project references с aliases,
CompilationOptions и ParseOptions, отдельно множество diagnostics.

Строгие строковые снимки сохранены. Дополнительно объяснены различия только
регистра путей Windows: для PreviewHandlerCommon шесть полей содержат
`previewpane/common` против `previewpane/Common`. В solution указан Common,
в ProjectReference разных csproj встречаются оба написания. `fsutil file
queryCaseSensitiveInfo` сообщил disabled, а queryFileID для обоих написаний
вернул один ID `0x0000000000000000000100000008206d`. Компаратор отдельно
показывает эти дельты как explainedCase, сохраняя сырые значения. Options,
aliases и сообщения diagnostics таким способом не нормализуются.

**Round-robin N=4:** run 2, FancyZonesEditor — отсутствуют 11 generated WPF
documents, включая App.g.cs, CanvasEditorWindow.g.cs и GridEditorWindow.g.cs.
Отличаются и множество, и порядок documents. Причина исчезновения именно
этих WPF items не установлена; гипотеза — конкурирующий design-time markup
build на общих obj. Это unexplained, не natural variance и не допустимая
эквивалентность. Другие два измеренных прогона имеют только объяснённый регистр.

**Round-robin N=8:** run 1, ColorPickerUI — отсутствуют 17 generated WinUI
documents, включая App.g.cs/App.g.i.cs и XamlTypeInfo.g.cs. Добавились два
Failure diagnostics о невозможности доступа к общим `obj/x64/Debug/input.json`
и `output.json`: файлы использовал другой процесс. Конкуренция за общие
intermediate inputs подтверждена сообщениями MSBuild; observable результат
изменён, дельта не устранена и остаётся неэквивалентной. Это не повод объявить
успех по совпавшему количеству проектов.

Первые 20 уникальных расхождений на категорию и комбинацию, включая
объяснённый регистр, вынесены в [differences.md](task-04-parallel-loader/differences.md).
Полные before/after и результаты каждого из трёх сравнений находятся в
[comparison.json](task-04-parallel-loader/comparison.json).

Отдельная ошибка первоначального spike: попытка отвергнуть все неизвестные
target IDs при слиянии. Roslyn сохраняет в ProjectInfo opaque references на
незагруженные native projects; Project.ProjectReferences показывает только
цели внутри Solution, AllProjectReferences сохраняет и отсутствующие цели.
У PowerToys таких references 901, в baseline и итоговом merged info. Теперь
они сохраняют исходные IDs и свойства; все присутствующие цели перепривязаны
на канонические IDs. Четыре ранних неуспешных прогона N=2 сохранены в
[preflight-failures](task-04-parallel-loader/preflight-failures/) и заменены
полной серией после исправления. У основных 44 прогонов нет timeout, exception
группы или нулевого результата; Failure diagnostics внутри успешно
завершённого open учтены отдельно и не скрыты статусом `ok`.

## Контроль: Files

| Режим | wall, с | ускорение | повторных проектов | пик BuildHost, GB | busy cores | эквивалентность |
|---|---:|---:|---:|---:|---:|---|
| baseline | 10,929 | 1,00× | 0 | 0,202 | 1,48 | identical |
| round-robin N=2 | 11,023 | 0,99× | 6 | 0,364 | 2,45 | identical |
| round-robin N=4 | 8,727 | 1,25× | 13 | 0,598 | 4,06 | identical |

11 уникальных ключей; суммы по группам 11/17/24. Diagnostics событий 15/16/17,
но множества одинаковы. BuildHost 1/2/4; MergeMs 70–88 мс.
N=2 на 0,86 % медленнее baseline и требует больше памяти/CPU; утверждать,
что любой parallel всегда делает не хуже, нельзя. N=4 сокращает wall примерно
на 20 %, сохраняя проверяемый результат, с почти тройным пиком памяти.
Отдельный go/no-go для Files не выносится.

## Validation и проверка исполнителем

- Spike: MCP Release build — passed, warnings 0. MCP `check-merge` — passed:
  предпочтение requested copy, сохранение TFM keys, first-found fallback,
  ProjectId/DocumentId, aliases, EmbedInteropTypes и opaque missing target.
- RoslynMcpServer.sln: MCP Release build — passed. Четыре предупреждения в
  неизменённых файлах: два CS8603 NuGetFallbackAssemblyResolver, CS8601
  LifecycleTestHost/HostSession, xUnit1031 WpfTemporaryProjectCallbackTests.
- SourceStructure: focused **123/123 passed**. Тот же полный набор из 123
  тестов присутствует в финальном проходящем SDK 10.0.401 шарде; точные
  identities/kinds структурных ограничений не менялись.
- Полный `Category!=AnalyzerLifecycle`, Release, стандартный MCP на SDK
  10.0.401: **1362 passed / 1 failed**. Незакоммиченный файл, существовавший
  до spike, DependencyPartialProofTests требует профиль SDK 10.0.300, но
  использует путь загруженной Microsoft.Build assembly из 10.0.401.
  Focused повтор воспроизвёл тот же сбой.
- Только pin CLI на 10.0.300 не помогает: testhost меняет CWD и bootstrap
  всё равно выбирает 10.0.401. VSTest runsettings с ROSLYN_MCP_WORKSPACE,
  указывающим на каталог с global.json 10.0.300, даёт focused **1/1 passed**.
- Полный main с этим глобальным pin: **1360 passed / 3 failed / 0 skipped**:
  RawImportEvidenceTests — binlog v26 при reader до v25; Concurrent_load —
  timeout; CrossProcessRoundtrip multitarget — cleanup файла, занятого
  процессом. Первую несовместимость нельзя скрывать успешным focused тестом.
- Финальное разделённое покрытие: SDK 10.0.401 — **1362/1362 passed**, исключён
  ровно один SDK-specific метод; SDK 10.0.300 — этот метод **1/1 passed**.
  Оба запуска Release, без skip, без изменения чужих code/tests. Мультимножество
  testName результатов **точно совпало** с полным запуском на 1363 случая
  (1362 уникальных display names; один повторяется). Это полное по inventory
  покрытие, но **не зелёный единый запуск main CI**, поэтому формальная
  приёмка задачи оставлена pending.
- Специализированные MCP тестовые tools не задают runsettings/working directory;
  для контролируемых SDK запусков использован MCP execute_dotnet_command.
  Shell попытка focused запуска закончилась connection timeout testhost после
  90 с; это aborted/zero-test, не pass. AnalyzerLifecycle не запускался:
  production load/prepare, provenance и lifecycle host не изменены.
- Проверка исполнителем: один раунд, независимых рецензентов нет. Проверены
  границы public API, отсутствие общего ProjectMap, перепривязка всех
  известных целей, сохранение unresolved targets, соответствие Compile
  исключению SourceStructure и разделение предварительных/итоговых замеров.

[Validation inventory](task-04-parallel-loader/validation-inventory.json).
TRX полного pinned запуска и обоих шардов (`task-04-main.trx.gz`,
`task-04-main-401.trx.gz`, `task-04-main-300.trx.gz`) лежат вне репозитория
в `<repo>/artifacts/parallel-load/task-04-evidence/`.

Воспроизводимые параметры последних запусков (MCP execute_dotnet_command):

```text
workingDirectory=<repo>
test <repo>/RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --no-restore --filter "Category!=AnalyzerLifecycle&FullyQualifiedName!~DependencyPartialProofTests.Framework_selection_matches_installed_pack_files_without_loading_analyzers" --logger "trx;LogFileName=task-04-main-401.trx" --results-directory <repo>/artifacts/parallel-load/TestResults --verbosity minimal

workingDirectory=<repo>/artifacts/parallel-load/validation-sdk-10.0.300
test <repo>/RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --no-restore --settings <repo>/artifacts/parallel-load/sdk-10.0.300.runsettings --filter "Category!=AnalyzerLifecycle&FullyQualifiedName~DependencyPartialProofTests.Framework_selection_matches_installed_pack_files_without_loading_analyzers" --logger "trx;LogFileName=task-04-main-300.trx" --results-directory <repo>/artifacts/parallel-load/TestResults --verbosity minimal
```

Каталог validation-sdk-10.0.300 содержит только global.json:

```json
{"sdk":{"version":"10.0.300","rollForward":"disable"}}
```

Runsettings задаёт окружение testhost, не меняя настройки пользователя:

```xml
<RunSettings>
  <RunConfiguration>
    <EnvironmentVariables>
      <ROSLYN_MCP_WORKSPACE>&lt;repo&gt;/artifacts/parallel-load/validation-sdk-10.0.300</ROSLYN_MCP_WORKSPACE>
    </EnvironmentVariables>
  </RunConfiguration>
</RunSettings>
```

Для формальной приёмки code/tests нужен единый зелёный main CI после
согласования SDK-specific теста в уже существующей работе workspace-load-cache.
Этот spike его контракт и код не меняет. Данный blocker не изменяет no-go:
порог wall не выполнен ни одной комбинацией N ≤ 4, а faster round-robin
дополнительно меняет документы/diagnostics.
