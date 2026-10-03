# Epoch 0 — Feasibility, profile и baseline

Статус: **proposed experiment; production hit запрещён**. Admission profile
`sdk-project-v1`, overlay admission и hydrate host выбраны 2026-10-01.
Профиль расширен Razor и Web 2026-10-03. Сравнение со свежим
`MSBuildWorkspace` остаётся приёмкой.

## Задача

До production реализации доказать выполнимость host, persistence, dependency
admission, semantic equivalence и измерений. Fixture может разрешить продолжить
experiment, но не public activation.

## Обязательная работа

1. Зафиксировать commit `9867318ddb5294ce144bf024b9a61a1a2e3814c3`,
   source 1.3.21, отдельно running MCP, Roslyn/MSBuild/SDK, hardware и параметры.
2. Первым gate выбрать production hydrate host, который восстанавливает DTO без
   скрытого DTB. `OpenSolutionAsync`/`OpenProjectAsync` с последующей подменой
   не считается skip-DTB.
3. Построить operation/capability matrix: read, text edit,
   add/remove/rename document, project mutation, disk reconciliation; указать
   host capability, writer, preflight, result/failure/partial и reload.
4. Не принимать молча read-only downgrade, свой writer `.csproj` или
   reload-on-write. Выбранный контракт — `AdhocWorkspace` и writer только для
   `.cs`, раздел ниже. A-WRITE сохраняется.
5. Для semantic DTO и dependency evidence назвать отдельные public/supported
   источники. Проверить positive, known-absent, wildcard regions и target inputs.
   Binlog/evaluation не объявлять полными без evidence.
6. Versioned detector до hit различает supported, unsupported и unknown.
   Unknown отвергает весь request. Для dependency closure действует профиль
   `sdk-project-v1` ниже. Вне его evidence production hit по-прежнему запрещён.
7. Сравнить candidate production host с независимым fresh MSBuild oracle.
   Для supported generator проверить полный generated document set, texts,
   marker/constant и diagnostics; unavailable output = failed/not-run.
8. Metadata fast-open исследовать как отдельный O7 comparator с собственным
   outcome и измерениями. Его результат не является cache go и не меняет
   full-source profile.
9. Назвать target solution и обязательные SDK/TFM/instances/generators/imports.
   Если target multi-target, exact inner mapping становится gate. Baseline
   обычной загрузки офисного ПК выбран ниже. Hit-rate и miss-overhead остаются
   в U-ARB-03.
10. Измерить end-to-end: probes, capture, optional prepare, compilation и первый
    полезный semantic call. Порог 70% из прежнего proposal — только предложение,
    не нормативный budget.

Trace: R-01, R-02, R-03, R-06, E0-01, E0-02, E0-03, E0-04, E0-05,
V-03, V-04.

## Выбранный admission profile

Владелец 2026-10-01 зафиксировал профиль **`sdk-project-v1`**. Это ответ
U-ARB-06, не разрешение production hydrate и не закрытие U-ARB-03.
Hydrate host выбран отдельно ниже. Capture schedule выбран в эпохе 2.

Spike: SDK `10.0.300`, Roslyn `Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.9.0.
Сравнивались design-time binlog (`DesignTimeBuild`, `SkipCompilerExecution`,
`ProvideCommandLineArgs`) и обычный build, оба при `ProjectImports=None` и
`ProjectImports=Embed`. `Embed` увеличил лог и не добавил отсутствовавших
путей. `CoreCompile` и пользовательские target перед ним в этом прогоне
выполнялись.

Поддержанный request — проект `Microsoft.NET.Sdk`. Регион membership —
каталог этого проекта по стандартному glob `**/*.cs` плюс явные `Remove`,
записанные в проекте и в импортированных props. Корень монорепы регионом не
является. Walk-up `Directory.Build.props` / `Directory.Build.targets` входит
только как файл, который import-событие реально разрешило.

| Категория | Источник | Не считается evidence |
|---|---|---|
| Positive import | Design-time binlog, `ProjectImportedEventArgs` с непустым `ImportedProjectFile`. `ProjectImports=None` достаточен | Список уже открытых Roslyn Document |
| Known-absent `Exists` | То же событие: `UnexpandedProject` задан, `ImportedProjectFile` пуст | Флаг `ImportIgnored`: на отсутствующем `Local.props` он остался `false` |
| Wildcard-регион | Текст `Include` / `Remove` в файле проекта и в импортированных props. Preprocess эти шаблоны сохраняет | События binlog. Шаблона `secret\**\*.cs` и пустого `missing-dir\**\*.txt` в логе не было; совпавшие файлы в параметрах задачи регион не заменяют |
| Toolset / SDK | Каталог SDK по полному пути `Sdk.props` в том же binlog (`...\sdk\<version>\...`) | Строка TFM или имя SDK |
| Пользовательский target | `unknown` | Путь в параметре задачи или в командной строке `Exec`. Task может прочитать файл внутри своей DLL, и лог этого не покажет |

Пользовательский target — `Target` в файле проекта или в импорте вне каталога
SDK. Generated file, который пишет только такой target, вместе с ним вне
профиля. Target из каталога SDK отдельно не интерпретируются: их закрывает
идентичность каталога SDK.

С 2026-10-03 тот же профиль допускает `Microsoft.NET.Sdk.Razor` и
`Microsoft.NET.Sdk.Web` на условиях раздела
[Razor и Web](#расширение-профиля-razor-и-web).

Detector до hydrate и до загрузки DLL:

- `supported` — каждый проект request совпадает с `sdk-project-v1`, и у каждой
  обязательной категории есть свой источник;
- `unsupported` — узнаваемая черта вне профиля: не SDK-проект, пользовательский
  target, glob, который нельзя ограничить каталогом проекта;
- `unknown` — обязательная категория без evidence.

`unsupported` и `unknown` дают ordinary load на весь request, не partial hit.
Появление файла на known-absent пути — miss. Хеши только уже увиденных файлов
по-прежнему не доказывают отсутствие неучтённого входа.

`AnalyzerProvenanceCaptureService` этот профиль не реализует: replay читает
только `TaskOutput` item `Analyzer` и затем удаляет binlog.

## Расширение профиля Razor и Web

Владелец 2026-10-03 расширил `sdk-project-v1` проектами
`Microsoft.NET.Sdk.Razor` и `Microsoft.NET.Sdk.Web`. Это дополнение U-ARB-06,
не новый гейт, не разрешение disk-hit и не закрытие U-ARB-03.

Проверено на SDK `10.0.300`. Таргеты `Microsoft.NET.Sdk.Razor`,
`Microsoft.NET.Sdk.Web` и `Microsoft.NET.Sdk.StaticWebAssets` лежат в
`sdk\10.0.300\Sdks\`. Их закрывает уже выбранная идентичность каталога SDK.
По отдельности они не интерпретируются.

К региону каталога проекта добавляются три глоба из этих SDK:

- `**/*.cshtml`;
- `**/*.razor`;
- `wwwroot/**`, включая `wwwroot/.well-known/**`.

`wwwroot` проекта по `ProjectReference` — тот же глоб у другого проекта того
же request. Ссылка на проект вне request оставляет путь без региона и даёт
`unknown`.

Генераторы Razor — DLL из `Sdks\Microsoft.NET.Sdk.Razor\source-generators\`.
На `10.0.300` это `Microsoft.CodeAnalysis.Razor.Compiler.dll`,
`Microsoft.AspNetCore.Razor.Utilities.Shared.dll` и
`Microsoft.Extensions.ObjectPool.dll`. Хеш содержимого считается так же, как
у analyzer DLL. Каталог проверки — каталог SDK, не каталог pack. Эти DLL
получают `.cshtml` и `.razor` как `AdditionalFiles` и порождают код на
семантике. Попадание в кеш второй MSBuild для них не запускает.

Статический ассет, чей путь лежит вне каталога проекта и вне каталога SDK
(содержимое NuGet-пакета), — `unknown`. Свой `Target` в файле проекта или в
импорте вне каталога SDK по-прежнему `unknown`. Любой такой вход отвергает
весь request.

Проверка `OrchardCore` v3.0.1 (`b9c4b2f23e56ef11fbdbd28603c871d1b0fc9deb`):
каждый проект Razor или Web импортирует target вне каталога SDK либо содержит
свой `Target`. Это расширение тот request не пускает.

Текст Razor в живой сессии по-прежнему не синхронизируется
([эпоха 3](../epoch-3-live-consistency/spec.md#выбранный-контракт-чтения)). Смена
байтов файлов этих глобов — miss поколения на следующей загрузке.

## Выбранное admission overlay

Владелец 2026-10-01 выбрал повторную проверку DLL и новый `LoadSessionId`.
Это ответ U-ARB-04. Hydrate host выбран отдельно ниже. Disk-hit не включён.

Spike того же дня, SDK `10.0.300`, Roslyn `Workspaces.MSBuild` 5.9.0. Два
независимых design-time прогона записали один и тот же `ProjectInstanceId`.
В `TaskOutput` / `Analyzer` лежали генераторы из `Microsoft.NETCore.App.Ref`,
не из каталога SDK. Явный `<Analyzer Include>` был в командной строке
компилятора и не был item `Analyzer`. Metadata содержала `AssemblyVersion` и
`PublicKeyToken`, но не хеш содержимого. `ProjectStarted.Properties` в этом
прогоне был пуст; `GlobalProperties` содержали только переданные флаги
design-time build.

Переносимая identity — путь файла проекта-потребителя, inner TFM и полный
путь DLL. `ProjectId`, `LoadSessionId`, `ProjectInstanceId` и shadow-путь в
DTO не пишутся и доказательством не являются.

Перед overlay в новом процессе:

1. Заново разрешить каталог SDK по правилу `sdk-project-v1` и каталог pack,
   в котором лежит DLL. Расхождение с записанным путём отказывает overlay.
2. Прочитать DLL по записанному пути и посчитать хеш содержимого. Сравнить с
   хешем, который capture посчитал с диска в момент записи. `AssemblyVersion`
   и хеш манифеста эту проверку не заменяют.
3. Совпало — собрать новый in-memory snapshot с новым `LoadSessionId` и
   передать его в существующий gate. Snapshot, прочитанный с диска, в gate не
   передаётся.
4. Не совпало, DLL не читается или identity неоднозначна — опубликовать base
   graph без overlay.

Явный analyzer, который виден компилятору и отсутствует в `TaskOutput` item
`Analyzer`, делает request не overlay-ready. Такой DLL из DTO не загружается.

В DTO и в лог попадает только allowlist свойств: флаги design-time build,
`Configuration`, `Platform`, `TargetFramework`. Остальные global и environment
properties не сохраняются. Секреты не пишутся.

Каталог evaluation cache — user-private namespace из контракта, отдельно от
analyzer shadow store. Порча и обрыв записи дают отказ overlay. Замена DLL
без обновления записанного хеша тоже даёт отказ. Согласованная правка и DLL,
и записи кэша тем же пользователем границей безопасности не является.

## Выбранный hydrate host

Владелец 2026-10-01 выбрал **`AdhocWorkspace` и writer только для `.cs`**.
Это ответ U-ARB-05. Disk-hit не включён. Сравнение кандидата со свежим
`MSBuildWorkspace` остаётся приёмкой эпохи и этот выбор не заменяет.

Проверено на `Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.9.0. Публичные
методы наполнения `MSBuildWorkspace` — `OpenSolutionAsync` и
`OpenProjectAsync`. `Create()` оставляет 0 проектов. `AddSolution` у этого
типа нет. `AdhocWorkspace.TryApplyChanges` после смены текста вернул успех и
не изменил байты файла; `AddDocument` не создал ни `.cs`, ни `.csproj`.
`MSBuildWorkspace.ApplyDocumentAdded` пишет файл и элемент проекта через
`RemoteBuildHost`. На SDK-проекте явный `<Compile Include>` даёт NETSDK1022.

| Операция | Host | Кто пишет диск | Reload |
|---|---|---|---|
| read | опубликованный снимок Adhoc | никто | нет |
| text edit существующего `.cs` | правка снимка; `TryApplyChanges` файл не пишет | `PersistDocumentChangesAsync` | нет |
| add/remove/rename `.cs` при SDK glob | правка снимка | адаптер создаёт или удаляет файл | нет |
| явный `<Compile Include>` без глобов | `unsupported` | никто | отказ до записи, либо граф stale |
| `PackageReference`, `rename_project` | не через `TryApplyChanges` | уже существующие helper-ы | граф stale, следующий обычный load |
| analyzer reference | только память | никто | нет |
| disk reconciliation | отражение известных документов | никто, `.csproj` не пишется | нет |

Preflight A-WRITE выполняется до любой записи. Отказ preflight не меняет байты.
Partial сообщает только успешно записанные пути. Откат нескольких файлов не
обещается. Analyzer reference в проектный файл не попадает; overlay включается
только повторной проверкой DLL.

`Open*` плюс подмена текстов skip-DTB не считается. Read-only hydrate и
reload перед каждой project mutation не выбраны.

## Выбранный baseline офисного ПК

Владелец 2026-10-03 взял отчёт `20260922-120221-bondarev` как численный
baseline обычной загрузки. Это хост и latency/resource часть U-ARB-03.
Hit-rate, miss-overhead и public activation этим не заданы. Порог 70%
порогом не становится. Disk-hit не включён. Медианы двух корпусов не
складываются. Числа ниже скопированы из Warm median отчёта и заново не
считались.

Хост: `bondarev`, Windows 11, 6 процессоров, 65318 МБ. Сервер file version
`1.4.14.0`. Другой отчёт baseline эти числа не заменяет.

Сценарий отчёта — новый процесс, series `warm`, неизменное дерево,
`load_workspace` и первый `find_symbol_definition`. `targetFramework` не
передавался, `shadowCopyInSolutionAnalyzers` не включался. Restore в бюджет
не входит. Edit, build, restart, live, capture и hit в отчёте отсутствуют.

### orchard-wide

Pin `b9c4b2f23e56ef11fbdbd28603c871d1b0fc9deb`, `OrchardCore.slnx`. SDK
запрошен `10.0.200` rollForward `latestMajor`, разрешён `10.0.401`. Compare
key `b9c4b2f23e56ef11fbdbd28603c871d1b0fc9deb|10.0.401|`. В ответе загрузки
227 экземпляров проектов. Инвентарь отчёта: 233 csproj, 30 с
`TargetFrameworks`, 93 Razor/Web, 2 с `OutputItemType="Analyzer"`,
`Directory.Build.props` и `NuGet.config`.

| Метрика | n | median | p95 | min | max |
|---|---|---|---|---|---|
| load_workspace | n=10 | 52.67 s | 56.46 s | 52.17 s | 56.46 s |
| find_symbol_definition | n=10 | 25.37 s | 26.48 s | 24.97 s | 26.48 s |
| useful (load + semantic) | n=10 | 78.05 s | 82.20 s | 77.28 s | 82.20 s |

Доля semantic в useful: 33%. Peak working set десяти warm-попыток в таблице
отчёта: 1109–1152 МБ.

### roslyn-deep

Pin `013d3a758df6c137497ff37a93f0d4bed103853a`,
`src/Compilers/CSharp/Portable/Microsoft.CodeAnalysis.CSharp.csproj`. SDK
`10.0.301` rollForward `patch`, compare key
`013d3a758df6c137497ff37a93f0d4bed103853a|10.0.301|`. В ответе загрузки 6
экземпляров проектов. Инвентарь отчёта: 351 csproj, 125 с
`TargetFrameworks`, 8 Razor/Web, 3 с analyzer item, Arcade.Sdk и Helix.Sdk
`10.0.0-beta.26459.2`.

| Метрика | n | median | p95 | min | max |
|---|---|---|---|---|---|
| load_workspace | n=10 | 4.47 s | 4.70 s | 4.29 s | 4.70 s |
| find_symbol_definition | n=10 | 9.63 s | 9.89 s | 9.24 s | 9.89 s |
| useful (load + semantic) | n=10 | 14.11 s | 14.42 s | 13.63 s | 14.42 s |

Доля semantic в useful: 68%. Peak working set десяти warm-попыток в таблице
отчёта: 693–756 МБ.

Оба корпуса содержат черты вне `sdk-project-v1`: свой target, импорт вне
каталога SDK, у Roslyn ещё Arcade и набор `TargetFrameworks`.
OrchardCore/Roslyn не являются обязательными target: владелец 2026-10-03
уточнил, что корпуса были выбраны случайно. Их результаты остаются
историческим baseline. Новый кандидат — BTCPayServer `v2.4.3`, основной
`.csproj` со всеми транзитивными ссылками; пин и протокол заданы в
[baseline benchmark](../baseline-benchmark.md). Admission и достаточность этой
нагрузки для U-ARB-03 требуют отдельной проверки. Новый прогон не переносит
на другой корпус численный baseline офисного ПК.

## Приёмка

- Host и capability/write contract выбраны либо production route остановлен.
- Есть две таблицы: hydration data и admission evidence.
- Для каждого profile отдельно записаны:
  `positive equivalence passed`, `negative admission passed`, `not run`.
- Unknown condition/custom target и absent path becoming present отвергаются
  до hydrate/publication; unsupported не засчитывается как equivalence.
- Fresh MSBuild oracle сравнивает graph, options, memberships, references,
  diagnostics, navigation, generated output, encoding и persistence.
- Functional fixture без target workload разрешает только `experiment allowed`.
- Workload и budget утверждены до результатов для `public activation allowed`.

## Handoff

Использовать общий [handoff template](../handoff-template.md). Отдельно указать
verdict для experiment, implementation, public activation, next epoch и series
completion. Host и persistence выбраны; metadata comparator имеет отдельный
verdict. Public activation этим не открывается.

Trace: H-01 — ACCEPT WITH MODIFICATION.
