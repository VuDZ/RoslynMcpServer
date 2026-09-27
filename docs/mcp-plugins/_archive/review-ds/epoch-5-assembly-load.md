ID: E5-01
Severity: Medium
Category: Lifecycle

Target:
epoch-5-assembly-load.md / «Контекст загрузки» / абзац про `Register` (строки 44–47) и «Результат на плагин» (строки 50–51)

Claim:
Исключение из `Register` — пропуск плагина: причина пишется в результат, процесс продолжается; синглтоны, которые `Register` успел добавить, откатывать нечем. Результат на плагин — «загружен или пропущен», третьего состояния нет.

Evidence:
`AddToolsFrom` (эпоха 1) регистрирует фабрики тулов прямо в `IServiceCollection` — это фактически `AddSingleton<McpServerTool>`: именно `GetServices<McpServerTool>()` наполняет `McpRuntimeToolCollection` через SDK-шный `IConfigureOptions<McpServerOptions>`, что видно по `McpToolCatalogTests.MeasureSurface` (строка 604 — там `IOptions<McpServerOptions>.Value` резолвят до чтения коллекции). `PluginAssemblyLoad` получает живой `IServiceCollection` «после `AddRoslynMcpServerTools` и до `Build`» (строки 54–56) и удалить из него уже добавленные дескрипторы не может.

Failure scenario:
1. `Register` плагина успевает выполнить `context.AddToolsFrom<ImpactTools>()`, затем бросает (например, второй `AddToolsFrom` падает на разборе типа чужой зависимости).
2. `PluginAssemblyLoad` пишет по этому плагину «пропущен», но фабрики `impact_*` остаются в `IServiceCollection`; после `Build` SDK добавляет их в `tools/list`.
3. `get_mcp_server_info` показывает `Plugins: skipped …` и одновременно `Registered MCP tools` с тулами этого плагина — агент вызывает тул «пропущенного» плагина и не имеет способа узнать, что причина пропуска относится к его же тулам.

Suggested change:
Определить третье состояние «загружен частично» с инвентарём тулов из `AddToolsFrom`, либо сделать регистрацию двухфазной (контекст собирает фабрики, `PluginAssemblyLoad` переносит их в DI после успешного `Register`). Формулировку «пропущен» для такого случая убрать.

Confidence:
High
