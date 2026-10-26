# BuildHost/task-04 — Извлечение ProjectFileInfo

Дата: 2026-10-26. **In progress: локальная часть шага 0 выполнена; офисные матрицы получены без новой разбивки
extraction, поэтому измерения и приёмка шага 0 ещё не завершены.**
Оптимизационного кандидата пока нет, порог go ещё не задан. Production-поведение extraction сохранено.

## Объём изменения

В `ProjectInstanceReader` добавлены дочерние spans существующего `extract`:

- `extract-compiler-args`: `GetCompilerCommandLineArgs`, включая материализацию массива; `arguments`.
- `extract-fallback-args`: только вызванный fallback `ReadCommandLineArgs`; `arguments`.
- `extract-properties`: исходное чтение свойств и преобразование выходных путей; `targetFramework`.
- `extract-documents`: исходные фильтрация документов, пути, `Link`, logical path и folders; `documents`.
- `extract-additional-editorconfig`: исходные преобразования обоих наборов; `additionalDocuments`, `analyzerConfigDocuments`.
- `extract-items`: package/metadata references, capabilities, content, code page и checksum;
  `packageReferences`, `metadataReferences`, `projectCapabilities`, `contentFiles`.
- `extract-globs`: весь исходный `Project?.GetAllGlobs().Select(GetFileGlobs).ToArray()`;
  `fileGlobs`, `includes`, `excludes`, `removes`, `imports` текущего `Project`.
- `extract-result`: исходное создание ответа, включая project references; `projectReferences`.

Вычисления и их порядок не менялись; контракт и comparator не изменены. Всё включается существующей
`ROSLYN_BUILDHOST_TRACE_DIRECTORY`. Без неё `Start` возвращает null; аргументы `span?.Set`, в том числе суммирование
patterns и чтение imports, не вычисляются. Ошибки записи по-прежнему обрабатывает `BuildHostTrace`.

`WallBreakdown` даёт частям отдельные категории, которые существующий `trace-summary` автоматически агрегирует.
Категории — собственное время, без повторного счёта родителей. Строка `host: ProjectFileInfo extraction` теперь
означает остаток родительского `extract`, включая запись дочерних spans; для сравнения со старой трассой нужно
сложить её с `host: extraction / ...`. Таблица проектов в `trace-report` сохраняет **полное** время `extract`.
Сумма медиан отдельных категорий не обязана совпадать с медианой полного extraction.

Особенность исходного контракта: после multi-TFM сборок `Project` уже восстановлен к внешней конфигурации.
В фикстуре оба результата Lib читают пустые globs этого внешнего `Project`, а не globs своего TFM. Это наблюдение,
не изменение поведения и не разрешение переопределить поля ответа.

## Проверки

- Release fork и spike: build succeeded, 0 warnings/errors.
- `Test-BuildHostTrace.ps1`: **74/74**. Проверены семь обязательных частей на каждом из шести результатов,
  отдельный fallback после намеренной ошибки до `CoreCompile`, счётчики, границы parent/child, сумма собственного
  времени до окна загрузки, одинаковые workspace/diagnostics с трассой, без трассы и при недоступном каталоге трассы.
  В исходном comparator дополнительно прошли 19 self-checks.
- Release solution build: succeeded. Предупреждения в неизменённых `NuGetFallbackAssemblyResolver.cs` (CS8603),
  `HostSession.cs` (CS8601), `WpfTemporaryProjectCallbackTests.cs` (xUnit1031).
- Windows main по фильтру CI: три запуска **1466 passed / 1 failed / 0 skipped**; все **123 SourceStructure passed**.
  Падает неизменённый `DeclarationSearchTimingTests.Unreadable_document_reads_are_attributed_to_their_project`:
  в журнале first-chance исключений нет ожидаемой группы Broken, присутствуют ошибки чтения XML reference packs
  других тестов; в одном повторе также Win32Exception. Изолированный запуск теста **1/1 passed**. Третий полный
  запуск вне sandbox дал тот же сбой. Эти прогоны не считаются зелёным main.
- Повтор **всего** main с тем же CI-фильтром и `-- xUnit.ParallelizeTestCollections=false`:
  **1467 passed / 0 failed / 0 skipped**, 4 мин 15 с. Код, фильтр и настройки репозитория не менялись.
  `SearchExceptionRecorder` слушает process-wide `FirstChanceException`, а в JSON оставляет 20 групп с наибольшим
  count; фоновые исключения соседних тестов могут вытеснить ожидаемую группу с шестью чтениями. Отключение
  параллелизма устраняет этот источник помех; обычный параллельный прогон остаётся с воспроизведённым сбоем.
  Исходные TRX — `TestResults/task-04/`; компактные счётчики и hashes — [validation.json](task-04/validation.json).
- `AnalyzerLifecycle`, `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`: **13 passed / 0 failed / 0 skipped**.
- [Сценарии task-00, Windows, A + B](task-04/windows-scenarios.md): **all checks passed**. Фикстура с ошибками,
  missing/unsupported и multi-TFM — identical (прогрев + три раунда), диагностики совпадают поэлементно в порядке
  выдачи. Нет тихого fallback при удалённом host DLL; отмена в ходе загрузки и kill ведут себя как original.
  Ранняя отмена оставляет по одному host у обеих сторон — прежнее ограничение upstream из task-00b; runner
  фиксирует и затем завершает эти процессы.

Исходная трассовая фикстура и её лог — ignored
`spikes/BuildHostFork/results/trace-check-0c755e80e300461abee3db77b9701bef/` и `results/task-04-trace-check.log`.

## Linux: identical с трассой

Ubuntu 24.04 (WSL), SDK 10.0.113, MSBuild 18.0.36, runtime 10.0.13; `RoslynMcpServer.sln`, A + B (4 потока),
прогрев + три измеренных fresh-process раунда против пакетного host 5.9.0. Для защиты Windows outputs использована
изолированная копия tracked HEAD с текущими изменёнными C# файлами в ignored `results/task-04-linux/source`.
Посторонние untracked файлы рабочего каталога в неё не включались. Отдельный bash-runner использован потому,
что в WSL нет PowerShell; устанавливать дополнительные инструменты не потребовалось.

[Сравнение](task-04/linux-summary.md): **identical**, диагностик в другом порядке нет, первые поиски совпадают,
чужих и оставшихся BuildHost нет. [Трасса](task-04/linux-trace-summary.md): 40 согласованных запросов,
0 вне допуска, 0 нетрассированных операций, полные traces во всех раундах; 5 проектов, 487 документов.

Медиана окна `OpenSolutionAsync` — 7630 мс; `GetAllGlobs` — 3230 мс (**42,3 % окна**), документы — 2 мс,
каждая из остальных частей — около 1 мс или меньше. Это указывает дорогую часть на этой среде; 3230 мс — только
идеальный верхний предел устранения всей её работы, не обещание ускорения. Путь находится на `/mnt/e` (DrvFS),
поэтому стоимость файловой системы WSL не переносится на native Linux или Windows. Нагрузочные проверки в это
время тоже выполнялись; Linux-прогон подтверждает корректность, а не порог производительности кандидата.

В раунде 1 сервер (242 документа, 125 imports) тратит на globs 2260 мс, Tests (228, 132) — 330 мс,
LifecycleTestHost (8, 123) — 200 мс, DeadlockWitnessHost (4, 113) — 167 мс, Plugin (5, 123) — 198 мс.
Близкое число imports и документов не объясняет разницу само по себе; состав glob expressions и дерево каталогов
тоже различаются. Причинная зависимость от документов, TFM и imports пока не установлена.

Сырые результаты и runner остаются в ignored `spikes/BuildHostFork/results/task-04-linux*`.

## Windows: Roslyn.slnx, A + B с трассой и без

Windows 10.0.26200, Ryzen 7 9800X3D (16 логических процессоров), 93,7 GiB RAM; решение на commit
`013d3a758df6c137497ff37a93f0d4bed103853a`, закреплённый SDK 10.0.301. Сервер, fork и spike собраны SDK 10.0.402.
Команда — `Invoke-BuildHostForkMatrix.ps1 -Variants fork-ab,fork-ab-trace`, остальные параметры по умолчанию,
три измеренных раунда плюс прогрев. Запуск начался после завершения тестов и Linux-проверки.

- [Матрица](task-04/windows-roslyn-matrix.md): **оба варианта identical** против пакетного original; natural и
  unexplained differences — 0, первые поиски совпадают, host из своей папки, оставшихся процессов нет.
  Диагностики совпадают поэлементно и в порядке выдачи во всех измеренных раундах. 44 workspace failures
  присутствуют у обеих сторон; это не безошибочная загрузка решения, но форк новых ошибок не внёс.
- [A + B](task-04/windows-roslyn-fork-ab-summary.md): медиана загрузки **38877 мс**, original — 47966 мс (×0,810).
- [A + B с трассой](task-04/windows-roslyn-fork-ab-trace-summary.md): **39086 мс**, ×1,0054 к A + B.
  Диапазоны 38503–39715 и 39024–39463 мс пересекаются: наблюдаемая разница **+0,54 %**, отдельно от разброса
  не установлена. Медиана пика host 1521 против 1526 MB (×0,997), клиента — 762 против 757 MB (×1,007).
  Это цена инструментирования, не результат оптимизации extraction.
- [Трасса](task-04/windows-roslyn-trace-summary.md): все четыре раунда complete; 2544 согласованных запроса,
  0 вне допуска, 0 нетрассированных операций. Медиана собственно окна `OpenSolutionAsync` — **38756 мс**;
  метрика load стенда выше неё, поскольку охватывает ещё создание workspace и подготовку загрузчика.
- `GetAllGlobs` с преобразованием результата — **8389 мс, 21,6 % окна** (8247–8572 мс); медиана полного
  `extract` из сырых spans — **8593 мс**, то есть globs занимают около **97,6 % extraction**. Документы с
  метаданными — 151 мс, additional/editorconfig — 12 мс, остальные items/properties — 12 мс,
  compiler args — 6 мс, fallback — 2 мс, свойства — 4 мс, создание ответа с project references — 3 мс.

[Счётчики и суммы по раундам](task-04/windows-roslyn-extraction-counts.json):

- 434 результата по 318 путям; 114 путей с несколькими TFM. Медиана globs у single-TFM путей — 6246 мс,
  у всех результатов multi-TFM путей — 2143 мс.
- Все **повторные** обращения к globs одного пути после первого стоят вместе всего **21–31 мс** за раунд
  (медиана 24 мс, около **0,06 % окна**). Поэтому гипотеза «посчитать globs один раз на все TFM» на этом
  решении почти ничего не даёт. Причина дешевизны повторов этой трассой не устанавливается.
- У 136 результатов globs пустые; их обработка всё равно занимает 1249–1329 мс за раунд. Пустой результат
  не доказывает, что его получение бесплатно, и не разрешает заменить его константой.
- Число imports — 119–215. В раунде 1 самый дорогой вызов — 169 мс при 690 документах, 198 imports и 14 globs;
  следующий — 156 мс при 861, 199 и 15. Среди дорогих вызовов есть 104 мс при 186 документах и 128 imports.
  Это наблюдение о составе, не доказательство причинной зависимости от количества документов или imports.

**Локальный вывод шага 0:** дорогая часть определена — первое чтение globs каждого проекта с преобразованием
результата. Идеальный верхний предел устранения всей этой работы на потоке запросов — около **8,39 с / 21,6 %**
окна; это не разрешение отбросить поле и не прогноз реального выигрыша. Верхний предел устранения остальных
частей extraction — лишь около 0,2 с. Простое переиспользование между TFM не выглядит полезным кандидатом.
Следующее обоснованное исследование — внутренности первого получения globs или перенос этого чтения с потока
запросов при доказанной стабильности `Project`; оптимизация пока не написана. Итог по офису и порог go остаются
pending по постановке task-04.

Сырые observations/traces — ignored `spikes/BuildHostFork/results/task-04-roslyn/`;
[параметры среды](task-04/windows-roslyn-environment.json) сохранены без локальных путей проектов.

Исходники замеров: tracked base `0052647545e4103fd31e1c2de676fb2b0be42f6e` с правками task-04.
SHA-256 изменённых C# файлов (эти же файлы перенесены в Linux-копию):

```text
ace9a9940e68c0bf281d04d23e32f9dffb3271ed17895d180a51a2b8fc5c0b52  ProjectInstanceReader.cs
59006f0ebd872215e215bf5e9de1c8ec4e1331dc91aff227ab21d50ca8b899e1  WallBreakdown.cs
```

## Команды локальной валидации

```powershell
./spikes/BuildHostFork/Test-BuildHostTrace.ps1
dotnet build RoslynMcpServer.sln -c Release
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --no-restore `
  --filter 'Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk' `
  --logger 'trx;LogFileName=task-04-main-serial.trx' --results-directory TestResults/task-04 `
  -- xUnit.ParallelizeTestCollections=false
$env:ROSLYN_MCP_ANALYZER_LIFECYCLE = '1'
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --no-restore `
  --filter 'Category=AnalyzerLifecycle' `
  --logger 'trx;LogFileName=task-04-lifecycle.trx' --results-directory TestResults/task-04
Remove-Item Env:ROSLYN_MCP_ANALYZER_LIFECYCLE
./spikes/BuildHostFork/Invoke-BuildHostForkScenarios.ps1 -ReferencesFirst -EvaluateAhead 4 `
  -OutDirectory spikes/BuildHostFork/results/task-04-scenarios
./spikes/BuildHostFork/Invoke-BuildHostForkMatrix.ps1 -Solution <Roslyn.slnx> `
  -Variants fork-ab,fork-ab-trace -OutDirectory spikes/BuildHostFork/results/task-04-roslyn
```

## Исправление относительных путей стенда, 2026-10-26

Офисный запуск с явно переданным относительным `OutDirectory` дошёл до restore, но упал при записи
`environment.json`: `New-Item` создавал каталог относительно текущей location PowerShell, а
`[IO.File]::WriteAllText` отсчитывал тот же относительный путь от каталога процесса. Эти каталоги могут
различаться после `Set-Location`. До task-04 matrix-скрипт тоже имел этот дефект; пример команды проявил его.

В `Invoke-BuildHostForkMatrix.ps1`, `Invoke-BuildHostForkBaseline.ps1` и `Invoke-BuildHostForkScenarios.ps1`
`Solution` и `OutDirectory` теперь превращаются в абсолютные пути через
`$ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath`. Это путь от текущей location
PowerShell, включая ещё не созданный каталог. Автоматические имена `results/matrix-<time>`, `baseline-<time>`
и `scenarios-<time>` по-прежнему вычисляются от `$PSScriptRoot`; передавать `OutDirectory` не требуется.
Код C#, расчёт `ProjectFileInfo` и правила сравнения этим исправлением не менялись.

Поведенческая проверка намеренно оставляет PowerShell в корне репозитория, а process directory переводит
в системный temp; в таком окружении выполнены реальные скрипты:

- Matrix с относительными `Solution`/`OutDirectory`, A + B с трассой и без: **оба варианта identical**;
  созданы `environment.json`, `matrix.md`, обе `summary.md` и `trace-summary.md` в нужном каталоге.
- Matrix **без `OutDirectory`**, с абсолютным `Solution`: **identical**, результат автоматически создан в
  `spikes/BuildHostFork/results/matrix-20261026-185025/`, включая `trace-summary.md`.
- Baseline с относительными обоими путями и трассой A + B: **identical**, все три отчётных файла на месте.
- Scenarios с относительными обоими путями и A + B: **all checks passed**.

Matrix/baseline здесь используют прогрев + один измеренный раунд на трассовой фикстуре; scenarios — обычный
полный сценарный прогон. Это функциональная проверка путей, не новые performance-замеры. Runner, лог и сырые
результаты — ignored `spikes/BuildHostFork/results/task-04-path-check*` и `task-04-paths-*`.
Разрешение пути дополнительно проверено в **Windows PowerShell 5.1.26100.9711**. Release solution build после
исправления успешен; прежнее предупреждение CS8603 в неизменённом `NuGetFallbackAssemblyResolver.cs`.
Повторный полный Release main с CI-фильтром и `xUnit.ParallelizeTestCollections=false` после исправления:
**1467 passed / 0 failed / 0 skipped**, включая **123 SourceStructure**; 4 мин 17 с.
TRX — `TestResults/task-04/task-04-path-fix-main.trx`, счётчики и hash добавлены в `task-04/validation.json`.

## Полученные офисные матрицы, 2026-10-26

Владелец передал `E:\temp\spikes\matrix-20261026-184930` (Roslyn.slnx) и `matrix-20261026-192401`
(офисное решение). Оба прогона — original, A + B и A + B с трассой, прогрев + три измеренных раунда;
Intel Core i5-9500, 6 логических процессоров, 63,8 GiB RAM. Копии сводок ниже не содержат офисных имён и путей.

- **Roslyn.slnx:** [матрица](task-04/office-pc-roslyn-matrix.md),
  [A + B](task-04/office-pc-roslyn-fork-ab-summary.md),
  [A + B с трассой](task-04/office-pc-roslyn-fork-ab-trace-summary.md),
  [сводка трассы](task-04/office-pc-roslyn-trace-summary.md),
  [среда](task-04/office-pc-roslyn-environment.json).
  Оба варианта **identical**, без natural/unexplained differences, диагностики совпадают в порядке выдачи,
  первые поиски совпадают, оставшихся host нет. Original 108120 мс, A + B 84537 мс (×0,782), с трассой 85932 мс
  (×1,0165 к A + B). Общее extraction — 19025 мс, **22,3 %** окна; разбиения внутри него нет.
  Пик host у A + B 1671 против 1487 MB original (**+12,4 %**); у traced A + B — 1661 MB. Это измерение
  существующего A + B, а не памяти нового кандидата task-04.
- **Офисное решение:** [матрица](task-04/office-solution-matrix.md),
  [A + B](task-04/office-solution-fork-ab-summary.md),
  [A + B с трассой](task-04/office-solution-fork-ab-trace-summary.md),
  [сводка трассы](task-04/office-solution-trace-summary.md),
  [среда](task-04/office-solution-environment.json).
  Оба варианта **identical** с теми же проверками ответов и процессов. Original 43702 мс, A + B 32273 мс
  (×0,738), с трассой 32433 мс (×1,0050 к A + B). Общее extraction — 10235 мс, **32,2 %** окна;
  разбиения внутри него тоже нет. Пик host — 553 MB у A + B и 561 MB у traced A + B (×1,0145).
  Два host у обеих сторон; по 10 операций каждой загрузки обслужены нетрассированным host, как в прежних
  офисных замерах. 1674 workspace failures совпадают у сторон и не объявляются успешной безошибочной загрузкой.

**Эти результаты подтверждают сравнение A + B, но не завершают шаг 0 task-04.**
[Инвентаризация новой инструментации](task-04/office-extraction-instrumentation-inventory.json): во всех четырёх
Roslyn JSONL есть по **434 `extract`, 0 `extract-*` и 0 дочерних spans extraction**. Значит, в этих загрузках
host не выполнял новую инструментацию `ProjectInstanceReader`. Это не просто старое имя категории в formatter:
недостающих измерений нет в сырых данных, и пересчёт `trace-summary` их не восстановит.

Для офисного решения в переданном комплекте есть `report.json`/`report.md`, но нет сырых host JSONL;
во всех отчётах также только общий `host: ProjectFileInfo extraction`. Из них невозможно определить, какая
часть extraction дорогая. `spikeCommit=0052647545e4` и версия `5.9.0-fork+35d9211...` совпадают и у локального
стенда с незакоммиченными правками, поэтому сами по себе не доказывают наличие изменений task-04 в сборке.

Подготовлен ignored-пакет `spikes/BuildHostFork/results/task-04-office-update.patch`: текущие изменения только
`ProjectInstanceReader.cs`, `WallBreakdown.cs`, `Test-BuildHostTrace.ps1` и трёх runner-скриптов. SHA-256:
`6d5702a68b906177e610827e80df9c65da71b8c7830d0ee34202c6ee3967d653`.
Проверены обратная применимость к рабочим файлам и прямая применимость к изолированной копии tracked HEAD;
после применения все шесть файлов совпадают с проверенными локальными с учётом LF/CRLF.
По решению владельца от 2026-10-26 основной способ передачи — **коммит в исследовательской ветке Git**;
пакет больше не требуется. Инструментация, проверки, исправление путей и evidence входят в один коммит.

## Что требуется до завершения шага 0

На офисном компьютере обновить ветку `research/workspace-load-performance` до коммита с инструментацией
task-04. После доставки коммита в remote использовать обычное обновление Git. Затем выполнить быструю
трассовую фикстуру и повторить матрицу **на обоих решениях**:

```powershell
git pull --ff-only
./spikes/BuildHostFork/Test-BuildHostTrace.ps1
./spikes/BuildHostFork/Invoke-BuildHostForkMatrix.ps1 -Solution <Roslyn.slnx> -Variants fork-ab,fork-ab-trace
./spikes/BuildHostFork/Invoke-BuildHostForkMatrix.ps1 -Solution <office.sln> -Variants fork-ab,fork-ab-trace
```

Трассовая фикстура должна завершиться `Trace checks passed` и проверить `extract-globs` и остальные
части; именно это подтверждает сборку инструментации до длинных измерений. `OutDirectory` можно не передавать.

Каждый прогон включает пакетный original, прогрев и три измеренных раунда. Нужны `matrix.md`,
`fork-ab/summary.md`, `fork-ab-trace/summary.md`, `fork-ab-trace/trace-summary.md`; для зависимости стоимости от
состава проекта — сырые spans с counts (офисные пути остаются у владельца). Затем фиксируются дорогая часть,
её доля, верхняя граница выигрыша и **порог go до реализации** одного кандидата. Если кандидат меняет расчёт
поля вне `SolutionObservation`, сначала расширяется сравнение ответа host. Production-активация — отдельное
решение владельца; task-04 пока не accepted.
