# Coverage ревью task-04, раунды 1–2

## Повторная проверка, раунд 2

Дата 2026-10-28, та же reviewer-сессия и база `07224d1`. Исходные сведения раунда 1 ниже сохранены как история. Повторно прочитаны текущие HostDiscovery, DedicatedHost, FakeWorkspace, RouteOptions, RoutingChecks, RoutingFixture, основной evidence report и исправленная строка epoch README; независимо проверены все изменённые пути release/startup и отмены. Дополнительный поиск `next-launch`, `BeforeClosingBarrier`, `ExitBarrier`, `load-waiter-ended`, `activeWaiters`, `cancelledWaiters`, `remainingA` подтвердил, что наблюдения и assertions связаны с реальными backend/lifecycle событиями.

Все 18 текущих JSON под task-04-data полностью разобраны программно, включая новые `closing-race-red`, `fixes-quick-windows`, `fixes-quick-linux`, `task-03-regression-windows` и три сохранённых round-1 файла. Основные final raw заново сверены с summary: 32 records/ОС; по 10 cold/recovery на протокол; 76 owner/initialization, 72 load-start, четыре load-failure, два lease-rejected, fatal=0; максимум один load на `(key,generation)`. Round-1 identity совпали с первым ревью и отделены от final.

Для каждого поколения независимо воспроизведён набор активных waiter по уникальному ID: start добавляет ID, end удаляет его; active/cancelled counts каждого события точно совпадают с пересчётом. Final Windows/Linux имеют по 474 start и end. В single-cancel подтверждено active=9/cancelled=1 до success. В all-cancel подтверждён порядок: active=0/cancelled=2 → waiterId=3 → load-success, при единственном load-start. Для lifecycle каждой версии подтверждены два owner/init/load/exit/lease-released, три closing-candidate, два closing, одно admission-closed, ноль exit-barrier-timeout. Это закрывает F-02/F-03; F-01 дополнительно проверен assertions текущего Switching и отсутствием нового поколения A в raw.

`closing-race-red.json`: selected=lifecycle, results=0, error tools/call, failedFixture имеет ровно один owner/init/load, closing/admission-closed/exit/lease-released и leaked=[]. Это поведенческий red уже начатого сценария, а не build failure или abort среды. Root cause C04-13 сверена с изменением HostDiscovery: до первого spawn host lease теперь перепроверяется и при уже удерживаемом launch lock. Новых review ID для этой причины не создавалось — она найдена при исправлении F-02.

`fixes-quick-*`: по 14 записей, error=null, identity совпадают с final платформенными binary. `task-03-regression-windows.json`: шесть записей, failures=0, identity первого среза; это дополнительный regression общих transport/handshake файлов, не замена final route matrix.

Самостоятельно выполнено `dotnet spikes/PipeRelay/bin/Release/net10.0/PipeRelay.dll routing-checks lifecycle` с обычным Windows token: PASS для `2025-11-25` и `2026-07-28`, exit code 0, raw error=null. В обеих записях owner/init/load/lease-released=2, admission-closed=1, fatal=0, exit-barrier-timeout=0. Игнорируемый runtime artifact `bin/Release/net10.0/routing-result.json` теперь относится к этому reviewer запуску; evidence Windows/Linux не менялись. Identity текущего Windows бинаря независимо пересчитана и совпала с final raw: `40D61B51C7DA415E90DE414D576D02EF8DDD6171DBA5EB18F0F51EAD28EAD009`; Linux final — `13CC69DF2EC3303DCD3B45E4423DD242BC91CFAFEFC3AF5E246CA3A5C0F660B1`.

`git diff --check` прошёл; HEAD/production версия не изменились. Статусы task/epoch/topic/docs остаются in progress и согласованы после F-04. Основной отчёт содержит Главное, ограничения и получателей task-01/task-02; red C04-13 отслежен. Перед завершением раунда дополнительно целиком прочитан новый `validation-final.json` (девятнадцатый JSON): база `07224d1`, production 1.5.18, configuration Release, build succeeded, точный main filter `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`, 1432 total/passed и 0 failed. Координатор подтвердил отсутствие изменений кода после него. Повторное ревью закрывает четыре findings и принимает standalone research scope; окончательные статус и bookkeeping принадлежат координатору. Полную матрицу/Linux/main повторно в этой reviewer-сессии не запускал; source не менял.

## Исторический coverage раунда 1

Дата: 2026-10-28. База и HEAD совпадают: `07224d1a8e49627aded77fb401d4c7059a9f3ef9`; production `RoslynMcpServer.csproj` — 1.5.18. Проверялся рабочий diff task-04 до исправлений по этому ревью. Другие reviews не читались.

## Прочитано

- `.agents/skills/research-task-review/SKILL.md`, `AGENTS.md`, полностью `docs/code-style.md`; применимого `.editorconfig` вне upstream fork нет.
- Task-04, целиком topic README и epoch README; `docs/README.md`, в том числе «Учёт задач, ревью и статистики». Сверены решения владельца, применимость моделей, U-C-01…08, ограничения и статусы.
- Целиком `evidence/task-04-routing-and-recovery-spike.md` и все 11 JSON в `evidence/task-04-data/`: `windows`, `linux`, `summary`, `startup-windows`, `startup-linux`, `linux-clock-red`, `future-budget-red`, `linux-access-0777`, `codex-mcp-kill`, `validation-initial`, `validation-pre-review`.
- Текущий `spikes/PipeRelay/**`: все Routing и Routing/Models, RoutingChecks, RoutingFixture, ProcessIdentity; общие Program, Facade, CounterHost, CounterState, BuildIdentity, LocalHandshake, BoundedStream, transport Handshake; старые SpikeChecks, SessionChecks, TransportChecks, ClientPair, CountBenchmark, WindowsAccessCheck; csproj, README, check_unix_access.py. Сверен diff общих файлов.
- Целиком `spikes/HostSurvival/codex_lifecycle.py` и его diff; `.github/workflows/test-suite.yml`. Production-код темы не менялся; принятие Roslyn-интеграции не рассматривалось.

## Независимые проверки полноты

Поиск `rg` по всем route/fixture/check файлам: `CallToolAsync`, `SendRequestAsync`, `Completion`, `IsCompleted`, `TryLock`, `Kill`, `Task.Delay`, `Stopwatch`, `TickCount64`, `UtcNow`, `Before.*Barrier`, `LaunchBarrier`. Цель — найти пути, отличающиеся от обычного forwarded call, и отличить контролируемые гонки от до/после последовательности.

Результат: отдельный путь Switch проверен собственным process probe; startup имеет явные barriers; lifecycle closing barrier отсутствует; фактический выход load waiter не наблюдается, хотя чтение cancellation frame наблюдается. Найдены F-01–F-03. Дополнительный поиск `SetCurrentDirectory`, `SetEnvironmentVariable`, `AppContext.SetSwitch`, `Console.Set`, `ThreadStatic`, `AsyncLocal` в spike не нашёл скрытой мутации этих состояний. Это поиск полноты, а не текстовый тест семантики.

Raw прочитаны через `json.loads` полностью, включая вложенные results/events/failedFixture. Независимо пересчитаны события и группы `(key,generation)`, проверены дубли owner/initialization/load-start/ready/load-success/load-failure. Для каждого cold/recovery fixture — ровно два owner, initialization и load-start и два разных поколения. Для финальных файлов проверено наличие всех заявленных полей событий. Сравнение с summary — точное по event counters, record count и identity.

Результат на каждой ОС: 32 records; по 10 cold/recovery на каждый из двух протоколов, всего 20+20; 76 owner и initialization, 72 load-start, 68 success, четыре failure, два lease-rejected; ни один load на поколение не повторяется, fatal = 0. Исторические red/startup файлы пересчитаны отдельно и в эти числа не включены. В `all-waiters` raw есть 2/3 cancellation frames на Windows и 2/2 на Linux; request ID и подтверждённое прекращение waiter в них отсутствуют (F-03).

## Что запускалось

1. `dotnet build spikes/PipeRelay/PipeRelay.csproj -c Release --no-restore` — passed, 0 warnings/errors. Пересчитанная identity бинаря совпала с final Windows raw.
2. Собственный Python process probe через stdin/stdout JSON-RPC, без изменения исходников: initialize `2025-11-25`; count на A; switch на B с закрытым LoadBarrier; дождаться B `load-start`, убить только B; дождаться ответа switch, затем count на том же фасаде. Обычный пользовательский Windows token потребовался из-за известного ограничения `CurrentUserOnly` с sandbox token.

   Результат: A сначала `{count:1, hostPid:53264, generation:c95b28a7-e053-4005-a452-1e698367ff10}`; B PID 48824 убит после load-start; switch вернул `IsError=true` в пределах 3 с; следующий count вернул A с той же generation/PID и count 2 в пределах 3 с. Гипотеза зависания Switch после смерти B **опровергнута**, finding не создан. Frontend вышел с кодом 0. По recorded PID/birth stamp все созданные этим probe host завершены.

   Runtime fixture: `spikes/PipeRelay/bin/Release/net10.0/r4review-vfq6x543` (ignored artifact). Исходный sandbox запуск `r4review-cnkfqnmn` получил `Host unavailable` до B load и timeout ожидания условия; это ограничение среды, не контрактный red и не pass. Его frontend и recorded host также завершены. Ещё одна попытка создать файлы в sandbox `%TEMP%` была запрещена до запуска процессов.
3. `git diff --check` — passed; только предупреждения нормализации LF/CRLF. `git log 07224d1..HEAD` пуст: base drift отсутствует.
4. Raw `linux-access-0777.json` сверено с кодом probe: mode 0777, owner UID 1000 — accepted, UID 65534 — ConnectionResetError, затем owner accepted, ownedHostExited true. Raw Codex: 0.164.0-alpha.11.1, backend alive, MCP dead, ordinary/detached children alive спустя 2 с, cleanupComplete true. Эти внешние опыты в reviewer-сессии заново не запускались.

## Покрытие требований

1. Discovery/совместимость: проверены состав ключа в коде, handshake, stale ready, future budgets, descriptor rejection, TMPDIR; canonical production root явно отложен. Поколения и отдельные connection/request/progress IDs прослежены по коду; отдельной инъекции позднего старого кадра не было.
2. Ownership/startup: cold/recovery ×10 на протокол/ОС подтверждены raw; launcher death до/после lease, host death до readiness, loser child, crash loop есть в коде и final raw.
3. Shared load: success/failure, кеширование отказа и single waiter cancellation прочитаны; политика при нуле backend waiter требует наблюдения по F-03.
4. Switch: existing/new destination, in-flight A, failed B сохраняет привязку, session activation/list_changed/subscription и subsequent recovery имеются. Уход инициатора с оставшимся A отсутствует (F-01).
5. Crash/replay: 10 proxy сходятся к новому поколению, interrupt/error и effect-before-kill без replay имеются. Session process не пересоздаётся.
6. Повторный отказ: crash loop с одним launch и общим cooldown, failure admission timestamp и cached shared failure проверены кодом/raw. Real Roslyn budgets не выводились из fake времени.
7. Lifecycle/publish: EOF инициатора/других proxy, reconnect в grace, exit после последнего, kill последнего, shadow/source/partial проверены. Детерминированная closing race отсутствует (F-02). Windows live-proxy publish и macOS честно не объявлены pass и переданы task-01.
8. Bookkeeping/validation: основные статусы `in progress` согласованы; остаточная строка `task-04 planned` в epoch README — F-04. Финальные счётчики и accepted scope обновляет координатор. Предварительная validation main содержит 1432/1432 и точный основной CI filter. Финальная Release build/main после fixes остаётся обязательной и принадлежит координатору.

Полную матрицу Windows/Linux и main suite в reviewer-сессии повторно не запускал: все final raw проверены программно, независимая проверка сосредоточена на отсутствующем сценарии. SourceStructure/AnalyzerLifecycle отдельно не запускались: production C# не менялся; standalone spike не затрагивает analyzer lifecycle. Ревью не заменяет финальную validation, Roslyn spike, macOS или production acceptance.
