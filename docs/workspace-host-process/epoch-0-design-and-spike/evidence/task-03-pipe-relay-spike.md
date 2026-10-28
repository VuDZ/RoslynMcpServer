# C/E0/task-03 — Локальный транспорт и MCP-фасад

## Главное

- **Completed, 2026-10-28, standalone spike.** На Windows и WSL Linux прошли
  четыре сочетания двух моделей host и двух версий MCP: общий счётчик,
  изоляция сессий, progress, отмена, подписки и обрыв вызова.
- SDK 2.2 позволяет оставить MCP-состояние в фасаде и выполнять инструменты
  через другой SDK MCP-сеанс по named pipe. Собственный JSON-RPC router не нужен.
- В Windows proxy после работы занимает **19,9–21,9 MiB private / 53,3–55,6 MiB
  working set**, с Workstation GC; сборок Roslyn/MSBuild/production-сервисов нет.
  Это измеренный SDK-стенд, а не обещание размера production proxy.
  **Это столько же, сколько пустой сервер 1.5.18** из task-05: 18,4 / 61,0 MiB
  (Workstation), 23,3 / 59,8 (Server). Чистый proxy без Roslyn экономит единицы MiB
  на процесс; требование снято [решением владельца 2026-10-28](../../README.md#сравниваемые-модели-и-проверки-применимости-2026-10-23) ([ревью F-05](../../review-task-03-05-2026-10-28/result.md)).
- Windows `count`: p50 **0,39–0,45 мс** через фасад против **0,12–0,14 мс**
  напрямую. Это одна серия по 200 вызовов на вариант; сравнение моделей
  по такой разнице не принимается.
- **C03-01:** token-only отмена в первоначальном forwarding оставляла host
  работать. Явная доставка cancel с внутренним id исправила стенд. SDK 2.2 сам
  шлёт cancel, но fire-and-forget; место потери трассирует task-04.
- **C03-02:** короткое Unix-соединение могло оставить следующего владельца
  без listener. Исправлено сохранением следующего instance до обслуживания
  принятого; regression «владелец → nobody → владелец» проходит.
- Windows: owner-only DACL и отказ anonymous identity. Linux: наблюдён настоящий
  Unix socket, UID 65534 получает `EACCES` от прав файла сокета, UID 1000 сохраняет
  доступ; проверка peer UID в `CurrentUserOnly` на Linux не выполнялась ([F-06](#unknowns-и-передача-дальше)).
  macOS и реальный UI-harness не проверялись.
- **Рекомендация:** сохранить SDK-фасад и стандартный локальный транспорт
  как основу task-04 для обеих shared-моделей. Выбор модели владельцем остаётся
  pending; нулевой вариант и условия темы сохраняются в [README](../../README.md).

## База и способ

Исходная база — `378159fac6f2a9605e246eb9eb8b21ce34593de4`, production **1.5.18**.
Изменения — только `spikes/PipeRelay/` и документы этой задачи.
Проект не включён в solution, CI или publish; корневой csproj исключает `spikes/**`.
Исполнитель — Codex; точный model ID текущего запуска недоступен.
Независимое ревью — `claude-opus-5-5`, 2026-10-28, раунд 1; [результат](../../review-task-03-05-2026-10-28/result.md), [исправления](../../evidence/2026-10-28-task-03-05-review-fixes.md).

Дата — **2026-10-28**. Windows 11 / kernel build 26200, x64, 16 logical CPU;
SDK 10.0.402, runtime 10.0.13. Linux — WSL Ubuntu 24.04.4,
kernel `6.6.87.2-microsoft-standard-WSL2`, runtime 10.0.13.
В WSL выполнены те же framework-dependent DLL; отдельная Linux-сборка не заявлена.

Пакет **ModelContextProtocol 2.2.0** совпадает с основным проектом.
API проверены `explore_assembly`, `decompile_type` и `get_decompiled_method_body`
по установленному `ModelContextProtocol.Core.dll`; SHA-256 и сигнатуры —
[sdk-api.json](task-03-data/sdk-api.json). Использованы `McpServer.Create`,
`StreamServerTransport`, `StreamClientTransport`, `McpClient` и
`StdioClientTransport`. Все проверки запускают реальные процессы и SDK-клиенты.
Глобальные конфиги клиентов, полные environment и command lines в данные не включены.

## Что реализовано и проверено

[README стенда и команды](../../../../spikes/PipeRelay/README.md).
В модели ролей runner запускает `auto --local-host` с локальным MCP-клиентом
и подключает remote proxy к тому же счётчику. В модели отдельного host запускает
`host`, а оба stdio-клиента используют proxy. Автоматического выбора владельца нет.
`direct` — счётчик по stdio без IPC, транспортный контроль нулевых IPC-расходов.
Он не заменяет production baseline и сравнение памяти из task-05.

Каждый фасад имеет свою `McpServerPrimitiveCollection`, negotiated protocol
и подписки; `toggle` меняет только её. Общий `CounterState` создаётся только
для host/direct, а не чистого proxy. См.
[Program](../../../../spikes/PipeRelay/Program.cs:7) и
[Facade](../../../../spikes/PipeRelay/Facade.cs:17).

Внутреннее подключение имеет отдельную SDK MCP-сессию `2025-11-25`.
Фасад принимает `2025-11-25` и `2026-07-28`; во втором случае SDK использует
discovery/per-request metadata, а list changes доставляются после `subscriptions/listen`.
Внешние id/progress tokens заменяются внутренними UUID, уведомления связываются
с ожидающим вызовом. Generation фиксирован на соединение; смена host в task-03 отсутствует.
[Пересылка](../../../../spikes/PipeRelay/Facade.cs:87).

### Канал и границы сообщений

- `NamedPipeServerStream` / `NamedPipeClientStream`, byte mode, asynchronous,
  `PipeOptions.CurrentUserOnly` с обеих сторон.
- До MCP: четыре байта длины little-endian и UTF-8 JSON длиной **1–4096 байт**.
  Version=1, build identity, тестовый workspace key (имя канала), harness id,
  generation. Клиент посылает пустую generation, host — свой GUID;
  ответ подтверждает key/identity/harness id. Harness id здесь тестовый `spike-<PID>`.
- Identity — SHA-256 упорядоченных имён и SHA-256 содержимого apphost и всех DLL
  каталога. Совместимость production-конфигураций/плагинов остаётся U-C-01.
  Изменённая DLL реально запущена в другом каталоге: её stdio-сеанс инициализируется,
  но host отвергает подключение до исполнения `count`.
- После handshake: SDK NDJSON, предел **65536 байт без LF** на внутреннюю строку
  в обе стороны. Handshake/connect — **5 с**; forwarding — **15 с**,
  отдельный бюджет явной отправки отмены — **2 с**. Страховочный TTL процесса —
  **3 минуты**; это не production idle policy и не отдельный idle deadline каждого чтения.

Источники: [identity](../../../../spikes/PipeRelay/BuildIdentity.cs:10),
[handshake](../../../../spikes/PipeRelay/Transport/LocalHandshake.cs:9),
[лимит строки](../../../../spikes/PipeRelay/Transport/BoundedStream.cs:62),
[listener](../../../../spikes/PipeRelay/CounterHost.cs:10).

### Матрица результата

Каждое сочетание — отдельный host и два клиента, один финальный прогон.

| ОС | Внешний протокол | Модель ролей | Отдельный host |
|---|---|---|---|
| Windows | 2025-11-25 | PASS | PASS |
| Windows | 2026-07-28 | PASS | PASS |
| WSL Linux | 2025-11-25 | PASS | PASS |
| WSL Linux | 2026-07-28 | PASS | PASS |

Во всех восьми случаях проверены:

- общий `count`, совпадающие host PID/generation;
- одинаковый request id **777** у разных клиентов с разными результатами `count`/`slow`;
- id **888** и progress token `same` в двух параллельных `slow`: один отменяется,
  другой завершает 20 progress reports; на host ровно одна отмена и ноль активных операций;
- локальная видимость `extra`, отсутствие чужих list changes; для July до listen
  проверено отсутствие уведомлений в окне 100 мс, после listen — положительная доставка;
- пять отказов handshake, oversized NDJSON и изменённая executable DLL;
  после отказов нормальный клиент продолжает работу;
- kill host во время `slow`: ожидающий proxy-вызов получает явную ошибку в пределах
  deadline 3 с; `tools/list` отвечает, следующий `count` возвращает ошибку,
  stdio-сеанс proxy жив. Recovery/replay не выполняются.

Поле `disconnect.elapsedMilliseconds` включает kill, получение ошибки, list
и следующий отказ; его нельзя выдавать за точную задержку завершения процесса.
Убитая локальная MCP-сессия совмещённого host не считается выжившей.
Проверки: [SessionChecks](../../../../spikes/PipeRelay/Checks/SessionChecks.cs:11).
Данные: [Windows](task-03-data/windows.json), [Linux](task-03-data/linux.json).

Windows ACL: успешный доступ владельца, чтение DACL с allow только owner SID,
реальная impersonation anonymous SID и `UnauthorizedAccessException`, восстановление
исходной identity. Второй именованный interactive logon не запускался.
[WindowsAccessCheck](../../../../spikes/PipeRelay/Checks/WindowsAccessCheck.cs:11).
Linux: реальный socket `/tmp/CoreFxPipe_<key>`, mode `0755`, owner UID 1000;
UID 65534 не может выполнить handshake (`PermissionError`, errno 13), затем
UID 1000 снова выполняет handshake. Отказ приходит из `connect()` по правам файла
сокета (`0755` при umask 022), до сервера. Проверка peer UID в .NET
(`NamedPipeServerStream.HandleAcceptedSocket` → `GetPeerID` при `CurrentUserOnly`)
и ветка `CounterHost.cs:262` на Linux не выполнялись; при umask 000 защита держится
только на ней ([ревью F-06](../../review-task-03-05-2026-10-28/result.md)). Root используется только тестовым driver для
выбора существующих UID и cleanup своих объектов.
[Скрипт](../../../../spikes/PipeRelay/check_unix_access.py:15),
[linux-access.json](task-03-data/linux-access.json).

## Измерения

Одна серия на вариант: 10 warmup и **200 последовательных успешных `count`**,
`Stopwatch.GetElapsedTime`, p50/p95 по nearest rank. Windows и Linux измерены
отдельно; контроль напрямую и фасад используют один SDK и тот же счётчик.
Это небольшой текущий workload, не оценка задержек Roslyn или нагрузочный SLA.

| ОС / протокол | Direct p50 / p95, мс | Роли, proxy p50 / p95, мс | Отдельный host, proxy p50 / p95, мс |
|---|---:|---:|---:|
| Windows / 2025-11-25 | 0,1444 / 0,2347 | 0,4475 / 0,5307 | 0,3904 / 0,4617 |
| Windows / 2026-07-28 | 0,1240 / 0,2109 | 0,4081 / 0,4938 | 0,4144 / 0,4896 |
| WSL / 2025-11-25 | 0,4156 / 0,4789 | 0,9404 / 1,0197 | 0,9119 / 0,9819 |
| WSL / 2026-07-28 | 0,3743 / 0,4151 | 0,9588 / 1,0576 | 0,9059 / 0,9883 |

Всего 12 серий / 2400 измеренных вызовов; исходные samples сохранены в JSON.
Разницу между моделями в одной короткой серии нельзя интерпретировать как их
устойчивое преимущество. Вариант B не использовался, экономия production RAM не измерялась.

Память — один snapshot каждого proxy и host после прогрева и отрицательных проверок,
до kill, без принудительного GC. `Process.Refresh` перед чтением
`PrivateMemorySize64` / `WorkingSet64`; все proxy подтвердили `IsServerGC=false`.
Windows private ниже — private bytes API .NET; Linux-счётчик .NET имеет другую
семантику и не приравнивается к Windows `PrivateUsage`. Диапазоны по четырём случаям,
MiB=1048576 байт; полные per-PID значения и assembly inventory сохранены в JSON.

| ОС | Proxy PrivateMemorySize64 / WS, MiB | Host PrivateMemorySize64 / WS, MiB |
|---|---:|---:|
| Windows | 19,9–21,9 / 53,3–55,6 | 18,7–21,2 / 50,3–54,3 |
| WSL Linux | 155,9–164,0 / 72,6–75,8 | 144,2–160,9 / 69,9–72,0 |

Тестовый exe содержит и runner; inventory/`toggle` — часть измеренного стенда.
Это не нижняя граница всех возможных proxy. Отсутствие Roslyn/MSBuild проверено
runtime inventory и отсутствием их project/package references; DI production не используется.

**Сравнение с пустым сервером** ([ревью F-05](../../review-task-03-05-2026-10-28/result.md)). Пустой 1.5.18 в task-05
на той же Windows: 18,4 MiB private / 61,0 MiB WS с Workstation GC и 23,3 / 59,8 с Server GC.
Proxy стенда — 19,9–21,9 / 53,3–55,6. Сборки Roslyn — mapped images, общие между
процессами, и приватной памяти почти не добавляют. Отказ от MSBuild/Roslyn/plugins
в proxy экономит единицы MiB на процесс. Оговорки: plugins=none, моменты замера разные
(пустой через 10 с, proxy после нагрузки). Режим GC фиксируется при старте процесса:
в модели ролей proxy, ставший host, сохраняет свой GC, поэтому там нужен Server GC
во всех процессах (около +5 MiB private на proxy); отдельный host может их смешивать.

## TDD и найденные проблемы

Release build исходного стенда прошёл. До forwarding два процесса держали
независимые счётчики. `dotnet .../PipeRelay.dll verify all` вернул **1** с тремя
ожидаемыми отказами: общий счётчик, отмена в общем host и ошибка после kill.
После реализации базовые три проверки прошли, затем runner расширен до финальной матрицы.
[TDD, промежуточные отказы и regressions](task-03-data/tdd.json).
Sandbox `Access denied` и ошибка shell quoting до запуска WSL не являются TDD red.

<a id="c03-01"></a>
**C03-01 — доставка отмены.** В первоначальной связке SDK после отмены ожидания
host сообщал `cancelled=0, active=1`; наблюдение входящего явного cancel подтвердило
отмену tracker фасада, но не завершение host-операции. Исправление spike — отдельный
внутренний request id и ожидаемая отправка `notifications/cancelled` перед выходом
из forwarding. [Код](../../../../spikes/PipeRelay/Facade.cs:118),
[regression](../../../../spikes/PipeRelay/Checks/SessionChecks.cs:92).
Статус: spike fixed; production-дефект репозитория не объявлен. По ревью
([F-08](../../review-task-03-05-2026-10-28/result.md)): SDK 2.2 сам отправляет `notifications/cancelled` при отмене токена
(`McpSessionHandler.RegisterCancellation`), но не ожидает отправку, и её ошибки
не видны; точное место потери не трассировано. Получатель дальнейшей проверки —
task-04 (трассировка внутреннего канала при отмене); task-01 требует явной доставки отмены. Общая отмена не сводится
к отмене локального `Task`/ожидания.

<a id="c03-02"></a>
**C03-02 — срок жизни Unix listener.** Контроль с UID обнаружил: owner accepted,
nobody denied, следующий owner получил connection reset. Принятое короткое
соединение могло завершиться до создания следующего server instance и освободить
общий Unix listener. Следующий instance теперь создаётся раньше обслуживания
принятого. Это объяснение по lifetime instance; отдельной трассировки runtime не было.
[Исправление](../../../../spikes/PipeRelay/CounterHost.cs:38),
[regression](../../../../spikes/PipeRelay/check_unix_access.py:15).
Статус: fixed в spike; владелец — исполнитель task-03. Проверка different UID
не ослаблялась: все три стадии обязательны.

## Unknowns и передача дальше

| Вопрос | Почему не решён | Получатель |
|---|---|---|
| Автозапуск, владение ключом, handoff, поколения при recovery | Здесь fixed host, роль задаёт runner | Task-04 — для выделенного host проверено ([результат](task-04-routing-and-recovery-spike.md)); handoff отложен вместе с моделью ролей |
| Удержание host после EOF / действия harness | Транспортный spike не определяет lifecycle policy; ограничения task-05 сохранены | Task-04 — host выходит сам после ухода всех proxy ([результат](task-04-routing-and-recovery-spike.md)); политика — task-01 |
| Полная production identity, plugins и размещение этапов tools | Проверены только три fake-инструмента и build identity spike | Task-01 по task-00 |
| Где теряется fire-and-forget отмена SDK (C03-01) | SDK шлёт cancel без ожидания; в task-04 автоматическая отмена до host не дошла ни разу (C04-04), место потери не трассировано | Task-01: явная доставка отмены с ожиданием |
| Путь Unix socket от `TMPDIR` и лимит его длины (ревью F-07) | `PipeStream.GetPipePath` = `Path.GetTempPath()` + `CoreFxPipe_` + имя; разный `TMPDIR` разводит процессы, путь ≤ 108 байт на Linux / 104 на macOS; абсолютное имя поддерживается | Task-04 — закрыто: канал и lock под одним явным root, `TMPDIR` не участвует ([результат](task-04-routing-and-recovery-spike.md)); выбор root — task-01 |
| Проверка peer UID `CurrentUserOnly` на Linux (ревью F-06) | Отказ получен правами файла сокета | Task-04 — закрыто: при сокете `0777` чужой UID отвергнут peer-проверкой ([результат](task-04-routing-and-recovery-spike.md)) |
| Реальные Roslyn RAM, свежесть, analyzer lifecycle, нагрузка | Roslyn отсутствует | Task-02 после design |
| macOS / реальный UI-harness | Среда macOS отсутствует; обязательный контроль — SDK-клиент | Непроверено; не pass |

Значения стенда не переносятся в production ([ревью F-09](../../review-task-03-05-2026-10-28/result.md)): deadline пересылки
15 с (load, build и test идут минуты; нужен бюджет по инструменту и liveness по
progress), `Lazy<Task<McpClient>>` навсегда запоминает неудачное подключение (proxy,
стартовавший раньше host, не подключится), TTL процесса 3 минуты. Task-04 их заменяет.

Состояние фасада необходимо отделять от workspace в обеих shared-моделях.
Task-03 не выбрал ни роли, ни отдельный host: его результаты подходят обеим.
Task-04 получает рабочую основу и два учтённых ограничения; его старт по-прежнему
требует явного выбора проверяемых моделей владельцем. Зависимости задач не изменены.

## Validation и учёт

- Release build spike через MCP: **PASS, 0 warnings/errors**.
- Windows full runner: **4/4**, WSL full runner: **4/4**, разные UID: **PASS**.
  Cleanup fixture проверяет выход своих host; независимый последующий inventory
  не нашёл PipeRelay-процессов: [Windows](task-03-data/windows-cleanup.json),
  [Linux](task-03-data/linux-cleanup.json).
- [Release build solution](task-03-data/solution-build.txt): **PASS**.
  [Main suite](task-03-data/main-suite.txt),
  `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`: **1432 passed,
  0 failed, 0 skipped**. SourceStructure включён в main suite.
- AnalyzerLifecycle отдельно не запускался: production lifecycle и его
  конфигурация не менялись. Production C# не менялся; version/CHANGELOG не менялись.
- Самопроверка: **154** локальные ссылки/anchors, восемь fresh generation,
  **2400** samples, identity обеих финальных сборок, privacy evidence и согласованность
  статусов/Depends on — **PASS**; `git diff --check` — **PASS**.
  Независимое ревью — [раунд 1](../../review-task-03-05-2026-10-28/result.md); приёмка эпохи не объявлена.
