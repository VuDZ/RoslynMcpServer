# Claude Code smoke и статистика на Linux

Дата: **2026-10-30**. Ветка: `usage-statistics`. Версия: **1.5.21**.
Статус: **сбор и протокол работают на Linux с Claude Code; D-01/D-02 и мелкие
замечания исправлены в 1.5.22 и проверены автоматизированно; повторный native Linux
smoke 1.5.22 подтвердил `harness=claude` — [повторная проверка](#повторная-проверка-1522);
D-03 записан отдельной задачей**.
[Исправления и validation 1.5.22](implementation.md#исправления-ревью-1522). Проверки Cursor и dsh
и проверка OpenCode владельцем этим прогоном не закрываются.
[Машиночитаемое подтверждение](claude-code-smoke-validation.json).

## Подключение

Машина владельца: Ubuntu, SDK 10.0.113 в `/usr/bin` и 10.0.301 в `~/.dotnet`
(MCP получает `~/.dotnet` первым в `PATH`). Claude Code 2.1.314, native-установка.

- `dotnet publish -c Release -r linux-x64` — self-contained, файл **1.5.21.0**.
- Launcher владельца `~/.local/share/roslyn-mcp/launch.sh` запускает неизменяемый
  снимок самой свежей сборки из `$ROSLYN_MCP_REPO/bin`. В записи `roslyn` user-конфига
  Claude Code заданы `ROSLYN_MCP_REPO` (этот worktree) и
  `ROSLYN_MCP_LOG_DIR=~/.cache/roslyn-mcp/logs` — прежнее место текстовых логов.
  Статистика пишется в стандартный `~/.local/state/roslyn-mcp/usage/`.
- Запущенная сессия Claude Code держит конфиг MCP, прочитанный при старте:
  после `stop_mcp_server` она подняла прежний снимок 1.5.17. Новый конфиг применился
  только после перезапуска сессии. `get_mcp_server_info` затем показал 1.5.21.0 и
  ожидаемые каталоги конфига, plugins, логов и `usage/`.

## Проверки

Вне MCP, Release, Linux:

- Release build — PASS.
- Main `Category!=AnalyzerLifecycle&Category!=RequiresWindows&Category!=RequiresPinnedSdk`
  — **1483/1483 PASS**, без skips по TRX. SourceStructure входит в набор.
- `pwsh 7.6.8 scripts/Test-McpUsage.ps1` — **49 сценариев / 161 assertion PASS**.
  Это первый прогон анализатора в Linux PowerShell.

Через подключённый MCP:

| Шаг | Результат |
|---|---|
| `load_workspace` Release, 5 проектов | свежее открытие 5,32 с |
| `run_dotnet_build` | PASS |
| `run_test_by_filter`, тот же main-фильтр, решение загружено | **1397/1483**, 86 падений — исчерпан inotify ([D-03](#d-03-загруженное-решение-занимает-почти-весь-лимит-inotify)) |
| Сабагент A: `UsageWorkspaceInstrumentationTests`, `find_symbol_references` | 5/5 PASS; 23 ссылки |
| Сабагент B: `UsageJsonlWriterTests`, `UsageMemorySamplerTests` | 3/3 и 2/2 PASS |
| `reset_workspace`, затем тот же main-фильтр | **1483/1483 PASS** |

Lifecycle не запускался: изменение 1.5.21 касается только классификатора
`ToolLogAnalyzer`, а не analyzer/shadow-copy lifecycle.

## Сабагенты Claude Code переиспользуют MCP родителя

После перезапуска сессии в JSONL одна запись `start`: процесс с родителем — самим
процессом Claude Code. Все 14 вызовов родителя и двух сабагентов имеют один `proc`,
непрерывные `call` и `seq` 1–45 без пропусков. Сабагенты сразу видели решение,
загруженное родителем, и собственных `load` не делали.

Вызовы шли параллельно внутри одного MCP: main родителя и два `run_specific_test`
сабагентов — **до 3 одновременных вызовов**, **43,1 с** при ≥2. В
[прогоне Codex](codex-smoke.md) у каждого сабагента был свой MCP, а наложения внутри
одного процесса не было.

Для Q-04 и M3 это значит: сабагенты Claude Code не создают копий MCP и дублирования
памяти не добавляют; их нагрузка — параллельные вызовы общего процесса.

## Наблюдения

Окно процесса: 14:26:44–14:38:20 UTC, 45 событий, 13/13 полных снимков памяти,
1 свежее открытие и 1 успешный reset через 415 с после загрузки. Пик private
(`RssAnon` + `VmSwap`) — **0,344 GiB**, working set — 0,497 GiB. BuildHost в снимках —
0: после загрузки дочерних BuildHost у процесса нет, это подтверждено по дереву
процессов при повторной загрузке.

14 завершённых вызовов, один `success=false` — первый main с 86 падениями, что верно.
Штатный `Measure-McpUsage.ps1` на копии `usage/` отработал без ошибок: пробелов `seq`,
heartbeat-разрывов и неполных переходов нет; медиана свежего открытия K1 — 5,32 с;
в JSON/Markdown нет путей. В отчёт вошли и два тестовых процесса из
[D-02](#d-02-тест-пишет-в-настоящий-каталог-пользователя).

## Дефекты

### D-01. Native Claude Code распознаётся как `cli`

Исправлено в коде 1.5.22: resolver учитывает путь `claude/versions/<версия>`.
Настоящий native Linux Claude Code с 1.5.22 записан как `claude` —
[повторная проверка](#повторная-проверка-1522). Ниже — исходное наблюдение 1.5.21.

Native-установка Claude Code на Linux — бинарник
`~/.local/share/claude/versions/2.1.314`. [HarnessNameResolver](../../Diagnostics/HarnessNameResolver.cs)
у не-node родителя смотрит только на имя образа (`2.1.314`) и возвращает `cli`.
Все события настоящей сессии записаны с `harness=cli`. По
[проверке по harness](README.md#проверка-по-harness) устойчивый ложный `cli` у одного
из пяти harness — дефект резолвера, который исправляется до сбора. Тот же `cli` дал и
health-check `claude mcp get`. Windows и macOS native-установки этим прогоном не проверялись.

### D-02. Тест пишет в настоящий каталог пользователя

Исправлено в 1.5.22: дочерний процесс получает временный `ROSLYN_MCP_HOME`;
stdio-тест проверяет каталоги логов/usage и наличие текстового лога в нём.
Ниже — исходное наблюдение 1.5.21; остальные запуски настоящего entrypoint также проверены.

`PluginStartupTests` запускает настоящий сервер и убирает из окружения все
`ROSLYN_MCP_*` ([PluginStartupTests.cs](../../RoslynMcpServer.Tests/Plugins/PluginStartupTests.cs#L691)),
поэтому процесс пишет логи и статистику в `~/.local/state/roslyn-mcp`. Каждый прогон main
добавляет в корпус процесс `cli` с cwd во временном каталоге теста и без `exit`.
Анализатор считает его настоящим процессом с правой цензурой. Нужен изолированный
`ROSLYN_MCP_HOME` для дочернего процесса; аналогичные запуски сервера в тестах
проверить тем же правилом.

### D-03. Загруженное решение занимает почти весь лимит inotify

Не регрессия ветки: [WorkspaceDiskWatcherStarter](../../Services/Workspace/WorkspaceDiskWatcherStarter.cs)
создаёт `FileSystemWatcher` на каждый входной каталог, включая каталоги пакетов NuGet,
reference packs и SDK. На Linux каждый watcher — отдельный экземпляр inotify. Для этого
решения — **109 watcher**, у процесса MCP **110 экземпляров** при
`max_user_instances = 128` на пользователя. Тестам, IDE и второй копии MCP остаётся
около 16. После `reset_workspace` у процесса 1 экземпляр, и тот же main проходит.

Для статистики это ограничение Linux-копий: вторая копия с загруженным решением
останется без watcher. Сбор это не блокирует; задача записана отдельно и реализована в 1.5.23 (109 → 5 watcher):
[disk-watcher-inotify](../disk-watcher-inotify/README.md).

## Замечания ревью кода 1.5.21

Не блокировали сбор; все три замечания исправлены в 1.5.22
([проверки](implementation.md#исправления-ревью-1522)). Исходные замечания к 1.5.21:

- [Program.cs](../../Program.cs) получил UTF-8 BOM, которого нет в `main`; при этом
  [implementation.md](implementation.md#исходная-validation-1519) утверждает
  сохранение исходных EOL. Случайное изменение кодировки.
- [ToolLogAnalyzer](../../Diagnostics/ToolLogAnalyzer.cs#L52) строит regex в условии
  с полным именем пространства имён, без статического поля.
- В [Program.cs](../../Program.cs) исключение `host.StopAsync()` внутри `catch` заменит
  исходное исключение хоста.

## Повторная проверка 1.5.22

Дата: **2026-10-30**. Статус: **D-01 подтверждён исправленным на native Linux Claude Code**.

`dotnet publish -c Release -r linux-x64` коммита `7e5e71c` — файл **1.5.22.0**; launcher
владельца взял его после перезапуска сессии Claude Code 2.1.314. `get_mcp_server_info`
показал 1.5.22.0 и прежние каталоги логов и `usage/`.

- Все события нового процесса записаны с `harness=claude` и `ver=1.5.22`; предыдущий
  процесс 1.5.21 при перезапуске сессии записал `exit`.
- Родитель: `get_mcp_server_info`, свежий `load_workspace` Release (5 проектов),
  `find_symbol_references`. Сабагент: `get_mcp_server_info`, `find_symbol_references`
  (14 ссылок) и `run_specific_test` для `HarnessSessionIdTests`.
- Вызовы сабагента попали в тот же процесс, что и вызовы родителя: один `start`,
  непрерывные `call` 1–6; поиск родителя и `get_mcp_server_info` сабагента выполнялись
  одновременно.
- `HarnessSessionIdTests` — **27/28**: упал `Windows_reads_this_process_image_and_command_line`
  с `DllNotFoundException: kernel32.dll`. Тест помечен `RequiresWindows` и на Linux
  исключается фильтром main; `run_specific_test` категории не фильтрует, поэтому это не
  дефект. Вызов записан как `success=false`, что соответствует результату.

После проверки решение выгружено (`reset_workspace`), чтобы MCP не держал inotify (D-03).

## Evidence

Локальные, вне репозитория:

- Linux build/main/TRX: `~/tmp/usage-review-151226/`.
- Копия `usage/` и отчёт анализатора: `~/tmp/usage-claude-153826/`.
- Текстовый лог MCP: `~/.cache/roslyn-mcp/logs/mcp-20261030.log`.
- Резервная копия конфига Claude Code до изменения: `~/tmp/claude.json.bak-20261030-152233`.

Сырой JSONL не коммитится.
