# Disk watcher и лимит inotify на Linux

Создано: **2026-10-30**. Статус: **проблема записана; решение не выбрано, код не начат**.
Источник: [Claude Code smoke статистики использования, D-03](../usage-statistics/claude-code-smoke.md#d-03-загруженное-решение-занимает-почти-весь-лимит-inotify).

## Проблема

После загрузки решения [WorkspaceDiskWatcherStarter](../../Services/Workspace/WorkspaceDiskWatcherStarter.cs#L69)
создаёт один `FileSystemWatcher` на каждый дескриптор входного каталога. На Linux каждый
watcher — отдельный экземпляр inotify, а `fs.inotify.max_user_instances` по умолчанию
равен **128 на пользователя**, на все его процессы сразу.

Измерено 2026-10-30 на 1.5.21 для `RoslynMcpServer.sln` (5 проектов, Release):

| Каталоги | Watcher |
|---|---|
| Пакеты в `~/.nuget/packages` | 81 |
| `~/.dotnet` (reference pack, SDK analyzers config) | 2 |
| Каталоги репозитория, `bin/`, `obj/` | 26 |
| **Всего** | **109** |

У процесса MCP было 110 экземпляров inotify из 128. Остальным процессам пользователя
досталось около 16: main-набор тестов, запущенный через этот MCP, дал 86 падений
`The configured user limit (128) on the number of inotify instances has been reached`.
После `reset_workspace` у процесса остался 1 экземпляр, и тот же набор прошёл 1483/1483.

Это не регрессия ветки `usage-statistics`: watcher на каталоги входов появился раньше.
На Windows ограничения на число экземпляров нет, поэтому прогоны на Windows проблему
не показывали.

## Последствия

- Вторая копия MCP с загруженным решением, IDE или `dotnet watch` у того же пользователя
  получают отказ inotify. Сервер при отказе не падает: символьный поиск остаётся на
  снимке загрузки до `reset_workspace` (`InputCoverageReason.WatcherStartFailed`).
- Тесты и другие процессы с `FileSystemWatcher` или `reloadOnChange` падают с `IOException`.
- Для [статистики использования](../usage-statistics/README.md) это ограничение
  Linux-копий: при нескольких копиях с решениями часть из них работает без watcher.

## Направления

Выбор за владельцем; ниже не решение, а варианты для обсуждения.

1. Не ставить watcher на неизменяемые каталоги: пакеты NuGet по версии, reference packs
   и SDK. Их смена видна по `project.assets.json` и restore в каталогах проекта.
2. Сократить число экземпляров: точечные каталоги под общим корнем покрыть одним
   рекурсивным watcher. Рекурсия расходует `max_user_watches` (на машине владельца
   30 460), а не экземпляры; классификация по роли файла уже есть.
3. Для оставшихся внешних каталогов — опрос по времени изменения вместо inotify.
4. Диагностика: число watcher и отказы inotify в `get_mcp_server_info`.

## Проверки

Тест на число watcher для решения с внешними пакетами на Linux; повтор main через MCP
с загруженным решением; две копии MCP с одним решением на Linux без отказов inotify.
По `AGENTS.md` — SourceStructure, Release build и полный main; AnalyzerLifecycle, если
изменение затрагивает путь загрузки и публикации workspace.
