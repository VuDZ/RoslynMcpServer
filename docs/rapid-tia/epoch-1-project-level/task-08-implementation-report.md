# E1/task-08 — отчёт реализации

- Дата: **2026-11-06** (Europe/Moscow); база `14fe3ee`, ветка `rapid-tia`.
- Исполнитель: Codex, сессия `/root/task08_implementer`; точный runtime model ID неизвестен, средой не предоставлен.
- Статус: **accepted, 2026-11-06**; [раунд 2 — ACCEPT](evidence/task-08-review-round-2.md), [финальная validation координатора](evidence/task-08-final-validation.md), [живые проверки](evidence/task-08-live-validation.md).
- Scope: `estimate_affected_tests` в `core`/`lite`, baseline/current интеграция, проектная оценка, runtime-инвентарь T07-C1, формат ответа и подробности, публичная документация и каталог. Флаг `run`, граф и task-09+ вне scope.
- Версия: после зелёной Release-сборки замороженного C# координатор разрешил minor bump **1.5.18 → 1.6.0**. `Version=1.6.0`, `AssemblyVersion=1.6.0.0`, `FileVersion=1.6.0.0` изменены вместе; PackageVersion не менялась. Последующая финальная validation выполняется на этой версии.
- Первоначальный RED: [evidence координатора](evidence/task-08-red.md). Поздние два RED → GREEN: [отдельный отчёт](evidence/task-08-additional-red-green.md).
- Независимое ревью: Codex, отдельные `/root/task08_review_round1` и `/root/task08_review_round2` (точные runtime model ID средой не предоставлены); **2 раунда / 3 исправления после ревью**. [Раунд 1](evidence/task-08-review-round-1.md), [раунд 2 — ACCEPT](evidence/task-08-review-round-2.md); R1-001/R1-002/R1-003 закрыты.

## Реализация

`TestTools.EstimateAffectedTests` первым шагом получает опубликованный solution после существующего disk sync. Передаёт настоящий снимок `BaselineSession`, карту входов, путь загрузки и оба текущих stale-флага в `TestImpactEstimator`. Baseline capture не ожидается: незавершённый снимок честно становится unknown в конкретном ответе.

Для baseline/current membership используется существующий `BaselineSession.Inventory`: ключи физического проекта, loaded context, logical path и occurrence сохраняются при новых Roslyn IDs после reload. Метод и `BaselineDocumentSnapshotBuilder.Key` доступны внутри assembly; тела алгоритмов не менялись. Аналогично переиспользован существующий поиск тестовых проектов mapper.

Текущие C# снимки сравниваются прежним detector. Неизменённые Known документы, trivia и возврат к baseline не дают дельту. Pending/Unknown не считаются неизменёнными: путь явно назван в `Not covered`, применяется проектное расширение. Удалённые baseline-документы остаются входом. Новые C# пути до reload читаются с диска и относятся mapper к ближайшим проектам, поэтому новые тесты остаются видимы напрямую, даже пока workspace их ещё не содержит.

Composer получает полный текущий документный инвентарь, включая неизменённые partial-части. Список простых имён баз объединяет original baseline и текущие снимки, включая удалённых наследников. Объединение консервативно захватывает документы всего solution, поэтому совпадения имён вне тестовых проектов могут дополнительно расширять ответ.

`RequiresFullScan` не превращается в доказательство отсутствия изменений после сравнения C#. Каждый ответ называет постоянный разрыв наблюдения за не-C# входами и возможное устаревание графа, а все текущие тестовые проекты включаются целиком. Настоящие watcher flags отдельно управляют stale/reload: появление нового source не выбирает независимые проекты; после reload удалённый `.cs` не даёт повторный совет reload.

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
- Lifecycle capture/publication/shadow-copy поведение не менялось. Два baseline helper algorithms только доступны caller и размещены перед private members; mapper discovery после R1-002 дополнен runtime-инвентарём и выполняет дополнительное semantic перечисление. Исполнитель не запускал AnalyzerLifecycle; финальный scope validation определяет координатор.
- Task-09 `run`, MTP, graph и активация следующих эпох не реализованы. Task/spec requirements не менялись, посторонний `.agents/skills/prepare-github-release/` сохранён. Commit/push исполнитель не выполнял.
- Статусы task, README эпохи, программы и индекса обновлены координатором после обязательной validation и приёмки. Эпоха целиком и task-09+ остаются непринятыми.

## Окончательная передача

Координатор подтвердил финальную Release solution noIncremental build **PASS** на 1.6.0, focused **58/58**, SourceStructure **123/123**, полный main **1970/1970**, без падений и пропусков. [Final validation](evidence/task-08-final-validation.md). Живые проверки: **19 планов / 61 успешное test execution** на адаптерах и **16/16** по фильтру из чистой owner-пробы; initial/edit/restore — **0 → 1 → 0**, observation gap **0/3** ([evidence](evidence/task-08-live-validation.md)). Длительный замер частоты gap и другие owner solutions не заявляются. Три замечания раунда 1 закрыты, раунд 2 — ACCEPT; задача принята 2026-11-06 в указанном scope, статусы и evidence согласованы.
