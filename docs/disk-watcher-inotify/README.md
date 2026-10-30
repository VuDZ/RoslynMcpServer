# Disk watcher и лимит inotify на Linux

Создано: **2026-10-30**. Статус: **решения O-01…O-05 приняты владельцем 2026-10-30; реализованы в 1.5.23 и проверены на Linux 2026-10-30, включая main через MCP в Claude Code с загруженным решением — [реализация](#реализация-1523); Windows-прогон впереди**.
Источник: [Claude Code smoke статистики использования, D-03](../usage-statistics/claude-code-smoke.md#d-03-загруженное-решение-занимает-почти-весь-лимит-inotify).

## Проблема

После загрузки решения [WorkspaceDiskWatcherStarter](../../Services/Workspace/WorkspaceDiskWatcherStarter.cs)
создавал до 1.5.23 один `FileSystemWatcher` на каждый дескриптор входного каталога. На Linux каждый
watcher — отдельный экземпляр inotify, а `fs.inotify.max_user_instances` по умолчанию
равен **128 на пользователя**, на все его процессы сразу.

Измерено 2026-10-30 на 1.5.21 для `RoslynMcpServer.sln` (5 проектов, Release):

| Каталоги | Watcher |
|---|---|
| Пакеты в `~/.nuget/packages` | 81 |
| `~/.dotnet` (reference pack, SDK analyzers config) | 2 |
| Каталоги репозитория, `bin/`, `obj/` | 22 |
| Каталоги-предки репозитория (`/`, `/home`, …) для walk-up `Directory.Build.*` и `global.json` | 4 |
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

## Как были устроены подписки до 1.5.23

Правила заданы эпохой 2 [project-input-watching](../archive/project-input-watching/epoch-2-watchers-and-shared-sync.md):
рекурсия разрешена для каталогов проектов; внешний вход получает нерекурсивный
watcher на свой каталог; рекурсивный watcher на предка запрещён; объединение подписок
не расширяет их охват.

- [WorkspaceInputMapBuilder](../../Services/Workspace/WorkspaceInputMapBuilder.cs) даёт
  рекурсивную подписку каждому каталогу проекта и точечную — каждому входу вне каталогов
  проектов, а также любому входу с сегментом `bin`, `obj` или `artifacts`, даже внутри
  каталога проекта (`NeedsPointSubscription`). Каждая metadata-ссылка на пакет — отдельный
  каталог `~/.nuget/packages/<id>/<версия>/...`.
- [WorkspaceDiskWatcherStarter](../../Services/Workspace/WorkspaceDiskWatcherStarter.cs)
  пропускает только точный дубль: точечную подписку на тот же каталог, что и рекурсивная.
- Рекурсивный watcher проекта уже доставляет события из `bin/` и `obj/` внутри него:
  `IsIgnoredPath` отбрасывает их, только если пути нет в карте входов
  (`ShouldClassifyIgnoredPath`). Классификатору не важно, какой watcher доставил событие.
- `IsSharedFrameworkOrPackage` распознаёт пакеты и shared framework по подстрокам пути,
  но на подписки не влияет.

## Решения владельца

| ID | Вопрос | Решение 2026-10-30 |
|---|---|---|
| O-01 | Watcher на неизменяемые каталоги | Не ставить на каталоги под корнями пакетов NuGet и под корнем dotnet (`packs/`, `shared/`, `sdk/`). Изменение внутри каталога пакета той же версии видно только после `reset_workspace`; обычный restore в такой каталог не пишет. |
| O-02 | Как определить неизменяемые корни | Точные корни: `packageFolders` из `project.assets.json` проектов и корень dotnet, от которого сервер резолвит SDK и MSBuild. Путь под известным корнем — без watcher; корень неизвестен — наблюдение как сейчас. Подстроки вида `/packs/` для этого не используются: они скрыли бы пользовательский каталог с таким именем. |
| O-03 | Покрытые подписки (вариант 2a) | Не создавать точечную подписку, каталог которой лежит внутри рекурсивной подписки каталога проекта, и рекурсивную подписку, вложенную в другую рекурсивную. Охват не расширяется, инвариант эпохи 2 сохраняется. Объединение под новым общим предком (вариант 2b) отклонено. |
| O-04 | Диагностика | `get_mcp_server_info` показывает число запущенных watcher и отказы старта с причиной. В событие `load` статистики добавляются `watchers` и `watcher_failures`: добавленные поля, `v` остаётся 1 по правилам [эволюции схемы](../usage-statistics/README.md#эволюция-схемы). |
| O-05 | Обходной путь | В README раздел Linux: `fs.inotify.max_user_instances` через `/etc/sysctl.d/`, с объяснением, зачем он нужен при нескольких копиях MCP и IDE. |
| — | Свой Linux-watcher с одним экземпляром inotify на процесс | Отложен до данных статистики: если M4 на Linux покажет много одновременных копий с загруженными решениями. Опрос по времени изменения не нужен. |

Ожидаемый эффект для `RoslynMcpServer.sln`: O-01 убирает 83 watcher (81 пакет, reference
pack, SDK). `RoslynMcpServer.csproj` лежит в корне репозитория, поэтому по O-03 его
рекурсивная подписка покрывает остальные 21 каталог репозитория. Точечные подписки на
четыре каталога-предка вне репозитория остаются по дизайну: **109 → 5**. При обсуждении
предки ошибочно были отнесены к каталогам репозитория и называлось 109 → 1; измерение —
в [реализации](#реализация-1523). В типовом `src/A`, `src/B`, `tests/T` остаются
рекурсивные подписки проектов и точечные подписки вне них — корневой
`Directory.Build.props`, общий `artifacts/`, предки. Сокращение затрагивает и Windows.

## Scope

- Корни: чтение `packageFolders` из `project.assets.json` загруженных проектов и корня
  dotnet; построение подписок в `WorkspaceInputMapBuilder` с O-01 и O-03.
- Классификация событий не меняется: входы под исключёнными корнями остаются в карте,
  их неизменность — допущение O-01, а не доказанная нерелевантность.
- Диагностика O-04 в `get_mcp_server_info`, [схеме статистики](../usage-statistics/schema.md)
  и анализаторе (агрегат по машине: watcher на копию, отказы).
- README (Linux, O-05), CHANGELOG, версия.

## Проверки

- Подписки: пакет NuGet и reference pack без watcher; пользовательский каталог `packs/`
  наблюдается; нестандартный `NUGET_PACKAGES` из `packageFolders` распознан; неизвестный
  корень — подписка как раньше.
- Покрытие: точечная подписка внутри рекурсивной не создаётся; вложенная рекурсивная не
  создаётся; подписка вне каталогов проектов остаётся нерекурсивной. Изменение файла в
  `obj/` внутри проекта по-прежнему обрабатывается через рекурсивный watcher.
- Linux через MCP: main с загруженным решением без отказов inotify; две копии MCP с одним
  решением без отказов; число экземпляров inotify процесса после загрузки.
- По `AGENTS.md`: SourceStructure, Release build и полный main; AnalyzerLifecycle,
  поскольку меняется путь загрузки и публикации workspace.

## Реализация 1.5.23

Дата: **2026-10-30**. Статус: **O-01…O-05 реализованы и проверены на Linux, включая подключённый
Claude Code; Windows-прогон впереди**.

- [ImmutableInputRoots](../../Services/Workspace/ImmutableInputRoots.cs) читает
  `packageFolders` из `obj/project.assets.json` проектов потоковым разбором и строит
  `sdk`, `packs`, `shared` от каталога MSBuild вида `<dotnet>/sdk/<версия>`. Нечитаемый файл,
  относительный путь и другой layout дают отсутствие корня — наблюдение как раньше.
- [WorkspaceInputMapBuilder](../../Services/Workspace/WorkspaceInputMapBuilder.cs) не создаёт
  точечную подписку под неизменяемым корнем (O-01); вход остаётся в карте.
- [WorkspaceDiskWatcherStarter](../../Services/Workspace/WorkspaceDiskWatcherStarter.cs)
  сначала запускает внешние рекурсивные watcher, затем остальные; покрытие решается только
  реально запущенными рекурсивными watcher, поэтому отказ покрывающего оставляет точечные
  подписки на месте (O-03). Отказы старта возвращаются с причиной.
- Диагностика O-04: `get_mcp_server_info` — `Disk watchers: N (M recursive)`, число отказов и
  до пяти каталогов с причиной; `load` в статистике — `watchers`, `watcher_failures`;
  анализатор — агрегат `disk_watchers` по машине, строки без полей считаются неизвестными.
- O-05: README, разделы установки на Linux (английский и русский).
- Найдено при реализации: Microsoft.Build.Locator 1.11 не имеет свойства зарегистрированного
  экземпляра, поэтому `MsBuildEnvironmentInfo.RegisteredMsBuildPath` всегда пуст. Выбранный
  каталог MSBuild теперь запоминается при регистрации (`SelectedMsBuildPath`); без этого корень
  dotnet не находился, и `packs`/`sdk` оставались под наблюдением.

Измерение опубликованного self-contained linux-x64 1.5.23.0 через stdio: две одновременные
копии с изолированным `ROSLYN_MCP_HOME`, каждая загрузила `RoslynMcpServer.sln` (Release,
5 проектов):

| | До (1.5.21) | 1.5.23 |
|---|---|---|
| Watcher на копию | 109 | **5**: рекурсивный на репозиторий + `/`, `/home`, `/home/vudz`, `/home/vudz/projects` |
| Экземпляры inotify процесса | 110 | **6** |
| Отказы старта watcher | — | 0 у обеих копий; `load` в статистике: `watchers=5`, `watcher_failures=0` |

Validation на Linux (SDK 10.0.113, Release):

- Release build — PASS. SourceStructure — **123/123**; main
  `Category!=AnalyzerLifecycle&Category!=RequiresWindows&Category!=RequiresPinnedSdk` —
  **1502/1502**; `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`, `Category=AnalyzerLifecycle` — **13/13**;
  без skips по TRX. Прогон выполнен на финальном коде.
- `Test-McpUsage.ps1` (pwsh 7.6.8) — **50 сценариев / 167 assertions PASS**, включая
  сценарий `disk-watchers`.
- Новые тесты: `ImmutableInputRootsTests` (корни, ошибки разбора, точность против
  пользовательского `packs/`, подписки карты с корнями и без), покрытие и отказ покрывающего
  watcher в `WorkspaceDiskWatcherStarterTests`, строки `get_mcp_server_info` в
  `McpToolCatalogTests`, поля `load` в `UsageWorkspaceInstrumentationTests`. Классы,
  использующие статический `FailNextStartForTests`, выделены в непараллельную коллекцию.

Локальные evidence: `~/tmp/watchers-1523-final-170134/` (logs и TRX),
`~/tmp/watcher-probe-ze8ii3xq/` (логи и usage двух копий).

Через MCP в Claude Code 2.1.314 (сессия перезапущена, launcher владельца, 1.5.23.0):
после `load_workspace` Release `get_mcp_server_info` показал `Disk watchers: 5 (1 recursive)`;
у процесса MCP 6 экземпляров inotify, у всех процессов пользователя — 8 из 128. Main
`Category!=AnalyzerLifecycle&Category!=RequiresWindows&Category!=RequiresPinnedSdk` через
`run_test_by_filter` при загруженном решении — **1502/1502 PASS**; в 1.5.21 тот же прогон дал
86 падений из-за исчерпания inotify. Событие `load` в статистике: `harness=claude`,
`watchers=5`, `watcher_failures=0`.

Впереди: Windows main и lifecycle.
