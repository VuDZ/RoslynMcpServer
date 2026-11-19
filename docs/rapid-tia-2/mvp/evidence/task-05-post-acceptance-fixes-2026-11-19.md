# Task-05 — исправления после приёмки

**2026-11-19**, версия **1.8.1**. Основа — `aef213b`, принятая task-05. Владелец поручил исправить замечания
разбора реализации, прогнать тесты на Linux и запушить. [Отчёт](../task-05-implementation-report.md) ·
[задание](../task-05-tool-answer-and-run-hi.md).

## Замечания и исправления

- **PA-01, производительность `run`.** После сборки каждого тестового проекта `GetPairAsync(solution)`
  заново искал тестовые проекты решения. Для этого он разбирает все документы, поэтому на solution с десятками
  тестовых проектов поиск повторялся N раз. Теперь `AssemblySnapshotSession.InputsAsync` выполняется один раз
  за запрос, и каждый проект получает пару через `GetPairAsync(IReadOnlyList<IlAssemblyInput>)`. Если
  входы получены раньше, чем начался новый baseline, это ловит проверка `AssemblySnapshotPair.BelongsTo`,
  и проект идёт целиком. Поведение не изменилось, поэтому красного теста нет: без счётчика вызовов повтор
  поиска не наблюдаем. Покрытие дают существующие сценарии `run` с несколькими проектами.
- **PA-02, исключение при сужении.** Исключение после успешной сборки, кроме отмены, давало `not run`, и
  тесты не запускались. Теперь проект запускается целиком с причиной `Narrowing failed: …`
  (`TestNarrowingWholeKind.NarrowingFailed`), а ошибка пишется в лог. Для теста добавлен хук
  `AssemblySnapshotSession.BeforePairForTests`.
- **PA-03, подсказка после собственной сборки `run`.** Подсказку «build and call again» для `NotRebuilt`,
  `DependentNotRebuilt` и `ReferenceAssemblyMissing` теперь добавляет форматтер, а не условия сужения. Без
  `run` текст прежний. После сборки внутри `run` ответ говорит, что исходник, вероятно, изменился во время
  сборки. У `DependentNotRebuilt` подсказка без запуска больше не называет проект: он виден по пути выхода
  в сообщении.
- **PA-04, счётчик изменившихся типов при `run`.** При запуске строка говорит
  `identified in assemblies compared after the builds of this run`: она охватывает только проекты, собранные
  в этом запуске. Оценка без `run` пишет прежнее `identified in compared assemblies`.
- **PA-05, регистр `depth`.** Значения принимаются без учёта регистра.
- **PA-06, правка только комментария.** Она проверяется лишь при `run: true`, это решение R1-02. Теперь это
  записано в README EN/RU, `AGENTS.md.sample` и `docs/ARCHITECTURE.md`. В описание инструмента правило не
  добавлено, чтобы не увеличивать схему `tools/list`.

## Красный прогон

Linux x64, .NET SDK **10.0.114** (`global.json` 10.0.100, `latestFeature`), Release. Новые тесты и хук
были на месте, продуктовых исправлений ещё не было.

```bash
TMPDIR=$HOME_DIR/testtmp DOTNET_PROCESSOR_COUNT=2 NuGetAudit=false MSBUILDDISABLENODEREUSE=1 \
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --no-restore \
  --filter 'FullyQualifiedName~EstimateAffectedTestsDepthTests.Depth_names_ignore_case|FullyQualifiedName~EstimateAffectedTestsDepthTests.Edit_during_the_run_build|FullyQualifiedName~EstimateAffectedTestsDepthTests.Narrowing_failure_after|FullyQualifiedName~EstimateAffectedTestsRunTests.Fresh_direct_test_edit'
```

Выполнено **4** теста, **4** ожидаемых падения:

- `Depth_names_ignore_case` — `Invalid depth` для `Direct`;
- `Edit_during_the_run_build_keeps_the_project_whole_without_asking_for_another_build` — проект шёл целиком
  по `not newer than the last edit`, но без объяснения про правку во время сборки;
- `Narrowing_failure_after_the_build_runs_the_whole_project_and_names_the_error` — `Outcome: passed`
  не найден, исход был `not run`;
- `Fresh_direct_test_edit_reports_the_compiled_type_without_widening_the_method_filter` — нет новой
  формулировки счётчика при `run`.

TRX `~TEMP_ROOT/task05fix/task-05-fixes-red.trx`, SHA256
`b50a3988e8453fd1aa68ce6e77f4c1b90504b7b36b9d2172bf683d142dec6cd3`.

## Зелёные прогоны

Целевой прогон `EstimateAffectedTests|TestImpactFormatterTests|Testing.Narrowing|Testing.Snapshots|DotNetTestExecutorTests|TestImpactComposerTests`
прошёл **210/210** без пропусков.

Итоговая проверка на том же дереве, Linux x64, SDK 10.0.114, Release:

- `dotnet build RoslynMcpServer.sln -c Release` прошёл без ошибок. Метаданные сборки: версия `1.8.1`.
- Linux main `dotnet test RoslynMcpServer.sln -c Release --no-build --no-restore` с фильтром workflow
  `Category!=AnalyzerLifecycle&Category!=RequiresWindows&Category!=RequiresPinnedSdk` дал **2610/2610**:
  `total`, `executed` и `passed` совпадают, пропусков нет, 15 мин 55 с. В это число входят все **123**
  теста SourceStructure, инвентарь ограничений структурного анализа не менялся. TRX
  `~TEMP_ROOT/task05fix/main/main-linux.trx`, SHA256 `a58b4c8e6e92ea3746e15eada9ab5dfabc4a2ba573e169bb3c242f23eb3c69ef`.
- AnalyzerLifecycle не запускался: правки не затрагивают загрузку, подготовку и публикацию workspace,
  а также жизненный цикл анализаторов.
- Схема `tools/list` не изменилась: описания инструмента и параметров остались прежними.

## Дополнительная проверка на Windows

**2026-11-19**, SDK **10.0.402**, Release. После fast-forward до `718e5be` сборка решения прошла. На этом
коммите прошли **128/128** целевых проверок: четыре регрессии из исправлений, весь SourceStructure (123)
и точный размер MCP-каталога.

У PA-02 выявился отдельный случай. Когда до сборки был выбран прямой фильтр, после исключения в сужении
ответ сообщал о целом проекте, но прежний аргумент `--filter` сохранялся. В регрессии на настоящем MSBuild
и VSTest исполнился один тест из шести. После правки команды все шесть исполнились, но `includeDetails`
показывал исходный выбранный тест. Следующая проверка воспроизвела и это расхождение.

Локальная правка использует целый проект и при построении команды VSTest, и при заполнении подробного
ответа. Регрессия охватывает исходный целый проект и прямой фильтр. В обоих случаях она проверяет
отсутствие `--filter`, запуск всех шести тестов и появление в подробном списке теста вне исходного фильтра.

Красные TRX под `artifacts/task-05/windows-718e5be/` содержат по одному ожидаемому падению из двух.

```text
red/red.trx           C5E8F427E7040B0249B343C91F0BB9674A8114C242257B6E20E32CA7701A9F76
details-red/red.trx   6B38323312F1E7D5B6093C44873C4297D78D7C16D51838FA022AB9AD41025BAA
```

На итоговом рабочем дереве повторная Release-сборка через Roslyn MCP и **129/129** целевых проверок прошли.
В целевой набор вошли все **123** теста SourceStructure. Первый полный Windows main дал **2621/2622** без
пропусков. В `BaselineDeletedPathCostTests.Native_mass_delete_measures_backlog_and_keeps_node_modules_gap_free`
для 200 исходников и 20 000 файлов `node_modules` переполнился буфер `FileSystemWatcher`. Из ожидаемых
20 041 событий удаления тест получил 17 037. AnalyzerLifecycle с `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`
прошёл **13/13**. Результаты обоих запусков записаны в `artifacts/task-05/windows-718e5be/validation/`.

Изолированный повтор теста удаления прошёл **3/3**, повтор полного Windows main — **2622/2622** за 5 мин
39 с. Для повтора использован тот же Release-бинарник с `DOTNET_PROCESSOR_COUNT=4` вместо `2` в первом
запуске. В обоих прогонах выставлены `NuGetAudit=false` и `MSBUILDDISABLENODEREUSE=1`. Main запущен с
фильтром `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk` и `--no-build --no-restore`.
`Assert-NoSkippedTests.ps1` подтвердил отсутствие пропусков в изолированном повторе, полном повторе и
AnalyzerLifecycle. Контрольные суммы SHA256 для TRX под `artifacts/task-05/windows-718e5be/` приведены ниже.

```text
retry/isolated/isolated.trx          C110AF8FA1F7658314ADCD85942F62E0C378D18C2B04E8545F11971CE1F81356
retry/main/main.trx                  DBA4F21AC66F1D259F4D8DD0A399425A2BA3E2C39B48028C43261D3EE25EA726
validation/lifecycle/lifecycle.trx   74D6A85340143E1FF30C7E43245527C914BB77C25EB25A5123D5F67931BEC28B
```

Windows-проверка охватывает `718e5be` с локальной правкой резервного запуска PA-02. Linux-прогон выше
относится к опубликованному `718e5be`.
