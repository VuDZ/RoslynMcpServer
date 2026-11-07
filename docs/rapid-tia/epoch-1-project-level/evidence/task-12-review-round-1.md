# Task-12 — независимое ревью, раунд 1

- Дата: **2026-11-08**, Europe/Moscow.
- Рецензент: **Codex**, сессия `/root`; точный runtime model ID средой не предоставлен.
- Ветка: `rapid-tia`, обновлена fast-forward до `2c9b1e709d79efe2398bed8cdebfb8a230178be9` — `Stop treating rebuilt project outputs as affected-test changes (1.7.1)`.
- База: `64667118892003bc2a332e2af265eb67299c0ab1`.
- Scope: весь diff `6466711..2c9b1e7`, связанные mapper/builder/estimator/composer/formatter, тесты, [task-12](../task-12-build-output-signal-med.md), [отчёт реализации](../task-12-implementation-report.md), [RED](task-12-red.md), [GREEN](task-12-green.md), R-19 и происхождение T09-C1.
- Правило возврата: **R-18** — только доказанная молчаливая потеря тестов или неверный ответ / инструкция в конкретном реальном сценарии. Огрубление, стоимость и принятые ограничения не создают новый раунд. Статическое расширение до сборки, reload с другой конфигурацией и сравнение не-C# по содержимому исключены из scope задачи.
- Вердикт: **ACCEPT**. MUST-FIX замечаний по R-18 не подтверждено. Это результат независимого ревью, а не изменение статуса задачи или повторная приёмка эпохи.
- Второй рецензент: [Grok 4.7](task-12-review-round-1-grok.md) — **ACCEPT**.

## What this change does

Изменение убирает T09-C1: перезапись DLL проекта больше не переводит производителя и зависимые по графу тестовые проекты в whole-project до конца baseline. `ProjectImpactMapper` подавляет путь только тогда, когда **каждое** его вхождение имеет роль `Output` либо `MetadataDependency` и ссылается на производителя, присутствующего в текущем solution. Для такого пути остаются потребители метаданных вне графового замыкания производителей. Если их нет, путь не попадает в `Paths`, счётчик файлов и `Not covered`.

Watcher, input map, baseline, распознавание исходников и выполнение тестов не меняются. Путь с дополнительной ролью остаётся обычным входом; внешняя DLL без производителя также сохраняет прежнее поведение. Документация заменяет описание прежнего расширения правилом R-19; обязательная сборка текущих исходников при ручном запуске фильтра сохранена.

## Проверено

1. **Границы изменения и правила репозитория.** Прочитаны `AGENTS.md` и целиком `docs/code-style.md`. В production C# изменён только [ProjectImpactMapper](../../../../Services/Testing/ProjectImpactMapper.cs); новые строки не меняют surface инструментов, зависимости, lifecycle, input-map inventory или правила baseline. Остальной diff — пять файлов тестов/фикстур, версия и документация. Production, тесты, task/README и implementation evidence рецензентом не редактировались; посторонняя untracked `.agents/skills/prepare-github-release/` не затронута.

2. **Роли путей и принадлежность производителя.** Проверены `WorkspaceInputMapBuilder.Build`, `IndexOutputs` / `AddOutput`, `IndexDocuments` / `DocumentRole`, `IndexMetadata` / `OutputProducer`, `IndexProjectFiles` / `IndexWalkUp`, а также `InputPathEntry.Producers`. Оба выходных пути получают `Output` с ID своего проекта; точное совпадение metadata reference получает `MetadataDependency` с первым найденным output-производителем. `UserInput`, `AdditionalFile`, `AnalyzerConfig`, `EvaluationInput`, `Generated` и `Unknown` не проходят новый предикат. У внешней DLL производитель отсутствует, поэтому она тоже не подавляется. Это проверка ролей, а не расширения имени или расположения файла в `bin`/`obj`.

   Отдельная проба текущих Release-assemblies через существующую `ProjectImpactFixture` подтвердила: `AdditionalFile` на том же физическом выходном пути оставляет путь в результате и выбирает его тестовый проект. Возврат к обычному `ProjectsForPath` сохраняет существующий приоритет document ownership; этот приоритет данным diff не меняется.

3. **Граф, различные выходные пути и отсутствующие ID.** Проверены `IsSolutionBuildProduct`, `ConsumersOutsideProducerGraph`, `ExpandProjects` и создание `LoadedProjectInstance`. Граф использует `ProjectId`, а классификация — канонический полный путь. Общий каталог при разных именах DLL и разные TFM-пути сами по себе производителей не объединяют. Потребитель, связанный с другим проектом, остаётся выбранным, если у него нет графового пути к производителю именно данного выходного файла.

   Дополнительные пробы в памяти: обычный потребитель вне графа выбран, `Paths=1`; при сохранённой старой карте и удалённом производителе новый предикат выключается, живой metadata-потребитель остаётся выбранным; удалённый потребитель отфильтровывается без исключения. Неоднозначность двух производителей **одного полного пути** рассмотрена отдельно в ограничениях ниже.

4. **Analyzer-ссылки не выданы за обычные metadata references.** `IndexMetadata` обходит `Project.MetadataReferences`, но не `Project.AnalyzerReferences`. Поэтому `OutputItemType=Analyzer` не получает в этой карте отдельного `MetadataDependency` для analyzer-потребителя; наличие связи в `ProjectReferences` следует проверять по реально загруженному графу. Новый предикат не доказывает полноту analyzer/generator dependencies. Это существующая граница карты и документированного уровня 0; конкретной новой потери от task-12 в этой области не воспроизведено. Специальная живая проба analyzer-проекта в данном ревью не проводилась.

5. **HintPath: измеренный факт и граница вывода.** В [ProjectImpactMapperIntegrationTests](../../../../RoslynMcpServer.Tests/Testing/ProjectImpactMapperIntegrationTests.cs) новый тест действительно собирает проекты настоящим `DotNetCliRunner`, проверяет существование `OutputFilePath` и `OutputRefFilePath` и наличие обоих в карте. Проверка `consumer.ProjectReferences == [app.Id]` непосредственно подтверждает promotion точного HintPath на выход загруженного `App` в этой конфигурации. Затем оба output-пути дают пустые project/test/path/not-covered наборы, а вход проекта выбирает `Consumer` и `Specs`. Тест использует `app.FilePath` (файл проекта), а не редактирование `.cs`; распространение от owner через тот же граф дополнительно видно в `ProjectsForPath` / `ExpandProjects` и существующих mapper tests.

   Это подтверждает отсутствие необходимости добавлять отдельную статическую связь **для измеренного точного HintPath**. Общий вывод implementation report «для MSBuild-загрузки не нужна» нельзя читать как доказательство для копий DLL, произвольных custom targets или всех multi-target конфигураций. Статическое расширение таких связей до сборки прямо исключено из task-12 и не является основанием возврата.

6. **Ответ и stale/gap guards.** Проверены [TestImpactEstimator.EstimateAsync](../../../../Services/Testing/TestImpactEstimator.cs), `DocumentsAsync`, [TestImpactComposer.ExpandedProjects](../../../../Services/Testing/TestImpactComposer.cs), [TestImpactFormatter.Format](../../../../Services/Testing/TestImpactFormatter.cs). Счётчик файлов берётся из `mapped.Paths`, причины whole-project — из оставшихся path owners, а `Not covered` дополняется независимо от них. Поэтому подавленный output не остаётся ложной причиной расширения или неизвестным путём. C#-сравнение, compilation errors и persistent observation-gap расширение остаются самостоятельными ветками.

   Проба с единственным output-путём подтвердила: `projectGraphStaleFromGraphFile=true` по-прежнему выбирает оба тестовых проекта фикстуры при `Paths=0`, `RequiresReload=true`; membership stale без graph-file stale сохраняет `RequiresReload=true`, не выбирая посторонние проекты. Общий main suite включает существующие reload/gap проверки.

7. **Шесть новых cases и RED provenance.** Две строки theory проверяют основной и reference output; ещё два mapper cases разделяют сохранение потребителя вне графа и внешней DLL. Они используют настоящую `WorkspaceInputMapBuilder`, а не вручную присвоенные роли. Копия framework assembly нужна этим unit fixtures только как существующий валидный PE-файл; реальная identity сборки этими cases не измеряется. Это дополняется настоящей MSBuild-интеграцией из пункта 5.

   В [EstimateAffectedTestsRunTests](../../../../RoslynMcpServer.Tests/Testing/EstimateAffectedTestsRunTests.cs) `Estimate_after_run_keeps_the_filter_and_a_restored_edit_leaves_an_empty_plan` использует [TestImpactRunFixture](../../../../RoslynMcpServer.Tests/Testing/TestImpactRunFixture.cs): настоящие SDK/xUnit-проекты, предварительный build, `SolutionManager.LoadAsync`, запись через `EditingTools`, реальный `run: true`, повторный estimate и восстановление исходного текста. Проверяются одинаковые `Run:` строки, один изменённый файл после запуска и нулевой план после отката. Это не заглушка executor.

   RED-описание согласуется с кодом базы `6466711`: прежний `ProjectsForPath` возвращал output owner вместе с metadata owners и оставлял DLL в `Paths`; внешняя DLL работала уже тогда. Поэтому заявленные **5 failed / 1 passed** соответствуют указанным assertions, включая whole-project после run. Первоначальная ошибочная гипотеза о HintPath отделена от поведенческого RED. Исторический Linux RED-прогон независимо не воспроизводился; исходного TRX в пакете нет, поэтому сам факт его выполнения подтверждается авторским evidence, а не новым прогоном рецензента.

8. **Потребительские тексты, история и версия.** Сверены README EN/RU (tool reference и запись 1.7.1), `AGENTS.md.sample`, `docs/ARCHITECTURE.md`, CHANGELOG и R-19. В README и sample ручной фильтр по-прежнему требует явный `noBuild: false`; предупреждения о приблизительности и обязательном полном suite/CI сохранены. Результат пустого estimate не назван проверкой корректности. Версия `1.7.1 / 1.7.1.0 / 1.7.1.0` соответствует исправлению поведения без новой tool schema. Добавленная историческая запись 1.6.0 сверена с `98773ad`: версия 1.6.0, core tool, счётчики 55/16, estimate-only API, baseline первой загрузки и прежние warning / `Not covered` / runtime-name guards действительно существовали в этом коммите.

9. **Собственные прогоны рецензента, Windows, Release, текущий `2c9b1e7`.** Выполнены через специализированные Roslyn MCP runners:

   - `run_dotnet_build`, `RoslynMcpServer.sln`, `configuration=Release`, `noIncremental=true`: **PASS**. Три прежних предупреждения: CS8603 в `NuGetFallbackAssemblyResolver`, CS8601 в `HostSession`, xUnit1031 в `WpfTemporaryProjectCallbackTests`.
   - `run_test_by_filter`, `RoslynMcpServer.Tests.csproj`, `noBuild=true`, `noRestore=true`, фильтр `FullyQualifiedName~ProjectImpactMapper|FullyQualifiedName~EstimateAffectedTestsRunTests|FullyQualifiedName~SourceStructure`: **175/175 PASS**, без failed/skipped/timeout. Включены все новые cases и весь SourceStructure.
   - Полный main suite с Windows-фильтром workflow `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`, тот же Release build: **2010/2010 PASS**, без failed/skipped/timeout.

   Это собственные результаты; авторские Linux **6/6**, **660/660** и **2002/2002** из GREEN/report не переименованы в независимый прогон. Отдельные reflection-пробы не включены в числа тестового suite.

## MUST-FIX

Нет подтверждённых замечаний, требующих возврата по R-18.

## Ограничения

- **Один путь — несколько производителей.** `OutputProducer` выбирает первый output, а `ConsumersOutsideProducerGraph` объединяет графовые замыкания всех `entry.Producers`. Карта не устанавливает, какой проект последним записал общий физический файл. В пробе в памяти с `Library` и `Isolated`, имеющими один output, `Specs → App → Library` и metadata reference `Specs` на тот же файл, output полностью подавлен; при добавлении изменённого отдельного исходника `Isolated` выбирается только `Isolated`. Это неоднозначность модели, а не доказанное различение производителей. Отдельная реальная MSBuild-проба (SDK 10.0.402, одинаковые `AssemblyName=Common` / `OutputPath`, разные проекты) подтвердила два `Output` на один путь и такое подавление. После обычной сборки потребителя вызов его проверочного метода завершился `Runtime Case PASS`; потери падающего теста в этом сценарии не получено. Не заявляется безопасность всех коллизий, custom copy targets или layout без копирования зависимостей; по R-18 без конкретной подтверждённой потери это ограничение, а не MUST-FIX.
- **Границы вспомогательной пробы.** Первоначальные попытки реальной пробы остановились на sandbox NuGet access и загрузке зависимости из асинхронного PowerShell reflection-вызова; они не считаются PASS. Повтор с подготовленными зависимостями вне sandbox выполнил build/load/map/build/runtime-проверку. Автоочистка задержалась из-за загруженной DLL; после завершения процесса временный каталог удалён отдельно, отсутствие проверено. Новых постоянных файлов пробы в репозитории нет.
- **Изменения бинарника без отслеживаемого входа.** Новые свойства CLI, внешняя смена SDK и ручная подмена output больше не дают прежнего косвенного сигнала производителю/графовым потребителям. Это реальная потеря прежнего эвристического сигнала, явно раскрытая implementation report и следующая из R-19. R-01 сам по себе не оправдывал бы любую регрессию, но отдельного нового воспроизводимого случая за пределами принятого изменения сигнала здесь не установлено. Для таких операций пустой estimate не означает, что тесты не нужны; полный suite/CI остаётся обязательным.
- **Сохраняющиеся границы.** Non-C# возврат к исходному содержимому, reload с другой конфигурацией, статическое расширение file-reference потребителя до сборки, полнота analyzer/generator inputs и реальные multi-target загрузки не закрыты этим исправлением. Новые сообщения о rebuild следует читать вместе с этими оговорками, а не как обещание неизменности плана при любом изменении generated C#.
- **Приёмка и bookkeeping.** Этот отчёт не изменяет `implemented, ревью не проводилось` в task/index/report и не активирует эпохи 2–4. Согласование статусов, evidence links и финальный acceptance остаются за координатором согласно условиям ревью.

Не запускались отдельные Linux, AnalyzerLifecycle и RequiresPinnedSdk suites, новый owner measurement и живой analyzer/multi-target matrix. Production, тесты и статусы не изменялись; commit/push не выполнялись.

Verdict: ACCEPT
