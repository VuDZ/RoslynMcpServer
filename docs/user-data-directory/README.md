# Каталог пользователя сервера

Создано: **2026-10-29**. Статус: **решения O-01…O-04 согласованы владельцем 2026-10-29; реализовано и автоматизированно проверено 2026-10-30 в версии 1.5.19, [отчёт validation](../usage-statistics/implementation.md)**.

## Проблема

Всё «глобальное» у сервера привязано к каталогу установки (`AppContext.BaseDirectory`):

| Что | Где сейчас | Код |
|---|---|---|
| Текстовые логи `mcp-*.log` | `<exe>/logs/` | [Program.cs](../../Program.cs#L40), [McpServerInfoHelper](../../Services/McpServerInfoHelper.cs#L55), `tail_tool_log` в [UtilityTools](../../Tools/UtilityTools.cs#L1525) |
| Глобальный `RoslynMcp.jsonc` | рядом с exe, затем cwd | [RoslynMcpFileSettings](../../Config/RoslynMcpFileSettings.cs#L151) |
| Drop-in plugins | `<exe>/plugins/` | [PluginDiscovery](../../Plugins/PluginDiscovery.cs#L29), вызов в [Program.cs](../../Program.cs#L96) |

Коллеги получают сервер через npx. Лончер пакета ставит сервер в кеш npm:
`%LOCALAPPDATA%\npm-cache\_npx\<hash>\node_modules\roslynmcp-<платформа>\RoslynMCP`.
При обновлении hash каталога `_npx` остаётся прежним, а каталог пакета заменяется
целиком; логи старой версии стираются (проверено владельцем 2026-10-29). Вместе с ними
пропадут глобальный `RoslynMcp.jsonc` и всё, что положено в `plugins/` рядом с exe.
Кеш npm, кроме того, может быть очищен.

Обновление сейчас переживают только переменные среды из конфига MCP самого harness
(`ROSLYN_MCP_PLUGINS` и другие), `RoslynMcp.jsonc` в cwd проекта и явные пути к plugins
вне каталога пакета.

У владельца каталог установки стабилен: Windows publish (`publish-and-verify.ps1`
сохраняет `logs/`) и Linux launcher (snapshot с общим каталогом логов). Эти схемы
ломать нельзя.

## Что сделать

Ввести постоянный каталог пользователя сервера и сделать его источником глобальных
настроек, plugins и местом логов.

| ОС | Каталог |
|---|---|
| Windows | `%LOCALAPPDATA%\RoslynMcpServer\` |
| Linux | `$XDG_CONFIG_HOME/roslyn-mcp/` для конфига и plugins, `$XDG_STATE_HOME/roslyn-mcp/` для логов и статистики (по умолчанию `~/.config` и `~/.local/state`) |
| macOS | `~/Library/Application Support/RoslynMcpServer/` |

Содержимое:

- `RoslynMcp.jsonc` — глобальный конфиг пользователя;
- `plugins/` — drop-in plugins пользователя;
- `logs/` — текстовые логи;
- `usage/` — поток статистики использования (спецификация — ветка `usage-statistics`,
  `docs/usage-statistics/README.md`).

Каталог exe остаётся источником, как сейчас, ради обратной совместимости.

## Решения владельца

| ID | Вопрос | Статус |
|---|---|---|
| O-01 | Порядок источников конфига | Решено 2026-10-29: exe → каталог пользователя → cwd. Каждый следующий источник переопределяет значения предыдущего. |
| O-02 | Порядок и дубликаты plugins | Решено 2026-10-29: `exe/plugins` → пользовательские `plugins/` → пути из конфига → `ROSLYN_MCP_PLUGINS`. Для одного ID берётся первый plugin, прошедший проверку discovery; остальные пропускаются с предупреждением. |
| O-03 | Где по умолчанию текстовые логи | Решено 2026-10-29: пользовательский `logs/` для всех установок; `ROSLYN_MCP_LOG_DIR` переопределяет каталог текстовых логов. |
| O-04 | Переопределение всего каталога | Решено 2026-10-29: `ROSLYN_MCP_HOME` задаёт общий корень для конфига, plugins, логов и статистики на всех ОС. Отдельный `ROSLYN_MCP_LOG_DIR` имеет приоритет для текстовых логов. |

Без `ROSLYN_MCP_HOME` используются каталоги ОС из таблицы выше. Если переменная
задана, структура едина: `<home>/RoslynMcp.jsonc`, `<home>/plugins/`,
`<home>/logs/`, `<home>/usage/`. `ROSLYN_MCP_LOG_DIR` меняет только место
текстовых логов; путь статистики остаётся `<home>/usage/`.

В рамках O-03 `publish-and-verify.ps1` и диагностика сервера должны показывать
фактический путь логов. Для сохранения прежнего размещения у владельца можно
задать `ROSLYN_MCP_LOG_DIR`; это относится и к Linux launcher с общим каталогом
логов между snapshot.

## Затрагиваемое

- Запись логов, `tail_tool_log`, `read_log_tail` по умолчанию, `get_mcp_server_info`: новый путь логов и найденные источники конфига и plugins.
- `RoslynMcpFileSettings.LoadFromDefaultLocations` и `PluginStartup` / `PluginDiscovery`: третий источник.
- README (разделы Logs, конфиг, plugins), [PRIVACY.md](../../PRIVACY.md) — где сервер хранит локальные данные, `AGENTS.md.sample` при необходимости, [samples/RoslynMcpPlugin/README.md](../../samples/RoslynMcpPlugin/README.md).
- Npx-лончер (вне этого репозитория): изменение не требуется — пользовательский каталог логов выбран по умолчанию.

## Связанное

- Статистика использования (ветка `usage-statistics`) пишет `usage/` в этот же каталог.
- Решение P-01 в замороженной ветке `research/workspace-host-process`: будущий список
  plugins — из глобального конфига и конфига выбранного solution/project. Глобальным
  конфигом там станет `RoslynMcp.jsonc` из этого каталога.

## Проверки

Тесты на порядок источников конфига и plugins, на отсутствующий и недоступный каталог
пользователя (сервер стартует, как сейчас), на победителя среди дубликатов plugins,
на `ROSLYN_MCP_HOME` и приоритет `ROSLYN_MCP_LOG_DIR`. По
`AGENTS.md` — SourceStructure, Release build и полный main suite; версия и CHANGELOG.
