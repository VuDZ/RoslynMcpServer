# Task-04 — маршрутизация и восстановление выделенного host

## Главное

- Статус: **completed, 2026-10-28**. Принят standalone spike выделенного host без Roslyn: Windows/WSL, две версии MCP; независимое ревью — 3 раунда: 4 исправления подтверждены в раундах 1–2, 7 замечаний раунда 3 закрыты в документах.
- Проверяется выделенный host. Роли, promotion/demotion и handoff отложены [решением владельца](../../README.md#сравниваемые-модели-и-проверки-применимости-2026-10-23).
- Два OS file lock подходят для проверяемого выделенного host. Исправлены реальные сбои cooldown при сдвиге UTC и ожидания освобождаемого lease при closing; бюджеты теперь монотонны.
  На Linux `FileStream.Lock` — POSIX `fcntl` lock процесса: закрытие любого другого дескриптора того же файла в процессе снимает его молча. Spike открывает каждый lock-файл в процессе один раз; production и модель ролей должны держать это правило явно ([ревью раунда 3](../../review-task-04-round-3-2026-10-28/result.md), F-04).
- Идентичные proxy присоединяются к одному fake load. Привязка меняется после успешной загрузки B; выполняющийся A-вызов и оставшийся на A proxy сохраняют прежнее поколение. Отмена всех waiter подтверждается на backend, а не числом кадров.
- **Ответ выполняющегося A-вызова задерживается до конца загрузки B.** Switch держит gate сессии на время load B, а завершение вызова ждёт тот же gate; проба ревью: вызов 1 с ответил через 6,2 с. Проверка серии переключается только на готовый B и этого не видит. Это дефект стенда, не подхода: исправление — load B вне gate; передано task-01 ([ревью раунда 3](../../review-task-04-round-3-2026-10-28/result.md), F-01).
- **Отказ общей загрузки «залипает» на поколение:** повтор возможен только после отключения всех клиентов. Для реальной работы это неприемлемо; правило явного retry — решение task-01 ([ревью раунда 3](../../review-task-04-round-3-2026-10-28/result.md), F-05).
- Прерванный вызов возвращает неопределённый исход без replay. Следующий явный вызов восстанавливает подключение; старое соединение не обслуживает новое поколение.
- Найдены ограничения SDK 2.2: автоматическая отмена не наблюдалась в канале, а SDK stdio cleanup может убить дерево вместо прямого EOF. Runner отправляет EOF непосредственно; отмена доставляется явно и с ожиданием.
- Linux-контроль сокета `0777` подтвердил peer UID: UID 1000 принят, UID 65534 отвергнут, затем UID 1000 снова принят.
- В Codex app-server после смерти MCP при живом backend оба тестовых ребёнка остались живы. Поэтому host самостоятельно следит за наличием подключений.
- Shadow host работает из проверенной копии. **Обновление publish при живом proxy из этого же publish на Windows не доказано:** обычный proxy удерживает DLL. Это вопрос bootstrap для task-01.
- Roslyn, реальные load/build/test, macOS и интеграция с production сюда не входят. Следующий шаг — task-01, затем task-02.

## База и воспроизведение

База: `07224d1`, production 1.5.18; результаты относятся к рабочему diff task-04 поверх этой базы.
Изменяются только `spikes/PipeRelay`, один probe в `spikes/HostSurvival` и документация.
SDK ModelContextProtocol 2.2.0, .NET 10. Windows и WSL Linux — отдельные запуски.
Идентификаторы сборки и runtime будут сохранены в raw JSON финальных серий.

```powershell
dotnet build spikes/PipeRelay/PipeRelay.csproj -c Release
dotnet spikes/PipeRelay/bin/Release/net10.0/PipeRelay.dll routing-checks all
```

`quick` выполняет по одному холодному старту/recovery вместо десяти.
`verify recovery-red` воспроизводит исходный дефект фиксированного фасада task-03:
после убийства host следующий вызов остаётся ошибкой. Ожидаемый exit code этого
контроля — 1; это поведенческий red, не ошибка сборки.

Для Linux копируются только top-level DLL/JSON в
`spikes/PipeRelay/artifacts/linux-bin/`, затем запускается тот же runner:
`/usr/bin/dotnet /absolute/path/PipeRelay.dll routing-checks all`.
На Windows положительные `CurrentUserOnly` проверки выполняются обычным
пользовательским токеном, поскольку sandbox меняет token.

## Проверяемый механизм

### Discovery и запуск

[RouteFiles](../../../../spikes/PipeRelay/Routing/RouteFiles.cs) строит ключ SHA-256
из пользователя, identity сборки, абсолютного rendezvous root, стартового контекста
и эффективного fake descriptor. Chat/PID в ключ не входят. Root — явно заданный
доверенный каталог fixture; его выбор и проверка владельца для production остаются task-01.
Unix root имеет mode 0700. Канал и lock лежат под одним root, `TMPDIR` не участвует.
Unix используется абсолютный pipe path с ограничением 103 UTF-8 байта; Windows —
имя named pipe. macOS явно отвергается: `FileStream.Lock` там не поддерживается.

[HostDiscovery](../../../../spikes/PipeRelay/Routing/HostDiscovery.cs) сначала проверяет
живой канал. Readiness-файл является наблюдением runner, а не источником готовности.
Под `launch.lock` кандидат проверяет `host.lock`, готовит образ и запускает ребёнка.
Launch lock удерживается до проверенного handshake/MCP подключения.
Ребёнок получает `host.lock` до `initialization` и держит его до выхода.
При смерти launcher другой proxy может стать инициатором, но живой host lease
запрещает вторую инициализацию. При гонке детей только владелец lease начинает работу.
ОС освобождает locks после смерти процесса; lock-файлы не удаляются и не заменяются.

Handshake ограничен 4096 байт; подтверждает binary identity, полный ключ, descriptor
и ненулевое поколение. Клиент не выбирает поколение. Каждое соединение привязано
к своему `RouteConnection`; request/progress IDs создаются заново внутри соединения.
Ошибка или закрытие старого транспорта не переводит его сообщения в новое соединение.

### Загрузка, смена привязки и replay

[FakeWorkspace](../../../../spikes/PipeRelay/Routing/FakeWorkspace.cs) хранит один
Task загрузки на поколение, включая отказ. Отмена waiter отменяет его ожидание,
но не общий Task. **При отмене всех waiter, пока proxy подключены, load продолжается.**
Если все proxy отключились, host прекращает работу после grace; lifetime отменяет load.
Неуспешный load остаётся общим результатом текущего поколения: автоматического
повтора и очереди из десяти load нет. Новая попытка требует завершить это поколение
(отключить клиентов) либо выбрать другой descriptor/context.

`load-waiter` и `load-waiter-ended` дают уникальный backend waiter ID,
число активных ожиданий и накопленное число фактических отмен. Single-cancel
проверяет одну отмену при девяти оставшихся waiter. All-cancel до открытия barrier
дожидается нуля активных waiter и двух отмен; третий waiter входит в ещё не
завершённую попытку, и только затем runner разрешает общий load.

[RoutingSession](../../../../spikes/PipeRelay/Routing/RoutingSession.cs) сохраняет
MCP-фасад и его tool collection. Switch держит gate на получение нового
соединения, load и commit привязки. Учёт завершения уже принятых вызовов ждёт тот же
gate, поэтому их ответы задерживаются до конца switch (F-01 ревью раунда 3); новые вызовы
чата на время switch ждут. Отмена клиентом switch возвращается как `IsError`, а не как отмена. Старое соединение сохраняется для уже принятых
вызовов; новый вызов после commit работает на B. Неудачный B не изменяет A.
Переключение инициатора выделенного A не требует handoff и не перезагружает A.
В проверке остаётся третий активный proxy A: после ухода инициатора он получает
прежние generation/PID и следующий counter 5; на ключе A по-прежнему один
owner/initialization/load. Активный `extra` и подписка проверяются после recovery.

Выполняющийся tool request не повторяется. При потере транспорта возвращается
`IsError` с неопределённым исходом. Следующий явный вызов может получить новое
соединение до начала исполнения. Это внутренний recovery без нового external initialize.
`effect` пишет наблюдаемый эффект и ждёт barrier ответа; kill в этой точке должен
оставить ровно одну запись и ошибку клиенту.

### Бюджеты

- Bootstrap/connect: общий deadline 8 с. Окно одной попытки 250 мс охватывает pipe connect, handshake и MCP initialize, поэтому `InitializationTimeout` 3 с фактически не действует (уточнено ревью раунда 3, F-02). Под конкуренцией proxy бросают подключение и пробуют снова: в первом поколении cold start 10–20 `connected` на 10 proxy в этих raw, 31–56 на нативном Linux рецензента. Значение 250 мс — стенда, в production не переносится.
- Один запуск на явную попытку; нет автоперезапуска ребёнка внутри неё. Минимальный интервал между запусками — 2 с: `next-launch` пишется при каждом запуске и повторно после отказа; чистый exit его снимает. Kill host в первые 2 с после старта даёт немедленную ошибку cooldown вместо recovery; проверки recovery ждут 2,1 с до вызова и этот случай не покрывают (F-03). Текст ошибки «Outcome may be unknown» выдаётся и для запроса, не отправленного в host.
- При конкуренции proxy проверяют тот же бюджет под launch lock; ждать бесконечно и fallback в локальный workspace нельзя.
- Запрос, начатый до зарегистрированного startup failure, получает этот отказ даже после истечения cooldown. Новую попытку инициирует только новый вызов после cooldown; ожидающие запросы не образуют очередь запусков.
- Fake load: 30 с; обычный forwarded tool: 2 минуты. Старый общий предел 15 с и TTL процесса 3 минуты в режиме routing отсутствуют.
- Доставка отмены: отдельный deadline 2 с; transport completion прерывает ожидание ответа независимо от tool deadline.
- После последнего валидного подключения: grace 1 с; до первого подключения host ждёт до 8 с. Новое подключение в grace отменяет выход.
- Handshake: 3 с, preface 4096 байт, NDJSON 65536 байт; fixture deadline 90 с, закрытие proxy 5 с.
- External MCP discovery runner: initialization 30 с без отдельного короткого discover probe. Это устраняет ошибочную диагностику «unsupported version» при задержке запуска под нагрузкой.

Это бюджеты лёгкого контроля; реальные Roslyn/build/test бюджеты определяют task-01/task-02.

### Lifetime и публикация

[DedicatedHost](../../../../spikes/PipeRelay/Routing/DedicatedHost.cs) считает реальные
соединения. Admission и решение об exit синхронизированы одним gate; host lease
освобождается после закрытия listener/connections. На Linux запуск через `setsid`
не освобождает host от обязанности самостоятельно выйти после EOF/SIGKILL всех proxy.
Windows breakaway и обход Job не применяются.

Для exit race runner управляет двумя отдельными барьерами. До атомарного closing
новое admission сохраняет поколение. После closing входящий handshake отвергается,
пока прежний host всё ещё держит lease; runner непосредственно проверяет занятый
OS lock и отсутствие второй initialization. Затем разрешается выход, и тот же
запрос proxy получает новое поколение в пределах общего bootstrap deadline.
Барьерный таймаут 8 с — страховка только тестового interleaving, не idle unload.

[ShadowImage](../../../../spikes/PipeRelay/Routing/ShadowImage.cs) копирует bundle
в уникальный staging, сверяет identity и файлы, затем публикует каталог rename.
Partial image не запускается. Host исполняется из image, исходный publish не нужен
для текущего поколения. Runner отдельно проверяет изменение исходной DLL после
закрытия proxy, который был запущен из этой копии, пока другой proxy продолжает
работать с тем же shadow host. Это **не** обещание обновить все DLL при живых
обычных proxy из того же Windows publish: bootstrap этих proxy — получатель task-01.

## Матрица

Полная серия прошла на Windows 10.0.26200 и Ubuntu 24.04.4 WSL, runtime 10.0.13:
на каждой ОС 32 записи сценариев, 20 cold rounds и 20 recovery rounds
(по 10 на MCP 2025-11-25 и 2026-07-28). Каждый cold/recovery использует 10 proxy.
В каждой наблюдаемой фазе одного ключа один owner/initialization/load;
после kill — ровно одно новое поколение. Максимум load-start на поколение — 1.
Во всей серии каждой ОС 76 owner/initialization, 72 load-start, 4 ожидаемых load-failure,
2 lease-rejected и **0 unexpected fatal**. Разные ключи допускают независимые host.
Runner завершился с кодом 0 после проверок cleanup каждого fixture.
[Финальная проверка процессов](task-04-data/cleanup-final.json): Windows spike процессов 0;
Linux дети проверены по PID/birth stamp в каждом fixture, peer-UID host завершён.

Raw: [Windows](task-04-data/windows.json), [Linux](task-04-data/linux.json),
[пересчёт](task-04-data/summary.json). Файлы содержат binary identity, полный ключ,
descriptor, поколения, PID/birth stamp, UTC и uptimeMs. UTC используется как дата;
порядок и бюджеты опираются на монотонное время. Проверки реализованы в
[RoutingChecks](../../../../spikes/PipeRelay/Checks/RoutingChecks.cs):

- 10 cold proxy, один owner/initialization/load; kill готового host и 10 concurrent recovery; по 10 повторов на протокол.
- A → готовый B, A → новый C; выполняющийся A завершается на A; отказ нового B сохраняет живую привязку; инициатор A переключается без handoff.
- Общий load success/failure, отмена одного и всех waiter, наблюдаемый эффект перед смертью host без replay.
- Смерть launcher и host до ready, crash loop с общим cooldown; разные descriptor дают независимые host.
- Activation и list_changed/subscriptions на MCP 2025-11-25 и 2026-07-28 после switch/recovery; progress/cancel без external initialize.
- Stale discovery, несовместимый descriptor, разные TMPDIR, вызов 16 с, partial shadow image, исходный publish.
- Прямой EOF инициатора, остальные proxy, reconnect в grace, exit после последнего proxy и SIGKILL; cleanup по recorded PID и устойчивому birth stamp.
- Ролевые handoff/release, promotion/demotion и отсутствие подходящего ролевого кандидата — N/A по решению владельца.

## Контрпримеры и дефекты

- **C04-01, исправлено:** отдельные root fixture имели разные locks, но одинаковое имя Windows pipe. Root включён в стартовый namespace ключа; независимые прогоны больше не делят канал.
- **C04-02, защитный контракт:** ожидание ответа явно состязается с `Client.Completion`; recovery и effect-kill проверяют bounded error. Причина ранней задержки отдельно не изолировалась, поэтому это не объявляется доказанной ошибкой SDK.
- **C04-03, исправлено:** MCP 2026 subscription удерживал фасад после EOF. EOF теперь отменяет lifetime серверного протокола; regression — закрытие подписанного фасада в Switching.
- **C04-04, зафиксировано для task-01:** SDK 2.2 регистрирует автоматическую отмену fire-and-forget; она не наблюдалась на host. Explicit awaited notification обеспечивает доставку. Runner сохраняет trace; требование надёжной отмены нельзя заменить отменой локального wait.
- **C04-05, ограничение harness, task-01:** SDK StdioClientSessionTransport cleanup ждёт процесс до закрытия stdin и затем может убить дерево. Runner использует Process + StreamClientTransport и непосредственный EOF. Принудительное завершение дерева остаётся crash/recovery, не обычным disconnect.
- **C04-06, исправлено в runner:** Linux Process.StartTime не является устойчивым межпроцессным stamp. PID guard использует starttime из /proc/PID/stat; Windows использует StartTime ticks.
- **C04-07, исправлено в runner:** чтение событий конкурировало с append на Windows. Reader допускает FileShare.ReadWrite и читает только завершённые JSONL записи.
- **C04-08, исправлено в runner:** утверждение о двух отменах опережало доставку уведомлений; добавлено ожидание наблюдаемых cancel frames. Отдельный короткий SDK discovery probe под нагрузкой давал ложное «unsupported version»; runner использует единый initialization deadline 30 с. Это настройка тестового harness, а не изменение версии протокола.
- **C04-09, исправлено в runner:** выбор последнего owner из несортированных событий мог убить host другого ключа; deadline кандидата маскировал ошибку проверки. События теперь несут полный key/descriptor, victim выбирается по ключу; unexpected fatal отвергает startup серию. Старые raw с этим дефектом не являются финальной validation.
- **C04-10, исправлено:** ожидающий запрос мог запустить следующего кандидата после cooldown общего отказа. Admission timestamp сравнивается с failure timestamp под launch lock; crash-loop regression не допускает второй launch в той же группе ожидающих. Отдельный barrier проверяет смерть launcher до lease, проигравшего ребёнка и ожидание оставшегося владельца.
- **C04-11, исправлено в runner:** старт запроса на proxy ещё не означает admission load waiter на host. Перед отменой runner дожидается наблюдаемых load-waiter для всех участников и отправляет cancel с известным request ID; прежняя проверка могла отменить запрос до создания backend call.
- **C04-12, исправлено:** UTC в WSL сдвинулось назад во время startup. После Task.Delay(2100) старый UTC cooldown ещё отвергал следующий вызов. [Наблюдаемый red](task-04-data/linux-clock-red.json) сохранён целиком; первый более ранний Linux cold-recovery отказ не имел raw и не считается отдельным доказательством причины. [Детерминированный red stale/future budget](task-04-data/future-budget-red.json) также завершился кодом 1. Бюджеты и admission/failure используют Environment.TickCount64; недопустимые future/повреждённые записи не блокируют ключ навсегда. Полные Windows/Linux серии после исправления прошли.
- **C04-13, исправлено после F-02 ревью:** при подключении во время closing proxy получал launch lock, видел ещё занятый host lease и затем не проверял его освобождение. [Управляемый red](task-04-data/closing-race-red.json) завершился ошибкой bootstrap deadline без нового owner. Теперь до единственного spawn повторно проверяется host lease под тем же launch lock; чистый exit снимает startup cooldown. Regression удерживает closing host на barrier, проверяет невозможность второго owner и затем успешный внутренний переход после release.

Исходный red task-03 получен до механизма recovery. Последующие red выявлялись
при последовательном расширении process runner; ошибки компиляции и abort
не считаются поведенческими red. Это не набор заранее записанных red для каждого
пункта: приведены фактически наблюдённые контрпримеры.

## Дополнительные наблюдения F-02/F-06

[check_unix_access.py](../../../../spikes/PipeRelay/check_unix_access.py) с флагом
`--world-socket` chmod-ит реальный socket в 0777; отказ UID 65534 — ConnectionResetError,
а не filesystem EACCES. Host сохраняет работоспособность для владельца.

[codex_lifecycle.py](../../../../spikes/HostSurvival/codex_lifecycle.py) с
`--event mcp-kill` запускает только app-server discovery с probe, без thread/model call.
Убивается только recorded MCP; backend остаётся жив, ordinary и detached child
живы через 2 с. Затем собственные процессы probe завершаются. Наблюдение локально
для этой версии Codex и Windows; оно не переносится на другие harness.

## Что получает task-01

Известный механизм OS locks подходит для Windows/Linux лёгкого контроля.
Нулевой вариант сохраняет независимые ordinary workspace и не требует IPC/recovery;
простое улучшение ordinary через reset/GC рассматривается отдельно по task-05.
Этот spike не измеряет выигрыш RAM и не отменяет нулевой вариант.

[Task-01](../task-01-architecture-decision-hi.md), владелец — исполнитель этой задачи,
должен определить canonical user identity/root/descriptor, права каталога,
bootstrap обычного proxy на Windows, cleanup старых immutable images, retry/reset
после shared load failure, приёмлемые бюджеты и обработку harness tree kill.
Из ревью раунда 3 ([ревью раунда 3](../../review-task-04-round-3-2026-10-28/result.md)): switch без удержания
ответов текущего workspace (F-01); раздельные бюджеты connect/handshake/initialize
(F-02); cooldown только после отказа и различение «не отправлено»/«исход неизвестен» (F-03);
один дескриптор lock на процесс и ключ на Linux (F-04); явный retry отказавшей
загрузки (F-05); identity по всем файлам образа и пересоздание повреждённого образа (F-08).
Для macOS нужен другой проверенный OS primitive; местной среды macOS нет.
Совмещение ролей и idle workspace unload остаются отложенными.
Task-02 проверяет настоящий Roslyn, содержимое workspace и реальные build/test пути.

## Validation и ревью

Финальная validation после исправлений: Release solution build passed; main suite
`Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk` — 1432 total/passed, 0 failed;
[полный результат](task-04-data/validation-final.json).
Production не менялся; SourceStructure входят в main suite, AnalyzerLifecycle
для standalone spike не требуется. Полные route серии прошли на обеих ОС;
новый lifecycle дополнительно независимо повторён рецензентом на Windows.
Дополнительный [regression task-03](task-04-data/task-03-regression-windows.json)
прошёл на срезе до review fixes; общие transport/handshake файлы после него не менялись.
Это шесть записей (включая direct), четыре host/protocol комбинации, failures=0.
Peer-UID [контроль 0777](task-04-data/linux-access-0777.json) повторён на финальной Linux identity.
[Codex MCP-only kill](task-04-data/codex-mcp-kill.json): версия 0.164.0-alpha.11.1, cleanupComplete=true.
Полные raw до исправлений ревью сохранены отдельно: [Windows round 1](task-04-data/review-round-1/windows.json),
[Linux round 1](task-04-data/review-round-1/linux.json), [summary round 1](task-04-data/review-round-1/summary.json).
Эти identity/счётчики относятся к срезу первого ревью; основные windows/linux/summary
содержат финальные полные серии после fixes. Startup и red JSON — исторические
снимки других identity, не дополнительные финальные pass.

[Независимое research-task-review, раунд 1](../../review-task-04-2026-10-28/result.md)
выявило F-01–F-03 (major coverage) и F-04 (minor bookkeeping).
Четыре замечания исправлены; quick и полные матрицы Windows/WSL прошли.
После F-02 получен отдельный поведенческий red C04-13 и исправлен механизм discovery.
Отчёт приведён к research-task-authoring после этих исправлений.
Повторное research-task-review подтвердило закрытие F-01–F-04 и приняло этот scope;
новых must-fix нет. Исполнитель и рецензент — разные сессии Codex,
точные model ID недоступны; рецензент `/root/task04_research_review`.
Bookkeeping task/epoch/topic/docs завершён координатором после этой приёмки.

[Ревью раунда 3](../../review-task-04-round-3-2026-10-28/result.md) — `claude-opus-5-5`
(Claude Code), 2026-10-28. Полная серия повторена на нативном Ubuntu 26.04.1: 32 записи,
exit 0. Восемь замечаний: F-01–F-06 и F-08 закрыты в документах (F-01 и F-08 — передачей
в task-01), F-07 (удаление fixture root в `/tmp`) — код runner, не применялось.
Код spike в раунде 3 не менялся; [validation исправлений](../../evidence/2026-10-28-task-04-review-fixes.md).
