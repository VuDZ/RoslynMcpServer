# Rapid TIA 2, task-03 и task-04 — исправления по ревью `6fc6d9e`

Дата: **2026-11-19**, Europe/Moscow. База: `6fc6d9e` (`rapid-tia`).

- **Task-03, N-03.** Ошибка открытия или чтения потока возвращает `Unreadable` без метки файла.
  Следующий запрос повторяет чтение даже после снятия блокировки без изменения размера или времени DLL.
  Успешное чтение и ошибки разбора стабильного образа сохраняют метку. Запись снимка «до правки» сохраняется.
  [Исправление](../../../../Services/Testing/Snapshots/AssemblySnapshotReader.cs).
- **Task-04.** Карта изменённых путей передаёт все найденные в `WorkspaceInputMap` роли. Для каждого владельца
  путь считается C#-исходником, если это `.cs` и все известные для владельца роли — `UserInput`. Совмещение
  с `AdditionalFile` сохраняет выбор проекта целиком, в том числе при правке только тела тестового метода.
  [Передача ролей](../../../../Services/Testing/ProjectImpactMapper.cs),
  [выбор причины](../../../../Services/Testing/TestImpactComposer.cs).

## Регрессии

1. `Current_snapshot_retries_a_temporarily_locked_assembly_without_another_build` — после завершения первого
   чтения сборка пересобирается и открывается с `FileShare.None`. Во время блокировки она `Unreadable` с
   `Inaccessible`. После снятия блокировки состояние должно стать `Read` с изменившимся типом, без ещё одной
   сборки. На исходном коде сохраняется `Unreadable`.
2. `Test_method_edit_in_a_file_also_used_as_an_additional_file_requires_project_expansion` — правка тела теста
   в compile-документе, который также входит в `AdditionalFiles`, даёт прямой тест и выбор проекта целиком с
   причиной `NonCSharpInput`. На исходном коде выбор проекта целиком отсутствует.
3. `Rebuilt_source_also_used_as_an_additional_file_keeps_the_test_project_whole` — две скомпилированные версии
   DLL, управляемые временные метки и снимки одной сессии. Оценка и сужение получают решение Roslyn с
   обновлённым текстом обоих документов. На исходном коде результат `ChangedTypesWithoutTests`, после
   исправления — `WholeProject` с `NonCSharpInput`.

Red окончательных регрессий на Ubuntu с SDK 10.0.402 — **3 ожидаемых падения из 3**, без пропусков
(`red-controlled.trx`).

В первой интеграционной пробе с живой записью и синхронизацией файла с двумя ролями прогон завис и был
остановлен на обеих платформах. Проба с реальным MSBuild и уже согласованным текстом обоих документов на
исходном коде вернула `Narrowed`. На одном из следующих Linux-прогонов эта проба упала раньше, поскольку
время DLL оказалось позже публикации baseline. В регрессии из рабочего дерева временные метки задаёт
фикстура. Она проверяет оценку и сужение с уже согласованным текстом обоих документов.
Первоначальное зависание разобрано в [проверке живой синхронизации](#живая-синхронизация-файла-с-двумя-ролями).

## Проверки

Финальные прогоны — Windows и Ubuntu 24.04 (WSL), .NET SDK **10.0.402**, Release. Linux использует отдельную
Git-копию, отдельный `TMPDIR` под `~/tmp` и `DOTNET_PROCESSOR_COUNT=2`. В обеих средах отключены NuGet audit и повторное
использование узлов MSBuild. Целевой фильтр включает Snapshots, Narrowing, ProjectExpansionReasonTests,
TestImpactComposerTests, ProjectImpactMapperTests и все 123 SourceStructure. `AnalyzerLifecycle` запущен с
`ROSLYN_MCP_ANALYZER_LIFECYCLE=1`, основной набор — с фильтрами `.github/workflows/test-suite.yml`.

Release-сборка решения прошла на обеих платформах с нулём ошибок и тремя прежними предупреждениями.

- **Windows** — основной набор **2506/2506** (4 мин 43 с), целевой **284/284**, `AnalyzerLifecycle` **13/13**.
- **Linux** — основной набор **2498/2498** (4 мин 8 с), целевой **284/284**, `AnalyzerLifecycle` **13/13**.

На каждой платформе целевой набор включал все три регрессии и 123 теста SourceStructure. Скрипт
`Assert-NoSkippedTests.ps1` подтвердил ноль пропусков и отказов во всех финальных TRX.
Логи и TRX сохранены в `TestResults/task03-04-followup/windows-final/` и `TestResults/task03-04-followup/linux-final/`.

## Промежуточные прогоны Linux

Первый полный Linux-прогон на SDK 10.0.115 в копии из `git archive` дал 2476/2498. Проверка Git не нашла `.git`,
а тесты watcher и плагинов сообщили об исчерпании лимита inotify или потере наблюдения. В том же прогоне
также упали проверки сбора ссылки на `Solution` и времени файлов в существующем сценарии MSBuild.
Повтор запущен из отдельной копии ветки с Git-метаданными и `DOTNET_PROCESSOR_COUNT=4`.
Он дал 2494/2498. Четыре оставшихся падения были в существующих сценариях захвата baseline и выполнения плана.
Для последующих прогонов использованы SDK 10.0.402 и `DOTNET_PROCESSOR_COUNT=2`. Системный лимит inotify
оставлен равным 128.

На SDK 10.0.402 перед стабилизацией новой регрессии основной набор дал 2496/2498. Контрольный прогон исходного
`6fc6d9e` на том же SDK и с тем же ограничением параллельности дал **2493/2495**. В обоих прогонах
`Real_filter_and_run_execute_the_same_expected_tests_with_only_name_substring_extras` получил два плана
вместо одного. Ещё один тест контроля упал при проверке закрытия наблюдателя
(`Root_error_routes_deleted_or_gap_without_clearing_prior_gaps_or_bypassing_session_closure`).
Контрольный TRX — `linux-control/main.trx`.

## Живая синхронизация файла с двумя ролями

**2026-11-19.** `UpdateDocumentInMemoryAsync` захватывал `_workspaceLock` и через
`ApplyWorkspaceWriteUnderLockAsync` и `PersistDocumentChangesAsync` вызывал `WriteTrackedTextAsync`.
После записи файла этот метод запускал `PublishOwnNonCSharpTextAsync`. Карта видела у того же пути роль
`AdditionalFile`, поэтому публикация ждала повторного захвата того же семафора. Внешняя операция могла
освободить его только после завершения публикации.

Запись рабочего пространства теперь использует [общую запись на диск](../../../../Services/Workspace/SolutionManager.cs)
с учётом собственных событий watcher и baseline. Если применение compile-текста или последующая сверка
успешны, сохранённый текст с исходной кодировкой добавляется к слою дополнительных документов под уже
взятым семафором. Один снимок публикует обе роли. Отсрочка неподтверждённых внешних правок остаётся в
механизме дисковой синхронизации.

[Регрессионная проверка](../../../../RoslynMcpServer.Tests/Workspace/WorkspaceNonCSharpDiskSyncTests.cs)
загружает настоящий проект MSBuild, сначала синхронизирует внешнюю правку, затем обновляет тот же файл через
`UpdateDocumentInMemoryAsync`. Проверяются обычное применение и сверка после отказа `TryApplyChanges`.
На прежнем коде оба варианта уже записали файл на диск, но прервали ожидание только по отмене через 10 секунд
и вернули `Cancelled` вместо `FullSuccess` или `ReconciliationSucceeded`. Red — два ожидаемых падения
в `TestResults/dual-role-disk-sync/red-windows.trx`.

Release-сборка решения на SDK 10.0.402 прошла на Windows и Ubuntu 24.04 WSL с тремя прежними
предупреждениями. На каждой платформе прошли **166/166** целевых тестов, включая оба варианта регрессии и
все 123 SourceStructure, и **13/13** AnalyzerLifecycle с опцией `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`.
Полный Windows main — **2508/2508**, без пропусков.

Первый Linux main — **2498/2500**. Повторились два ранее наблюдавшихся сбоя планов тестов:
`Real_MSBuild_disk_and_Mcp_edits_select_the_dependent_test_project(mcp: False)`
([предыдущий прогон](#промежуточные-прогоны-linux)) и
`Real_filter_and_run_execute_the_same_expected_tests_with_only_name_substring_extras`
(также воспроизведён на исходном `6fc6d9e`). Исходный TRX этого прогона — `TestResults/dual-role-disk-sync/linux/main.trx`.

Повторный Linux main с отдельным `TMPDIR` и `DOTNET_PROCESSOR_COUNT=1` — **2498/2500**, без пропусков.
Два падения пришлись на другие существующие тесты:
`Root_lifetime_guards_preserve_csharp_baseline_membership_and_conservative_roles` (случай `AnalyzerConfig`)
и `Empty_run_has_no_process_reports_and_keeps_the_warning_and_limitations` (четыре сгенерированных документа
получили `Unknown` при первичном захвате baseline).
Отдельный запуск всех четырёх методов, падавших в двух полных Linux-прогонах, дал **17/17**, без пропусков.
Артефакты этих прогонов — `TestResults/dual-role-disk-sync/`: `windows/`, `linux/`, `linux-retry/main.trx`
и `linux-affected/affected.trx`. См. [повтор после слияния](#полный-прогон-после-слияния-с-main).

## Полный прогон после слияния с main

**2026-11-19.** В диагностическом полном прогоне на Ubuntu 24.04 WSL до слияния прошли 2499 из 2500 тестов.
Запуск подписок возвращал `IOException` с лимитом inotify 128. В выборках было до 120 открытых экземпляров
inotify в testhost. Для упавшего `Real_MSBuild_inherited_only_test_project_is_selected_after_base_or_production_edit`
время записи исходников и сгенерированных документов проекта `Isolated` оказалось на 0,75–1,36 секунды позже
публикации снимка. Захват baseline оставил эти документы неизвестными, и оценка добавила независимый проект
целиком. Диагностические артефакты — `TestResults/wsl-main-stability/diag-main.trx` и `diag-watch-samples.log`.

В `rapid-tia` влита вся `origin/main` до `6f84e89` (merge-коммит `b0d5f11`). Коммиты `4f31eff` и `ed2b9df` убирают избыточные
подписки на покрытые каталоги и неизменяемые корни SDK/NuGet, сохраняя подписки за junction и symlink.
Слияние сохранило отдельное наблюдение за перемещением и удалением корней Rapid TIA. Фикстуры оценки
выставляют время записи исходников до загрузки и отключают генерацию посторонних SDK-документов. Тесты
статистики дожидаются окончания записи журнала перед проверкой событий.

На Ubuntu 24.04 WSL с SDK **10.0.402** Release-сборка прошла. Основной набор CI — **2590/2590** (3 мин 19 с),
SourceStructure — **123/123**, AnalyzerLifecycle — **13/13**. Во всех трёх запусках ноль пропусков.
Использованы штатный фильтр Linux CI и обычная параллельность WSL. Лимит inotify остался равным 128.

На Windows с тем же SDK Release-сборка прошла, SourceStructure — **123/123**, основной набор CI — **2601/2601**
(4 мин 49 с), AnalyzerLifecycle — **13/13**. Во всех трёх запусках ноль пропусков. До ожидания записи журнала
полный набор дал 2600/2601 из-за отсутствующего события
`tool` в тесте `Config_lazy_load_marks_builtin_or_plugin_calls_but_loaded_reads_keep_the_catalog_bit`.
Логи и TRX — `TestResults/wsl-main-stability/merge-{linux,windows}-*.{log,trx}`.
