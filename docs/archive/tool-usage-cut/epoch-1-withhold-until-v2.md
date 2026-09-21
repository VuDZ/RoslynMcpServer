# Эпоха 1. Скрыть девять инструментов до v2.0

Статус: сделана. От базы full 63 / lite 19. После эпохи: full **54**, lite **19**.

## Цель

Эти имена не попадают в `tools/list` ни в `full`, ни в `lite`. Тела методов остаются. Удаление кода — v2.0, и только если этот список к тому моменту не пересмотрят.

Имена (ноль вызовов в отчёте; ту же правку закрывают `apply_patch` и `update_file_content`):

- `remove_using`
- `organize_usings`
- `add_property_to_class`
- `add_field_to_class`
- `add_type_to_class_bases`
- `remove_member`
- `implement_interface`
- `extract_interface`
- `move_type_to_new_file`

## Почему не просто вычеркнуть строку каталога

`McpToolCatalog.Validate` требует взаимного совпадения: каждое имя из `Build()` имеет `[McpServerTool]`, и каждый `[McpServerTool]` есть в `Build()`. Удаление только записи каталога роняет старт. Удаление только атрибута прячет намерение «метод ещё жив, но не публикуется».

## Поведение после эпохи

В `McpToolCatalog` — список `WithheldUntilV2` с этими девятью именами и комментарием: удалить методы в v2.0, если решение не изменится.

`Build()` их не содержит. `All` и `CreateSurface` (оба профиля) их не содержат. `Validate` вычитает список из «атрибут есть, в каталоге нет» и падает, если имя из списка больше не имеет атрибута или атрибут есть у постороннего имени.

На каждом из девяти методов остаётся `[McpServerTool]` и комментарий с той же формулировкой про v2.0.

`RefactoringTools` перестаёт быть DI-host: оба его инструмента в списке. Класс не удалять. Хосты `AstTools` остаются: у них ещё есть публикуемые методы (`add_using`, `add_method_to_class`, `update_method_body`).

## Файлы

- `Hosting/McpToolCatalog.cs` — список, `Build()`, правка `Validate`.
- `Tools/AstTools.cs`, `Tools/RefactoringTools.cs` — комментарий на методах, атрибут не снимать.
- `RoslynMcpServer.Tests/McpToolCatalogTests.cs` — см. контракт ниже.
- README «Agent tools by version»: патч, full 54, lite без изменений этой эпохи.
- `RoslynMcpServer.csproj`: `Version`, `AssemblyVersion`, `FileVersion` вместе, после зелёной сборки.

## Контракт

- `get_mcp_server_info` и workspace health показывают 54 в full и прежние 19 в lite.
- Прямой вызов метода из теста по-прежнему компилируется. Через MCP имени нет.
- `list_tool_groups` / `get_tool_help` этих имён не показывают.
- Группы `editing` и остальные id не меняются.

## Тесты

- Ни full, ни lite не регистрируют ни одного имени из `WithheldUntilV2`.
- `DiscoverAttributedTools` по-прежнему находит все девять.
- Каталог без списка по-прежнему не принимает лишний атрибут и не принимает имя без атрибута.
- Существующие тесты тел этих методов не удалять и не переписывать под «метода нет».

## Вне эпохи

Перенос `find_symbol_references`, `find_implementations`, `get_call_graph`, `get_code_skeleton` — эпоха 2. Редкие группы, которые уже не в lite, не трогать.
