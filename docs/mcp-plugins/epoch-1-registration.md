# Эпоха 1. Регистрация тулов

Статус: не начата.

## Цель

Плагин, уже созданный в процессе, регистрирует свои синглтоны и тулы в тот же
`IServiceCollection` и ту же коллекцию MCP, что у встроенных тулов. Сборки с
диска эта эпоха не читает.

Контракт имён — [README.md](README.md). Класс тула хост создаёт на вызов через
`ActivatorUtilities`, как [`McpToolDescriptor.CreateFactory`](../../Hosting/McpToolDescriptor.cs).

## Поведение

Публичные типы в `RoslynMcpServer.Plugins`:

- `IRoslynMcpPlugin` — `string Name { get; }`, `void Register(RoslynMcpPluginContext context)`.
- `RoslynMcpPluginContext` — `IServiceCollection Services` и `AddToolsFrom<T>() where T : class`.

`AddToolsFrom<T>` обходит открытые методы `T` с `[McpServerTool]`. Для каждого
принятого имени регистрирует `T` как singleton (один раз) и фабрику тула тем же
способом, что [`McpToolRegistry.RegisterSelectedTools`](../../Hosting/McpToolRegistry.cs):
`AddSingleton` фабрики `Func<IServiceProvider, McpServerTool>`. В
[`McpToolCatalog`](../../Hosting/McpToolCatalog.cs) имя не добавляется.
`HostTypes` каталога не растёт. `lite` / `full` эти тулы не фильтрует.

Имя пропускается, в результат регистрации попадает причина, остальные имена
того же плагина регистрируются:

- нет префикса, который контекст получил при создании (сравнение ординальное);
- имя есть в `McpToolCatalog.All`;
- то же имя уже зарегистрировано другим вызовом `AddToolsFrom` в этом процессе.

Пустой или пробельный префикс — исключение до обхода методов. Это ошибка хоста,
не битый плагин: префикс приходит из манифеста, который проверяет эпоха 2.

`Register` вызывается до `IHost.Build`. После `Build` фабрики видны в
`McpRuntimeToolCollection`. Синглтон, добавленный плагином в `Services`,
резолвится из того же провайдера. Конструктор тула получает тот же
`SolutionManager`, что `GetRequiredService<SolutionManager>()`.

## Файлы

- `Plugins/IRoslynMcpPlugin.cs`
- `Plugins/RoslynMcpPluginContext.cs`
- `RoslynMcpServer.Tests/PluginRegistrationTests.cs` — класс плагина и класс тула
  живут в тестовой сборке. `DiscoverAttributedTools` смотрит только сборку хоста,
  каталог от этого не разъезжается.

Версию не менять. `authoring.md` и контракт темы не править. В таблице эпох
этого README — только строка эпохи 1.

## Тесты

Хост как в `McpToolActivationTests`: `AddRoslynMcpServerTools`, затем `Register`,
затем `Build`.

- Тул `sample_loaded_workspace` есть в `McpRuntimeToolCollection`, число имён
  каталога `full` на один больше.
- Тул без префикса `sample_` в коллекцию не попадает.
- Тул с именем `find_usages` не подменяет встроенный.
- Второй плагин с тем же именем тула это имя не добавляет.
- Синглтон из `Register` и `SolutionManager` внутри тула — те же экземпляры,
  что у провайдера.
- `McpToolCatalog.All` и `HostTypes` не содержат имя плагина и тип его тула.
- `list_tool_groups` по-прежнему строится только из каталога: имя плагина там
  не появляется. Отдельный абзац help не добавлять — это эпоха 6.

## Вне эпохи

`plugin.json`, каталоги, теневая копия, `AssemblyLoadContext`, `Program.cs`,
`get_mcp_server_info`, шаблон `samples/RoslynMcpPlugin`.
