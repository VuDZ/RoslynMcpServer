# 6 — Multi-root disk watcher

Pri **P2**. SemVer: **patch**. Блокер для п. 11. Не ждать workspace-load-cache.
Разбор: [../port-candidates-1-2-3-4-7.md](../port-candidates-1-2-3-4-7.md#6-multi-root-disk-watcher).

## Цель

Saved `.cs` в проекте вне каталога `.sln` попадает в dirty-set (известные
документы). Состав solution не менять.

## Файлы

- `Services/SolutionManager.cs` — `ComputeWatchRoots`, список watcher'ов
- `RoslynMcpServer.Tests/SolutionManagerWatchRootsTests.cs` (рецепт форка)

Общий helper корней (sln dir ∪ project dirs, без вложенных дублей) спроектировать
так, чтобы п. 11 его переиспользовал — не LCA-merge форка.

## Правка

`ComputeWatchRoots(loadedFile, projectFilePaths)` + один recursive watcher на
корень; сбой одного корня не слепит остальные. Disk-sync по-прежнему только
`WithDocumentText`. Нет `AddDocument`/`RemoveDocument`.

## Тесты

Nested dir отбрасывается; внешний project dir — корень; пустые пути игнор.

## Acceptance

Известный `.cs` вне папки `.sln` виден `find_symbol_*` без `reset_workspace`.
Новый/удалённый файл — по-прежнему composition-stale.

## Не копировать

Disk-sync форка; watch `obj`/`bin`; Epoch 3 coverage map.
