# Workspace load cache v2 — заморожено

Статус: **заморожено 2026-11-21 решением владельца** вместе с
[workspace state cache](../../workspace-state-cache/README.md). Программа отложена 2026-10-07 в пользу
постепенного MVP состояния workspace и с тех пор не возобновлялась.

Документы программы и тесты `RoslynMcpServer.Tests/WorkspaceLoadCache/` удалены из `main`. Вместе с тестами
ушла категория `RequiresPinnedSdk`: фикстуры, закреплённые на SDK 10.0.300, main-набор и так исключал с
2026-10-24. Всё сохранено в теге
[`archive/research-workspace-state-cache`](https://github.com/VuDZ/RoslynMcpServer/tree/archive/research-workspace-state-cache/docs/backlog/workspace-load-cache)
(коммит `ff7b54b`).

Цели программы: O1 — быстрее первый полезный семантический запрос в новом процессе на большом решении,
включая reuse неизменного дерева; без git/ast, кэша VS, внутреннего хранилища Roslyn и сериализации
`Solution`, `Compilation`, `SyntaxTree` и текстов. Там же — эпоха 3 (live consistency) с вариантами политики
свежести, которые рассматривались и не были выбраны, и baseline benchmark на OrchardCore.
