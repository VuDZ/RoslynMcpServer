# Эпоха 7 — урезать lifecycle-набор

Статус: **выполнена; независимая приёмка [принята](epoch-7-acceptance.md)** (повтор после E7-1, E7-2). Зависимости: нет.
Результат шага: выполнен (см. «Результат»).

v2 (эпохи 1–6) и v3 (S1–S8) остаются закрытой историей. Эта эпоха их не
переоткрывает и runtime-контракт overlay не меняет. Режется только способ
проверки: хостовый `Category=AnalyzerLifecycle`.

## Основание

Юниты закрывают таблицу решений внутри одной функции: `AnalyzerReferenceShadowCopierTests`,
`AnalyzerShadowPublicationPlannerTests`, `AnalyzerShadowGenerationPublisherTests`,
`AnalyzerLoaderContractTests`, `AnalyzerProvenanceCaptureGateTests`,
`AnalyzerProvenanceBindingTests`. Они не проводят переход `SolutionManager`
через настоящий MSBuild host. Такой переход остаётся на `LifecycleTestHost`.
Повтор уже закрытого решения через хост убирается: IDE и MCP обрывают прогон
по таймауту (`tools/call` ~60 с, `host-response-timeout`).

`Epoch1SemanticInventoryTests` уже обычный `[Fact]`: он сканирует исходники и
хост не стартует, но `[Trait("Category", "AnalyzerLifecycle")]` сажает его на
шард `lifecycle-epoch1`.

## Что оставить

Тринадцать методов, все в одном классе с `[Collection("AnalyzerLifecycle")]`
и тем же trait. Имена можно сохранить. Assertions переносимых методов не
ослаблять. Режим фикстуры у каждого метода свой, как в источнике: общий
`OutputPathMode` на набор не вводить.

`RedirectedMissingAnalyzerPath` пишет `AppendTargetFrameworkToOutputPath=false`,
оба TFM кладут одну `Generator.dll`. Для проверки «два shadow path» и для
сравнения с реальным output нужен `SdkDefaultCorrectPath`: SDK сам дописывает
TFM к каталогу, и `FindGeneratorOutputDll` находит существующую DLL.

| Проверка | Режим | Откуда перенести |
| --- | --- | --- |
| Opt-in исполняет marker из shadow, загруженный путь равен shadow, реальный output не загружен | `SdkDefaultCorrectPath` | `V3PersistentPublicationStateTests.Successful_opt_in_executes_exact_marker_from_shadow_path` и проверки `RecordLoadPath` при `SdkDefaultCorrectPath` в `Epoch1WritePathTests` |
| Тот же процесс после V2 не исполняет V2 (cached и reset+load) | `RedirectedMissingAnalyzerPath` | `Epoch3LoaderContractTests.V1_to_V2_cached_and_reset_refuse_execution_process_restart_runs_V2` |
| Новый процесс исполняет точный V2 | `RedirectedMissingAnalyzerPath` | `Epoch3LoaderContractTests.V1_to_V2_process_restart_executes_exact_V2` |
| FileSystemWatcher доставляет сохранение; marker и `.csproj` целы; загруженный путь — shadow, не реальный output | `SdkDefaultCorrectPath` | Строка `Real_FSW_delivery_then_production_flush_publishes_text_and_marker` только для `SdkDefaultCorrectPath`, вместе с веткой `RecordLoadPath` этого режима |
| Новый `.cs` после flush не дописывает `<Compile Include>` | `SdkDefaultCorrectPath` | `DiskSyncCsprojMutationTests.New_cs_flush_does_not_mutate_csproj_and_type_appears_after_reload` |
| Удалённый известный `.cs` остаётся в снимке, `updateDocument` не создаёт файл заново | `SdkDefaultCorrectPath` | `DiskSyncCsprojMutationTests.Deleted_known_cs_stays_in_snapshot_and_updateDocument_does_not_recreate_file` |
| Production capture выбирает каждый загруженный inner TFM, два разных `ShadowCopyPath` | `SdkDefaultCorrectPath`, затем `MakeGeneratorMultiTargeted` | `F09ProductionCaptureTests.Production_snapshot_selects_each_loaded_inner_tfm_for_rollout` |
| Fail-closed replay удаляет временные binlog | `SdkDefaultCorrectPath` | `F09ProductionCaptureTests.Replay_failures_publish_fail_closed_status_and_delete_temporary_files` |
| Corrupt capture не публикует semantic snapshot и не загружает реальный output | `SdkDefaultCorrectPath` | `V3RegressionBaselineTests.V3_R3_corrupt_capture_does_not_publish_or_execute_real_output` |
| Два процесса, kill посреди публикации, выживший переиспользует поколение | как в источнике | `Epoch2ImmutableShadowTests.Two_processes_share_one_root_kill_mid_publish_then_survivor_reuses` |
| Удержанный edit той же сессии после другой записи отклоняется до side effects | `SdkDefaultCorrectPath` | `V3RegressionBaselineTests.V3_R1_same_session_stale_candidate_is_rejected_before_any_write` |
| Ошибка prepare, затем `updateDocument`: real output не исполняется | `SdkDefaultCorrectPath` | `V3RegressionBaselineTests.V3_R2_prepare_failure_stays_fail_closed_after_text_edit` |
| Ошибка prepare, затем доставка FSW и flush: admission остаётся `Banned`, real output не исполняется | `SdkDefaultCorrectPath` | `V3PersistentPublicationStateTests.Watcher_flush_after_prepare_failure_stays_fail_closed` |

Первая строка обязана сохранить оба сравнения, а не только marker и префикс
temp. Корень фикстуры тоже лежит под temp, поэтому `StartsWith` временного
каталога не отличает shadow от исходного output.

- `OverlayAnalyzerPath` и `LoadedAnalyzerPath` равны и не равны полному пути
  `FindGeneratorOutputDll`.
- Реальный output отсутствует в `ProcessAnalyzerAssemblies` и в загруженных
  сборках; shadow path в `ProcessAnalyzerAssemblies` присутствует.
  Это ветка `RecordLoadPath` для `SdkDefaultCorrectPath`.

`Basic_generation_opt_in_load_exact_V1_and_repeat_without_changes` для этой
строки не использовать: там нет сравнения с реальным output.

Capture-тест не переводить на `RedirectedMissingAnalyzerPath`. Утверждение
«два различных `ShadowCopyPath`» остаётся. Replay и R3 не сливать в один метод:
replay проверяет `Missing` / `Incomplete` и удаление временных файлов, R3 —
`Corrupt` и запрет загрузки реальной DLL.

R2 и flush после ошибки prepare оба остаются. R2 входит через `updateDocument`,
flush — через `waitDirty` и `flushFind`. Юнит ни один из этих входов с ban не
соединяет.

Удалённый `.cs` остаётся: другого теста, что `updateDocument` не создаёт файл
заново, нет.

Оракул «Roslyn исполнил эту DLL» в юнит не переписывать.

## Что удалить

Host-методы под `RoslynMcpServer.Tests/AnalyzerLifecycle/`, которых нет в таблице.
В том числе:

- матрицы эпох 1 и 3, кроме двух строк V1→V2;
- обе строки `OutputPathMode` у write-path теорий, кроме FSW на
  `SdkDefaultCorrectPath`; write-path эпохи 4 целиком;
- `UArb04LoadBoundaryEvidenceTests`;
- `F09CaptureSpikeTests` и остальные методы `F09ProductionCaptureTests`;
- `Epoch5S4AcceptanceTests`;
- `Force_refresh_all_flush_sets_composition_stale_without_new_file`;
- остальные методы `V3WriteBaseFreshnessTests` (reload, другой документ, watcher
  как источник stale, unknown context, fresh apply, under-lock);
- остальные методы `V3PersistentPublicationStateTests` (reconciliation, cancel,
  cached false/omitted, cached true);
- `V3PartialPrepareTransitionTests`, `V3ProvenanceFailureGateTests`.

Явно снято с хоста:

- foreign path и «не подменять чужое имя» — copier;
- private helper и identity collision — loader contract;
- пригодность capture и exact-output bind — gate и binding;
- идентичность поколения и сбой staging — publisher;
- multi-consumer, flag on/off, concurrent load, матрица U-ARB-04.

`GeneratorConsumerFixture`, `LifecycleHostClient`, `Epoch1HostOps` оставить
в объёме выживших тестов. Мёртвые host-операции не сохранять. Оба значения
`OutputPathMode` нужны выжившим методам, enum не сужать до одного.

## Инвентаризация и CI

С `Epoch1SemanticInventoryTests` снять только trait `AnalyzerLifecycle`.
`[Fact]` и проверки не менять. После этого класс попадает в джоб `unit`.

Четыре шарда `lifecycle-epoch1`, `lifecycle-v3`, `lifecycle-epoch4-5`,
`lifecycle-rest` заменить одним джобом `lifecycle`:

- filter: `Category=AnalyzerLifecycle`;
- timeout меньше 45 минут (ориентир 20);
- `ROSLYN_MCP_ANALYZER_LIFECYCLE=1` только на этом джобе.

Джоб `unit` переменную не задаёт и фильтр `Category!=AnalyzerLifecycle` не
меняет. Объединение `unit` и `lifecycle` по-прежнему покрывает все найденные
тесты без пересечения. Пустых именованных шардов не оставлять.
`.cursor/rules/roslyn-mcp-ci.mdc` привести к этой партиции.

`Assert-NoSkippedTests.ps1` не ослаблять. Skip из-за недоступного MSBuild host
на джобе `lifecycle` остаётся красным.

## Гейт локального прогона

`AnalyzerLifecycleFactAttribute` и `AnalyzerLifecycleTheoryAttribute` читают
`ROSLYN_MCP_ANALYZER_LIFECYCLE` до `LifecycleEnvironment.Probe`. Значение ровно
`1` — сегодняшняя проба хоста. Иначе `Skip` сразу, процесс хоста не стартует.
Текст skip называет переменную и говорит, что её ставит джоб `lifecycle`.

MCP-тулы переменную не выставляют. В `[Description]` тулов и в
`AGENTS.md.sample` категорию этого репозитория не писать. Правило CI этого
репозитория говорит агенту не выбирать эти классы через `run_specific_test`.

`Version` / `AssemblyVersion` / `FileVersion` не менять: поведение сервера
не меняется.

## Приёмка

- Host-методов с trait `AnalyzerLifecycle` ровно тринадцать, по одной строке
  таблицы, все в одном классе.
- Первая строка на `SdkDefaultCorrectPath`: `LoadedAnalyzerPath` равен
  `OverlayAnalyzerPath`, оба не равны `FindGeneratorOutputDll`, реальный output
  нет среди загруженных сборок процесса.
- Capture-строка по-прежнему даёт два разных `ShadowCopyPath` после
  `MakeGeneratorMultiTargeted`. Фикстура этой строки не ставит
  `AppendTargetFrameworkToOutputPath=false`.
- R1 отклоняет удержанный edit до записи. R2 после `updateDocument` и flush
  после `waitDirty` не исполняют реальный output. R3 остаётся отдельным методом
  от replay.
- `Epoch1SemanticInventoryTests` без этого trait и зелёный на фильтре `unit`.
- `dotnet test --filter Category=AnalyzerLifecycle` без переменной: все host
  кейсы skipped, хост не стартует, exit 0.
- Тот же фильтр с `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`: кейсы исполняются, не skip.
- `test-suite.yml`: джоб `unit` без переменной; джоб `lifecycle` с `=1` и
  одним фильтром категории. Четырёх старых имён шардов нет.
- Юнит-файлы copier / planner / publisher / loader / gate / binding не удалены
  и не ослаблены.

## Результат

Выполнено.

Единственный носитель `Category=AnalyzerLifecycle` — `AnalyzerLifecycleHostTests`
(один `public sealed partial` класс, `[Collection("AnalyzerLifecycle")]`,
`[Trait("Category", "AnalyzerLifecycle")]`), ровно тринадцать `[AnalyzerLifecycleFact]`
методов. Имена взяты из источников; assertions перенесены без ослабления. Метод на строку
таблицы, без объединений: две строки V1→V2 остались двумя методами. Файлы класса:

| Файл | Методы | Строки таблицы |
| --- | --- | --- |
| `AnalyzerLifecycleHostTests.cs` | 4 | opt-in marker из shadow с обоими сравнениями; V1→V2 cached и reset отказывают; V1→V2 новый процесс исполняет точный V2; два процесса на одном корне с kill посреди публикации |
| `AnalyzerLifecycleHostTests.Publication.cs` | 5 | capture по каждому загруженному inner TFM; replay fail-closed; FSW доставка + flush; новый `.cs`; удалённый `.cs` |
| `AnalyzerLifecycleHostTests.Regression.cs` | 4 | R1; R2; R3; flush после ошибки prepare |

Метода `Watcher_flush_stales_held_candidate_and_keeps_flushed_text` нет: раздел «Что удалить»
снимает остальные методы `V3WriteBaseFreshnessTests`, включая watcher как источник stale.
R1 покрывает ту же границу stale-кандидата и остаётся отдельным методом.

Первая редакция этой эпохи сливала две строки V1→V2 в один метод и держала счёт «тринадцать»
лишним watcher-методом. Приёмка ([epoch-7-acceptance.md](epoch-7-acceptance.md), E7-1 и E7-2)
это отклонила: строки снова две, watcher удалён.

Вспомогательные типы (по типу на файл): `LifecycleHostClient`, `LifecycleHostOps`
(build/load/oracle/marker, restart-required, целостность `.csproj`, forced rebuild),
`LifecycleHostAssert` (сравнение путей, «реальный output не опубликован», отсутствие
publication IO, compact-лог), `GeneratorConsumerFixture` (в объёме выживших тестов, оба
значения `OutputPathMode`), `AnalyzerLifecycleGate`, `LifecycleEnvironment`,
`AnalyzerLifecycleFactAttribute`. `AnalyzerLifecycleTheoryAttribute` сохранён с тем же гейтом,
хотя теорий в наборе больше нет: иначе гейт пришлось бы собирать заново при первой
возвращённой теории.

Две находки по фикстуре, обе исправлены в тестовом коде (runtime не затронут):

- `RedirectedMissingAnalyzerPath` кладёт `Generator.dll` то в `artifacts\Generator\`, то в
  `artifacts\GeneratorDebug\`, в зависимости от того, какую редакцию `Directory.Build.props`
  увидела оценка `OutputPath`. Прежний поиск по одному соглашению возвращал `null` или
  промежуточную копию из `obj` (в том числе ref-assembly, которая перезаписывается на каждой
  компиляции даже без изменения IL). Теперь `FindGeneratorOutputDll` берёт самый свежий
  не-ref кандидат и пересчитывается после rebuild.
- Generator собирается с `Deterministic=true`, поэтому «дописать комментарий и пересобрать»
  не меняет байты DLL, а `obj\...\ref\Generator.dll` меняет их всегда — на этом прежняя
  проверка forced rebuild и проходила. Теперь в маркер generated-кода подставляется уникальный
  токен, так что проверка «байты изменились» стала настоящей.

`Epoch1SemanticInventoryTests` без trait, `[Fact]` и проверки не тронуты; `TestDiscoveryHelperTests`
и `TestFilterHelperTests` не менялись. Джоб `unit` (`Category!=AnalyzerLifecycle`) — 649 тестов,
джоб `lifecycle` — 13; пересечения нет.

### Прогон

SDK `10.0.204`, `dotnet test RoslynMcpServer.Tests.csproj`, x64 `C:\Program Files\dotnet`.

| Проверка | Команда | Результат |
| --- | --- | --- |
| `unit` | `--filter "Category!=AnalyzerLifecycle"` | 649 всего, 648 passed, 1 failed |
| `lifecycle` без переменной | `--filter "Category=AnalyzerLifecycle"` | 13 всего, 13 skipped, exit 0, 74 ms; host не стартовал |
| `lifecycle` с `ROSLYN_MCP_ANALYZER_LIFECYCLE=1` | то же + runsettings с переменной | 13 passed, 0 skipped, 2 m 56 s |

Переменная выставлялась через `--settings` (runsettings `RunConfiguration/EnvironmentVariables`):
`dotnet test` не пробрасывает её из окружения оболочки в testhost. Локально это заменяет
`ROSLYN_MCP_ANALYZER_LIFECYCLE=1` из джоба `lifecycle`; в CI переменную задаёт `env` шага.

Единственный красный `unit`-тест — `WorkspaceAnalyzerSanitizerTests.GetSanitizedPublishedSolution_takes_workspace_lock_and_async_does_not_call_sync`,
и он красный до этой эпохи: тест режет `Services/SolutionManager.cs` от
`GetSanitizedPublishedSolution()` до `GetOrCreateSanitizedPublishedSolution()`, и в этот
диапазон попадает аргумент `prepared.Results` метода `CompletePrepare`, то есть подстрока
`.Result`. `SolutionManager.cs` этой эпохой не менялся (`git status` по файлу пуст), тест не
ослаблялся и не удалялся; починка вне объёма эпохи.

`Assert-NoSkippedTests.ps1` и `.cursor/rules/roslyn-mcp-ci.mdc` приведены к партиции
`unit` + `lifecycle` (timeout 20, `ROSLYN_MCP_ANALYZER_LIFECYCLE=1` только на `lifecycle`).
Четырёх имён шардов `lifecycle-epoch1`, `lifecycle-v3`, `lifecycle-epoch4-5`,
`lifecycle-rest` в `test-suite.yml` больше нет.

`Version` / `AssemblyVersion` / `FileVersion`, `[Description]` тулов, `McpToolHelpCatalog`,
`AGENTS.md.sample`, README и `docs/ARCHITECTURE.md` не менялись: runtime-поведение сервера
и агентская политика те же.
