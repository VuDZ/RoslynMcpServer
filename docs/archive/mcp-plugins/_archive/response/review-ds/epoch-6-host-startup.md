# Response: review-ds/epoch-6-host-startup.md

## review-ds/E6-01

### Reviewer claim

Строку пропуска нельзя записать в лог хоста до `Build`: Serilog попадает в DI только после `Build`, в подпись `PluginStartup` логгер не входит, статический `Serilog.Log` не выставляется. Тест проверяет stderr и info, не файл лога.

### Related findings

Complements `review-astra/E6-01`: та карточка про процесс и `tools/list`, эта — про канал лога на том же участке старта.

### Problem assessment

`ACCEPT`

### Problem rationale

`Program.cs` регистрирует Serilog через `AddSerilog` и получает логгер из контейнера после `Build`. Эпоха 6 вызывает старт до `Build` и передаёт stderr, не логгер. В репозитории нет присваивания статического `Serilog.Log.Logger`. Контракт темы обещает «stderr + лог». Исполнитель может выполнить только stderr и пройти приёмку. После успешного старта в `logs/mcp-*.log` строки пропуска не будет.

### Correction assessment

`MODIFY`

### Correction rationale

Не снимать обещание лога. Не требовать `ILogger` внутри `PluginStartup` до `Build`, если его ещё нет.

Порядок: пропуски пишутся в stderr сразу; тот же список исходов логируется хостом сразу после `Build`, до `RunAsync`, одним проходом по уже собранному отчёту, без второго discovery. Если упадёт сам `Build`, в файле лога строк может не быть; stderr к этому моменту уже заполнен. Это предел, его стоит написать в эпохе, а не делать вид, что файл лога существует до контейнера.

Создавать отдельную фабрику логгера до `Build` и передавать её в старт тоже закрывает дыру, но дублирует Serilog. После-`Build` достаточно для сценария «сервер поднялся без плагина, инженер открыл mcp-*.log».

### Evidence

`Program.cs`, `AddSerilog` и `Build`. Эпоха 6, подпись метода и фраза «ту же строку пишет лог». README, «stderr + лог». Тест эпохи 6 не читает файл лога.

### Classification

`ARCHITECTURE DEFECT`

### Severity

High у ревьюера. Совпадает относительно обещания «лог». На запущенном сервере это единственный долговечный канал; stderr клиент MCP легко не показывает.

### Required outcome

После успешного старта строка пропуска есть и в stderr, и в `logs/mcp-*.log`. До `Build` метод старта не обязан иметь `ILogger`.

### Proposed specification change

Эпоха 6: разнести каналы по времени. Приёмка читает файл лога после `Build`, не только stderr.

### Downstream consequences

`Program.cs` после `Build` логирует отчёт. Второй обход каталога плагинов не делается.

---

## review-ds/E6-02

### Reviewer claim

`get_tool_help` для плагина нечем наполнить: форматтер читает только `McpToolCatalog`, а эпоха 1 запрещает туда писать. `PluginLoadOutcome` несёт имена, не `MethodInfo` и не описание. `Kind` и `Group` у плагина не определены. Близкие имена тоже берутся из каталога.

### Related findings

Overlaps with `review-ds/E1-01`: help и `IsToolActive` до резолва options тоже смотрят в каталог.

### Problem assessment

`ACCEPT`

### Problem rationale

`McpToolHelpFormatter.FormatToolHelp` ищет дескриптор в `McpToolCatalog.All` и печатает `Kind` / `Group` / параметры из `MethodInfo`. `FormatUnknown` строит близкие имена из того же каталога. Эпоха 5 не сохраняет описание и метод. Эпоха 6 велит править форматтер и не называет источник метаданных. Приёмка «в ответе есть текст `[Description]` шаблона» толкает исполнителя к незапланированному шву.

### Correction assessment

`ACCEPT`

### Correction rationale

Носитель — список принятых тулов плагина, который старт кладёт в DI вместе с отчётом: имя, текст `[Description]`, параметры метода (или сам `MethodInfo`). Форматтер для известного плагинного имени читает этот список, не каталог и не статью `McpToolHelpCatalog`.

`Group` в help — слово `plugin`, и это не группа `list_tool_groups`. `Kind` не выдумывать как read/write/process: в манифесте классификации нет; строка Kind для плагина — `plugin`. Близкие имена неизвестного тула включают имена загруженных плагинных тулов плюс каталог.

Читать схему из уже созданного `ProtocolTool` можно как способ достать параметры, если `MethodInfo` не сохранять. Описание `[Description]` в протоколе тула может не совпасть с текстом help встроенных тулов один в один; спека должна выбрать один источник и назвать его. Предпочтение защиты: сохранить `MethodInfo` или текст описания в отчёте старта, чтобы help не зависел от того, что SDK положил в JSON Schema.

### Evidence

`Hosting/McpToolHelpFormatter.cs`, `FormatToolHelp` и `FormatUnknown`. Эпоха 1, запрет каталога. Эпоха 5, поля исхода. Эпоха 6, абзац help и список файлов. README: `get_tool_help` берёт `[Description]`, статей в каталоге help нет.

### Classification

`DOCUMENTATION DEFECT`

### Severity

Medium у ревьюера. Совпадает: хост без этого можно собрать, help будет врать или не соберётся честно.

### Required outcome

`get_tool_help` по имени загруженного плагина возвращает его `[Description]` и параметры, не требуя строки в `McpToolCatalog` и `McpToolHelpCatalog`. Неизвестное имя может предложить имя плагина.

### Proposed specification change

Эпоха 6: тип метаданных плагинного тула, кто его регистрирует, что печатается вместо Kind/Group, откуда берутся близкие имена.

### Downstream consequences

Новый маленький тип в DI, не расширение каталога. `list_tool_groups` по-прежнему без плагинов.
