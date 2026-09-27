ID: E1-01
Severity: Medium
Category: Measurement

Target:
epoch-1-registration.md / «Поведение» (строки 38–41) и «Тесты» (строки 58–59)

Claim:
«После `Build` фабрики видны в `McpRuntimeToolCollection`»; тест «Тул `sample_loaded_workspace` есть в `McpRuntimeToolCollection`, число имён каталога `full` на один больше».

Evidence:
Коллекция наполняется не на `Build`, а SDK-шным `IConfigureOptions<McpServerOptions>` (`McpServerOptionsSetup`, транзиентный, `AddMcpServer`) в момент первого получения `IOptions<McpServerOptions>.Value`, и только из `GetServices<McpServerTool>()`. Поэтому существующие тесты перед чтением коллекции явно делают `_ = host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value;` — `McpToolCatalogTests.MeasureSurface` (строка 604) и `McpToolGroupEnablementTests.Resolve` (строка 184). Собственный `McpToolActivationService.EnsureStartupToolsUnlocked` вызывается только из `EnableGroup` (строка 124). В `McpToolActivationService.CurrentToolCount` (строка 67) при пустой коллекции идёт fallback на `_surface.RegisteredToolCount`, где плагинных тулов нет вовсе.

Failure scenario:
1. Исполнитель пишет приёмку эпохи 1 буквально: `AddRoslynMcpServerTools` → `Register` → `Build` → читает `McpRuntimeToolCollection`.
2. Коллекция пуста; чтобы тест позеленел, он либо наполняет её в `Register`, либо добавляет плагинные тулы в `McpToolSurface`/`McpToolCatalog` — ломая требование «`HostTypes` каталога не растёт» и «`McpToolCatalog.All` не содержит имя плагина».
3. Если этого не делать, `get_mcp_server_info` в тестовом хосте и при вызове до первого `IOptions`-резолва показывает `Registered MCP tools` без плагинных тулов (fallback на `surface`), то есть заявленное в эпохе 6 «число включает тулы плагинов» неверно.

Suggested change:
Записать в эпохах 1/5/6, что коллекция наполняется при первом `IOptions<McpServerOptions>.Value`, и требовать этот шаг в тестах (как `MeasureSurface`); либо в эпоху 6 включить явное наполнение коллекции из отчёта старта, чтобы `CurrentToolCount` не зависел от порядка резолва.

Confidence:
High
