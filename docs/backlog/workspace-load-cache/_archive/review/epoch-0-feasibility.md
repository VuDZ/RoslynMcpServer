ID: E0-01
Severity: Blocker
Category: Hydration

Target:
epoch-0-feasibility.md / «Работа» / пункты 2–4

Claim:
Загрузить решение обычным MSBuild, извлечь DTO, закрыть workspace и восстановить новый через публичные API. Для каждого поля контракта назвать публичный API. Reflection к внутренностям Roslyn как обход не использовать.

Evidence:
Пункты не называют тип целевого `Workspace`. От этого зависит, существует ли публичный API вообще (R-01). Таблица «API → поле DTO» может быть заполнена полями `ProjectInfo`/`DocumentInfo` и при этом не доказывать восстановление *MSBuild*-семантики. Приёмка V01–V06 ссылается на verification oracle «свежий `MSBuildWorkspace`» (V-01).

Failure scenario:
1. Эксперимент hydrates в `AdhocWorkspace`, таблица API зелёная.
2. Handoff: go, «публичные API достаточны».
3. Epoch 1 требует эквивалентных записей в `.csproj`; API для этого на Adhoc нет. План возвращается в R-01 после того, как основной маршрут уже открыт.

Suggested change:
Сделать выбор host первым go/stop решением эпохи, до таблицы полей. Три явные попытки: (1) `MSBuildWorkspace` без `Open*`, (2) `AdhocWorkspace`+`ProjectInfo`, (3) Open+подмена documents (это не skip DTB). Каждая — отдельный вердикт.

Confidence:
High

---

ID: E0-02
Severity: High
Category: Support profile

Target:
epoch-0-feasibility.md / пункт 5; приёмка «неподдерживаемые случаи явно перечислены»

Claim:
Определить первый профиль и алгоритм распознавания, включая отрицательные зависимости / условные imports. Если достоверное распознавание невозможно — no-go для профиля.

Evidence:
Алгоритм распознавания должен удовлетворить cache-contract §4 без интерпретатора (R-02, C-01). «Явно перечислить unsupported» не заменяет отсутствие способа *обнаружить* условный `Exists` на неизвестном проекте. Приёмка допускает, что generator fixture закончится unsupported — и тем самым позволяет выкинуть самый жёсткий контрактный случай из профиля и всё равно пойти дальше.

Failure scenario:
1. Профиль = «два SDK-проекта без Directory.Build и без generator».
2. V04/V12 помечены unsupported, не failed.
3. go → Epoch 2 на профиле, который не пересекается с целью README. Stop не срабатывает, потому что «для данного профиля» распознавание тривиально.

Suggested change:
No-go, если алгоритм не умеет отвергнуть (не «пропустить») проект с условным import / `Exists` / custom target *до* disk-hit. Unsupported fixture — это красный вход в профиль, не зелёный обход приёмки.

Confidence:
High

---

ID: E0-03
Severity: High
Category: Observability

Target:
epoch-0-feasibility.md / приёмка V01–V06; verification V04

Claim:
Сопоставить DTO и поведение, включая diagnostics, navigation, source generator и analyzer configs. Generator fixture обязан быть проверен или обоснованно unsupported.

Evidence:
`find_symbol_definition` не ищет source-generated documents (roslyn#63375, `ARCHITECTURE.md`). `get_file_content` читает диск, не SG. `get_diagnostics_for_file` смотрит diagnostics документа-потребителя. В репозитории нет MCP-инструмента, возвращающего текст generated document. Тот же разрыв уже ломал oracle overlay-серии (review E1-01 shadow-copy).

Failure scenario:
1. V1 и V2 генератора эмитят одно имя типа, меняется константа.
2. Oracle — «нет CS0103» / definition not found в обоих случаях.
3. Epoch 0 пишет «SG эквивалентен» или «unsupported, идём дальше» без наблюдения emitted source.

Suggested change:
In-process `GetSourceGeneratedDocumentsAsync` в тестовом хосте `SolutionManager`. Запретить MCP diagnostics и `find_symbol_definition` как oracle версии/тела generator.

Confidence:
High

---

ID: E0-04
Severity: High
Category: Measurement

Target:
epoch-0-feasibility.md / приёмка «median … не более 70% baseline»; «если большого решения нет, performance-gate непройден»

Claim:
Задан perf budget для Epoch 2: median времени до первого полезного ответа ≤ 70% baseline, p95 не хуже. Это цель, не результат. Без большого решения полезность ускорения нельзя объявлять доказанной.

Evidence:
«Первый полезный ответ» включает compilation + generators + overlay prepare (R-04), не только DTB. Пропуск DTB может быть малой долей e2e. Приёмка эпохи 2 требует «0 DTB» (другой критерий). Handoff-шаблон позволяет go при непройденном perf, если написать «эксперимент». README всё равно держит 0–3 как основной маршрут.

Failure scenario:
1. Hydration на fixture работает, большое решение не измерено или 70% не достигнуто из-за compilation.
2. Outcome = go «функционально», perf-gate «оставлен Epoch 2».
3. Epoch 2 принимает 0 DTB как success и включает opt-in. Критерий README так и не выполнен.

Suggested change:
Без большого решения outcome ≠ go в основной маршрут. Функциональный прототип = revise/stop для disk-hit, не «go без perf». First-semantic обязан включать prepare overlay, если профиль содержит in-solution generator.

Confidence:
High

---

ID: E0-05
Severity: Medium
Category: Scope

Target:
epoch-0-feasibility.md / пункт 6 `LoadMetadataForReferencedProjects`

Claim:
Отдельно исследовать `LoadMetadataForReferencedProjects`. Не считать изменение свойства доказательством ускорения или safe fallback.

Evidence:
Это центральная ставка соседнего плана v1 epoch-1, не дискового snapshot. Свойство меняет *состав* графа (project vs metadata), то есть семантический режим, который контракт §2 ещё не вводит. Исследование внутри Epoch 0 смешивает два механизма в одном go/stop.

Failure scenario:
1. На большом sln флаг даёт меньшее время Open.
2. Handoff Epoch 0 включает «metadata ускоряет» как часть обоснования disk-cache.
3. Epoch 2 или исполнитель добавляет флаг как fallback «почти hit», хотя контракт запрещает смешивать hydrated и неполный граф.

Suggested change:
Вынести исследование в Epoch 4A. В Epoch 0 оставить один вопрос: эквивалентная гидрация полного source graph публичными API.

Confidence:
High
