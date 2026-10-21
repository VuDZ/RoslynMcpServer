# task-01 — приёмка, 2026-10-21

Закрывает [task-01](../task-01-linux-green-suite-hi.md). Baseline:
[baseline-2026-10-21.md](baseline-2026-10-21.md).

## Прогоны

Оба SDK стоят рядом, как в `test-suite.yml`: `10.0.300` и последний `10.0.x`
(`10.0.402`). Фикстуры с `global.json` идут на `10.0.300`
(`rollForward: disable`), тестовый хост — на `10.0.402`.
`DOTNET_MULTILEVEL_LOOKUP=0`.

| ОС | Фильтр | Passed | Failed | Skipped |
|---|---|---|---|---|
| Linux (WSL2) | `Category!=AnalyzerLifecycle&Category!=RequiresWindows` | 1388 | 0 | 0 |
| Linux (WSL2) | `Category=AnalyzerLifecycle`, `ROSLYN_MCP_ANALYZER_LIFECYCLE=1` | 13 | 0 | 0 |
| Windows | `Category!=AnalyzerLifecycle` | 1392 | 0 | 0 |
| Windows | `Category=AnalyzerLifecycle`, `ROSLYN_MCP_ANALYZER_LIFECYCLE=1` | 13 | 0 | 0 |

Конфигурация Release. Разница 1392 − 1388 = 4 — это перечень `RequiresWindows`
ниже: на Linux они исключены фильтром, а не пропущены. На Windows они входят
в main suite. Число тестов относительно baseline (1338 в main suite) не
уменьшилось; прирост — тесты других незакоммиченных веток в том же дереве.

`net10.0-windows` в `Ordinary_unsupported_controls_refuse_without_a_snapshot("multitarget")`
на Linux даёт ожидаемую причину `ambiguous-project-instance`. Сборка фикстуры
не падает с NETSDK1100.

## Группа A — `\` в путях `.sln` / `.slnx`

Исправление production. Сравнение и имя файла идут через `/` на любой ОС;
в classic `.sln` по-прежнему пишется `\`.

Проверенные места, которые читают путь проекта из решения:

- `Services/Build/SolutionProjectTargetResolver.cs` — `ListFromSln`, `CollectSlnxProjects`.
- `Services/Editing/ProjectRenameHelper.cs` — classic `.sln` и `.slnx`
  (сравнение и запись). Include в `.csproj` уже переводился в разделитель ОС.

Остальной production-код либо смотрит только на расширение (`.sln` / `.slnx`),
либо читает из решения конфигурации (`SolutionConfigurationCatalog`), а не пути проектов.

| Падение | Чем закрыто |
|---|---|
| `SolutionProjectTargetResolverTests.ListFromSln_root_project_keeps_display_name` | нормализация пути |
| `ProjectRenameHelperTests.Happy_path_renames_dir_csproj_refs_sln_and_slnx` | нормализация пути |
| `ProjectRenameHelperTests.Multiple_solutions_all_updated` | нормализация пути |

## Группа B — каталог пакета NuGet

`RestoreAssets` берёт каталог из `libraries[*].path`, а не из ключа target.
Инвентарь известных ограничений структурного анализа не менялся.
Восемь тестов группы проходят на Linux:

- `SourceSetAnalysisTests.Production_reachability_records_only_the_known_delegate_limits`
- `SourceSetAnalysisTests.Production_scope_is_usable_cached_and_covers_the_solution_manager`
- `WorkspaceAnalyzerSanitizerTests.GetPublishedSolutionAfterDiskSyncAsync_still_returns_raw_published_snapshot`
- `WorkspaceAnalyzerSanitizerTests.GetSanitizedPublishedSolution_takes_workspace_lock_and_async_does_not_call_sync`
- `WorkspaceWriteBoundaryTests.Production_TryApplyChanges_has_single_SolutionManager_call_site`
- `IsolatedWorkspaceHydratorTests.Hydrate_owned_calls_have_no_open_loader_process_or_apply_with_exact_callback_limits`
- `IsolatedWorkspaceHydratorTests.Workspace_dispose_is_called_before_the_observed_disposal_notification_on_both_owned_paths`
- `HydrateHostExperimentTests.Hydrate_transitive_calls_have_no_open_or_process_with_exact_external_admission_limit`

## Группа C — данные тестов

| Падение | Чем закрыто |
|---|---|
| `HarnessSessionIdTests.Resolve_maps_immediate_parent` (4 случая с `C:\…\*.exe`) | production `HarnessNameResolver` нормализует путь образа так же, как командную строку; тест идёт на обеих ОС |
| `RawImportEvidenceTests.Actual_none_channel_is_inspected_before_owned_fixture_cleanup` | ожидаемый путь собирается разделителем ОС |
| `OracleFixtureBuilderTests.Fresh_msbuild_reproduces_the_observed_razor_generator_output` | эталон Razor переводится под разделитель и перевод строки текущей ОС |
| `OracleFixtureBuilderTests.FilePath_rejects_paths_outside_the_owned_root` | ожидания строятся разделителем ОС |
| `DependencyEvidenceRunnerTests`…`("custom-target")` | target читает файл через `ReadLinesFromFile` / `WriteLinesToFile`, без `cmd` и `cat`. Replay по-прежнему не записывает этот файл во входы компилятора |

Тот же `cmd /c type` оставался в `OracleFixtureBuilder.CustomTargetXml`. На Linux
сборка сценария падала, и oracle считал это неожиданным отказом эксперимента.
Фикстура переписана теми же задачами MSBuild.

## Группа D — внедрение сбоя

`FileChangeBlocker`: на Windows открытый файл с `FileShare.Read`, на Unix снятие
бита записи (у файла — чтобы не записать, у каталога — чтобы не удалить).
Под root блокировка правами не работает, и хелпер отказывается продолжать.

| Падение | Чем закрыто |
|---|---|
| `OracleFixtureBuilderTests.Failed_cleanup_of_the_error_path_is_reported_not_swallowed` | `BlockDelete` |
| `OracleFixtureBuilderTests.CreateAsync_reports_failed_cleanup_after_materialization_started` | `BlockDelete` |
| `HydrateHostExperimentTests.Second_file_io_failure_reports_only_committed_first_path` | `BlockWrite` |

Тот же приём в `SupplementalManifestTests`: удержание `FileShare.Read` на Linux
не мешает `WriteAllText`. Запись и удаление `Directory.Build.props` блокируются
отдельно (файл и его каталог), чтобы соседние каталоги по-прежнему принимали новые файлы.
Запись сгенерированного `AssemblyInfo.cs` блокируется так же.

## Группа E — зависание `multitarget`

Полный прогон больше не ждёт 3 минуты: сценарий проходит, причина отказа —
`ambiguous-project-instance`. Отдельно гипотеза «node reuse держит унаследованный
stdout» на одном прогоне не воспроизвела зависание, но стек baseline — это
`RoundtripFixture.BuildAsync`, который читает stdout до конца и не ставил
`MSBUILDDISABLENODEREUSE`. Переиспользованный узел MSBuild наследует этот канал,
и `ReadToEndAsync` не завершается, пока узел жив. Фикстура ставит ту же переменную,
что `DotNetCliRunner`. Таймаут теста не увеличивался.

## Пропуски baseline

Оба `WpfTemporaryProjectWatchingTests` помечены `RequiresWindows`. `WpfFact`
остаётся защитой локального прогона без фильтра. В отфильтрованном Linux-прогоне
эти тесты не попадают в выборку, поэтому skipped = 0.

## Молчаливые проходы

Ранний `return` вне Windows снят. `TestAssemblyPathResolverTests` проверяет обе ОС.
Unix-ветка `IsolatedWorkspaceHydratorTests` проверяет отказ кодека.
`HarnessSessionIdTests.Linux_reads_parent_process_id` на Linux читает pid родителя;
на Windows метод сразу выходит — это не зелёный проход Linux без проверки.

## Перечень `RequiresWindows`

Одна категория на репозиторий. Состав сверяет
`WindowsOnlyTestInventoryTests` (выполняется на любой ОС). Перед `GetTypes`
тест регистрирует MSBuild: иначе на Linux, если локатор ещё никто не вызывал,
не находится `Microsoft.Build.Framework` 15.1.0.0 и инвентарь падает, не дойдя до сверки.

| Тест | Причина |
|---|---|
| `HarnessSessionIdTests.Windows_reads_this_process_image_and_command_line` | образ и командная строка читаются Windows-API |
| `DotNetHostResolverTests.ResolveDotNetExecutable_on_64bit_windows_prefers_program_files_dotnet` | 64-битный хост под Program Files есть только на Windows |
| `WpfTemporaryProjectWatchingTests.Real_WPF_build_preserves_the_cached_graph_after_flush_and_repeated_loads` | сборка WPF требует Windows Desktop SDK |
| `WpfTemporaryProjectWatchingTests.Real_project_edit_delivered_during_WPF_build_still_invalidates_the_graph` | то же |

## Отмена после первой записи

`Cancellation_after_first_write_reports_committed_path_and_unapplied_state` в baseline
проходил и потом стабильно падал даже без правок задачи: `WriteAllBytesAsync` успевал
записать второй файл раньше, чем наблюдатель отменял операцию. Большой второй файл
не создаёт окно. Второй persist теперь ждёт, пока наблюдатель отменит токен по уже
записанному первому файлу, либо пока наблюдение не истечёт. Смысл проверки тот же:
отмена привязана к зафиксированным байтам, а не к длительности записи.
