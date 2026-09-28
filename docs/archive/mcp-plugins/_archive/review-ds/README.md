# Review: falsification of Плагины MCP (docs/mcp-plugins)

Каталог: замечания к спецификациям, не к тону документов.
Сверка: `RoslynMcpServer.csproj` (Version 1.4.15), `Hosting/` (`McpToolCatalog`, `McpToolRegistry`,
`McpToolDescriptor`, `McpRuntimeToolCollection`, `McpToolActivationService`,
`RoslynMcpServiceCollectionExtensions`, `McpToolHelpFormatter`), `Tools/`, `Config/RoslynMcpFileSettings.cs`,
`Program.cs`, публичная поверхность `Services/SolutionManager.cs`, `docs/README.md`,
`docs/ARCHITECTURE.md`, `RoslynMcp.jsonc.sample`, `RoslynMcpServer.sln`,
`RoslynMcpServer.Tests/` (`McpToolCatalogTests`, `McpToolGroupEnablementTests`), MCP SDK 1.3.0
(`ModelContextProtocol`, `ModelContextProtocol.Core`), MSBuild-оценка на SDK 10.0.204.
Runtime — рабочее дерево (Version 1.4.15), расхождения с «исходниками 1.4.15» из плана нет.

Цель ревью — опровергнуть предложенную архитектуру, а не улучшить её изложение.

| ID | Sev | Spec |
| --- | --- | --- |
| E4-01 | Blocker | epoch-4-sample-plugin.md |
| R-01 | High | README.md |
| E6-01 | High | epoch-6-host-startup.md |
| E5-01 | Medium | epoch-5-assembly-load.md |
| E2-01 | Medium | epoch-2-manifest-and-paths.md |
| E6-02 | Medium | epoch-6-host-startup.md |
| E1-01 | Medium | epoch-1-registration.md |

Прочитаны, карточек не дали: `authoring.md`, `epoch-3-shadow-copy.md`, `epoch-7-documentation.md`.

---

ID: R-01
Severity: High
Category: Support profile

Target:
README.md / «Контракт "внутренняя кухня"» / абзац «Совместимость: плагин собирают против той же `Version`…» (строки 141–144) и таблица поля `minHostVersion` в authoring.md (строки 98–99)

Claim:
Совместимость плагина обеспечивается сверкой `minHostVersion` из `plugin.json` с `Version` хоста до `Assembly.Load`: тот же мажор и хост не старее — план принят; другой мажор — каталог пропускается.

Evidence:
Связывающая плагин поверхность — это не `Version` продукта, а версии сборок, которые загрузчик подставляет из хоста по имени (`RoslynMcpServer`, `ModelContextProtocol`/`ModelContextProtocol.Core`, `Microsoft.CodeAnalysis.*`; README, строки 120–126). Обе оси уже двигались внутри мажора 1. `.cursor/rules/roslyn-mcp-overview.mdc`: «**1.0.35** — Roslyn 5.9.0» — патч продукта сменил мажор Roslyn. `docs/ARCHITECTURE.md` §11: «Runtime group activation is SDK-version-sensitive. `McpRuntimeToolCollection` depends on MCP SDK internals and must be revalidated on package upgrades». В `RoslynMcpServer.csproj` `<Version>1.4.15</Version>` и `ModelContextProtocol` 1.3.0 — независимые числа, и правило `roslyn-mcp-version-bump.mdc` предписывает менять только `Version` продукта. Проверок версии MCP SDK или Roslyn в плане нет ни в `PluginManifest` (эпоха 2), ни в `PluginAssemblyLoad` (эпоха 5).

Failure scenario:
1. Хост выпускает 1.5.0 и поднимает `ModelContextProtocol` до 1.4.x — по правилам репозитория это minor продукта, а `Version` плагина/`minHostVersion` остаётся той же осью.
2. Плагин с `minHostVersion: "1.4.16"` проходит проверку: мажор тот же, хост не старее — план не пропущен.
3. Загрузчик отдаёт плагину `ModelContextProtocol.Core` 1.4.x, собранный плагин связан с типами 1.3.0 → падение при разборе методов/`AddToolsFrom` (`TypeLoadException`/`MissingMethodException`) уже после того, как `get_mcp_server_info` пообещал загруженный плагин.

Suggested change:
Гейт совместимости должен стоять на той оси, что реально связывает: `plugin.json` объявляет версии `ModelContextProtocol` и Roslyn (или `AssemblyName.Version` ключевых сборок), а `PluginDiscovery` сравнивает их с фактически загруженными хостом. `Version` продукта оставить как человекочитаемую подсказку, а не как гарантию.

Confidence:
High
