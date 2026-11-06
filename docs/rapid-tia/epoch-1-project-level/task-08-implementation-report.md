# E1/task-08 — отчёт реализации

- Дата: **2026-11-06** (Europe/Moscow); база `14fe3ee`, ветка `rapid-tia`.
- Исполнитель: Codex, сессия `/root/task08_implementer`; точный runtime model ID неизвестен, средой не предоставлен.
- Статус: **accepted повторно, 2026-11-06**, версия **1.6.1**; R2-001/R3-001 закрыты, [раунд 4 — ACCEPT](evidence/task-08-review-round-4.md), [финальная validation](evidence/task-08-final-validation-round-4.md). Владелец явно разрешил четвёртое ревью сверх прежнего лимита R-18.
- Scope: `estimate_affected_tests` в `core`/`lite`, baseline/current интеграция, проектная оценка, runtime-инвентарь T07-C1, формат ответа и подробности, публичная документация и каталог. Флаг `run`, граф и task-09+ вне scope.
- Текущая версия: **1.6.1**, patch для R2-001 после зелёной сборки и разрешения координатора; `Version=1.6.1`, `AssemblyVersion=1.6.1.0`, `FileVersion=1.6.1.0`. Первоначальная поставка task-08 подняла minor **1.5.18 → 1.6.0**. PackageVersion не добавлялась. Новая финальная validation выполняется на 1.6.1.
- Первоначальный RED: [evidence координатора](evidence/task-08-red.md). Поздние два RED → GREEN: [отдельный отчёт](evidence/task-08-additional-red-green.md).
- Независимое ревью: Codex, отдельные `/root/task08_review_round1`, `/root/task08_review_round2`, `/root/task08_review_round3`, `/root/task08_review_round4` (runtime model ID средой не предоставлены); Claude Opus 5.5, второй рецензент раунда 2. **4 раунда / 5 исправлений**, R1-001/R1-002/R1-003/R2-001/R3-001 закрыты; [окончательный ACCEPT](evidence/task-08-review-round-4.md).

## Реализация

`TestTools.EstimateAffectedTests` первым шагом получает опубликованный solution после существующего disk sync. Передаёт настоящий снимок `BaselineSession`, карту входов, путь загрузки и оба текущих stale-флага в `TestImpactEstimator`. Baseline capture не ожидается: незавершённый снимок честно становится unknown в конкретном ответе.

Для baseline/current membership используется существующий `BaselineSession.Inventory`: ключи физического проекта, loaded context, logical path и occurrence сохраняются при новых Roslyn IDs после reload. Метод и `BaselineDocumentSnapshotBuilder.Key` доступны внутри assembly; тела алгоритмов не менялись. Аналогично переиспользован существующий поиск тестовых проектов mapper.

Текущие C# снимки сравниваются прежним detector. Неизменённые Known документы, trivia и возврат к baseline не дают дельту. Pending/Unknown не считаются неизменёнными: путь явно назван в `Not covered`, применяется проектное расширение. Удалённые baseline-документы остаются входом. Новые C# пути до reload читаются с диска и относятся mapper к ближайшим проектам, поэтому новые тесты остаются видимы напрямую, даже пока workspace их ещё не содержит.

Composer получает полный текущий документный инвентарь, включая неизменённые partial-части. Список простых имён баз объединяет original baseline и текущие снимки, включая удалённых наследников. Объединение консервативно захватывает документы всего solution, поэтому совпадения имён вне тестовых проектов могут дополнительно расширять ответ.

`RequiresFullScan` сохраняет первоначальный контракт C# rescan после reload/reset и настоящих разрывов. После R2-001 отдельный `HasObservationGap` отличает настоящий разрыв от обычного окна reload: только настоящий разрыв включает все текущие тестовые проекты целиком. Обычный reload оставляет постоянную оговорку о возможных незамеченных не-C# изменениях в `Not covered`, а C#-дельты отображаются обычным путём. Настоящие watcher flags отдельно управляют stale/reload: появление нового source не выбирает независимые проекты; после reload удалённый `.cs` не даёт повторный совет reload.

`VstestTestInventory` сопоставляет baseline-ключи методов с конкретными текущими классами через Roslyn symbols. Вложенность даёт `+`, унаследованные source-тесты включаются под каждым конкретным владельцем, в том числе при source-базе в другом проекте. Partial-части и унаследованные тесты входят до утверждения полноты. Непрочитанный/синтаксически сломанный документ либо metadata-only test без source-ключа не позволяет объявить полный инвентарь класса.

Закрытый негeneric-наследник xUnit/MSTest использует собственное runtime-имя. Для открытого generic имя не угадывается. Из NUnit generic-форм известна измеренная `[TestFixture(typeof(int))]` → `<Int32>`; прочие формы передаются unresolved. `[TestFixture]` с constructor arguments и `[TestFixtureSource]`, включая унаследованные metadata базовых классов, дают `HasFixtureArguments` и проект целиком. Граница остаётся прежней: планировщик доверяет caller, formatter ничего не переводит самостоятельно.

`VstestRunPlanner` переиспользован без изменения алгоритма. Ответ включает причины фильтров/расширений, предупреждение в первой строке каждого ответа (включая пустой, cancellation и ошибку), конкретные ограничения, compilation errors в изменённых документах и `project-level result only`. По умолчанию возвращается сводка с планами; `includeDetails` перечисляет изменённые/current-unknown члены, прямые тесты и текущие тесты выбранных целиком проектов, а также известные runtime-имена.

Новые зависимости, настройки, background lifecycle и выполнение процессов в инструмент не добавлены. Текст tool description не обещает граф вызовов и не публикует `run`/symbol-параметры.

## TDD и проверки

До production-поведения написаны четыре файла тестов и только два компилируемых контракта с `NotImplementedException`. Координатор наблюдал **49 executed / 49 failed**: 47 ожидаемых missing-behavior failures и два отсутствующих catalog registrations, без compile/fixture failures, skips и timeouts. Первоначальная ручная оценка 50 исправлена на фактический discovery 49.

Первоначальный пакет охватывает warning/empty/error/cancelled, summary/details, semantic compilation error, unknown baseline, permanent gap до/после reload, настоящие MSBuild disk и MCP edits, native C1/C2 watcher paths, unmapped path и graph-file stale, partial inventory и baseline/current base names; runtime nested/generic/inherited/fixture metadata; `core`/`lite` schema и описание.

16 adapter replay cases копируют **настоящие исходники** `spikes/AdapterTestNames`, грузят временный SDK-проект через MSBuild, изменяют выбранный метод через `EditingTools.WriteFile`, вызывают публичный инструмент и проверяют план по сохранённым фактическим FQN/IDs task-06. Framework attributes/assertions во временном replay-проекте — stubs, сами адаптеры в этих main tests не запускаются. Ещё три публичных сценария покрывают NUnit constructor/source arguments и один — unresolved generic. Живые adapter-прогоны выполняет координатор отдельно; frozen spike sources/catalog не менялись.

После первого GREEN добавлены **два** теста с отдельным RED перед соответствующим исправлением:

1. `Source_base_in_another_project_is_included_in_the_concrete_class_inventory`: **1/1 RED** (`[Inherited, Own]` против `[Own]`) → весь inventory **13/13 GREEN**.
2. `Details_of_project_expansion_list_every_current_test_in_the_selected_project`: **1/1 RED** (отсутствовал `Specs: Cases\`0.Case`) → **1/1 GREEN**, затем весь публичный пакет **38/38**.

Они явно не выдаются за первоначальную red-фазу; причины, точные сообщения и вызовы сохранены в [evidence](evidence/task-08-additional-red-green.md).

После первоначального RED также применены эквивалентные API-поправки: `Assert.Single(sequence.Where(predicate))` заменены на predicate overload; nullable-тип известного runtime-имени в assertion помечен `!`. Ожидания и количество исходных cases не менялись. Поэтому три первоначальных test-file hashes отличаются от зафиксированных RED; это не заявление о byte-identical freeze. Существующие catalog count/size assertions обновлены после измерения финальной схемы.

Focused validation исполнителя **до первого независимого ревью**, специализированный Roslyn MCP, Windows Release:

- `EstimateAffectedTests`: **38/38**.
- `VstestTestInventoryTests`: **13/13**. Всего новых cases: **51**.
- Весь `Hosting`: **80/80** (два новых catalog cases уже входят в 38).
- Весь `SourceStructure`: **123/123**, точные inventories identities/kinds/paths сохранены без ослабления.
- Начальная Release solution build — PASS; финальную после заморозки и после minor bump делает координатор.

Полный main CI suite, live adapter checks, owner-project smoke, независимое ревью и окончательная Release build не подменяются focused результатами исполнителя.

## Исправления раунда 1

[Независимое ревью](evidence/task-08-review-round-1.md) вернуло три реально воспроизведённых потери. По разрешению координатора исправления выполнил тот же исполнитель. Новые regression tests добавлены **до** production-исправлений. [RED координатора](evidence/task-08-review-round-1-red.md): **7 executed / 7 failed**, без ошибок фикстур, timeout и skips; NUnit дополнительно воспроизведён настоящим discovery и нулевыми запусками ошибочных фильтров.

- **R1-001, удаление до reload.** При disk sync отсутствующий файл оставляет cached `Document`. Теперь физическое отсутствие проверяется до сравнения с кешированным деревом: путь остаётся значимым, из baseline формируются удаления и выполняется проектное расширение. Регрессия использует Known baseline, настоящий native delete/event и публичный ответ до reload.
- **R1-002, inherited-only тестовый проект.** Существующий поиск проектов с собственными test method declarations дополнен ProjectId из полного runtime-инвентаря. Поэтому конкретный `Derived : Base` в другом проекте участвует в обычном транзитивном расширении, даже если собственных тестовых методов там нет. Две MSBuild-регрессии меняют базовый тест и обычный production-член. Первоначальная fixture без прямой ссылки не проходила inventory precondition и не считается RED: добавлена явная `Specs → Library` reference, затем координатор подтвердил два падения финального `Specs.csproj` assertion при неизменённом production.
- **R1-003, inherited NUnit fixture arguments.** Class attributes собираются по `BaseType` chain до решения о runtime identity. Constructor/source arguments абстрактной базы дают whole-project fallback конкретного наследника. Два inventory cases и два отдельных public-tool cases покрывают constructor и source варианты; каждый вариант проверяется отдельно, поэтому whole-project результат одного не маскирует второй.

После исправлений специализированный Roslyn MCP, Windows Release: **43/43** публичных/catalog/adapter cases и **15/15** inventory, всего **58** новых cases; **SourceStructure 123/123**, точные limits не менялись. [Focused GREEN после раунда 1](evidence/task-08-review-round-1-green.md). Исходные assertions не ослаблялись. Эти **3 post-review fixes и 7 regression cases** учитываются отдельно от двух поздних pre-review audit guards.

Повторная C#/project заморозка объявлена на версии **1.6.0**. После неё координатор провёл [финальные проверки](evidence/task-08-final-validation.md), [живые пробы](evidence/task-08-live-validation.md) и получил [независимый ACCEPT раунда 2](evidence/task-08-review-round-2.md).

## Размер публичной поверхности

Измерено существующим `McpToolCatalogTests.Surface_sizes_match_recorded_release_numbers`, minified `tools/list`, UTF-8:

- `full`: **54 / 45 807 → 55 / 46 574**.
- `lite`: **15 / 19 411 → 16 / 20 178**.
- Приращение: **1 tool / 767 bytes** в каждом профиле.
- `lite+files`: 24 / 25 234; `lite+editing`: 24 / 25 448; `lite+decompile`: 20 / 22 940; `lite+nuget`: 22 / 23 557; `lite+project`: 19 / 21 698; `lite+runtime`: 19 / 22 916; `lite+operations`: 20 / 21 939; dynamic enable files совпадает с startup files.
- Абсолютные бюджеты **50 KiB / 22 KiB** не изменены. Все точные catalog assertions проходят.
- Описания без bootstrap help: текущий общий total **21 652**, существовавший до task-08 набор **21 127**, новый tool+parameter **525** символов. Исторический тест сокращения на 40% по baseline исходного каталога сохраняет прежний порог **21 267** и исключает только новый `estimate_affected_tests`; абсолютные byte budgets по-прежнему считают все инструменты. Решение согласовано координатором; warning не урезан ради старого относительного порога.

Корневой README обновлён в обоих языках, включая реальные counts/bytes и описание estimate-only режима; JIT help добавлен в `McpToolHelpCatalog`.

## Ограничения и передача

- Это уровень проектов без графа: helper edits, неизвестные baseline/identity и gap дают широкий ответ. Такой результат не заменяет main suite, CI и дополнительные обязательные тесты из `AGENTS.md`.
- Сохранены ограничения task-03 наблюдения, task-04 input map/restore/build output и синтаксических ролей task-01/02. Full rebuild может выбирать все тесты (T04-C3).
- Измеренные generic-формы ограничены прежним task-06; другие формы выбирают проект целиком. Metadata-only наследование не позволяет доказать полный source-инвентарь класса; method filters разрешены без class collapse. Подробности описывают текущие известные source/runtime identities, не заменяют adapter discovery.
- На запрос перечисляются текущие C# документы и semantic-инвентарь, а для изменённых проектов читаются compiler diagnostics. Нового кеша и performance benchmark нет; цена и частота gap измеряются координатором на owner solution по R-18.
- До R2-001 lifecycle capture/publication/shadow-copy поведение не менялось. В R3 baseline дополнен отдельным признаком настоящего разрыва при сохранении поколений, отмены capture, Unknown и C# rescan. Поэтому для нового исправления координатор дополнительно запускает opted-in AnalyzerLifecycle; историческая validation без него не подменяет новый прогон.
- Task-09 `run`, MTP, graph и активация следующих эпох не реализованы. Task/spec requirements не менялись, посторонний `.agents/skills/prepare-github-release/` сохранён. Commit/push исполнитель не выполнял.
- Статусы task, README эпохи, программы и индекса обновлены координатором после обязательной validation и приёмки. Эпоха целиком и task-09+ остаются непринятыми.

## Приёмка после раунда 2 — история

Координатор подтвердил финальную Release solution noIncremental build **PASS** на 1.6.0, focused **58/58**, SourceStructure **123/123**, полный main **1970/1970**, без падений и пропусков. [Final validation](evidence/task-08-final-validation.md). Живые проверки: **19 планов / 61 успешное test execution** на адаптерах и **16/16** по фильтру из чистой owner-пробы; initial/edit/restore — **0 → 1 → 0**, observation gap **0/3** ([evidence](evidence/task-08-live-validation.md)). Длительный замер частоты gap и другие owner solutions не заявляются. Три замечания раунда 1 закрыты, раунд 2 — ACCEPT; задача принята 2026-11-06 в указанном scope, статусы и evidence согласованы.

## R2-001 — исправление для последнего раунда 3

- Основание: [внешнее ревью раунда 2](evidence/task-08-review-round-2-claude.md) и новый раздел task-08 «Раунд 3». Владелец явно вернул задачу по грубости ответа, несмотря на общее правило R-18. Scope ограничен task-08; task-09 не начата.
- База исправления: `1f11b47`; тот же исполнитель `/root/task08_implementer`, точный runtime model ID средой не предоставлен. Статусы task/индексов и окончательное принятие остаются у координатора.
- TDD: [RED координатора](evidence/task-08-review-round-3-red.md) — **8 executed / 3 passed / 5 failed** на неизменённом production; все пять падений в ожидаемых assertions публичного ответа. До реализации один Fresh-case уточнён до обычного `using System; namespace Added`; отдельный MCP RED **1/1**, отсутствовал `FullyQualifiedName~`. Точный дополнительный raw output: ignored `TestResults/task-08-round-3-red/namespace-red-mcp.txt`; новых cases это не добавило.

### Минимальное изменение состояния baseline

`RequiresFullScan` и его существующие guards **не ослаблялись**: reset/reopen продолжает закрывать generation, отменять capture без ожидания, сохранять Known hashes, замораживать Pending как Unknown и объединять исходные/текущие C# rescan candidates. Добавлен только отдельный sticky `HasObservationGap`. Он устанавливается прежними `NoteObservationGap`, startup-gap seed и общей ошибкой capture; обычный `CloseGenerationUnderLock` его не поднимает. Настоящий разрыв не сбрасывается reload и очищается только с новым baseline другого solution.

Estimator расширяет все проекты только при `HasObservationGap`. При ordinary reload/reopen с `RequiresFullScan` остаётся `Not covered` о возможных пропущенных не-C# входах; C# дельты, причины, watcher stale и reload advice обрабатываются отдельно. Это сохраняет защиту от настоящих сбоев watcher, overflow, directory rename и snapshot failure.

### Новые и удалённые после загрузки C# файлы

Известное отсутствие физического пути в **исходном** source inventory позволяет сравнить новый загруженный документ с пустым снимком, если у пути не было известной исходной non-source роли (уточнение R3-001 ниже). Обычные imports и namespace относятся только к новому файлу и не дают самостоятельного расширения; `global using`, assembly attributes, directives, extension/operator и прочие binding-sensitive изменения проходят прежние правила расширения.

Одного отсутствия текущего ключа недостаточно: если физический путь был в исходном inventory другого context/TFM/logical membership, остаются Unknown и консервативное расширение. Созданный после загрузки и затем удалённый source без исходного пути не даёт вечной дельты после настоящего reload. Очистка сохраняет приоритет карты: AdditionalFiles, analyzer-config, evaluation/generated occurrences не отбрасываются по одному расширению `.cs`. Удаление исходного Known/Unknown source по R1-001 сохраняется.

Изменённый `.csproj` **не забывается** при reload: реальная сохраняющаяся правка `DefineConstants`/references остаётся основанием выбора собственного и зависимых тестовых проектов. Поэтому `.csproj`-регрессия ожидает `Specs` целиком без независимого `Isolated`; отдельный Fresh-flow доказывает возвращение фильтров и пустого плана после отката всех C# изменений. Новый кеш не-C# входов или механизм сравнения произвольных project files не добавлялся.

### Реальные reload-регрессии и корректировка fixture

Новый `EstimateAffectedTestsReloadTests` содержит **7 cases**: native Fresh + namespace/imports после реального reload; rollback с удалением Fresh и вторым реальным reload; сохраняющаяся native `.csproj` правка; два C# guard — внутри `BeforeDiskWatcherStartForTests` с историческим mtime и отсутствием watcher event, и после реального reopen; настоящий `NoteObservationGap`; capture failure. Все reload helpers проверяют `LastLoadReopenedGraph`, `!LastLoadWasCacheHit`, рост generation и сброс stale. Guard окна проверяет публичный фильтр, сохранённый Known snapshot и rescan membership. Прежний `Observation_gap...survives_reload` также принудительно меняет configuration на Release и проверяет настоящий reopen.

После первоначального RED и частичного production-исправления диагностика обнаружила дополнительный T04-C3 шум в nested fixture: `App/obj/...AssemblyInfo.cs` после первого load попадал в новый Library compile context. Правило исходного пути корректно оставляло его Unknown. По согласованию координатора **только новые R3 cases** используют `TestImpactWatcherFixture.LoadAsync(suppressGeneratedFiles: true)`: `GenerateAssemblyInfo`/`GenerateTargetFrameworkAttribute` выключаются до первого MSBuild load, default false сохраняет прежние fixtures. Это изолирует R2-001 от известного generated-membership ограничения; production правило context uncertainty и T04-C3 не ослаблены.

Две assertions переведены в эквивалентную форму `Assert.True(condition, fullResponse)` для полного текста ответа при падении; условия и количество cases сохранены. Первоначальный RED остаётся настоящими падениями публичного ответа, а не compile/setup failure; fixture isolation и изменение диагностики не выдаются за byte-identical исходный RED.

### Передача на validation

Специализированный Roslyn MCP, Windows Release, после исправления:

- R3 focused **8/8** — семь новых cases и прежний true-gap case.
- Все `EstimateAffectedTests` **50/50**; вместе с прежними 15 inventory cases task-08 теперь содержит **65** новых cases.
- `Baseline` **349/349**, прежние Pending/cancellation/generation/linked-context guards и assertions не менялись.
- `SourceStructure` **123/123**, точный inventory limits не менялся и не ослаблялся.

[Focused GREEN](evidence/task-08-review-round-3-green.md). Координатор подтвердил финальную Release solution noIncremental build на **1.6.1**, focused **65/65**, SourceStructure **123/123**, main **1977/1977** и opted-in AnalyzerLifecycle **13/13**; warnings только прежние CS8603/CS8601/xUnit1031. Новый isolated stdio server подтвердил v1.6.1.0 и реальный owner reload/restore/filter цикл ([validation](evidence/task-08-final-validation-round-3.md)). Patch Version/AssemblyVersion/FileVersion согласован после зелёной сборки, PackageVersion не добавлялась; CHANGELOG обновлён. Однако [последний раунд 3](evidence/task-08-review-round-3.md) закрыл R2-001 и выявил **R3-001** — удаление исходного globbed AdditionalFile `.cs` теряет уже наблюдённую дельту после reload. Задача остаётся reopened; принятие, коммит и push не выполнялись. Продолжение сверх лимита R-18 ожидает решения владельца.

## R3-001 — исправление и отдельно разрешённый раунд 4

Владелец явно разрешил продолжение после MUST-FIX третьего раунда: «Исправить и проверить ещё раз». Работу продолжил тот же `/root/task08_implementer`, runtime model ID средой не предоставлен. Исторические отчёты R3 не изменялись. Task-09 и дальнейшие эпохи не начаты; task/индексы и окончательная приёмка принадлежат координатору.

### TDD и точные preconditions

До production-исправления добавлен `EstimateAffectedTestsNonSourceReloadTests`, три cases. Два начинают с реальной MSBuild загрузки Debug: `Data/Payload.cs` существует как globbed `AdditionalFiles`, `EnableDefaultCompileItems=false`, путь отсутствует среди source Documents и baseline C# keys; input-map role явно AdditionalFile. После native delete/event публичный инструмент выбирает App целиком. Затем настоящий configuration reload без записи `.csproj` удаляет AdditionalDocument из current map; итоговый публичный ответ должен по-прежнему включать App и путь причины.

Вторая строка theory перед удалением делает промежуточный Release reload: тот же первоначальный AdditionalFile становится также compile source по условию исходного, неизменённого `.csproj`. Это не позволяет принять одно «когда-либо был source» за доказательство исходного отсутствия другой роли.

Третий case сохраняет неизвестный journal path: `EditingTools.WriteFile` действительно записывает `Unmapped.cs` вне проектных каталогов, проверяются journal и исходный `Not covered`; после удаления и настоящего reload неизвестный путь должен оставаться видимым. Первая версия ожидала native event вне watcher coverage и получила fixture timeout: она исправлена до production, такой timeout не считается RED. [Достоверный RED координатора](evidence/task-08-review-round-4-red.md) — **3 executed / 3 failed**, все падения только на конечных assertions публичного ответа; compile/setup/timeouts отсутствуют.

### Минимальное исправление provenance

`BaselineSessionSnapshot.LoadedSourcePaths` — immutable копия уже существующего `_rescanPaths`: доказательство, что путь хотя бы раз входил в загруженный C# inventory. Новый bounded `OriginalNonSourcePaths` фиксируется только при первом Publish baseline и сохраняется при reload того же solution. Источник — доступные path metadata: AdditionalDocuments, AnalyzerConfigDocuments, MetadataReference FilePath/Display, AnalyzerReference FullPath, project/output/ref-output и solution paths. Нет File IO, content hashes, дискового кеша или новой history framework. Новый baseline другого solution заново задаёт original metadata; immutable старые views не меняются.

Отбрасывание отсутствующего пути требует положительного доказательства загруженного source и исключает любой первоначально известный source/non-source путь. Одного отсутствия в refreshed map больше недостаточно. Та же исходная non-source защита применяется к cached missing Document и к решению сравнивать новый current Document с пустым снимком: initial AdditionalFile не становится «доказанно новым source» от смены configuration. Неизвестный путь без source proof остаётся в journal и достигает mapper/`Not covered`.

Сохранены R2 Fresh rollback и namespace/import exception, original Known/Unknown/context guards, mapped-role priority и разница `HasObservationGap`/ordinary reload. При исчезновении роли из refreshed map исходное наблюдённое изменение не забывается; текущие обычные правила mapper определяют closure либо конкретный uncovered path.

### Focused GREEN и передача

[GREEN исполнителя](evidence/task-08-review-round-4-green.md), специализированный Roslyn MCP, Windows Release:

- Новые R3-001 regressions **3/3**.
- Все `EstimateAffectedTests` **53/53** и inventory **15/15**, всего task-08 **68** новых cases.
- `Baseline` **349/349**, прежние assertions не менялись.
- `SourceStructure` **123/123**, прежние точные identities/kinds/paths limits сохранены.

C#/project повторно заморожены. Версия **1.6.1** сохранена по решению координатора: исправление входит в ту же ещё не поставленную итерацию; нового bump не было. CHANGELOG 1.6.1 дополнен сохранением удалённых additional inputs и неизвестных путей после reload. Финальные Release build, main CI, opted-in AnalyzerLifecycle, live R3-001 и независимый раунд 4 ещё не подменяются focused результатами. Коммит/push и статусы accepted исполнитель не выставлял.

### Test-isolation correction после первого main раунда 4

Координатор получил **1980 total / 1979 passed / 1 failed**: NonSourceReload regression на строке 64 увидела `HasObservationGap=true` при неизменённом NoGap assertion. Предварительные focused 3/3 и live R3/R2 probes прошли; этот полный main не выдаётся за успешный, lifecycle после него ещё не выполнялся.

Код fixture подтверждает общий process-wide `WorkspaceDiskWatcherStarter.FailNextStartForTests`, устанавливаемый `BaselineObservationGapTests` и потребляемый следующим watcher startup. Существующая коллекция `BaselineObservationGap` имеет `DisableParallelization=true` именно для такой изоляции. Новые R3/R4 native-классы ещё не использовали её. Отдельного trace источника неожиданного gap в исходном assertion нет, поэтому конкретная гонка не объявляется доказанной одним этим выводом.

В рамках первого test-fix цикла добавлены только `[Collection("BaselineObservationGap")]` у `EstimateAffectedTestsReloadTests` и `EstimateAffectedTestsNonSourceReloadTests`. Проверки `HasObservationGap`, native delivery, настоящего reload и все прочие assertions не ослаблены. Production, версия 1.6.1 и количество cases не менялись. C# немедленно повторно заморожен; координатор повторил Release/affected/main/opted-in lifecycle с PASS. [Дополнение к GREEN](evidence/task-08-review-round-4-green.md#test-isolation-correction-после-первого-полного-main) сохраняет незелёный первый main и эту fixture-коррекцию отдельно.

## Окончательная повторная приёмка после раунда 4

Координатор после последней test-isolation delta подтвердил Release solution build **PASS**, focused **68/68**, SourceStructure **123/123**, полный main **1980/1980** и opted-in AnalyzerLifecycle **13/13** ([evidence](evidence/task-08-final-validation-round-4.md)). Первый main 1979/1980 сохранён в истории, не выдан за pass. Живой повтор исходного R3-001 сохраняет выбранный Specs и конкретную Payload.cs дельту после reload; текущий owner solution подтверждает filter/restore/empty цикл R2-001. [Четвёртое независимое ревью — ACCEPT](evidence/task-08-review-round-4.md). Владелец явно разрешил этот дополнительный раунд; статусы task/индексов обновлены после фактической validation. Принят estimate-only scope на 1.6.1, task-09 и эпоха целиком не приняты. Linux этой версии не запускался, ограничения public input metadata/watcher и приблизительность оценки сохраняются.
