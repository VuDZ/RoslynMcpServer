# Эпоха 6. Старт хоста

Статус: не начата.

## Цель

Процесс MCP до `Build` обнаруживает плагины, копирует dev-пути, грузит сборки
и регистрирует тулы. Первый `tools/list` их видит. Битый плагин пишет причину
в stderr и в лог, процесс стартует с остальными тулами.

Поведение инструментов для агента меняется, поэтому после зелёной сборки —
патч `Version`, `AssemblyVersion`, `FileVersion` вместе. Число заранее не
фиксировать: следующий патч от версии в csproj на момент эпохи.

## Старт

В [`Program.cs`](../../Program.cs) после `AddRoslynMcpServerTools` и до
`builder.Build()` вызывается один метод старта. Ему передают `IServiceCollection`,
уже зарегистрированные настройки, `AppContext.BaseDirectory`, значение
`ROSLYN_MCP_PLUGINS` и stderr.

Метод сам вызывает открытие путей (эпоха 2), теневую копию (эпоха 3) и
загрузку (эпоха 5). Исключение одного плагина внутри этих шагов наружу из
метода старта не летит. Исключение самого метода, если оно всё же вышло, —
та же схема, что у сбоя каталога тулов: текст в stderr, ненулевой код, `return`
до `RunAsync`. Ожидаемый пропуск плагина таким сбоем не является.

На каждый пропуск stderr получает строку

`[RoslynMcp] plugin skipped (<id или путь>): <причина>`

Ту же строку пишет лог хоста. Успешная загрузка в stderr не расписывает список
тулов: его показывает `get_mcp_server_info`.

## Что видит агент

`get_mcp_server_info` дописывает плагины, не заменяя текущие строки. Новая
перегрузка [`McpServerInfoHelper.BuildInfoMarkdown`](../../Services/McpServerInfoHelper.cs).
Старые перегрузки текст не меняют, пока им не передали отчёт: существующие
тесты снимка остаются зелёными.

Инструмент передаёт отчёт. Нет загруженных и нет пропусков — строка
`- **Plugins:** (none)`. Иначе на каждый загруженный плагин строка с id, путём
entry, который реально грузили, и именами тулов. Пропуски — отдельными строками
с причиной. Число `Registered MCP tools` включает тулы плагинов, потому что они
в той же коллекции.

`get_tool_help` для имени загруженного плагина берёт `[Description]` и параметры
метода. Статью в `McpToolHelpCatalog` не добавляет. Неизвестное имя по-прежнему
даёт близкие имена каталога; имена плагинов в этот список близких тоже входят.

`list_tool_groups` имена плагинов не показывает. `lite` и `full` их не снимают.

`samples/RoslynMcpPlugin/plugin.json`: `minHostVersion` становится новой
`Version` хоста. Другие поля не менять.

## Файлы

- `Plugins/PluginStartup.cs`
- `Program.cs` — вызов до `Build`
- `Services/McpServerInfoHelper.cs` — новая перегрузка
- `Tools/ServerLifecycleTools.cs` — отдаёт отчёт в info
- `Hosting/McpToolHelpFormatter.cs` — help по загруженному тулу
- `samples/RoslynMcpPlugin/plugin.json` — только `minHostVersion`
- `RoslynMcpServer.csproj` — три свойства версии
- `README.md` — строка «Agent tools by version» и указатель RU на эту версию.
  Числа каталога full/lite не пересчитывать и не менять: плагины в каталог не входят.
- `.cursor/rules/roslyn-mcp-overview.mdc` — текущая версия и строка истории.
- `RoslynMcpServer.Tests/PluginStartupTests.cs`

`authoring.md` не переписывать. `AGENTS.md.sample` не расширять разделом про
автора плагина. В таблице эпох — только строка эпохи 6.

## Тесты

Временный `baseDirectory` с drop-in `plugins/sample/`, скопированным из выхода
шаблона (DLL, deps, pdb, `plugin.json` с уже новым `minHostVersion`).

- После `PluginStartup` и `Build` коллекция содержит `sample_loaded_workspace`
  и прежний набор каталога.
- Info содержит id `sample`, путь entry внутри временного drop-in и имя тула.
- Каталог `plugins` отсутствует — строка `(none)`, хост строится.
- Битый соседний подкаталог даёт строку пропуска и в захваченном stderr, и в
  info; тул шаблона на месте.
- `get_tool_help` по `sample_loaded_workspace` содержит текст `[Description]`
  шаблона. `list_tool_groups` этой строки не содержит.
- Профиль `lite`, собранный тем же старт-методом, тул плагина не выкидывает.

## Вне эпохи

Продуктовый раздел «как собрать плагин», правка `ARCHITECTURE.md`,
`RoslynMcp.jsonc.sample`, выгрузка ALC, уборка временных каталогов.
