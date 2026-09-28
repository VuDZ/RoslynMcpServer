# Arbitration Result

Run: каталоги `review-astra` и `review-ds`. Отдельный Run ID оркестрации не передан.
Дата: 2026-09-27. Хост, с которым сверены claims: исходники `1.4.15`.

Входы: `docs/mcp-plugins/` (README, authoring, эпохи 1–7), `review-astra/`, `review-ds/`, единая защита `response/`. Код хоста использован как evidence текущего поведения, не как требуемое будущее поведение плагинов. Спека сама себя не доказывает.

Спека, оба review и defence не изменялись. Набор `P-*` — proposed, не принятая норма.

# Executive summary

- Source findings: 19.
- Consensus: 8.
- Partial / boundary: 9.
- Correction dispute: 2 (`review-astra/E5-02`, `review-ds/E5-01`).
- Problem, целиком отвергнутая: 0.
- Verdict `UNRESOLVED` на целом finding: 0. Хвост `U-001` закрыт решением человека 2026-09-27 (ссылка Roslyn новее хоста — пропуск).
- Независимые кластеры с совпадением двух ревьюеров: 3 (`C-007`, `C-009`, `C-011`). Совпадение не использовалось как голос.
- `D-*`: 0.
- `A-*`: 1 (`A-001`), не входит в обязательный change set.
- Proposed changes: 15 (`P-001`…`P-015`).
- Human decisions: закрыты 2026-09-27. `U-001` — ссылка `Microsoft.CodeAnalysis` новее загруженной сборки хоста даёт пропуск; более старая версия при том же мажоре допустима. `A-001` — гейт `Microsoft.Extensions.*` в эту серию не входит.

# Decision points

1. **`U-001` (закрыт 2026-09-27).** При том же мажоре `Microsoft.CodeAnalysis` плагин пропускается, если версия ссылки строго новее загруженной сборки хоста (сравнение `Version`, включая patch: 5.9.2 на хосте 5.9.0 — пропуск). Более старый minor на новом хосте допустим (5.8 на хосте 5.9 — загрузка). Exact-равенство minor не требуется. Другой мажор по-прежнему пропуск в обе стороны.
2. **Откат против «загружен частично» (`C-011`).** Проблема одна. Норма: после исключения `Register` или неуспешного создания SDK-тула у этого плагина нет тулов в `tools/list` и нет регистраций, из-за которых падает `Build`. Третьего статуса нет. Механизм (буфер или снятие дескрипторов до возврата) остаётся implementation-defined.
3. **Ось совместимости (`review-ds/R-01`).** Дыра MCP 1.3 против 1.4 при том же мажоре продукта подтверждена. Продуктовый `minHostVersion` остаётся нижней границей сервера, а не «подсказкой». Снимать его нельзя.
4. **Native (`review-astra/E5-03`).** Обязательный `LoadUnmanagedDll` в эту серию не входит. v1 явно не резолвит native. Копирование `runtimes/` этого не обещает. Отдельного `U-*` нет: серия — managed test-impact на общем `Solution`. Вернуть native-резолвер можно только новым требованием.
5. **Материализация SDK (`review-astra/E1-01`).** `Build` не доказывает тул. `McpServerTool.Create` плагина входит в границу этого плагина до фиксации успеха. Общий catch вокруг всего старта MCP не ставится: поломка каталога хоста по-прежнему валит процесс.
6. **Доказательство теневой копии (`review-astra/E3-01`).** Тест байтов остаётся в эпохе 3. Эпоха 5 держит ALC, загруженный из копии, и перезаписывает исходный entry. `Assembly.Location` загруженного entry — путь копии. Отдельный процесс ОС и native-вызов не требуются.
7. **Лог пропуска (`review-ds/E6-01`).** Обещание «stderr + лог» сохраняется. `ILogger` внутри старта до `Build` не требуется: stderr сразу, те же строки в файл лога после успешного `Build`.

# Consensus decisions

## `C-003` — `review-astra/C-01`

- Class: `CONSENSUS`
- Problem: `CONFIRMED`
- Correction: `ACCEPTABLE`
- Verdict: `ACCEPT`
- Severity: High
- Proposed change: `P-003`
- Note: Рецепт authoring не задаёт `CopyLocalLockFileAssemblies` / `EnableDynamicLoading` и обещает, что свой пакет «копируется в выход как обычно». Для class library SDK этого не делает. Приёмка эпохи 4 смотрит только свою DLL и отсутствие двух сборок хоста.

## `C-004` — `review-astra/C-02`

- Class: `CONSENSUS`
- Problem: `CONFIRMED`
- Correction: `ACCEPTABLE`
- Verdict: `ACCEPT`
- Severity: Medium
- Proposed change: `P-004`
- Note: Self-contained publish с `PublishSingleFile=false` оставляет контракт в `RoslynMcpServer.dll`. Authoring разрешает `Reference` на exe. `Private=false` не добавляет метаданные apphost.

## `C-006` — `review-astra/E1-02`

- Class: `CONSENSUS`
- Problem: `CONFIRMED`
- Correction: `ACCEPTABLE`
- Verdict: `ACCEPT`
- Severity: Medium
- Proposed change: `P-007`
- Note: Эпоха 1 пишет «в этом процессе». `AddRoslynMcpServerTools` создаёт новую runtime-коллекцию на вызов. Статический набор имён сделает тесты эпох 1, 5 и 6 зависимыми от порядка. Один процесс MCP от этой правки не расширяется.

## `C-009` — `review-astra/E4-01`, `review-ds/E4-01`

- Class: `CONSENSUS`
- Problem: `CONFIRMED`
- Correction: `ACCEPTABLE`
- Verdict: `ACCEPT`
- Severity: Blocker
- Proposed change: `P-010`
- Note: Независимое совпадение. Хостовый csproj исключает только `RoslynMcpServer.Tests` и `LifecycleTestHost`. Эпоха 4 не разрешает править этот csproj; эпоха 7 запрещает менять csproj. SDK глобит `**/*.cs` под корнем проекта, включая `samples`. Ds дополнительно показал generated `.cs` из `obj` шаблона. Каталог тулов пополнять нельзя: путь шаблона уже зафиксирован как `samples/RoslynMcpPlugin/`, поэтому исключение дерева, а не перенос проекта.

## `C-010` — `review-astra/E5-01`

- Class: `CONSENSUS`
- Problem: `CONFIRMED`
- Correction: `ACCEPTABLE`
- Verdict: `ACCEPT`
- Severity: Blocker
- Proposed change: `P-011`
- Note: Эпоха 5: имя вне четырёх семейств идёт в `AssemblyDependencyResolver`, «нет пути — пропуск». `System.Runtime` не входит в семейства и не лежит в deps.json обычной библиотеки. Fail-closed для отсутствующей частной DLL сохраняется. Fallback в default context на любую чужую сборку хоста не разрешается.

## `C-013` — `review-astra/E6-01`

- Class: `CONSENSUS`
- Problem: `CONFIRMED`
- Correction: `ACCEPTABLE`
- Verdict: `ACCEPT`
- Severity: Medium
- Proposed change: `P-013`
- Note: Цель эпохи — первый `tools/list` процесса и `ROSLYN_MCP_PLUGINS`. Перечисленные тесты вызывают `PluginStartup` напрямую. In-process тесты остаются. Smoke обязан не писать в stdout ничего, кроме протокола MCP: это часть того же процессного контракта.

## `C-015` — `review-ds/E6-02`

- Class: `CONSENSUS`
- Problem: `CONFIRMED`
- Correction: `ACCEPTABLE`
- Verdict: `ACCEPT`
- Severity: Medium
- Proposed change: `P-015`
- Note: `FormatToolHelp` и `FormatUnknown` читают только `McpToolCatalog.All`. Эпоха 1 запрещает писать туда плагин. Исход эпохи 5 хранит имена, не описание и не параметры. `Kind` и `Group` для плагина — строка `plugin`; это не группа `list_tool_groups`.

# Disputed decisions

## `C-001` — `review-astra/R-01`

### Sources

- `review-astra/R-01`

### Dispute class

`PARTIAL`

### Reviewer positions

Рекомендованный вход `GetPublishedSolutionAsync` не сбрасывает очередь disk watcher. Встроенные semantic-тулы вызывают `GetSanitizedPublishedSolutionAsync`. Нужен отдельный вход после disk-sync и явный выбор raw/sanitized; приёмка вызывает плагин первым.

### Defence position

Проблема принята. Новый метод не нужен: руководство называет уже существующий `GetSanitizedPublishedSolutionAsync` и объясняет два других.

### Independent analysis

`GetPublishedSolutionAsync` возвращает `_solution` и не вызывает `FlushDirtyDocumentsUnderLockAsync`. Сброс есть у `GetPublishedSolutionAfterDiskSyncAsync` и `GetSanitizedPublishedSolutionAsync`. `NavigationTools` берёт второй. README и authoring в рецепте анализа называют первый. Общий экземпляр `SolutionManager` не равен общему снимку.

Отдельный новый API из correction ревьюера не следует: три метода уже есть. Снимок, совпадающий со встроенными semantic-тулами, — sanitized, не raw после disk-sync.

### Problem assessment

`CONFIRMED`

### Problem rationale

Расхождение рецепта и встроенных тулов на сохранённом `.cs` прямо бьёт в обещанный test-impact.

### Correction assessment

`MODIFY`

### Correction rationale

Норма — назвать существующий sanitized-вход и не вводить четвёртый метод. Оба других метода остаются объяснёнными. Приёмка без предварительного встроенного тула, который сам очистит очередь, принимается.

### Verdict

`ACCEPT WITH MODIFICATION`

### Severity

High. Исходная High сохраняется: тихий устаревший анализ, без падения процесса.

### Proposed change

`P-001`

### Remaining uncertainty

Нет.

## `C-002` — `review-ds/R-01`

### Sources

- `review-ds/R-01`

### Dispute class

`PARTIAL`. Хвост minor Roslyn был `UNRESOLVED REQUIREMENT`; закрыт `U-001` 2026-09-27.

### Reviewer positions

`minHostVersion` сверяет `Version` продукта. Связывают плагин сборки, которые загрузчик подменяет. `Version` продукта оставить человекочитаемой подсказкой. Гейт — версии MCP и Roslyn (или `AssemblyName.Version` ключевых сборок) против фактически загруженных.

### Defence position

Дыра принята частично. Продуктовый минимум остаётся. Добавить сверку MCP `Major.Minor` и мажора Roslyn. Exact-равенство всех патчей не следует из finding. Minor Roslyn — `U-001`. Источник ссылки — `deps.json`, запас — явные поля, если runtime-ассеты исключены.

### Independent analysis

README обещает сборку против той же `Version` и пропуск каталога при другом мажоре `minHostVersion`. В csproj `Version` `1.4.15` и `ModelContextProtocol` `1.3.0` независимы; правило репозитория бампит продукт. `Microsoft.CodeAnalysis` сейчас `5.9.0`. Обзор версий: патч `1.0.35` сменил Roslyn на 5.9.0. `docs/ARCHITECTURE.md` §11: активация групп чувствительна к внутренностям MCP SDK.

Эпоха 5 подставляет по имени `RoslynMcpServer`, `ModelContextProtocol*`, `Microsoft.CodeAnalysis*`, `Microsoft.Extensions*`. Гейт эпохи 2 на эти сборки не смотрит. Сценарий «мажор продукта тот же, MCP 1.3 против 1.4» гейт пропускает. Это дыра.

Тезис «продуктовая версия только подсказка» шире evidence. Плагин, собранный против более нового публичного API сервера, не должен грузиться в старый хост. Эта ось в спеке уже есть и другим finding не опровергнута.

Шаблон ставит `ExcludeAssets=runtime` на контракт хоста. Молчание `deps.json` о runtime-ассетах — ожидаемый случай, не редкий запас. Спека обязана назвать, откуда берётся версия ссылки в этом случае.

`Microsoft.Extensions.*` тем же загрузчиком подменяется, а гейта в correction нет. Это `A-001`, не расширение данного finding.

### Problem assessment

`PARTIALLY CONFIRMED`

### Problem rationale

Подмена сборок не покрыта продуктовым мажором. Снятие продуктового минимума требованиям не следует.

### Correction assessment

`MODIFY`

### Correction rationale

Оставить `minHostVersion` как нижнюю границу продукта: тот же мажор, хост не старее. До `Load` entry дополнительно:

- `ModelContextProtocol`: пропуск, если `Major.Minor` ссылки плагина не равен загруженной сборке хоста. Совпадение патча SDK этим finding не требуется.
- `RoslynMcpServer`: пропуск, если мажор ссылки другой или ссылка новее хоста. Совпадение каждого патча не требуется.
- `Microsoft.CodeAnalysis`: пропуск при другом мажоре. Minor закрыт `U-001`: ссылка новее хоста — пропуск; более старая версия при том же мажоре допустима.

Источник версии ссылки обязателен и при исключённых runtime-ассетах (compile-запись `deps.json` или явные поля `plugin.json`). Это не третий независимый гейт.

### Verdict

`ACCEPT WITH MODIFICATION`

### Severity

High на оси MCP. Exact-match всего списка сборок не принимается.

### Proposed change

`P-002`. Minor Roslyn входит в `P-002` по решению `U-001` (2026-09-27).

### Remaining uncertainty

Нет. `U-001` закрыт.

## `C-005` — `review-astra/E1-01`, `review-ds/E1-01`

### Sources

- `review-astra/E1-01`
- `review-ds/E1-01`

Смежные claims, не один и тот же дефект. Общий корень: фабрика `McpServerTool` отложена. Astra — исключение в момент создания тула после границы плагина. Ds — момент наполнения коллекции не `Build`, и счётчик при пустой коллекции берёт каталог.

### Dispute class

`PARTIAL`

### Reviewer positions

Astra: проверка имени не равна принятию сигнатуры SDK. `IOptions<McpServerOptions>.Value` падает уже после `Register` и `Build` и роняет общий старт. Нужна фаза материализации, привязанная к плагину; `Build` не считать доказательством.

Ds: коллекция наполняется при первом `IOptions<McpServerOptions>.Value`, не в `Build`. Буквальный тест эпохи 1 видит пустую коллекцию. `CurrentToolCount` при `_tools.Count == 0` возвращает число каталога, где плагинов нет. Либо писать этот шаг в тесты, либо наполнять коллекцию из отчёта старта.

### Defence position

Оба problem приняты. Материализация входит в ту же транзакцию, что откат `Register`, без catch на весь старт MCP. Второе наполнение коллекции из отчёта не нужно, если старт один раз резолвит options до счётчика. Fallback «пустая коллекция = каталог» не использовать, когда отчёт содержит плагинные тулы.

### Independent analysis

`McpToolRegistry.RegisterSelectedTools` кладёт `descriptor.CreateFactory()`. `McpServerTool.Create` выполняется внутри фабрики. Эпоха 1 считает, что после `Build` фабрики уже видны в коллекции. Эпоха 5 закрывает границу на возврате из `Register`.

`CurrentToolCount`: при пустой коллекции возвращается `_surface.RegisteredToolCount`. `Program.cs` печатает этот счётчик сразу после `Build` и до `RunAsync`, не резолвя `IOptions<McpServerOptions>`. Существующие тесты каталога перед чтением коллекции options резолвят.

Общий catch вокруг `Build` / options setup спрячет поломку встроенного каталога. Эпоха 6 уже отличает пропуск плагина от сбоя самого старта.

Второе наполнение коллекции из отчёта заводит второй источник списка тулов рядом с SDK. Для обязательного исхода достаточно резолва options в заданных точках чтения.

Запрет fallback «когда отчёт содержит плагинные тулы» без определения, что возвращать вместо него, легко даёт 0 или двойной счёт. Это лишняя норма, если специфицированные читатели резолвят options первыми.

### Problem assessment

`CONFIRMED` для обоих claims.

### Problem rationale

Исключение SDK выходит из границы плагина. Тест «после Build прочитать коллекцию» не совпадает с тем, когда SDK её наполняет, и толкает исполнителя положить имя в каталог.

### Correction assessment

`MODIFY`

### Correction rationale

Для `review-astra/E1-01`: создать SDK-тул каждого принятого метода до фиксации успеха плагина и до публикации в общий список. Исключение отбрасывает только этот плагин. Встроенные тулы остаются на текущих отложенных фабриках. Весь `Build` в «пропуск плагина» не оборачивать.

Для `review-ds/E1-01`: тесты эпох 1, 5 и 6 и старт хоста читают коллекцию и `CurrentToolCount` только после `IOptions<McpServerOptions>.Value`. Плагины в `McpToolCatalog` не писать. Второе наполнение из отчёта не вводить. Менять семантику fallback не требуется, пока эти читатели резолвят options первыми.

### Verdict

`ACCEPT WITH MODIFICATION`

### Severity

`review-astra/E1-01`: High. Один метод плагина срывает `tools/list` остальных.
`review-ds/E1-01`: Medium. Это дыра приёмки и порядка чтения, закрываемая тем же резолвом.

### Proposed change

`P-005` (материализация и отсутствие видимого хвоста), `P-006` (порядок чтения).

### Remaining uncertainty

Нет.

## `C-007` — `review-astra/E2-01`, `review-ds/E2-01`

### Sources

- `review-astra/E2-01`
- `review-ds/E2-01`

### Dispute class

`PARTIAL` (только severity)

### Reviewer positions

Оба: неверный тип `plugins` описан как «ошибка разбора, как у других ключей», но `TryReadFile` ловит только чтение текста. `ParseDocument` бросает `JsonException` мимо `ParseFailures`. Хост падает до старта плагинов. Astra: High. Ds: Medium. Исправление совпадает: тип → `ParseFailures`, хост жив. Astra дополнительно просит явный fallback на второй файл настроек.

### Defence position

Problem и correction приняты. Успешно прочитанный второй файл сливается как сейчас. Severity: High.

### Independent analysis

`TryReadFile` оборачивает в `try` только `File.ReadAllText`. `ParseDocument` вызывается после. `LoadFromDirectories` уже сливает два успешных чтения и кладёт мягкий отказ в `ParseFailures`, но до этой ветки исключение типа не доходит: оно вылетает из `TryReadFile`. `Program.cs` вызывает `LoadFromDefaultLocations` без catch.

После перевода `JsonException` в мягкий отказ существующий `Merge` сам оставляет успешный файл. Отдельный новый алгоритм слияния не нужен. Его надо назвать в эпохе, потому что аналогия «как у других ключей» сегодня ложна.

### Problem assessment

`CONFIRMED`

### Problem rationale

Независимая сверка одного и того же `TryReadFile`. Цифра severity на текст правки не влияет.

### Correction assessment

`ACCEPTABLE`

### Correction rationale

Ошибка типа известного ключа, включая `plugins`, становится `ParseFailures`. Процесс стартует. Второй успешно прочитанный файл сливается текущим `Merge`.

### Verdict

`ACCEPT`

### Severity

High. Исход — процесс не стартует из-за типа в jsonc, до плагинов и до `RunAsync`. Medium занижает доступность хоста.

### Proposed change

`P-008`

### Remaining uncertainty

Нет.

## `C-008` — `review-astra/E3-01`

### Sources

- `review-astra/E3-01`

### Dispute class

`PARTIAL`

### Reviewer positions

Тест эпохи 3 копирует байты и не держит ALC. Нужен интеграционный тест после загрузки: удержать контекст, вызвать частную зависимость, проверить locations, перезаписать исходные entry и dependency. Второй запуск видит новые байты. Отдельный процесс не назван прямо; native не требуется этим finding.

### Defence position

Problem принят. Unit-тест байтов остаётся в эпохе 3. Доказательство — в эпохе 5: загрузить из копии, перезаписать исходный entry, тип жив, второй запуск загрузчика видит новые байты. Замок частной DLL и второй OS-процесс в этот тест не входят.

### Independent analysis

Эпоха 3 сама пишет, что сборку не загружает. Тестовый entry — произвольные байты. Запись проверяется сразу после копирования. Эпохи 5 и 6 этот замок не закрывают. На Windows `LoadFromAssemblyPath` блокирует файл, который реально загружен.

Сужение «достаточно перезаписать entry» не ловит резолвер, который entry берёт из копии, а зависимость — из исходного `bin`. Это шаг 2 сценария ревьюера. Отдельный процесс и native для этого не нужны. Достаточно утверждения `Assembly.Location`.

Перезапись исходного файла зависимости избыточна, если location уже лежит в копии: исходный файл тогда не загружен. Частная зависимость в этом тесте обязательна только когда загружаемый плагин её имеет (`P-003`). Шаблон без неё доказывает замок entry.

### Problem assessment

`CONFIRMED`

### Problem rationale

Гарантия authoring «сборка проходит, пока MCP держит предыдущую копию» тестом не покрыта. Правило копирования при этом не опровергнуто.

### Correction assessment

`MODIFY`

### Correction rationale

Тест байтов остаётся в эпохе 3. Эпоха 5: ALC жив, entry загружен из копии, исходный entry перезаписывается, уже загруженный тип жив, второй запуск видит новые байты. `Assembly.Location` entry — каталог копии. Если этот прогон загружает частную managed-зависимость, её location тоже в копии. Второй OS-процесс и native не входят.

### Verdict

`ACCEPT WITH MODIFICATION`

### Severity

Medium. Дыра приёмки, не ошибка правила копирования.

### Proposed change

`P-009`

### Remaining uncertainty

Нет.

## `C-011` — `review-astra/E5-02`, `review-ds/E5-01`

### Sources

- `review-astra/E5-02`
- `review-ds/E5-01`

### Dispute class

`CORRECTION DISPUTE`

### Reviewer positions

Проблема совпадает: исключение из `Register` помечает плагин пропущенным, а дескрипторы уже лежат в общем `IServiceCollection`. Дальше либо `Build` падает на валидации DI, либо `tools/list` содержит тулы «пропущенного» плагина.

Astra: commit только после успеха, discard при отказе. Гарантия — на регистрации, не на произвольные побочные эффекты. Следующий плагин может занять имя.

Ds: третье состояние «загружен частично» с инвентарём тулов, либо двухфазный перенос в DI после успешного `Register`. Слово «пропущен» для этого случая убрать.

### Defence position

Problem принят. Только commit/discard. Третье состояние отклонено. Побочные эффекты вне DI не откатываются.

### Independent analysis

Эпоха 5 прямо пишет: исключение из `Register` — пропуск, синглтоны откатывать нечем. Результат бинарный: загружен или пропущен. `AddToolsFrom` эпохи 1 пишет фабрики в тот же `IServiceCollection`. README: один битый плагин не роняет сервер. Эпоха 6 показывает пропуски отдельно от загруженных плагинов и их имён тулов.

«Не роняет сервер» само по себе допускает и откат, и честный частичный статус. Бинарный результат спеки и расхождение info с `tools/list` частичный статус не допускают: тулы остались бы вызываемыми при статусе, который не является «загружен». Третье состояние — новый продукт, которого в требованиях нет.

Оба ревьюера уже допускают двухфазный перенос. Обязательна наблюдаемая семантика, не выбор буфера против снятия дескрипторов.

Материализация SDK (`C-005`) стоит в той же границе: неуспешный `McpServerTool.Create` не публикует плагин.

### Problem assessment

`CONFIRMED`

### Problem rationale

Спека одновременно запрещает откат и обещает чистый пропуск. Это несовместимо с DI-валидацией и с `tools/list`.

### Correction assessment

`MODIFY`

### Correction rationale

После неуспеха в контейнере хоста нет сервисов и тулов этого плагина, имя тула свободно, процесс стартует, статус skipped. Статуса «загружен частично» нет. Побочные эффекты вне DI не откатываются. Буфер до commit и mutate-then-remove оба допустимы, если к моменту статуса и к `Build` хвоста нет.

### Verdict

`ACCEPT WITH MODIFICATION`

### Severity

High. Либо падение `Build`, либо вызываемый тул при статусе пропуска. Medium у ds занижает оба исхода. Совпадение двух ревьюеров повышает уверенность, не цифру.

### Proposed change

`P-005`

### Remaining uncertainty

Нет. Предел «код плагина вне DI не откатывается» признан обоими ревьюерами и защитой.

## `C-012` — `review-astra/E5-03`

### Sources

- `review-astra/E5-03`

### Dispute class

`PARTIAL`

### Reviewer positions

Копирование `runtimes/` не задаёт загрузку native. P/Invoke не идёт через `AssemblyLoadContext.Load`. Либо нормировать `LoadUnmanagedDll`, RID и тест настоящего вызова, либо явно исключить native из v1.

### Defence position

Ложное обещание принято. Обязательный unmanaged loader не принят. v1 пишет отказ. Тест `native.bin` остаётся проверкой копирования файла.

### Independent analysis

Эпоха 3 копирует дерево, включая `runtimes/`, и проверяет наличие `native.bin` без вызова. Эпоха 5 резолвит только `AssemblyName`. Authoring говорит, что свои пакеты попадают в выход и годятся загрузчику, не различая managed и native.

Граница серии в README: шов для чужого test-impact, общий `SolutionManager` и Roslyn. Выгрузка ALC, hot-load, песочница и NuGet как источник вне концепции. Native-резолвер нигде не требовался. Ревьюер сам оставил явное исключение допустимым исходом.

### Problem assessment

`PARTIALLY CONFIRMED`

### Problem rationale

Текст можно прочитать как гарантию P/Invoke. Обязанности построить `LoadUnmanagedDll` в требованиях нет.

### Correction assessment

`MODIFY`

### Correction rationale

Принимается ветка явного отказа, уже предложенная ревьюером. v1 не резолвит native. Файл в `runtimes/` этого не обещает. Тест эпохи 3 остаётся тестом копирования. Отдельная эпоха native возможна только по новому требованию.

### Verdict

`ACCEPT WITH MODIFICATION`

### Severity

Medium. Исходная High описывала отсутствующий класс пакетов, которого серия не обещала. High была бы уместна, если отказ не записать и оставить текст как гарантию.

### Proposed change

`P-012`

### Remaining uncertainty

Нет, пока нет требования, что первый плагин коллег несёт native-зависимость.

## `C-014` — `review-ds/E6-01`

### Sources

- `review-ds/E6-01`

### Dispute class

`PARTIAL`

### Reviewer positions

Строку пропуска нельзя записать в лог хоста до `Build`: Serilog попадает в DI после `Build`, в подпись старта логгер не входит. Либо снять MUST про лог и оставить stderr, либо передать фабрику логгера, созданную до `Build`.

### Defence position

Problem принят. Обещание лога не снимать. Stderr сразу; тот же отчёт в лог после `Build`, без второго обхода плагинов. Если упал сам `Build`, файла лога может не быть. Отдельную фабрику до `Build` не заводить.

### Independent analysis

`Program.cs` регистрирует Serilog через `AddSerilog` и получает логгер из контейнера после `Build`. Эпоха 6 вызывает старт до `Build` и передаёт stderr. README обещает «stderr + лог». Тест эпохи проверяет stderr и info, не файл лога.

Снять лог — ослабить уже написанный контракт темы. Фабрика до `Build` тоже закрывает дыру, но дублирует конфигурацию Serilog. Для сценария «сервер поднялся, инженер открыл `logs/mcp-*.log`» достаточно записать уже собранный отчёт после `Build`.

### Problem assessment

`CONFIRMED`

### Problem rationale

Приёмка проходит на одном stderr. Долговечный канал после успешного старта пуст.

### Correction assessment

`MODIFY`

### Correction rationale

Обязательный исход — оба канала на успешном старте, а не конкретная фабрика до `Build`. Предел «сам `Build` упал — в файле лога строк может не быть, stderr уже заполнен» записать в эпоху.

### Verdict

`ACCEPT WITH MODIFICATION`

### Severity

High. Клиент MCP часто не показывает stderr. Процесс при этом жив, поэтому не Blocker.

### Proposed change

`P-014`

### Remaining uncertainty

Нет.

# Defence-originated findings

Отдельных `D-*` нет. Проверка защиты это не опровергла: fallback `IsToolActive`, generated `.cs` и падение валидации DI уже входят в `review-ds/E1-01`, `review-ds/E4-01` и `review-astra/E5-02`.

# Arbiter-originated findings

## `A-001`

- Claim: загрузчик подменяет `Microsoft.Extensions.*` по префиксу имени, а proposed-гейт `P-002` эту ось не проверяет. Пакет Extensions другой версии может дать тот же `TypeLoadException` после обещания в info, что и MCP.
- Evidence: эпоха 5, список семейств; эпоха 3 не копирует эти DLL. Ни один source finding гейт Extensions не требовал. Оба конца серии — `net10.0`, обычный случай совпадает с shared framework.
- Failure scenario: плагин ссылается на другой major `Microsoft.Extensions.*`, discovery смотрит только продукт, MCP и Roslyn, `Load` подставляет сборку хоста.
- Severity: Medium.
- Confidence: Medium. Класс дыры тот же, что у MCP; практический риск ниже, пока оба проекта на одном TFM.
- Related findings: `review-ds/R-01`, `P-002`.
- State: `DEFERRED`
- Verdict: вне этой серии (решение человека 2026-09-27). Отдельное ревью не заказывается. Остаток принят: плагин с другим мажором `Microsoft.Extensions.*` может получить `TypeLoadException` после записи в info. Обычный случай серии — оба конца на `net10.0`, пакет из shared framework.
- Potential follow-up: новый гейт только отдельным требованием. В `P-002` не включается.

# Unresolved questions

Открытых нет. `U-001` закрыт ниже.

## `U-001`

- Question: отвергать ли плагин, если мажор `Microsoft.CodeAnalysis` совпал, а minor нет (например 5.0 против 5.9).
- Why it matters: патч продукта `1.0.35` уже перевёл Roslyn на 5.9.0. Порог «любой другой minor» заставит пересобирать плагин на таком патче. Порог «только другой мажор» пропустит несовместимый API внутри мажора 5.
- Affected findings: `review-ds/R-01`, `P-002`.
- Known evidence: загрузчик подставляет `Microsoft.CodeAnalysis*` из хоста; продуктовый мажор эту подмену не видит; требования задают сверку мажора продукта до `Load` и не задают политику minor Roslyn. Обычная совместимость Roslyn внутри мажора не записана как контракт этой серии.
- Decision (2026-09-27): ссылка новее загруженной сборки хоста — пропуск; более старый minor на новом хосте допустим. Сравнение — `Version` целиком, не только `Major.Minor`: patch новее хоста тоже пропуск (5.9.2 на 5.9.0). Exact minor не требуется. Другой мажор остаётся пропуском. Остаток принят: Roslyn не обещает бинарную совместимость между minor, старый плагин на новом хосте может встретить удалённый API.
- Status: `CLOSED`. Входит в `P-002`.

# Proposed change set

## `P-001` — Тот же снимок, что у semantic-тулов

### Source findings

`review-astra/R-01` (`C-001`)

### Required outcome

Анализ, который должен совпасть со встроенными semantic-тулами, вызывает `GetSanitizedPublishedSolutionAsync`. Рядом явно, что делают `GetPublishedSolutionAsync` и `GetPublishedSolutionAfterDiskSyncAsync`. Новый метод `SolutionManager` не добавляется. Приёмка: изменение `.cs` на диске, очередь не сброшена чужим тулом, плагин или тестовый тул видит новый текст.

### Rationale

Общий менеджер не равен снимку после disk-sync. Встроенные semantic-тулы уже берут sanitized-вход.

### Affected specification sections

`docs/mcp-plugins/README.md` (пример и кухня), `authoring.md` («Чем пользоваться»), эпоха 5 (тест).

### Dependencies

Нет.

### Implementation freedom

Как тест доставляет событие watcher.

### Human state

`PROPOSED`

## `P-002` — Две оси совместимости

### Source findings

`review-ds/R-01` (`C-002`)

### Required outcome

`minHostVersion` остаётся нижней границей продукта: тот же мажор, хост не старее, сверка до `Load`. Дополнительно до `Load` entry:

- `Major.Minor` ссылки плагина на `ModelContextProtocol` равен загруженной сборке хоста, иначе пропуск;
- мажор ссылки на `RoslynMcpServer` совпадает и ссылка не новее хоста, иначе пропуск;
- мажор `Microsoft.CodeAnalysis` совпадает, иначе пропуск;
- при том же мажоре версия ссылки `Microsoft.CodeAnalysis` не новее загруженной сборки хоста, иначе пропуск (`U-001`: 5.10 и 5.9.2 на хосте 5.9.0 — пропуск; 5.8 на 5.9.0 — загрузка).

Версия ссылки читается и когда runtime-ассеты контракта исключены. Запасной носитель назвать в спеке (`deps.json` compile-запись или явные поля `plugin.json`). Не писать, что одна `Version` продукта гарантирует типы MCP и Roslyn. Exact-равенство minor Roslyn не писать.

### Rationale

Загрузчик подставляет сборки хоста. Продуктовый мажор ось MCP не видит. Снятие продуктового минимума шире finding.

### Affected specification sections

README («Совместимость»), эпоха 2 (правила пропуска), `authoring.md` (таблица `minHostVersion`).

### Dependencies

`U-001` закрыт и входит в этот исход. Отдельной правки minor нет.

### Implementation freedom

Конкретный разбор `deps.json`, если спека назвала запасной носитель и момент сверки (до `Load` entry).

### Human state

`PROPOSED`

## `P-003` — Частные managed-зависимости в выходе build

### Source findings

`review-astra/C-01` (`C-003`)

### Required outcome

Build плагина копирует частные managed-зависимости и не копирует сборки контракта хоста. Тест вызывает код пакета, которого нет в publish хоста. Если загрузка идёт из теневой копии, location этой DLL — каталог копии.

### Rationale

Дефолт SDK class library не копирует lock-file сборки. Рецепт authoring утверждает обратное.

### Affected specification sections

`authoring.md`, эпоха 4.

### Dependencies

Нет. Усиливает смысл копии эпохи 3 и не меняет её список исключений. Location частной DLL стыкуется с `P-009`.

### Implementation freedom

Какие именно свойства SDK включают копирование (`CopyLocalLockFileAssemblies`, `EnableDynamicLoading` или эквивалент), лишь бы исход выполнялся и контракт хоста не копировался.

### Human state

`PROPOSED`

## `P-004` — Ссылка на DLL, не на apphost

### Source findings

`review-astra/C-02` (`C-004`)

### Required outcome

Сборка плагина без исходников хоста ссылается на `RoslynMcpServer.dll` из publish, не на exe.

### Rationale

Exe self-contained publish — native apphost без метаданных CLR.

### Affected specification sections

`authoring.md`.

### Dependencies

Нет.

### Implementation freedom

Как автор получает соседние compile-time ссылки, если одной DLL хоста компилятору мало. Спека называет DLL контракта, не обязана перечислять весь граф NuGet.

### Human state

`PROPOSED`

## `P-005` — Пропущенный плагин не публикует тулы

### Source findings

`review-astra/E1-01`, `review-astra/E5-02`, `review-ds/E5-01` (`C-005`, `C-011`)

### Required outcome

Регистрации одного плагина не видны общему контейнеру и `tools/list`, пока `Register` не вернулся нормально и пока `McpServerTool.Create` не успел для каждого принятого метода. Исключение на любом из этих шагов оставляет контейнер без сервисов и тулов этого плагина, освобождает имена, пишет skipped, не роняет процесс. Статуса «загружен частично» нет. Побочные эффекты вне DI не откатываются. Общий catch вокруг старта встроенного каталога не ставится. Неуспешный метод назван в причине пропуска.

### Rationale

И отложенный `Create`, и запись в живой `IServiceCollection` происходят после обещанной границы «Register вернулся — плагин загружен». Частичный статус оставляет вызываемый тул.

### Affected specification sections

Эпохи 1 и 5. Формулировку эпохи 5 «откатывать нечем» убрать.

### Dependencies

`P-006` читает коллекцию уже после этой фиксации. `P-007` освобождает имя в том же наборе контейнера. `P-015` хранит метаданные только принятых тулов.

### Implementation freedom

Буфер дескрипторов до commit либо снятие уже добавленных дескрипторов до публикации статуса. Оба допустимы, если к статусу и к `Build` хвоста нет.

### Human state

`PROPOSED`

## `P-006` — Коллекцию читают после резолва options

### Source findings

`review-ds/E1-01` (`C-005`)

### Required outcome

Тесты эпох 1, 5 и 6 и старт хоста читают `McpRuntimeToolCollection` и `CurrentToolCount` только после `IOptions<McpServerOptions>.Value`. Плагины в `McpToolCatalog` не добавляются. Второго наполнения коллекции из отчёта старта нет.

### Rationale

Коллекцию наполняет SDK options setup из `GetServices<McpServerTool>()`, не `Build`. Fallback пустой коллекции на число каталога сегодня скрывает плагины, если читать раньше резолва.

### Affected specification sections

Эпохи 1, 5 и 6. `Program.cs` — порядок после `Build`, когда эпоха 6 его трогает.

### Dependencies

`P-005`: к моменту резолва в DI лежат только уже принятые тулы.

### Implementation freedom

Семантику `CurrentToolCount` до первого резолва можно не менять. Специфицированные читатели до этого окна не доходят.

### Human state

`PROPOSED`

## `P-007` — Уникальность имени на контейнер

### Source findings

`review-astra/E1-02` (`C-006`)

### Required outcome

Уникальность имени тула — один `IServiceCollection` и его коллекция. Повтор в новом контейнере того же процесса успешен. Повтор внутри одного контейнера по-прежнему пропускается. Тест двух хостов.

### Rationale

Коллекция тулов и так создаётся на хост. Фраза «в этом процессе» допускает static.

### Affected specification sections

Эпоха 1.

### Dependencies

`P-005`: после отката имя снова свободно в этом контейнере.

### Implementation freedom

Где лежит множество имён, если оно не переживает контейнер.

### Human state

`PROPOSED`

## `P-008` — Ошибка типа jsonc не роняет процесс

### Source findings

`review-astra/E2-01`, `review-ds/E2-01` (`C-007`)

### Required outcome

Ошибка типа известного ключа, включая `plugins` (не массив, элемент не строка), становится `ParseFailures`. Хост стартует. Успешно прочитанный второй файл настроек сливается текущим правилом cwd поверх exe. Встроенные тулы и исправные плагины из другого источника при этом живы.

### Rationale

Нынешний `try` покрывает только чтение текста. Аналогия эпохи 2 с «другими ключами» описывает сегодняшнее падение, не мягкий отказ.

### Affected specification sections

Эпоха 2, `RoslynMcpFileSettings`.

### Dependencies

Нет.

### Implementation freedom

Текст сообщения в `ParseFailures`.

### Human state

`PROPOSED`

## `P-009` — Замок файла доказан живым ALC

### Source findings

`review-astra/E3-01` (`C-008`)

### Required outcome

Эпоха 3 сохраняет тест копирования байтов. Эпоха 5: при удержанном ALC, загруженном из копии, исходный entry перезаписывается; загруженный тип жив; второй запуск загрузчика на том же исходном каталоге видит новые байты; `Assembly.Location` entry — каталог копии. Если прогон загружает частную managed-зависимость, её location тоже в копии.

### Rationale

Блокировка Windows сидит на пути `LoadFromAssemblyPath`, не на факте `File.Copy`. Location ловит резолвер, ошибочно сидящий на исходном `bin`.

### Affected specification sections

Эпоха 3 (пояснение, что тест байтов не про ALC), эпоха 5 (тест).

### Dependencies

`P-010` и `P-011`, иначе шаблон не из чего грузить. Проверка location частной DLL — вместе с фикстурой `P-003`, когда она есть. Без частной DLL достаточен entry.

### Implementation freedom

Как доставляется вторая запись байтов. Отдельный процесс ОС не требуется.

### Human state

`PROPOSED`

## `P-010` — Шаблон не компилируется в хост

### Source findings

`review-astra/E4-01`, `review-ds/E4-01` (`C-009`)

### Required outcome

Хостовый csproj исключает `samples\**` из `Compile` и `None`, включая generated `.cs` под `obj` шаблона. Приёмка: типа шаблона нет в сборке хоста после чистой и повторной сборки; `McpToolCatalog` не падает и не содержит `sample_loaded_workspace`. Эпоха 4 имеет право править хостовый csproj только этим исключением. Эпоха 7 не снимает его и не считает правку csproj запретом на уже внесённое исключение.

### Rationale

SDK глобит `**/*.cs` под корнем проекта. Каталог отвергает атрибутированный тул, которого нет в закрытом списке. Путь `samples/RoslynMcpPlugin/` уже задан контрактом темы.

### Affected specification sections

Эпоха 4, README серии (общий файл эпохи), эпоха 7 (исключение из запрета «csproj не менять»).

### Dependencies

Нет. Без этого эпохи 5–7 не становятся зелёными.

### Implementation freedom

Нет переноса шаблона в другой каталог: расположение уже нормировано.

### Human state

`PROPOSED`

## `P-011` — Три стадии резолва

### Source findings

`review-astra/E5-01` (`C-010`)

### Required outcome

Порядок: контракт хоста (четыре семейства, уже загруженная сборка) → `AssemblyDependencyResolver` плагина → framework / default context. Miss resolver для framework не является пропуском. Miss частной managed-зависимости после этих стадий — пропуск, не bind одноимённой сборки из хоста. Два теста: минимальный шаблон с вызовом BCL; отсутствующая частная DLL.

### Rationale

`System.Runtime` не входит в четыре семейства. Правило «нет пути — пропуск» отвергает исправный `net10.0` плагин. Полный fallback в хост тихо подхватит чужой пакет.

### Affected specification sections

Эпоха 5.

### Dependencies

Нет.

### Implementation freedom

Как отличают framework/default от отсутствующей частной сборки, если наблюдаемый исход двух тестов сохранён.

### Human state

`PROPOSED`

## `P-012` — Native в v1 не резолвится

### Source findings

`review-astra/E5-03` (`C-012`)

### Required outcome

v1 не резолвит native и не добавляет `LoadUnmanagedDll`. Наличие файла в `runtimes/` не обещает P/Invoke. Authoring не говорит, что любой свой пакет, включая native, годен загрузчику. Тест `native.bin` эпохи 3 — проверка копирования файла.

### Rationale

Серия — managed Roslyn на общем `Solution`. Ревьюер допустил явный отказ как альтернативу загрузчику. Текст сейчас выглядит сильнее контракта.

### Affected specification sections

`authoring.md`, эпохи 3 и 5.

### Dependencies

Нет. Не отменяет `P-003`.

### Implementation freedom

Нет unmanaged-резолвера в этой серии. Повторное включение — отдельное требование, не эта правка.

### Human state

`PROPOSED`

## `P-013` — Smoke настоящего entry point

### Source findings

`review-astra/E6-01` (`C-013`)

### Required outcome

Обязательный subprocess smoke entry point хоста: только `ROSLYN_MCP_PLUGINS` на dev-выход, первый `tools/list` содержит тул шаблона, `tools/call` его вызывает. Stdout не содержит не-протокольного вывода. In-process тесты `PluginStartup` остаются.

### Rationale

Цель эпохи — процесс. Приёмка сейчас вызывает метод напрямую и не видит ошибку проводки `Program` / переменной окружения.

### Affected specification sections

Эпоха 6, тесты.

### Dependencies

`P-010`, `P-011`. Stderr битого соседа на процессе стыкуется с `P-014`, но файл лога проверяет in-process приёмка `P-014`.

### Implementation freedom

Как поднимается subprocess и как читается первый `tools/list`, если это именно entry point хоста, а не вызов `PluginStartup` в testhost.

### Human state

`PROPOSED`

## `P-014` — Stderr сразу, лог после Build

### Source findings

`review-ds/E6-01` (`C-014`)

### Required outcome

Строка пропуска пишется в stderr в момент пропуска. Те же строки пишутся в лог хоста сразу после успешного `Build`, одним проходом по уже собранному отчёту, без второго discovery. Приёмка читает файл `logs/mcp-*.log`, не только stderr. До `Build` метод старта не обязан получать `ILogger`. Если упал сам `Build`, в файле лога этих строк может не быть.

### Rationale

`ILogger` появляется с контейнером. Контракт темы требует оба канала на поднятом сервере.

### Affected specification sections

Эпоха 6, `Program.cs`.

### Dependencies

Нет.

### Implementation freedom

Отдельная фабрика Serilog до `Build` не требуется и не запрещена, если файл лога после успешного `Build` содержит те же строки и второй обход плагинов не делается.

### Human state

`PROPOSED`

## `P-015` — Help плагина без каталога

### Source findings

`review-ds/E6-02` (`C-015`)

### Required outcome

Отчёт старта хранит описание и параметры принятых тулов. `get_tool_help` читает их и возвращает `[Description]` и параметры. `Kind` и `Group` для плагина — `plugin`. Это не группа `list_tool_groups`. Близкие имена неизвестного тула включают имена загруженных плагинных тулов и каталог. Записей в `McpToolCatalog` и `McpToolHelpCatalog` нет. Метаданные discarded-плагина в help не попадают.

### Rationale

Форматтер сегодня умеет только каталог. Эпоха 6 требует текст `[Description]`, не называя носитель.

### Affected specification sections

Эпоха 6, `McpToolHelpFormatter`.

### Dependencies

`P-005`, `P-006`.

### Implementation freedom

Хранить `MethodInfo` или уже снятые текст описания и параметры. Один источник назвать в эпохе. Не читать help из JSON Schema SDK, если этот источник не назван.

### Human state

`PROPOSED`

# Cross-finding consistency

Проверенные стыки:

- `P-005`, `P-006`, `P-007` и `P-015` согласованы. В общий контейнер попадают только принятые тулы; коллекцию читают после резолва options; откат освобождает имя того же контейнера; help хранит только принятые метаданные. Второго списка тулов из отчёта нет.
- `P-011` не противоречит `P-012`. Framework fallback не является native-резолвером и не является bind частной DLL из хоста.
- `P-003` не отменён `P-012`. Managed-пакет копируется. Native не обещается.
- `P-009` не подменяет `P-003`. Замок entry обязателен всегда. Location частной DLL обязателен, когда фикстура `P-003` участвует в загрузке.
- `P-010` блокирует осмысленный прогон эпох 5–7 и не конфликтует с запретом эпохи 7 менять csproj: запрет не снимает уже требуемое исключение `samples\**`.
- `P-008` закрывает падение процесса на типе jsonc. Это не изоляция битого плагина (`P-005`). Оба нужны: catch вокруг `Register` не ловит исключение, которое вылетело из чтения настроек раньше.
- `P-013` и `P-014` делят эпоху 6 без двойного discovery. Smoke проверяет протокол процесса. Файл лога проверяет приёмка после `Build`.
- `P-001` не вводит API, которое обошло бы `P-011` или загрузчик.
- `P-002` включает `U-001`: Roslyn не новее хоста, exact minor не требуется.
- `A-001` отложен человеком и не добавляет ось `Microsoft.Extensions.*` в `P-002`.

Эпоха 7 не закрывает серию сменой статуса, пока authoring и README темы не совпадают с принятыми `P-001`, `P-002`, `P-003`, `P-004` и `P-012`. Эти правки вносятся в контракт до реализации эпох, а не как расхождение «код уехал, контракт подогнать нельзя». Эпоха 7 по-прежнему не переписывает поведение под случайные имена в коде.

Набор не требует одновременно отката и частичной публикации, не требует и запрещает native, не велит и запрещает запись плагина в каталог.

# Complete ledger

`Source → Cluster → Class → Problem → Correction → Verdict → Severity → P/U`

- `review-astra/R-01` → `C-001` → `PARTIAL` → `CONFIRMED` → `MODIFY` → `ACCEPT WITH MODIFICATION` → High → `P-001`
- `review-astra/C-01` → `C-003` → `CONSENSUS` → `CONFIRMED` → `ACCEPTABLE` → `ACCEPT` → High → `P-003`
- `review-astra/C-02` → `C-004` → `CONSENSUS` → `CONFIRMED` → `ACCEPTABLE` → `ACCEPT` → Medium → `P-004`
- `review-astra/E1-01` → `C-005` → `PARTIAL` → `CONFIRMED` → `MODIFY` → `ACCEPT WITH MODIFICATION` → High → `P-005`
- `review-astra/E1-02` → `C-006` → `CONSENSUS` → `CONFIRMED` → `ACCEPTABLE` → `ACCEPT` → Medium → `P-007`
- `review-astra/E2-01` → `C-007` → `PARTIAL` → `CONFIRMED` → `ACCEPTABLE` → `ACCEPT` → High → `P-008`
- `review-astra/E3-01` → `C-008` → `PARTIAL` → `CONFIRMED` → `MODIFY` → `ACCEPT WITH MODIFICATION` → Medium → `P-009`
- `review-astra/E4-01` → `C-009` → `CONSENSUS` → `CONFIRMED` → `ACCEPTABLE` → `ACCEPT` → Blocker → `P-010`
- `review-astra/E5-01` → `C-010` → `CONSENSUS` → `CONFIRMED` → `ACCEPTABLE` → `ACCEPT` → Blocker → `P-011`
- `review-astra/E5-02` → `C-011` → `CORRECTION DISPUTE` → `CONFIRMED` → `MODIFY` → `ACCEPT WITH MODIFICATION` → High → `P-005`
- `review-astra/E5-03` → `C-012` → `PARTIAL` → `PARTIALLY CONFIRMED` → `MODIFY` → `ACCEPT WITH MODIFICATION` → Medium → `P-012`
- `review-astra/E6-01` → `C-013` → `CONSENSUS` → `CONFIRMED` → `ACCEPTABLE` → `ACCEPT` → Medium → `P-013`
- `review-ds/R-01` → `C-002` → `PARTIAL` → `PARTIALLY CONFIRMED` → `MODIFY` → `ACCEPT WITH MODIFICATION` → High → `P-002` (`U-001` закрыт)
- `review-ds/E1-01` → `C-005` → `PARTIAL` → `CONFIRMED` → `MODIFY` → `ACCEPT WITH MODIFICATION` → Medium → `P-006`
- `review-ds/E2-01` → `C-007` → `PARTIAL` → `CONFIRMED` → `ACCEPTABLE` → `ACCEPT` → High → `P-008`
- `review-ds/E4-01` → `C-009` → `CONSENSUS` → `CONFIRMED` → `ACCEPTABLE` → `ACCEPT` → Blocker → `P-010`
- `review-ds/E5-01` → `C-011` → `CORRECTION DISPUTE` → `CONFIRMED` → `MODIFY` → `ACCEPT WITH MODIFICATION` → High → `P-005`
- `review-ds/E6-01` → `C-014` → `PARTIAL` → `CONFIRMED` → `MODIFY` → `ACCEPT WITH MODIFICATION` → High → `P-014`
- `review-ds/E6-02` → `C-015` → `CONSENSUS` → `CONFIRMED` → `ACCEPTABLE` → `ACCEPT` → Medium → `P-015`
