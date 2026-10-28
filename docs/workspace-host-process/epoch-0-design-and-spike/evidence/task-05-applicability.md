# C/E0/task-05 — Применимость: процессы harness и память

## Главное

- **Cleanup discovery и выход/crash отдельного Codex app-server на Windows завершают весь probe Job.** Обычный child и
  `DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP` не спасаются. Breakaway возвращает
  Win32 error 5. Это отрицательный результат для такого способа автозапуска host.
- Это измерение `mcpServerStatus/list`, выхода stdio app-server и crash app-server.
  Ручной выход/перезапуск Codex Desktop завершил прежний MCP; после запуска
  workspace в этом чате не загружен. Desktop close/crash и дети при desktop-выходе
  **не проверены**; Reload Window недоступен в production меню Codex.
- **Реальные сабагенты Codex Desktop воспроизвели удержание MCP:** два агента
  добавили два отдельных MCP, загрузили один workspace и завершились; оба MCP
  остались живы минимум 121 с после подтверждённого `completed`, суммарно
  435,5 MiB private bytes / 594,9 MiB working set. Это подключённая версия 1.5.16.
- Наблюдение владельца о **20+ MCP и 10–20 ГБ относится к Codex на этом компьютере**.
  Исторический состав процессов восстановить нельзя. Перед экспериментами найден
  один живой рабочий MCP 1.5.16; для контроля создана отдельная группа 20 MCP 1.5.18.
- Пустой 1.5.18: медиана working set **59,8 / 61,0 MiB** и private bytes
  **23,3 / 18,4 MiB** для Server / Workstation GC. «18 МБ» нельзя считать working set текущего сервера.
- Пустой сервер уже загрузил шесть сборок Roslyn, `Microsoft.Build.Locator` и
  decompiler. Он не является реализацией чистого proxy без этих зависимостей.
- На малом решении минутный idle не вернул память. Reset + ещё минута также
  оставили большую часть private bytes; это не доказательство новой утечки.
- В синтетической группе восемь + восемь совместимых загрузок и четыре пустых
  процесса: **2606,4 MiB private bytes**, сумма working set **4096,9 MiB**.
  Это не воспроизведение прежних 10–20 ГБ и не замер физической экономии RAM.
- Рекомендация: продолжать независимый task-03 и разделение session/workspace
  в design. Обещание «пережить Reload» и автоматический отдельный host пока
  **не проходят go**. Выбор модели владельцем для task-04 остаётся открытым.
- **Статус: in progress, 2026-10-28.** Замеры памяти и доступные process controls
  выполнены; два реальных subagent и состояние MCP до/после ручного перезапуска
  проверены. Полная desktop-матрица и число MCP на чат/окно остаются незакрытыми.

## База, среда и воспроизводимость

База production: `b4d4b682cb786952d355221bc7a4180bb85f15b6`, сервер **1.5.18.0**.
Ветка `research/workspace-host-process`; production-код, solution и publish не менялись.
Исполнитель — Codex, `gpt-6.1-sol` по metadata harness. Независимого ревью нет;
его раунды и исправления остаются `—`, не нулём.

Дата всех новых наблюдений — **2026-10-28**, timestamps первичных файлов UTC.
Windows 11 build 26200, 16 logical CPU, 93,7 GiB RAM, SDK 10.0.402,
runtime MCP .NET **10.0.13**, x64. Запуск из build output Release, не старого publish.
Linux-контроль: WSL Ubuntu 24.04, kernel `6.6.87.2-microsoft-standard-WSL2`,
SDK 10.0.113, runtime 10.0.13. macOS отсутствует.

Harness: Codex Desktop **26.1021.3705.0**, его `codex.exe`
**0.164.0-alpha.11.1**; установлен Cursor **3.27.12**, commit
`df132ffba90dfc15a12e6b8f020ba4e5d026c4b0`. Claude Code в доступных CLI не найден.
Свежий профиль Cursor не подключил probe; его MCP-лог содержит `unauthenticated`.
Доступ Computer Use к тестовому окну истёк по ожиданию подтверждения.

Стенд и команды — [HostSurvival README](../../../../spikes/HostSurvival/README.md).
Новых зависимостей нет. Существующие Win32 memory/stdio helpers переиспользованы.
Полные environment, command lines, конфиги harness, tool metadata и содержимое
производственных логов в evidence не скопированы. Первичные файлы — [task-05-data](task-05-data/).

## Жизнь процессов

### Настоящий Codex app-server, Windows

Изолированный процесс того же установленного `codex.exe`; только initialize и
`mcpServerStatus/list(serverName=host_survival)`. Нет новых чатов, subagent,
model requests или изменения глобального конфига. `serve` реализует initialize,
пустые tools/resources/prompts и JSON-RPC Method not found для неизвестного метода.

Для EOF и crash вызов discovery удерживается до EOF; readiness barrier проверяет
живой MCP и оба дочерних процесса до действия. Каждый сценарий выполнен один раз.
`discovery-completed` отдельно проверяет обычное завершение временного inventory.

| Событие | Сам MCP | Обычный child | Detached без breakaway | С breakaway |
|---|---|---|---|---|
| Завершён временный discovery | Завершён до проверки через 1 с | Завершён | Завершён | CreateProcess error 5 |
| EOF stdin app-server | Завершён к проверке после выхода | Завершён | Завершён | CreateProcess error 5 |
| Kill app-server | Завершён к проверке после выхода | Завершён | Завершён | CreateProcess error 5 |

App-server завершился за **10,3 мс** после EOF своего stdin и за **7,9 мс** после
`Popen.kill`. Жизнь MCP/детей проверена ещё через 2 секунды: это верхняя граница
около **2,02 с**, не точная задержка kill. Все три были живы непосредственно перед событием.
Для discovery MCP/дети уже завершились до закрытия app-server, поэтому это
отдельное событие, а не свидетельство о его shutdown.

MCP и оба живых ребёнка сообщили `IsProcessInJob=true`, `LimitFlags=8192 (0x2000)`.
В нём нет `BREAKAWAY_OK`; detached без breakaway создаётся, а попытка с
`CREATE_BREAKAWAY_FROM_JOB` завершается error 5. Значения флагов и отличие
отделения от консоли от выхода из Job подтверждены
[Microsoft: job limits](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_basic_limit_information),
[process creation flags](https://learn.microsoft.com/en-us/windows/win32/procthread/process-creation-flags).

В финальных логах нет `stdin-eof`, `console-cancel`, `process-exit` или
`unhandled-exception`; при `hold` чтение stdin оставалось активным. Это наблюдаемое
резкое завершение, согласующееся с Job cleanup; конкретный вызов kill внутри
Codex не трассировался. Наличие/порядок EOF перед kill с меньшей задержкой не установлены.
Первичные результаты и JSONL: [Codex](task-05-data/codex-final-eof/codex-lifecycle.json).

### Контроль механизма ОС

Это runner, а не Cursor/Claude/Codex MCP transport. По одному запуску на событие.

| Среда / событие | MCP probe | Обычный child через 2 с | Отделённый child через 2 с |
|---|---|---|---|
| Windows runner / EOF probe stdin | EOF, exit 0 | Жив | Detached и breakaway живы |
| Windows runner / kill probe | Exit 1 без EOF | Жив | Detached и breakaway живы |
| WSL Linux / EOF probe stdin | EOF, exit 0 | Жив | setsid жив |
| WSL Linux / SIGKILL probe | Exit −9 без EOF | Жив | setsid жив |

Windows runner разрешал breakaway: MCP/ordinary/detached — Job flags `0x2800`,
breakaway — всё ещё во внешнем Job с `0x2000`. Успех CreateProcess не доказывает
выход из всех вложенных jobs. На Linux ordinary сохранил session/group родителя,
`setsid` создал session/group со своим PID. Различие с Codex измерено, поэтому
результат runner не переносится на harness.

Windows EOF/kill ожидались по сохранённому process handle; для детей проверялся
`GetExitCodeProcess`, на Linux — `/proc/<pid>/status` с исключением zombie.
Оставшиеся дети завершены через stop-файл; cleanup проверен self-check.
[Windows JSON](task-05-data/windows-final/control-lifecycle.json),
[Linux JSON](task-05-data/linux-final/control-lifecycle.json).

### Реальные сабагенты Codex Desktop, дополнение 2026-10-28

По прямому предложению владельца запущены два `collaboration.spawn_agent`:
`mcp_lifetime_a` и `mcp_lifetime_b`. Каждый вызвал настоящий Roslyn MCP:
`get_mcp_server_info` → `load_workspace` (это же решение, Release) → info,
подтвердил пять проектов, подождал 45 секунд и завершил работу. Файлы не
менялись, build/test, reset/stop MCP агенты не вызывали.

Это работающий Codex Desktop **26.1021.3705.0** с backend
**0.164.0-alpha.11.1**, Windows 26200. Реально настроенный MCP остаётся
**1.5.16.0** из прежнего publish; исходники checkout — 1.5.18, HEAD `723e6f3`.
Новые числа не смешиваются с GC-матрицей 1.5.18; фактический GC живых MCP
этим наблюдением не инструментировался.

До spawn, 16:24:26 МСК, был один PID 56052. После запуска — три PID:
56052, **59328**, **59412**, все с parent PID 33188 (Codex backend).
Whitelisted лог содержит две новые session `codex-fpcb` и `codex-2bft`,
`threadSource=subagent`, по три MCP-вызова, по пять успешно загруженных проектов.
Последние info — 16:25:34 и 16:25:48 МСК.

Координатор подтвердил через `list_agents`, что оба имеют статус `completed`,
затем провёл отдельное окно **16:28:58.819–16:31:00.107 МСК**, 121,288 с:
61 снимок с интервалом около 2 с. На всех снимках живы те же три PID с тем же
временем старта. Дополнительно сохранены 76 предыдущих снимков во время
работы/завершения агентов. Счётчики — `Get-Process.PrivateMemorySize64` и
`WorkingSet64`, как в Win32-контроле; start time исключает подмену PID.

| Новый MCP | Private bytes, MiB | Working set, MiB |
|---|---:|---:|
| 59328 | 221,6 | 298,2 |
| 59412 | 213,8 | 296,6 |
| Вместе, до округления строк | 435,5 | 594,9 |

Это последний снимок окна после `completed`, а не пик. Завершение работы
агента не освободило его MCP/workspace в течение измеренного времени.
После наблюдения координатор **явно остановил только два новых MCP** с проверкой
PID + start time + executable path; остался рабочий PID 56052. Это cleanup
эксперимента, не естественное завершение, его нельзя засчитать как shutdown harness.

[Сводка и условия](task-05-data/subagents-20261028/summary.json),
[все 137 снимков: первая серия](task-05-data/subagents-20261028/process-samples.jsonl),
[окно после completed](task-05-data/subagents-20261028/post-completion.jsonl),
[whitelisted лог](task-05-data/subagents-20261028/log-facts.json),
[cleanup](task-05-data/subagents-20261028/cleanup.json).

В этом запуске каждый из двух агентов добавил один MCP. Это подтверждает
реальное дублирование совместимого workspace и механизм накопления процессов,
описанный владельцем, но не восстанавливает состав прежних 20+ MCP.
`completed` агента не равен закрытию/архивации desktop-чата. Закрытие чата,
Reload, выход приложения и ordinary/detached child под этими событиями здесь
не инструментировались. Контракт lifetime остаётся вопросом U-C-07; новая
production-утечка или дефект Codex по одному этому наблюдению не объявлены.

### Ручной выход и перезапуск Codex Desktop, дополнение 2026-10-28

Владелец получил инструкцию выполнить Ctrl+Q, затем запустить Codex и вернуться
в этот чат; после этого сообщил «перезапустил». Само нажатие и момент выхода
не инструментировались. Это один ручной before/after замер на той же Windows
26200, Codex Desktop 26.1021.3705.0 / backend 0.164.0-alpha.11.1, MCP 1.5.16.0.

До действия, **17:19:19.701 МСК**, был один MCP **56052**, parent **33188**,
старт 2026-10-28 01:43:07.650 МСК; загружены пять проектов этого решения,
Release. Снимок первоначально готовился для Reload; Reload не выполнялся,
поскольку production-пункт меню недоступен. Перед полным выходом процесс
не перезапускался и workspace не сбрасывался.

После возврата, **17:38:00.357 МСК**, PID 56052 отсутствует; живы два новых MCP
**45500** и **46100**, старт **17:35:11.300 / 17:35:09.803**, parent **59364**.
Проверка backend в 17:39:31 подтвердила: старого 33188 нет, новый 59364
стартовал в 17:34:57.068; основной процесс desktop — в 17:34:55.245.
До проверки состояния `load_workspace` не вызывался.
`get_mcp_server_info` в **17:38:09.166**: **Workspace loaded: no**, load source
`not loaded`, full profile, plugins=none. Связь tool session `codex-h6sa`
с одним из двух PID отдельно не установлена.

**Прежний MCP и его workspace не пережили сообщённый выход/перезапуск.**
Это не проверка сохранения host при одном лишь отключении клиента. Нет точной
задержки завершения и различения EOF/сигнала/kill: внешний непрерывный монитор
не работал, shutdown/EOF marker старой session в выбранном окне лога не найден.
Ordinary/detached children к этому desktop-событию не подключались. Снимок двух
новых MCP не устанавливает правило количества процессов на чат или окно.
Эти новые рабочие процессы координатор не останавливал.

[Условия и ограничения](task-05-data/desktop-quit-20261028/summary.json),
[до](task-05-data/desktop-quit-20261028/before.json),
[после](task-05-data/desktop-quit-20261028/after.json),
[backend](task-05-data/desktop-quit-20261028/backend.json),
[whitelisted лог](task-05-data/desktop-quit-20261028/log-facts.json).

### Полнота требуемой harness-матрицы

`not-run` означает отсутствие наблюдения, а не pass или отрицательный результат.

| Harness × ОС | Закрытие чата | Reload окна | Штатный выход UI | Crash UI |
|---|---|---|---|---|
| Codex Desktop × Windows | not-run | unavailable (production menu) | MCP до/после проверен; дети и сигнал not-run | not-run |
| Cursor 3.27.12 × Windows | not-run | not-run | not-run | not-run |
| Claude Code × Windows | unavailable | unavailable | unavailable | unavailable |
| Codex / Cursor / Claude Code × WSL Linux | unavailable | unavailable | unavailable | unavailable |
| Все три × macOS | unavailable | unavailable | unavailable | unavailable |

**Уточнение доступности Reload, 2026-10-28.** По сообщению владельца пункта нет
в меню. Read-only проверка установленного `app.asar`, модуль
`.vite/build/main-BPBEjAxi.js`: `Reload Window` включается по `allowWindowReload`,
а флаг включён только при build flavor, отличном от Prod.
[Факты проверки](task-05-data/codex-reload-availability.json).
Предложение автора выполнить Reload до этой проверки было ошибкой.
Production Reload отмечен unavailable, не pending и не pass. Ручной выход
и повторный запуск описаны выше; исходный снимок PID/workspace сохранён.

Для Codex нет автоматизации UI: доступный
[computer-use SKILL.md](C:/Users/VuDZ/.codex/plugins/cache/openai-bundled/computer-use/26.1021.31345/skills/computer-use/SKILL.md)
отсылает к [guidance.md](C:/Users/VuDZ/.codex/plugins/cache/openai-bundled/computer-use/26.1021.31345/docs/guidance.md)
с прямым правилом: «Do not automate the ChatGPT desktop app UI
or Codex CLI or Codex extensions within Windows apps». Проверки через CLI/API
это ограничение не обходят и desktop-события не заменяют. Ручная последовательность
подготовлена в README probe. В Cursor дополнительно нет авторизованного тестового
подключения. На Linux доступны runtime и runner, но не эти native harness.

Число MCP на чат и окно **не установлено**. Для двух реальных subagent выше
измерено по одному новому MCP; универсальное правило для всех агентов не выведено.
В одном вызове discovery Codex создан один MCP; это не правило «один процесс на чат».
Получатель desktop-матрицы и недостающего счётчика — незакрытая часть этого
task-05; task-04 получает Job constraint и наблюдение удержания MCP после completed.

## Реальная ситуация и контрольная группа

В чате **«Обсудить реализацию workspace-host»**
(`01a165c7-480e-7da0-b767-9cf1197b8457`, turn
`01a165d0-4ba4-75d3-a700-5a4a86988ca5`) владелец описал Codex, около 20 MCP и
10–20 ГБ. В текущем чате уточнил, что речь об этом компьютере и о 20+ MCP.
Это реальное наблюдение владельца, не гипотетический пример. В доступной
переписке нет per-PID снимка с путями, конфигурациями и двумя memory counters.

Перед экспериментами список реальных процессов содержал один Roslyn MCP,
PID 56052, publish **1.5.16.0**. `get_mcp_server_info` подтвердил пять проектов,
full profile, plugins=none, explicit load. Лог session `codex-2jmy`:
`load_workspace` 2026-10-28 01:48:32 +03:00, решение этого checkout,
Configuration=Release, briefOutput=true; platform, targetFramework и buildArgs
опущены. Длительный процесс уже использовался для сборок/проверок, поэтому его
память не является свежим 1.5.18 baseline. [Снимок](task-05-data/live-inventory.json).

Whitelisted чтение 14 файлов `mcp-20261015.log`…`mcp-20261028.log` дало
3608 различных session tokens, 3580 из них с префиксом codex; у 182 есть load
requests. Это **не 3608 одновременно живых процессов**: лог не является
инвентарём liveness, четырёхсимвольные suffix могут повторяться, запрос load
сам по себе не доказывает успешную загрузку. Исторический private bytes/working
set лог не содержит. [Сводка чтения](task-05-data/log-summary.json).

Для воспроизводимого контроля запущено одновременно 20 процессов 1.5.18,
Server GC, full, plugins=none, defaults prewarm/BuildHost. Два приближения ключа
совместимости: одинаковый абсолютный workspace path + Release + опущенные
platform/TFM/buildArgs + одна сборка сервера/окружение. Итоговый ключ ещё задаёт U-C-01.

| Группа | MCP | Workspace | Private bytes, MiB | Сумма working set, MiB |
|---|---:|---|---:|---:|
| A | 8 | `RoslynMcpServer.sln`, 5 проектов | 1427,7 | 2097,1 |
| B | 8 | `samples/RoslynMcpPlugin/RoslynMcpPlugin.csproj`, 2 проекта | 1086,7 | 1761,2 |
| C | 4 | Без workspace | 92,0 | 238,6 |
| Всего | 20 | Два разных descriptor | 2606,4 | 4096,9 |

Один снимок группы, все 20 живы; idle от последнего ответа **60,0–89,8 с**.
У каждого PID сохранены workspace, явные load args, idle, оба счётчика и время.
Числа получены `GetProcessMemoryInfo`: `PrivateUsage` и `WorkingSetSize`.
Private bytes — частный commit, working set включает общие страницы DLL;
сумма working set не равна уникальной физической RAM. BuildHost в эти суммы не включён.
[Per-PID JSON](task-05-data/cohort.json).
Счётчики успешных load: A — пять проектов, B — два (включая ProjectReference
на сервер), во всех 16 — zero workspace diagnostic lines;
[whitelisted log counters](task-05-data/cohort-load-summary.json).

Отличия от исходной ситуации: запуск controller вместо 20 Codex-клиентов,
заранее выбранные два малых workspace и коэффициент дублирования 8; все idle,
одна версия 1.5.18 и одна конфигурация. Старые workspace/версии/доли idle
неизвестны. По этим данным нельзя объявить, какая часть прежних 10–20 ГБ была дубликатами.

## GC и пустой процесс

Три новых процесса на режим, чередование server/workstation между раундами.
После initialize — 10 с; load решения пяти проектов Release — ещё 10 с;
`find_symbol_definition(SolutionManager)` — idle 60 с; затем reset — ещё 60 с.
Ни GC.Collect, ни OS trim не вызывались. Hook при измерениях отсутствует.
Окружение GC экспериментов очищено от унаследованных GC/Roslyn overrides;
задаются только `DOTNET_gcServer=1/0` и full profile. Других конфигов в cwd/exe нет.

| Этап | Server private, MiB | Server WS, MiB | Workstation private, MiB | Workstation WS, MiB |
|---|---:|---:|---:|---:|
| Пустой, 10 с | 23,3 | 59,8 | 18,4 | 61,0 |
| Loaded, 10 с | 124,2 | 212,4 | 133,3 | 227,2 |
| После поиска | 165,9 | 282,1 | 144,9 | 270,2 |
| Idle, 60 с | 161,3 | 278,9 | 145,7 | 272,4 |
| Reset + idle, 60 с | 154,7 | 273,1 | 136,7 | 263,8 |

Таблица — медианы, [все 30 точек](task-05-data/memory.json).
Пустой private: server 23,14–23,42, workstation 17,84–18,68 MiB; WS:
59,67–60,57 и 60,46–61,25 MiB. Loaded server private колебался 124,11–176,00,
idle — 160,56–226,56 MiB. На n=3 и таком разбросе результат не является
статистическим сравнением режимов на всех решениях.

В этом контроле Workstation уменьшает private пустого процесса примерно на
4,8 MiB и медиану после idle на 15,7 MiB, но пустой working set не уменьшился.
Минута idle не освобождает workspace; reset не возвращает private к свежим 23/18 MiB.
Это наблюдение committed memory, а не проверка достижимости managed объектов.
Известный дефект удержания sanitized snapshot исправлен в 1.5.18 и имеет
[отдельный regression/validation](../../evidence/2026-10-28-task-00-review-fixes.md).
Новой production-утечки этот эксперимент не доказывает; новых production bugs не зарегистрировано.

Inventory двумя отдельными hook-запусками подтвердил `IsServerGC=true/false`
и runtime 10.0.13. Уже в пустом процессе загружены `Microsoft.CodeAnalysis`,
`.CSharp`, `.Workspaces`, `.CSharp.Workspaces`, `.Features`, `.CSharp.Features`,
`Microsoft.Build.Locator`, `ICSharpCode.Decompiler`. `Microsoft.Build` и
`Microsoft.CodeAnalysis.Workspaces.MSBuild` ещё нет; plugins нет.
[Server inventory](task-05-data/assemblies-gc1.json),
[Workstation inventory](task-05-data/assemblies-gc0.json).
Это согласуется с bootstrap [Program.cs](../../../../Program.cs:16), eager
Roslyn load до stdio transport и [plugin discovery до provider](../../../../Program.cs:96).
Default Server GC задан в [csproj](../../../../RoslynMcpServer.csproj:22).
Reset [очищает ссылки и Dispose workspace](../../../../Services/Workspace/SolutionManager.cs:768),
но это не обещание возврата process commit. Общая DI-регистрация
[оставляет SolutionManager singleton](../../../../Hosting/RoslynMcpServiceCollectionExtensions.cs:75).

Цена Workstation по latency **здесь не измерялась**. Прежний контроль темы D
на 434 проектах: первый поиск 21,458 → 12,864 с и 17,692 → 10,018 с при включении
Server GC, working set +14–19%; офис 1.5.13 дал первый поиск ×0,82.
[Матрица D/task-12](../../../workspace-load-performance/epoch-0-stage-measurement/evidence/task-12-server-gc-first-search.md),
[офис](../../../workspace-load-performance/epoch-0-stage-measurement/evidence/office-first-search-2026-10-23.md).
Это другие базы/решения; проценты не применены к новому малому контролю.
В прежней Roslyn-матрице записано 44 Failure загрузки; её ограничения сохраняются.
Переключать production GC по task-05 оснований нет. Настройка Workstation в
действительно чистом proxy остаётся кандидатом после появления такого proxy.

## Сравнение моделей: только условные границы

Пусть `L` — сумма памяти 16 loaded MCP, `H` — сумма минимальных наблюдаемых
представителей A и B, `p` — будущий proxy, `U` — четыре текущих пустых MCP.
Для private: `L=2514,4`, `H=267,6`, `U=92,0` MiB;
для WS: `L=3858,3`, `H=441,0`, `U=238,6` MiB.
Четыре процесса C оставлены неизменными. IPC/нагрузка host и retained CLR
после demotion не измерены; брать минимум группы для H оптимистично.

| Модель | Расход в контроле / формула | Условная верхняя экономия private / WS, MiB |
|---|---|---:|
| Текущий режим | Измерено `L+U` | 0 / 0 |
| Роли в MCP | Оценка `H+14p+U` | 2246,9 / 3417,3 при `p=0` |
| Отдельный host | Оценка `H+16p+U` | 2246,9 / 3417,3 при `p=0` |
| Idle unload без IPC | При всех 16 idle и возврате каждого к свежему пустому | 2145,7 / 2903,2 |

Это верхние границы **внутри выбранного распределения и допущений**, не прогноз
production. Для наглядности, если условно взять `p` равным медиане текущих
пустых процессов C (23,04 private / 59,69 WS), формулы дают экономию ролей
1924,3 / 2581,6 MiB, отдельного host — 1878,2 / 2462,3 MiB. Сам будущий proxy
не реализован и может иметь другой размер. Контроль показывает размер
возможности схлопывания совместимых копий, а не реализованную экономию.

Оценка idle unload предполагает, что все 16 имеют право выгрузиться, а CLR
возвращает память как после нового запуска. Измеренный reset этому не равен:
в root-контроле server private после idle/reset — 161,3 → 154,7 MiB.
Активные дубликаты idle unload не устраняет. При разных проектах или
несовместимых параметрах общая доля дубликатов может быть нулевой.
Ускорение холодного старта внешним вариантом B ни одной модели не приписано.
Отложенный владельцем HTTP-вариант в сравнение не возвращён.

## Решение, unknowns и передача дальше

Для task-01 сохранить текущий режим как измеренный контроль, idle unload как
условную альтернативу и обе shared-модели как кандидаты с открытой жизнеспособностью.
Чистый proxy требует bootstrap seam до MSBuild/Roslyn/plugins, а session state
требует отделения от workspace во всех shared-моделях; карта task-00 остаётся полезной.

Для task-04 не принимать гипотезу «достаточно detached child/ignore EOF»:
у измеренного Codex пути Job не позволяет такой lifetime. Это не доказательство
невозможности любых иных запусков вне Job. Их отдельный механизм и допустимость
дополнительных действий пользователя требуют выбора владельца и проверки.
У ролей остаётся возможность разделять память, пока host жив, но восстановление
через ordinary load не равно сохранению workspace после Reload.

| Unknown | Причина | Получатель |
|---|---|---|
| Desktop close/crash; сигнал и дети при quit; Reload других harness | Для quit есть лишь ручной before/after MCP; Reload Codex production недоступен | Этот task-05 |
| MCP на чат/окно; обобщение subagent | Для subagent есть лишь n=2; чаты/окна не наблюдались | Этот task-05 |
| Состав прежних 20+ MCP | Нет исторических per-PID memory снимков | Task-05 при повторении ситуации |
| Автозапуск host вне Job | Стандартные detach/breakaway не проходят | Task-04 после решения владельца |
| Реальная экономия и конкуренция клиентов | Shared-код ещё отсутствует | Task-02 после design |
| Память/latency чистого proxy и demotion | Нет такого процесса | Task-04 / task-02 |

В [README темы](../../README.md) записано **решение владельца pending**,
не выбор автора отчёта. Task-04 и зависимые design/activation автоматически не запускаются.

## Validation

- Release build solution и main CI filter
  `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`: **1432 passed,
  0 failed, 0 skipped** на финальной production-базе. Финальная сверка probe ниже
  сохраняется вместе с его исходниками; SourceStructure включён в main suite.
- Windows и WSL Release build probe: 0 warnings/errors; lifecycle self-check
  различает EOF/kill, проверяет resources/prompts/Method not found и cleanup.
  Codex wrapper проверяет handshake и живые процессы на readiness barrier.
- GC: 6/6 измерительных процессов, 30/30 memory точек, 5 проектов в каждом
  load, семантический контроль; оба hook inventory подтвердили запрошенный GC.
  Cohort: 20/20 initialize, 16/16 load, 20/20 memory точек; созданные MCP закрыты.
- Дополнение с реальными subagent: **PASS наблюдения**, два агента completed,
  137 снимков, из них 61 в окне после completed длиной 121,288 с; PID и start time
  сохранены. Cleanup остановил только два созданных MCP, исходный worker сохранён.
  Изменены только docs/data; C# и probe не менялись, build/test повторно не требуются.
- Ручной выход/перезапуск desktop: **PASS наблюдения до/после**, старые MCP
  и backend отсутствуют, новые PID/start times сохранены; info до load подтвердил
  отсутствие workspace. EOF/kill, время завершения и ordinary/detached children
  для этого события не проверены. Дополнение docs/data; задача целиком не принята.
- Найденные ошибки самого стенда исправлены: размещение Linux-копии внутри
  исключённого spikes, проверка exited process через GetExitCodeProcess,
  поддержка discovery методов. Предварительные неуспешные запуски не объявлены
  результатами harness или TDD red. Первая solution build с копией в корневом
  artifacts падала; последующий main запуск дал 1417/1432. После удаления причины
  полная сборка и suite повторены успешно, production не исправлялся.
- AnalyzerLifecycle отдельно не запускался: production lifecycle и его тестовая
  конфигурация не менялись. Pinned-SDK категория исключена как в актуальном CI.
- Markdown ссылки/anchors, отсутствие production diff, diff whitespace и состав
  переносимого evidence: **PASS**; после desktop-дополнения проверены 144 локальные
  ссылки/anchors в task, отчёте и трёх индексах, пять новых JSON и их взаимные ссылки.
  Evidence self-check подтвердил 6 GC runs, 30 точек, 20 cohort PID,
  2 inventories и 7 lifecycle cases; JSON проверен на запрещённые поля.
  Desktop неизвестные выше
  остаются not-run; независимой приёмки задачи и эпохи нет.
