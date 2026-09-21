# Срез выдачи по логам использования

Дата: 2026-09-21. Статус: **обе эпохи сделаны** (full 54, lite 15).

Основание — отчёт `mcp-usage/mcp-usage-aggregate.md` (две машины, 2026-08-10 — 2026-09-21, 3691 вызов `tools/call`). Это отдельная серия. [mcp-tool-surface-evolution](../mcp-tool-surface-evolution/README.md) её не заменяет и этим каталогом не отменяется: там по-прежнему запрещено убирать public-имена в 1.x и сжимать `full`. Здесь решение другое и узкое: девять имён уходят из регистрации до v2.0, четыре имени уходят из lite. Код методов остаётся.

База на момент плана: `McpToolCatalog` — full **63**, lite **19** (`InLiteCore` ≡ группа `core`). После обеих эпох: full **54**, lite **15**.

`(unparsed)` в отчёте — не инструмент. Это 296 строк, у которых парсер не достал имя. В эпохи не входит.

## Порядок

Эпохи трогают один и тот же `Build()` и одни и те же тесты каталога. Вторую не начинать, пока первая не влита: иначе счётчики full/lite разъедутся.

| Эпоха | Файл | Результат |
|---|---|---|
| 1 | [epoch-1-withhold-until-v2.md](epoch-1-withhold-until-v2.md) | 9 имён нет в `tools/list` ни в full, ни в lite. Методы и `[McpServerTool]` на месте, помечены к удалению в v2.0 |
| 2 | [epoch-2-lite-demotion.md](epoch-2-lite-demotion.md) | 4 имени выходят из `core`. В full остаются. Появляются группа `navigation` и перенос `get_code_skeleton` в `files` |

Каждая эпоха, которая меняет регистрацию: свой патч `Version` / `AssemblyVersion` / `FileVersion` после зелёного `run_dotnet_build`, строка в README «Agent tools by version». `PackageVersion` не трогать.

## Что серия не делает

Редкие инструменты, которых уже нет в lite, не переезжают и из full не выпадают:

- `editing`: `rename_symbol`, `get_code_fixes`, `apply_code_fix`, а также то, чем пользовались (`add_using`, `add_method_to_class`, `update_method_body`, `run_format`, `generate_test_method_stub`);
- группы `nuget` и `project` целиком;
- `run_dotnet_run` (`runtime`);
- `read_log_tail`, `tail_tool_log`, `stop_mcp_server`, `manage_agent_scratchpad` (`operations`).

Не входит: смена default-профиля с `full` на `lite`, удаление тел методов, правка `[Description]` у оставшихся инструментов, разбор `(unparsed)`.

`enable_tool_group` остаётся в `core`. Иначе lite не сможет включить `navigation` или `files`.
