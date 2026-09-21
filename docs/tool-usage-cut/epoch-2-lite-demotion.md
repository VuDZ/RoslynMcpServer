# Эпоха 2. Вывести четыре инструмента из lite

Статус: не начата. Делать после эпохи 1. От её результата (full 54 / lite 19). После этой эпохи: full **54**, lite **15**.

Если эпоху 2 всё же возьмут отдельно от эпохи 1, full остаётся 63, lite становится 15. Счётчики в этом файле написаны для порядка «сначала эпоха 1».

## Цель

Убрать из lite то, что в отчёте редкое и сейчас сидит в `core`. В full имена остаются. Default профиля не меняется: пустой `ROSLYN_MCP_TOOL_PROFILE` по-прежнему `full`.

`InLiteCore` обязан совпадать с группой `core`. Отдельный флаг «в каталоге, но не в lite» не вводить. Выход из lite — смена группы.

| Имя | Сейчас | После |
|---|---|---|
| `find_symbol_references` | `core` | новая группа `navigation` |
| `find_implementations` | `core` | `navigation` |
| `get_call_graph` | `core` | `navigation` |
| `get_code_skeleton` | `core` | существующая `files` |

## Группа navigation

Добавить `navigation` в `McpToolGroups.All` и `Describe`. Текст описания: иерархия типов, ссылки по известному объявлению, граф вызовов. Не дублировать `find_usages` и `find_symbol_definition`: они остаются в `core`.

В lite группы нет, пока клиент не включит её через `ROSLYN_MCP_TOOL_GROUPS=navigation` или `enable_tool_group`. Оба пути уже умеют произвольную группу из `All`; новый механизм не нужен.

`get_code_skeleton` оказывается рядом с дисковыми чтениями. Lite видит его только вместе с группой `files`.

Единственное изменение, которое full заметит помимо отсутствия девяти имён эпохи 1: `list_tool_groups` показывает `navigation`. Сами четыре имени в full на месте.

## Подсказка при падении загрузки

`WorkspaceLoadGuidance` сейчас предлагает `get_code_skeleton` как запасной путь без workspace. После переноса инструмент не в lite. Текст должен говорить, что имя есть в full и в группе `files`, а не что оно всегда в текущем `tools/list`. Смысл совета (диск и Grep, без Roslyn Compile) не менять.

## Файлы

- `Hosting/McpToolGroups.cs`
- `Hosting/McpToolCatalog.cs` — четыре вызова `Core<>` становятся `Group<>`.
- `Services/WorkspaceLoadGuidance.cs` и `WorkspaceLoadGuidanceTests`.
- `RoslynMcpServer.Tests/McpToolCatalogTests.cs`, `McpToolHelpTests` (обход `McpToolGroups.All` подхватит новую группу сам, если не зашит старый список id).
- README: Tool profiles, Reference (EN, при необходимости RU), строка «Agent tools by version».
- `RoslynMcpServer.csproj`: патч версии после зелёной сборки. Это смена состава lite, не новый инструмент.

`AGENTS.md.sample` не пополнять параметрами. Одна правка допустима только если sample обещает `get_code_skeleton` как шаг lite без группы `files`.

## Контракт

- Lite: нет `find_symbol_references`, `find_implementations`, `get_call_graph`, `get_code_skeleton`. Есть прежние `find_symbol_definition` и `find_usages`.
- Lite плюс группа `navigation` возвращает три имени навигации и не возвращает `get_code_skeleton`.
- Lite плюс группа `files` возвращает `get_code_skeleton` вместе с уже входящими в `files` именами.
- Full содержит все четыре имени.
- Сигнатуры и `[Description]` этих четырёх методов не менять. Ссылки из описания `find_usages` на `find_implementations` оставить: в full инструмент есть.

## Тесты

- Состав lite равен `InLiteCore` и не содержит четырёх имён.
- Неизвестный group id по-прежнему отвергается; `navigation` принимается.
- Тексты `WorkspaceLoadGuidance` не утверждают, что `get_code_skeleton` доступен в голом lite.

## Вне эпохи

Не переносить `rename_symbol`, `get_code_fixes`, `apply_code_fix`, NuGet, Project, `run_dotnet_run`, логи и `stop_mcp_server`. Не забирать в `core` `get_method_body` и `rename_symbol` (это другая серия, stage 1 evolution). Не удалять методы эпохи 1.
