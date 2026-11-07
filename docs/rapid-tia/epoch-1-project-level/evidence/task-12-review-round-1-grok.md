# Task-12 — независимое ревью, раунд 1

- Дата: **2026-11-08**, Europe/Moscow.
- Рецензент: **Grok 4.7**, независимая сессия. Среда сообщает модель как Grok 4.7.
- Ветка: `rapid-tia`. Коммит: `2c9b1e709d79efe2398bed8cdebfb8a230178be9` — `Stop treating rebuilt project outputs as affected-test changes (1.7.1)`.
- База: `64667118892003bc2a332e2af265eb67299c0ab1`. Проверен `git diff 6466711 2c9b1e7`.
- Scope: этот diff, `ProjectImpactMapper` вместе с `WorkspaceInputMapBuilder`, `TestImpactEstimator`, `TestImpactComposer`, `TestImpactFormatter`, тесты task-12, [задача](../task-12-build-output-signal-med.md), [отчёт реализации](../task-12-implementation-report.md), [RED](task-12-red.md), [GREEN](task-12-green.md), R-19 и происхождение T09-C1. Статическое расширение до сборки, reload с другой конфигурацией и сравнение не-C# по содержимому не являются основанием возврата.
- Правило возврата: **R-18** — только молчаливая потеря тестов или неверный ответ / неверная инструкция в конкретном реальном сценарии. Огрубление, цена и принятые ограничения нового раунда не создают.
- Вердикт: **ACCEPT**. MUST-FIX по R-18 нет. Это независимое ревью, не смена статуса задачи и не повторная приёмка эпохи.

Соседний отчёт: [черновик Codex](task-12-review-round-1.md).

## What this change does

`ProjectImpactMapper.MapAsync` считает путь продуктом сборки только когда **каждое** вхождение в `WorkspaceInputMap` имеет роль `Output` или `MetadataDependency` и `ProducerProjectId` проекта, который ещё есть в solution. Для такого пути владельцами остаются лишь metadata-потребители вне графового замыкания всех производителей этого пути. Если таких владельцев нет, путь не попадает в `Paths`, в счётчик файлов и в `Not covered`. Производитель и проекты, которые доходят до него по графу проектов, по этому пути не выбираются.

Любая другая роль на том же пути оставляет прежний `ProjectsForPath`. Внешняя DLL без производителя тоже остаётся прежним входом. Карта входов, watcher и baseline не менялись. Документация заменяет описание T09-C1 правилом R-19; явный `noBuild: false` для ручного фильтра сохранён.

## Проверено

1. **Границы diff.** Production C# изменён только в `Services/Testing/ProjectImpactMapper.cs` (`IsSolutionBuildProduct`, `ConsumersOutsideProducerGraph` и ветка в `MapAsync`). Схемы инструментов, каталог, baseline, watcher и input map не менялись. Остальное — тесты, фикстуры, версия `1.7.1` / `1.7.1.0` / `1.7.1.0` и документация. Patch соответствует исправлению поведения без новой поверхности: в `98773ad` optional-параметры ещё не было, minor `1.7.0` их добавил, этот коммит сигнатуру `estimate_affected_tests` не трогает.

2. **Роли и `IsSolutionBuildProduct`.** Сверено с `WorkspaceInputMapBuilder`: `IndexOutputs` / `AddOutput` пишут `OutputFilePath` и `OutputRefFilePath` как `Output` с `ProducerProjectId` своего проекта; `IndexMetadata` пишет `MetadataDependency`, а `OutputProducer` подставляет производителя только при точном попадании в уже записанный output и берёт **первый** такой output. `UserInput`, `AdditionalFile`, `AnalyzerConfig`, `EvaluationInput`, `Generated` и `Unknown` имеют другую роль, поэтому путь с любой из них предикат не проходит. У generated-документа производитель есть, но роль `Generated`, не `Output`. `Project.AnalyzerReferences` карта не индексирует: у `OutputItemType=Analyzer` и у голого `<Analyzer Include="…">` нет отдельного `MetadataDependency`. Внешняя DLL получает `MetadataDependency` с `ProducerProjectId == null` и не подавляется.

   Написанный вход, который попал бы только в `Output`/`MetadataDependency`, — это сам выход или ссылка на этот же канонический путь. Смешанный путь (например additional file на том же файле) идёт старым `ProjectsForPath` и владельца additional file не теряет; производитель при этом тоже остаётся в списке. Это прежнее огрубление, не новая потеря.

3. **`ConsumersOutsideProducerGraph`.** Замыкание строится по `ProjectId` из `entry.Producers` и `GetProjectsThatTransitivelyDependOnThisProject`. Ключ карты — канонический полный путь, поэтому общий каталог при разных именах DLL производителей не сливает. Разные TFM с разными `OutputFilePath` тоже остаются разными путями. Выход незагруженного TFM, которого нет в карте, по-прежнему отсекается старым `IsIgnoredPath` для `bin`/`obj`, если на него нет metadata-ссылки; если ссылка есть и путь не равен выходу загруженного проекта, производитель пустой и потребитель выбирается.

   Потребитель, который по графу зависит от проекта A, а файлом держит DLL проекта B **по другому пути**, отсекается только замыканием производителей пути B. Замыкание A его не прячет. Совпадение полных путей — отдельное ограничение ниже.

   `solution.GetProject(producer) == null` у любого вхождения выключает предикат: путь снова идёт в `ProjectsForPath`, который оставляет только живые `ProjectId`. Потребитель с `GetProject == null` отбрасывается и здесь, и в старом пути; проекта вне solution запустить нельзя. Исключения на отсутствующем id нет.

4. **HintPath.** `ProjectImpactMapperIntegrationTests.Real_project_outputs_select_nothing_and_a_HintPath_consumer_follows_the_project_graph` собирает фикстуру через `DotNetCliRunner`, проверяет существование `OutputFilePath` и `OutputRefFilePath`, наличие обоих в карте и `consumer.ProjectReferences == [app.Id]`. В `Consumer.csproj` записан только `<Reference><HintPath>` на `bin/Release/net10.0/App.dll`. Утверждение о `ProjectReference` поэтому доказывает промоцию именно этого пути, а не свойство фикстуры. Оба выхода дают пустые project/test/path/not-covered. `app.FilePath` — файл проекта, не `.cs`; `ProjectsForPath` возвращает App, `ExpandProjects` добирает тестовые проекты графа. Тот же `ExpandProjects` сработал бы для `.cs` App. Отдельной статической связи «производитель → потребитель по файлу» для этого измеренного HintPath не нужно: правка входа App уже выбирает Consumer и Specs.

   Вывод отчёта верен для этой MSBuild-загрузки. Его нельзя читать как обещание для копии DLL, произвольного custom target или HintPath, который не совпал ни с `OutputFilePath`, ни с `OutputRefFilePath`. Ветка вне графа для таких ссылок остаётся, и её проверяет тест в памяти.

5. **Ответ инструмента.** `TestImpactEstimator.EstimateAsync` кладёт в `ChangedPaths` только `mapped.Paths`. `TestImpactFormatter.Format` считает «in N files» от этого списка, а `Reasons` собирает `TestImpactComposer.ExpandedProjects` только по оставшимся владельцам путей. `Not covered` дополняется из `mapped.NotCoveredPaths` и из флагов baseline отдельно. Пропуск при `owners.Count == 0` стоит до записи и в `Paths`, и в `Not covered`, поэтому DLL не остаётся ни файлом, ни причиной «Project expansion…», ни строкой «Path outside the loaded input map».

   `requiresReload` вычисляется из `projectGraphStale || projectGraphStaleFromGraphFile` до цикла и циклом не сбрасывается. При `projectGraphStaleFromGraphFile` все текущие тестовые проекты выбираются после цикла, даже если единственный изменённый путь — подавленный выход. `TestImpactEstimator` по-прежнему пишет «Project graph stale; reload required…», когда `impact.ProjectGraphStale`. Отдельного нового теста на пару «только DLL + graph file» нет; управление флагом от набора путей не зависит.

6. **Выход без отслеживаемого входа.** Новое свойство командной строки, смена SDK и ручная подмена DLL больше не выбирают производителя и его графовых потребителей. Это реальная потеря прежнего косвенного сигнала. Она прямо следует из R-19 и названа в отчёте. R-01 сам по себе любой пропуск не оправдывает: он лишь говорит, что оценка приблизительна. Для этого класса решение даёт R-19. Потребитель вне графа по-прежнему выбирается, если ссылка лежит на том же пути и записана как `MetadataDependency`. Пустой план пользовательские тексты не называют проверкой: `AGENTS.md.sample` оставляет «Empty selection is not validation», README сохраняет обязательный полный suite/CI. Отдельного сценария потери сверх этого решения нет.

7. **Тесты и RED.** Шесть cases ходят в настоящий `WorkspaceInputMapBuilder.Build`, а не в вручную расставленные роли. Theory закрывает оба выходных пути без потребителей. Тест вне графа даёт файловую ссылку и Isolated, и Specs: Specs отсекается как потомок App, Isolated остаётся; слабое утверждение «кто-нибудь выбран» этот тест не принял бы. Охрана внешней DLL кладёт файл в `lib/`, не в выход проекта. Копия framework assembly нужна только как существующий PE для `MetadataReference.CreateFromFile`; идентичность сборки cases не проверяют.

   `Estimate_after_run_keeps_the_filter_and_a_restored_edit_leaves_an_empty_plan` использует `TestImpactRunFixture`: SDK-проекты, `DotNetCliRunner` build, `SolutionManager.LoadAsync`, запись через `EditingTools`, настоящий `EstimateAffectedTests(run: true)`, повторный estimate и `RestoreAsync`. Это не заглушка исполнителя. Фикстура — два независимых тестовых проекта; отсечение зависимого тестового проекта показывает тест в памяти, не этот сквозной.

   RED согласуется с `6466711`: старый `ProjectsForPath` возвращал все `ProjectId` вхождений, выход выбирал производителя и оставлял DLL в `Paths`. Цитата после `run` («1 members in 2 files», причина с `First.dll` и «Project-level expansion») и цитата после отката («0 members in 1 files», тот же whole project) совпадают с этим кодом и с `TestImpactFormatter` / `VstestRunPlanner`. Охрана внешней DLL на том коде уже проходила. Заявленные **5 failed / 1 passed** — это две строки theory, потребитель вне графа, интеграционный тест и сквозной `run`, против одной охраны. Исторический Linux RED заново не запускался, TRX в пакете нет.

8. **Документация и 1.6.0.** README EN/RU (справка инструмента и абзац истории 1.7.1), `AGENTS.md.sample` (bullet Builds; `noBuild: false` для ручного фильтра на месте), `docs/ARCHITECTURE.md`, CHANGELOG 1.7.1 и R-19 описывают тот же предикат: написанные входы, не продукт сборки; файловый потребитель вне графа остаётся. Добавленная запись CHANGELOG 1.6.0 сверена с `98773ad`: версия `1.6.0`, `Core<TestTools>("estimate_affected_tests")`, тесты каталога `55` и `16`, API только `includeDetails` без `run`, warning, `Not covered`, `project-level result only` и unknown runtime name в formatter. Baseline того коммита — первая загрузка solution.

9. **Свой прогон, Windows, Release, дерево `2c9b1e7`.** `run_test_by_filter` по `RoslynMcpServer.Tests.csproj`, `configuration=Release`, `noBuild=false`, фильтр шести новых cases: **6/6 passed**, 0 failed. В них интеграционный HintPath и сквозной `run`. Это не Linux-числа автора из GREEN (**6/6**, **660/660**, **2002/2002**) и не прогон координатора.

## MUST-FIX

Нет. Сценария, в котором путь с реальным сигналом написанного входа проглатывается, или файловый потребитель вне графа своего производителя теряется, по коду и по прошедшим cases не подтверждено.

## Ограничения

- **Один канонический путь — несколько производителей.** `OutputProducer` для metadata-строки запоминает первый output, но `entry.Producers` всё равно содержит всех авторов `Output`. Замыкание объединяет их графы. Потребитель, который по графу зависит от одного автора общего файла, не выбирается сменой этого файла, даже если последним его записал другой автор. Карта не знает, кто записал байты. Разные полные пути этот случай не создают. Отдельной MSBuild-пробы коллизии `AssemblyName`/`OutputPath` эта сессия не делала. По R-18 без показанной потери конкретного теста это ограничение модели, не возврат.
- **Analyzer-ссылки.** `IndexMetadata` не видит `AnalyzerReferences`. Потребитель с `ReferenceOutputAssembly=false` / `OutputItemType=Analyzer` или с голым `<Analyzer Include>` на выход не получает `MetadataDependency`. Смена DLL его не выбирает. Старый `ProjectsForPath` тоже не имел этого вхождения: он выбирал производителя выхода и его графовых потомков, не отсутствующего в карте analyzer-потребителя. Если ребра графа нет, правка исходников производителя тоже не доходит до такого потребителя; это прежний пробел уровня 0, а статическое расширение вне scope. Фраза README «references … only as a file» точна для `MetadataReferences` и шире, чем карта, для analyzer file ref. Тот же раздел по-прежнему называет source generators слепым пятном и требует полный suite. Живой analyzer-проект не загружался.
- **Доказательство HintPath.** Промоция измерена для точного HintPath на `bin/Release/net10.0/App.dll` при загруженном SDK-проекте, у которого есть и implementation, и ref output. Публичный `MSBuildProjectLoader` сопоставляет metadata-путь с выходным проектом, когда заданы оба пути; сборка 5.9.0 содержит `SelectProjectInfoByOutput`. Это не перепроверка каждого custom target.
- **Принятая потеря косвенного сигнала.** CLI-свойство, обновление SDK и ручная подмена выхода больше не расширяют план. Для графового потребителя это решение R-19, не случайный пропуск. Отчёт опирается на формулировку «по R-01»; достаточное основание — R-19, при том что R-01 не объявляет пустой план доказательством.
- **Не закрыто этим коммитом.** Возврат не-C# к прежним байтам, reload с другой конфигурацией, статическое расширение файловой ссылки до сборки, полнота analyzer/generator inputs. Новые формулировки про rebuild читаются вместе с этими оговорками.
- **Bookkeeping.** Статусы task/README этот отчёт не меняет. Согласование «ревью проведено», ссылок и финальной validation остаётся координатору.

Не запускались SourceStructure, полный main suite, AnalyzerLifecycle, RequiresPinnedSdk, Linux и повтор исторического RED. Production, тесты, документация реализации и статусы не редактировались.

Verdict: ACCEPT
