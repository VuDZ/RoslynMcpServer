# BuildHost/task-04 — Извлечение ProjectFileInfo

Дата: 2026-10-26. **In progress: шаг 0 выполнен, включая повторные офисные матрицы с новой инструментацией.**
Предварительный `RecordEvaluatedItemElements` — no-go по корректности. В стенде реализован эксперимент
**server GC + ConserveMemory=9 только в host**; вычисления `ProjectFileInfo` сохранены, полный RPC проверен.
Квалификация кандидата по офисным порогам и production-активация ещё не выполнены.

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
запросов при доказанной стабильности `Project`; оптимизация пока не написана. Корректные офисные матрицы и
общий итог шага 0 приведены ниже.

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

## Первые офисные матрицы без новой разбивки, 2026-10-26

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

## Повторные офисные матрицы с инструментацией, 2026-10-26

Владелец передал `matrix-20261026-195532` (Roslyn.slnx) и `matrix-20261026-202748` (офисное решение),
обе на commit **15dbfa6093a6**, тот же i5-9500. Новые сводки сохраняются отдельно от первых:

- **Roslyn.slnx:** [матрица](task-04/office-pc-roslyn-step0-matrix.md),
  [A + B](task-04/office-pc-roslyn-step0-fork-ab-summary.md),
  [A + B с трассой](task-04/office-pc-roslyn-step0-fork-ab-trace-summary.md),
  [трасса](task-04/office-pc-roslyn-step0-trace-summary.md),
  [среда](task-04/office-pc-roslyn-step0-environment.json).
  Оба варианта **identical**, natural/unexplained differences — 0, диагностики совпадают в порядке выдачи,
  первые поиски совпадают, оставшихся host нет. Original **107250 мс**, A + B **84304 мс** (×0,786),
  A + B с трассой **85061 мс** (×1,0090 к A + B). Диапазоны A + B и traced пересекаются; +0,90 % —
  наблюдаемая разница медиан, а не доказанная изолированная цена под-span.
  Пики host 1662 и 1660 MB, клиента 732 и 765 MB (×1,045 для traced клиента).
  Сам A + B по пику host выше original 1466 MB на **13,4 %**; это исходная точка сравнения будущего кандидата.
- **Офисное решение:** [матрица](task-04/office-solution-step0-matrix.md),
  [A + B](task-04/office-solution-step0-fork-ab-summary.md),
  [A + B с трассой](task-04/office-solution-step0-fork-ab-trace-summary.md),
  [трасса](task-04/office-solution-step0-trace-summary.md),
  [среда](task-04/office-solution-step0-environment.json).
  Оба варианта **identical** с теми же проверками. Original **43341 мс**, A + B **31966 мс** (×0,738),
  traced **32069 мс** (×1,0032 к A + B). Пики host 563 и 567 MB, клиента 271 и 273 MB.
  Первый поиск у traced — 31285 против 28969 мс A + B (**+8,0 %**); этот сдвиг не скрывается за небольшой
  разницей load. Медиана **суммы load + first search каждого раунда** — 60761 мс у A + B и 63354 мс traced
  (**+4,3 %**). У Roslyn.slnx — 127931 и 128914 мс (**+0,77 %**).

[Сырые метрики раундов без путей](task-04/office-step0-run-metrics.json) сохраняют load, первый поиск,
их сумму и hash результата поиска. Оба решения сняты с прогревом и тремя измеренными fresh-process раундами.
Сырые observations и traces остаются в переданных каталогах `E:\temp\spikes`; офисные имена не публикуются.

### Полнота и стоимость частей

[Инвентарь и счётчики](task-04/office-step0-extraction-counts.json): во всех восьми раундах traces complete,
часы согласованы, под-span находятся внутри родительских `extract`, обязательные части не потеряны.
Roslyn: **434 extraction**, **2990 под-span**, 318 путей, 114 multi-TFM путей; 49 VB-результатов не имеют
`extract-compiler-args`, потому что клиент не объявляет VB command line parser и provider у них null — это
предусмотренный ранний возврат, а не потерянная трасса. Офис: **137 extraction**, **959 под-span**;
5 остальных проектов обслужены пакетным net472-host, по 10 нетрассированных операций на раунд. Как раньше,
44 и 1674 workspace failures совпадают с original и не объявляются безошибочной загрузкой.

- **Roslyn.slnx:** `GetAllGlobs` с преобразованием — **18601 мс / 22,0 %** окна `OpenSolutionAsync`
  (медиана окна 84408 мс). Полный `extract` по сырым spans — **18898 мс**: доля globs **98,4 %**.
  Документы и metadata — 182 мс; остальные части — 3–23 мс каждая.
- **Офис:** категория `GetAllGlobs` — **9997 мс / 31,8 %** окна (31430 мс). Полное время glob spans —
  **10698 мс**, полного extraction — **10827 мс**: globs **98,8 %**. Разница с категорией — время, когда
  параллельно работали другие host spans, отнесённое `WallBreakdown` в overlap, а не потерянное измерение.
  Документы и metadata — 61 мс; остальные части — 2–4 мс каждая.
- **Повторы по TFM:** Roslyn — **39–67 мс**, медиана **45 мс** на все повторные вызовы одного пути после
  первого (около **0,05 % окна**); офис — **0**, все 137 трассируемых путей single-TFM. Кеширование только
  между TFM не даёт заметного ускорения на этих решениях.
- 136 Roslyn-результатов имеют пустые globs, но стоят вместе **2,64–2,69 с**. Imports: Roslyn 104–200,
  офис 112–135. У офисного проекта с 7 документами и 124 imports вызов стоит 204 мс, у проекта с 2985
  документами и 132 imports — 573 мс. Отдельный счётчик документов, imports или числа итоговых globs не
  объясняет цену; состав условий, item expressions и файловых входов тоже имеет значение.

### Почему первый GetAllGlobs дорогой и почему недостаточно одного флага

В `Microsoft.Build.dll` SDK **10.0.301**, file version **18.6.4.27133**, декомпиляцией проверена цепочка
`Project.GetAllGlobs` → `ProjectImpl.GetEvaluatedItemElements`: если `RecordEvaluatedItemElements` не установлен,
MSBuild добавляет его к `_loadSettings` и вызывает полную `Reevaluate`. В fork `LoadProjectCore` этот флаг при
создании `Project` не задан. Следующие вызовы уже не попадают в эту ветку, что согласуется с дешёвыми повторами.
SHA-256 исследованной DLL: `97afbadb1a9b5823ee7bfeb69890d42287334689a07e83415d0a6774c0bf5e82`.

Это доказывает наличие дополнительной evaluation, но не отдельно измеренную долю её времени: под-span
охватывает весь API и преобразование результата. Вложенная evaluation и собственно построение glob results
пока не разделены отдельными таймерами.

Проверена первая узкая гипотеза — сразу создать `Project` с `RecordEvaluatedItemElements`. **No-go по
корректности до изменения production-кода.** Контрпример `BuildOrderProbe check-globs`:

1. При начальной evaluation `marker.txt` отсутствует; item `after/**/*.cs` имеет `Condition="Exists('marker.txt')"`.
2. Design-time build создаёт marker. У обоих вариантов результат сборки содержит одинаковый `before/Before.cs`,
   а `Project.IsDirty` остаётся **false**.
3. Без раннего флага `GetAllGlobs` выполняет позднюю evaluation и возвращает `before/**/*.cs` **и** `after/**/*.cs`.
   С ранним флагом возвращается только `before/**/*.cs`.

Результат воспроизведён на [SDK 10.0.301 / MSBuild 18.6.4](task-04/record-item-elements-sdk10.0.301.json) и
[SDK 10.0.402 / MSBuild 18.9.15](task-04/record-item-elements-sdk10.0.402.json). Исходники —
`spikes/BuildOrderProbe/RecordItemElementsProbe.cs`; fixture генерируется в отдельном каталоге, проверки
документов, количества globs и `IsDirty` выполняются самой командой:

```powershell
dotnet build spikes/BuildOrderProbe -c Release
dotnet spikes/BuildOrderProbe/bin/Release/net10.0/BuildOrderProbe.dll check-globs <Roslyn.slnx> <output.json>
```

Это проверка публичного MSBuild API на контролируемом входе, **не** сравнение полных RPC-ответов. Уже этого
контрпримера достаточно отвергнуть безусловный ранний флаг: он меняет поле `FileGlobs`, которое старый
workspace comparator не захватывает. Проверка `IsDirty` не исправляет расхождение. Оптимизация, переключатель
и production-активация не добавлены; полное сравнение ответа host остаётся обязательным для будущего кандидата.

Release build spike и решения успешны; полный main с прежним CI-фильтром и
`xUnit.ParallelizeTestCollections=false` — **1467/0/0**, включая **123 SourceStructure**; первый запуск —
4 мин 23 с, окончательный — 4 мин 16 с. TRX `task-04-step0-main.trx` и `task-04-step0-final-main.trx`
включены в `validation.json`. `AnalyzerLifecycle` повторно не запускался:
после предыдущего успешного 13/0/0 изменена только исследовательская проба, production-код не менялся.

## Итог шага 0 и пороги следующего кандидата

**Шаг 0 выполнен, 2026-10-26:** обязательные решения и сценарии проверены; дорогая часть — первое получение
globs. Неперекрытый бюджет — **18,60 с / 22,0 %** на офисной Roslyn.slnx и **10,00 с / 31,8 %** в офисе;
с учётом перекрытия полное время globs в офисе — 10,70 с, около **34,0 %** окна. Эти значения — идеальные
верхние границы снимаемой работы при неизменной остальной загрузке, а не прогноз speedup. В остальных частях
extraction осталось лишь около 0,30 и 0,13 с; повторы между TFM и ранний безусловный Record — не полезные
совместимые кандидаты по разным причинам: первый почти ничего не выигрывает, второй меняет ответ.

До production-кода следующего кандидата фиксируются пороги **к A + B без трассы**, прогрев + минимум три
измеренных fresh-process раунда: **офисная загрузка быстрее не менее чем на 10 %**, Roslyn.slnx не медленнее
более чем на **2 %**, identical по task-00 и по затронутым полям RPC, те же диагностики и сценарии сбоев.
Load + first useful query учитывается отдельно по каждому раунду. Пик памяти host и клиента **+10 % —
ориентир**, как решил владелец; отклонения записываются явно. Production-активация остаётся отдельным решением.

**Task-04 остаётся in progress.** Следующий эксперимент сохраняет позднюю evaluation и все её входы;
результаты поиска и область проверки приведены ниже. Это не приёмка всей задачи и не production-активация.


## Следующий эксперимент: GC только в host, 2026-10-26

[CPU-профиль](task-04/getallglobs-cpu.json) снят через `dotnet-trace`, профиль
`dotnet-sampled-thread-time`: отдельная `HostSession`, проекты Roslyn.slnx в порядке решения, вызов
`GetAllGlobs` после design-time builds, SDK 10.0.301 / MSBuild 18.6.4, runtime 10.0.13. Это диагностическая
проба, не A+B-матрица. На 318 проектах секундомер дал **8175 мс**, sampled thread time — **8188 мс**;
98,6 % samples проходят через `Reevaluate`, 54,1 % — SDK resolution, 52,1 % — workload SDK resolver,
36,4 % — загрузку workload-манифестов. Стек `PollGC` присутствует в 15,5 % samples. Это вложенные доли,
их нельзя складывать или выдавать за отдельно измеренные GC pauses.

Общий `EvaluationContext`/`SharedSDKCache` не включён: `CachingSdkResolverService` кеширует по submission ID
и имени SDK, тогда как resolver получает ещё путь проекта, SDK version и другие входы. Это не доказательство
совпадения resolution для разных проектов. Общий файловый кеш также может скрыть созданные build файлы.
Изменять эти границы ради ускорения нельзя без дополнительного доказательства совместимости.

Выбран эксперимент, сохраняющий алгоритм и момент чтения: отдельная копия `bin-fork-gc`, в runtimeconfig
**только netcore BuildHost** добавлены `System.GC.Server=true` и `System.GC.ConserveMemory=9`. Клиент,
net472-host, DLL форка, `ProjectLoadSettings`, global properties и вычисления extraction не меняются.
Настройка через runtimeconfig также не добавляет GC-переменные в окружение MSBuild. По
[документации .NET](https://learn.microsoft.com/dotnet/core/runtime-config/garbage-collector)
`ConserveMemory` уменьшает heap ценой более частых collections; значение 9 — самый сильный режим экономии.
Это настройка опытной копии, не новая настройка production-сервера.

`Invoke-BuildHostForkMatrix.ps1` принимает opt-in варианты `fork-ab-gc` и `fork-ab-gc-trace`; прежние пять
вариантов и автоматический каталог результатов сохранены. GC-копию создаёт `Initialize-GcForkCopy`,
проверяющий путь удаления и конфликтующие overrides Server/ConserveMemory. В сценариях тот же профиль
включается `-ServerGc`, независимо от A/B. Без этих вариантов/переключателя GC форка прежний.

### Полный ответ ProjectFileInfo

В `spikes/BuildHostFork/Rpc/` добавлен последовательный клиент исходного named-pipe JSON протокола:
`rpc-capture HOST_DLL SOLUTION OUTPUT_JSON`. Он читает C#/VB записи решения и ссылки на C#/VB проекты,
запрашивает полный массив `GetProjectFileInfosAsync` и диагностики, проверяет завершение host и только
после этого записывает результат. Параметры runtime launch совпадают с Roslyn: `--roll-forward LatestMajor`,
`DOTNET_ROLL_FORWARD_TO_PRERELEASE=1`, без наследованного `MSBUILD_EXE_PATH`. Культура контрольных RPC — en-US.
Это отдельная проверка netcore-контракта; она не воспроизводит выбор net472-host и все операции workspace.
Прежний `ObservationComparer` не изменён и остаётся обязательным.

`rpc-compare LEFT RIGHT` сравнивает **все** JSON-поля, отсутствие/null/пустую строку, регистр и порядок
массивов, включая диагностики. Literal ordered-array comparison **не identical даже между двумя original**:
MSBuild формирует `Excludes` через `ToImmutableHashSet`, а порядок exclude/remove patterns меняется между
процессами. Сырые ответы и raw differences сохранены. Отдельный явный режим
`rpc-compare LEFT RIGHT glob-sets` сортирует только `FileGlobs.Excludes`/`Removes` по ordinal, сохраняя
дубликаты и регистр. Includes, порядок glob results, документы, folders, references и все остальные массивы
остаются упорядоченными. Никакого преобразования ответа host нет.

[Полная Roslyn.slnx](task-04/servergc9-rpc.json): пакетный host, повтор пакетного host, A+B и A+B+GC9 —
по **318 проектов, 434 ProjectFileInfo, 1363 glob results**, 24 имени поля в объединении ответов, по 3
host diagnostics. После сортировки только двух glob-наборов **0 отличий во всех полях** у каждой стороны.
Raw comparisons: 4420 отличающихся значений у original-repeat, 4741 у A+B и 4608 у A+B+GC9; все эти
различия исчезают исключительно при указанной сортировке. Это семантическое совпадение полных ответов,
а не заявление о побайтово одинаковых JSON. Workspace failures (44 в Roslyn) остаются отдельной метрикой.

`rpc-check` — **31 self-check**, включая изменение каждого из 24 полей, nested folders, порядок glob arrays,
missing/null/empty, изменение glob pattern. `Test-ProjectFileInfo.ps1` — **23 assertions + 3 полных сравнения**:
build создаёт marker, начальные документы не содержат After.cs, поздние globs содержат after/**/*.cs у
original, original-repeat, A+B и GC9; проверены Link/folders, additional/editorconfig/content и capability.
Все проверки пройдены. Фикстура генерируется заново, marker удаляется перед каждым независимым запуском.

### Контроль замера

Предварительные серии `task-04-servergc`, `task-04-servergc4` и timed rounds `task-04-servergc9` **исключены
из оценки эффекта GC**: общий `Invoke-ObserveRounds` применяет `ForkVariables` только к стороне `fork`,
и их контроль фактически имел A/B off. Значения −25 % и 1,45 → 1,71 ГБ из предварительного сообщения не
изолируют GC. [Причина и замена](task-04/discarded-gc-controls.json). Данные не скрыты и не переименованы
в корректный контроль. У последней серии пригодны отдельные парные traces и RPC: там `Invoke-Spike(..., true)`
явно применял A+B к обеим fork-сторонам.

Квалифицирующая домашняя серия — `task-04-servergc9-ab`: на каждой стороне каждого раунда явно применены
**A=true, B=4, solution entries=all**, один и тот же host DLL (SHA-256 в receipt). Прогрев + три измеренных
fresh-process раунда, чередование порядка, без трассы; конфигурация каждого запуска сохранена отдельно.
Процессор Ryzen 7 9800X3D (8 cores / 16 logical), 93,7 GiB RAM, Windows 11, публичная Roslyn.slnx
013d3a758df6c137497ff37a93f0d4bed103853a, SDK 10.0.301, runtime 10.0.13.

[Итог исправленной серии](task-04/servergc9-ab-summary.md) и [метрики/конфигурации всех раундов](task-04/servergc9-run-metrics.json):
**загрузка 39012 → 36669 мс (−6,0 %)**. Медиана суммы load + first useful query каждого раунда —
**56635 → 54080 мс (−4,5 %)**; первый поиск отдельно — 17425 → 17418 мс (−0,04 %).
Пик host **1518 → 1725 MB (+13,6 %)**, клиента 755 → 732 MB (−3,2 %).
Host CPU (нижняя граница sampler) **61156 → 68828 мс (+12,5 %)**: ускорение wall получено с большей
ценой CPU. Workspace **identical**, natural/unexplained differences — 0, диагностики в исходном порядке
совпадают, ответы первого поиска одинаковы, чужих/оставшихся host нет. Домашний критерий Roslyn.slnx
«не медленнее более чем на 2 %» пройден; это не проверка офисного критерия ≥10 %.

Единственная **диагностическая** пара трасс с A+B на обеих сторонах:
[A+B](task-04/servergc9-trace-original.md) → [A+B+GC9](task-04/servergc9-trace-fork.md):
окно 39199 → 35995 мс, `GetAllGlobs` **8663 → 8232 мс (−5,0 %)**, design-time builds 11130 → 9738 мс,
TFM evaluation 9854 → 9460 мс. Ускорение распределено по host, оно не приписывается целиком globs.
Обе трассы complete, 636 согласованных запросов, 0 вне допуска; snapshots workspace, диагностики и первый
поиск совпадают. По одной паре нельзя подтверждать устойчивую долю ускорения именно `GetAllGlobs`.

Владелец 2026-10-26 счёл расход порядка **1,7 против 1,45 ГБ приемлемым**. Это согласование диапазона памяти,
а не разрешение активировать профиль в production и не приёмка офисного порога скорости.

### Итоговая валидация стенда

- Release build форка и spike — без warnings/errors; Release solution build — успешен с прежними
  CS8603, CS8601 и xUnit1031 в неизменённых файлах.
- Полный main с CI-фильтром `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`, Release,
  `xUnit.ParallelizeTestCollections=false`: **1467 passed / 0 failed / 0 skipped**, включая
  **123 SourceStructure**, 4 мин 22 с. TRX `task-04-gc-main.trx`, hash и счётчики —
  [validation.json](task-04/validation.json). Причина serial execution прежняя: process-wide first-chance
  recorder в неизменённом тесте; обычный parallel main в этой итерации повторно не запускался.
- `Test-ProjectFileInfo.ps1` полностью прошёл в PowerShell 7 **и Windows PowerShell 5.1**;
  [receipt](task-04/servergc9-checks.json). У обоих проверена поздняя файловая condition после build.
- Маленькая матрица `fork-ab,fork-ab-gc,fork-ab-gc-trace` — все workspace и полные RPC совпадают
  с указанным учётом glob set order; traces complete.
- [Сценарии task-00 с A+B+GC9](task-04/servergc9-scenarios.md): **all checks passed**, включая missing,
  broken XML, unsupported language, удалённый host DLL, cancellation и kill. Early cancel оставляет по
  одному host у обеих сторон — прежнее ограничение upstream; runner фиксирует и завершает их.
  Дополнительно полные RPC на этой failure-фикстуре: **9 проектов, 10 ProjectFileInfo, 3 empty**,
  по 10 diagnostics — совпадают после сортировки только двух glob-наборов, остальные массивы упорядочены.
- Production C#/runtimeconfig не менялись; изменён только стенд. `AnalyzerLifecycle` в этой итерации
  повторно не запускался, прежний 13/13 сохранён. Linux GC9 и офисные пороги ещё не проверены.

### Воспроизведение на офисном компьютере

После получения коммита выполнить для Roslyn.slnx и затем для офисного решения (подставить свой путь):

```powershell
.\spikes\BuildHostFork\Invoke-BuildHostForkMatrix.ps1 -Solution <путь-к-решению> -Variants fork-ab,fork-ab-gc,fork-ab-gc-trace
```

Каталог задавать не нужно. Runner сохраняет matrix/summary/trace-summary и после замеров сам делает полные
RPC captures: два original controls и каждый вариант. `rpc-*-raw.txt` сохраняет строгие отличия,
`rpc-*-glob-sets.txt` — сравнение с сортировкой только двух glob-наборов; прочие различия завершают скрипт
ошибкой. Как раньше, raw JSON/traces содержат пути решения и не публикуются без редактирования.
Порог офиса ≥10 % к A+B ещё не проверен; task-04 остаётся in progress, production-профиль прежний.
