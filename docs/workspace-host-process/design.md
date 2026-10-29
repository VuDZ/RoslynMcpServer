# Выделенный workspace host: проект архитектурного решения

Дата: **2026-10-29**. База: `b2a7b80`, сервер **1.5.18**, MCP SDK **2.2.0**.
Статус: **proposed; техническое ревью принято, 2026-10-29; выбор владельца отложен до отдельных ревью**.
Результат [E0/task-01](epoch-0-design-and-spike/task-01-architecture-decision-hi.md).
Границы темы и решения владельца — в [README](README.md).
Факты действующего сервера ниже отделены от предлагаемого контракта shared-режима.

## Главное

- Рекомендация — **opt-in выделенный host на совместимый ключ**; default остаётся автономным stdio. Роли, handoff, promotion/demotion, HTTP и restore RAM не нужны выбранной модели.
- Дублирование настоящего workspace подтверждено task-05. Экономия production shared-режима ещё не измерена; task-02 сравнит все процессы при N=1/N=10, включая цену отдельного host.
- Proxy — обычный сервер с Roslyn/plugins, но без загруженного workspace. Его MCP-сессия, activation, defaults, CLI и отчёты остаются локальными.
- В ключ входят graph inputs, полный image manifest и стартовый контекст. Контекст с известными volatile harness IDs в environment отклоняет shared до создания key; task-02 сначала проверяет точный descriptor на настоящей MCP-паре.
- Switch готовит B вне gate привязки; запросы A продолжают получать ответы. Короткий commit меняет только будущие вызовы; отказ B сохраняет живой A.
- Shared load имеет один attempt и независимых waiter. После отказа следующий явный `load_workspace` клиента, уже получившего этот отказ, может открыть одну новую попытку без отключения всех клиентов.
- Предлагаемая lifecycle-семантика: `stop_mcp_server` завершает только свой proxy; `reset_workspace` выводит из обслуживания **общий host этого ключа**, включая его analyzer CLR. Это видимое всем изменение требует выбора владельца.
- Падение host не убивает CLI в живом proxy. Успех CLI и подтверждение build proof — разные результаты; доказательства не переносятся в новое поколение.
- Windows publish использует остановку proxy, удерживающих обновляемые исходные DLL. Непрерывное обновление этих DLL при живых proxy не обещается; новый bootstrap не предлагается. Host работает из immutable shadow image.
- Design допускает task-02 только после независимого ревью и выбора владельца. macOS, общий Roslyn, произвольные plugins и численные бюджеты требуют проверок, перечисленных в конце.

## 1. Выбор модели — U-C-07

Основания: [task-00](epoch-0-design-and-spike/evidence/task-00-state-inventory.md),
[его актуализация](epoch-0-design-and-spike/evidence/task-00-impact-2026-10-28.md),
[task-03](epoch-0-design-and-spike/evidence/task-03-pipe-relay-spike.md),
[task-04](epoch-0-design-and-spike/evidence/task-04-routing-and-recovery-spike.md),
[task-05](epoch-0-design-and-spike/evidence/task-05-applicability.md).
Приоритет имеют решения владельца от 2026-10-28, а не исторические рекомендации этих отчётов.

| Вариант | Память и работа | Механизмы и риск | Решение автора |
|---|---|---|---|
| Нулевой: текущий stdio | Каждый клиент держит собственный workspace. | IPC отсутствует; поведение известно. | Сохранить default и контроль task-02. |
| Простое улучшение нулевого: idle unload + GC | Убирает часть памяти idle, оставляет активные дубликаты. | Нужны таймер и повторные загрузки. | Отложено владельцем; вернуться при выборе нулевого варианта. |
| Выделенный host | N proxy и один workspace на ключ. | Нужны IPC, session split и recovery. | Рекомендуется для task-02. |
| Роли в MCP | Может сэкономить один proxy на ключ. | Добавляет handoff, demotion и ограничения CLR. | Отложено до измеримого ущерба N=1 или harness-блокера. |

В task-05 измерена синтетическая группа 20 MCP: 2606,4 MiB private bytes на Windows,
из них 16 загруженных копий двух descriptor. Это не состав прежних 10–20 ГБ владельца.
Подстановка условного proxy 23,04 MiB в формулу task-05 даёт **оценку** экономии
выделенного host 1878,2 MiB внутри этого распределения. Она использует оптимистичные
минимальные представители host, не учитывает новую нагрузку и не является результатом реализации.
Сумма working set не используется как уникальная физическая RAM.

Task-03 измерил p50 Windows `count` 0,39–0,45 мс через фасад против 0,12–0,14 мс напрямую
в одной серии по 200 вызовов на вариант. Это цена fake-вызова, не Roslyn SLA.
На малом Linux-контроле task-05 reset с aggressive GC вернул около 2/3 прироста памяти;
остаточный heap не разобран. Новая выгрузка по простою и GC в этот design не добавляются.
Вариант B остаётся внешней возможностью ускорения cold load без зачёта его выгод здесь.

Общая необходимая работа для обеих shared-моделей — session/workspace split,
дескриптор, владение ключом, фасад, no replay и proof boundaries. Выделенный host
позволяет сделать её без пригодности ролевых кандидатов и миграции workspace.
Риск рекомендации — RAM/IPC при N=1 и конкуренция CPU; решение о продолжении после замеров — владелец.

## 2. Совместимость и стартовый контекст — U-C-01

`HostKey = SHA256(canonical descriptor v1)`. Каноническая сериализация задаёт
порядок полей, длины UTF-8 строк, явный `null` и порядок списков; hash строки JSON
с произвольным порядком свойств не используется. При handshake сравниваются полный
ключ и все поля дескриптора, а не только имя канала.

| Поле | Правило v1 |
|---|---|
| User / namespace | Windows SID или effective UID Unix; версия внутреннего протокола и локальный rendezvous root. |
| Server image | SHA-256 манифеста всех копируемых файлов server/RID/native/BuildHost bundle. |
| Runtime / toolset | ОС, архитектура, фактические runtime/GC mode и зарегистрированные MSBuild instance path/version. |
| Startup context | Абсолютные начальный cwd и effective cwd после `ROSLYN_MCP_WORKSPACE`; исходный executable/config origin. |
| Workspace | Абсолютный путь `.sln`/`.slnx`/`.csproj`, нормализованный относительно cwd клиента. |
| Load properties | Эффективные Configuration, canonical Platform, TargetFramework; отсутствие остаётся отдельным `null`. |
| Host settings | Analyzer overlay policy, prewarm, BuildHost A/B/C/workers, provenance options и trace/window settings. |
| Environment | Digest полного снимка eligible startup environment после preflight ниже. |
| Plugins | Упорядоченные исходные пути, полные content manifests и принятый набор registrations/schemas. |
| Configuration | Эффективная проекция настроек host и источников SDK selection, включая найденные `global.json`. |

Состав среды выбран консервативно: MSBuild imports/conditions могут читать любую
переменную. **До вычисления key** preflight проверяет наличие `CODEX_THREAD_ID`,
`CODEX_SESSION_ID`, `CODEX_TOOL_CALL_ID`. Наличие любого из этих имён, даже с пустым
значением, делает startup context **ineligible shared**: явный отказ до host launch,
без создания отдельного host для каждого ID и без fallback в локальный workspace.
Эти три имени найдены в shell автора/рецензента; это не доказывает их наличие у MCP.
Их нельзя молча убрать только из hash, оставив разные значения доступными MSBuild.

Для такого контекста допустим ordinary либо заранее выбранная пользователем стабильная
startup environment без volatile IDs. Сервер сам не меняет окружение проекта и не
заимствует значения первого host. Проект, которому нужны эти переменные, остаётся
в ordinary до отдельного решения о его effective evaluation context.
Известный список v1 — точные три имени, без широких `CODEX_*`/`SESSION_*` фильтров.
Новые volatile names добавляются после names-only проверки harness, а не по сходству имени.
Произвольное пользовательское env-свойство считается semantic input, пока не доказано обратное.

Для eligible среды имена сортируются; Windows environment names сравниваются без
учёта регистра и канонизируются одинаково, Unix — ordinal, значения сохраняются точно.
Значения не пишутся в discovery/logs; hash не считается защитой секрета от того же user.
В v1 нет непроверенного allowlist «только PATH/DOTNET_*». Это может консервативно разделить
процессы с иной средой, включая разные startup profile variables. Runtime activation
и session defaults сами по себе key не меняют. Внутренне генерируемые IDs в env не внедряются.

**Go/no-go до интеграции:** task-02 начинает с двух настоящих MCP motivating harness
и вычисляет точный proposed descriptor, а не только env subset. При одинаковых workspace/
эффективных настройках сравниваются keys и names-only причины расхождений; значения
credentials/env не публикуются. Если различие вызвано volatile session/PID input либо
preflight отвергает запуск, нельзя объявлять схлопывание этого harness или переходить
к rollout на основании synthetic N=10. Результат возвращается владельцу для изменения
startup context/дизайна или выбора нулевого варианта. Подтверждение пары для 1.5.18
с этим descriptor в данной docs-only задаче ещё не получено.

Пути следуют [InputPathCanon](../../Services/Workspace/InputPathCanon.cs#L3):
`Path.GetFullPath`, Windows OrdinalIgnoreCase, Unix Ordinal. Для ключа Windows
используется одинаковое invariant case представление; разделители и конечный
separator нормализованы, корень сохранён. Symlink/junction/hardlink aliases не
склеиваются без доказанной эквивалентности. Дополнительные host из-за aliases —
допустимая консервативность; включение case-sensitive Windows directory проверяется
до share и пока отвергается как unsupported, чтобы не объединить разные файлы.
Project/solution contents не хешируются как часть идентичности workspace:
их изменение обрабатывают watcher, freshness и graph reload.

Пакетная версия, только apphost hash и случайный путь plugin staging недостаточны.
Манифест image включает относительный путь и hash каждого файла, включая `.deps.json`,
`.runtimeconfig.json`, native assets и plugin dependencies. Логи, rendezvous и runtime
temp не копируются. Конфигурация читается из **исходных** exe/cwd locations один раз,
а её эффективные значения передаются host отдельно. Относительные plugin paths
разрешаются как сейчас от cwd; shadow path не становится новым config origin.
Перед запуском проверяются исходный и скопированный манифесты; движущийся source
даёт bounded failure без исполнения смешанной сборки.

Host запускается с cwd/environment инициатора и теми же эффективными host options.
Он проверяет фактические MSBuild/runtime/plugin facts до readiness.
Если новый `global.json` уже не позволяет воспроизвести закреплённый toolset proxy,
вызов получает mismatch с действием restart proxy; чужой host не подменяет его выбор.
Full image manifest не заменяет analyzer admission: байты проектных analyzers
меняются независимо от сборки сервера и проверяются прежним gate.

### Defaults, lazy load и политика analyzer

Proxy хранит только свой `SessionLoadContext`. Как в
[LoadAndPrepareAsync](../../Services/Workspace/SolutionManager.cs#L893),
явное значение побеждает предыдущее успешное значение **этой сессии**, затем file default.
Пробельное значение считается omitted. `null` без предыдущего/file default означает
«свойство не передано MSBuild», а не принудительный Debug/AnyCPU/первый TFM.
Raw Platform сохраняется для CLI, canonical `Any CPU` → `AnyCPU` — для graph.
Build args: непустой load обновляет session suffix, omitted сохраняет его; в key он не входит.
Load TFM автоматически не добавляется обычному CLI build.

Если workspace при старте неизвестен, proxy остаётся unbound и не запускает host.
Явный load выбирает путь от cwd. Semantic lazy load разрешает file workspace/defaults
этого proxy и проходит тот же attach/single-flight путь. Ошибка lazy load не меняет
defaults другой сессии. Explicit load failure не фиксирует новые defaults.
Успешный load одного ключа с новыми CLI-only args меняет только свой контекст.

Overlay — свойство совместимости host: первый true создаёт overlay key; false/omitted
при уже включённом overlay **своей сессии и том же workspace** сохраняет sticky policy.
При переходе на другой workspace начинается policy явного аргумента, по умолчанию false,
как при fresh reopen текущего manager. Смена прочих graph inputs также начинает fresh policy.
Получить false после sticky true на том же graph можно через reset этой сессии/ключа.
Policy чужого host не наследуется. Mapping/provenance GUID не передаются как готовое разрешение.

Альтернатива — passed-arguments matcher текущего manager — отвергнута: omitted не
проверяет совместимость клиентов. Риск строгого descriptor — лишние host; сужать
его можно только после проверки семантики исключаемых inputs.

## 3. Владение, транспорт и безопасность — U-C-04/U-C-05

Windows root: `LocalApplicationData/RoslynMcpServer/WorkspaceHosts/v1`.
Unix root: `/tmp/roslyn-mcp-<euid>/v1` на локальной файловой системе, независимо от `TMPDIR`.
Root создаётся с owner-only доступом: DACL SID владельца на Windows, mode 0700 и
проверенный UID на Unix. Lock/config files — 0600 на Unix. Проверяются существующий
root, владельцы и отсутствие reparse/symlink подмены; открытия фиксируют проверенные
объекты, а не повторяют незащищённый path check. При невозможности проверки — отказ shared.
Root на сетевом filesystem не поддерживается. Угроза — другой OS user; код того же
пользователя уже имеет доступ к исходникам/процессам и не считается sandboxed.

Под root лежат `<full-key>/launch.lock`, `host.lock`, bounded metadata и socket.
Lock-файлы не unlink/replace во время работы и cleanup; иной inode означал бы второго владельца.
Windows pipe name содержит namespace/user/key digest; Unix — абсолютный socket path
под тем же root. До запуска проверяется предел **103 UTF-8 байта** для полного Unix path;
невместившийся путь даёт ошибку, без fallback в другой `TMPDIR`.
`CurrentUserOnly` проверяется с обеих сторон. У Unix socket добавляются права 0600 и
peer-UID validation; Windows проверяет owner/current SID и pipe DACL.

Рекомендация — стандартные OS locks. На Windows остаётся `FileStream.Lock`;
на Linux v1 сохраняет проверенный `fcntl` механизм с **одним открытым дескриптором
каждого lock-файла на процесс**. Process-local registry объединяет все callers ключа
и выдаёт один сериализованный lease; probes не открывают/закрывают второй descriptor
рядом с удерживаемым lock. Регистрация сохраняется до конца process/key use.
Любая дополнительная дескрипторная операция обязана идти через неё.
Для macOS нужен `flock(LOCK_EX|LOCK_NB)` с close-on-exec; тот же registry и lifetime.
`FileStream.Lock` там [не поддерживается](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.lock?view=net-10.0).
`flock` — известная примитива, но local macOS pass здесь нет; до проверки режим там disabled.
Семантика Linux [`flock`](https://www.man7.org/linux/man-pages/man2/flock.2.html)
не приписывается `fcntl` или непроверенному macOS runtime.

Последовательность cold/recovery для десяти proxy:

1. Попытаться подключиться и проверить готовность; PID/ready-файл готовность не доказывают.
2. Получить `launch.lock`; повторить handshake, затем проверить host lease.
3. Если lease занят, ограниченно ждать старта/closing и **повторять** probe, без нового heavy load.
4. Если свободен, подготовить image и запустить одного ребёнка; отметить startup attempt id.
5. Ребёнок получает `host.lock` **до** MSBuild/Roslyn workspace initialization. Проигравший ребёнок выходит до неё.
6. Подтвердить descriptor, новый случайный host generation и внутренний MCP initialize; отпустить launch lock.
7. Все attach/load данного key входят в один attempt. Убить launcher до/после шага 5 безопасно для exclusivity: новый launcher может породить ребёнка, но heavy initialization начинает только владелец host lease.

Каждое новое владение — новый `HostGeneration`, включая restart того же executable.
Физическая перезагрузка graph внутри host увеличивает `WorkspaceEpoch`; это не IPC generation
и не существующий `SolutionManager.LoadSessionId`. Последний сохраняется как отдельная
граница analyzer/write/input state. Старые DTO/tickets не принимаются при несовпадении любой границы.

До SDK идёт length-prefixed preface v1: полный key, descriptor digests, generation
ответа, session correlation и согласованные limits. Максимум 16 KiB; сырые environment
и конфиги в нём не передаются. Затем `StreamServerTransport`/`StreamClientTransport`
и отдельная SDK MCP-сессия 2025-11-25 на каждом подключении proxy. Это не новый
самописный JSON-RPC dispatcher. Поля private RPC проверяются как недоверенные входы.
Host не меняет descriptor при новом клиенте и не принимает runtime-команду сменить свой workspace path.

API SDK проверены по установленному `ModelContextProtocol.Core.dll`:
`McpServer.Create(ITransport, McpServerOptions, ILoggerFactory, IServiceProvider)`,
`ModelContextProtocol.Protocol.StreamClientTransport(Stream, Stream, ILoggerFactory)`,
`StreamServerTransport` и `DelegatingMcpServerTool.InvokeAsync(RequestContext<CallToolRequestParams>, CT)`.
[Протокол проверки и hash](epoch-0-design-and-spike/evidence/task-01-architecture-decision.md#проверка-sdk-и-исходников).
Host DI один на workspace/process; request session context и его stores отдельны.
Каждое SDK connection получает собственные options/collections/handlers.
Нельзя вызвать нынешний `RegisterCore` десять раз и получить изоляцию singleton state.

Внешние schemas инструментов строятся из существующего каталога и plugin registrations.
Внутренние `attach/load`, `execute`, `build plan/admission/confirm`, `retire` — private
SDK handlers с DTO; клиентский каталог их не показывает. Росlyn objects, delegate
`PullSnapshot`, `Solution`, ProjectId и write-context handles не сериализуются как переносимые объекты.
Host хранит их за opaque session/call ticket; ticket проверяет owner, generation, epoch и срок.

Альтернативы PID files, сканирование всех процессов, P2P и time-based lease takeover
отвергнуты: они не дают OS exclusivity до инициализации. Риск — platform primitive
и безопасное открытие файлов; проверка этих швов обязательна в task-02 до поддержки ОС.

## 4. Состояние и переключение — U-C-02/U-C-03

Proxy хранит внешний MCP endpoint, протокол/capabilities, activation, defaults,
стартовые cwd/config/env, лог-корреляцию, свои CLI и report payloads.
Host хранит один manager, watchers, published/sanitized snapshots, analyzer loader,
load single-flight, process caches и общую admission изменений. Клиенты разделяют
saved-disk изменения и CPU; изолированных ветвей Solution не появляется.

Каждый host call получает явные session max-results/preview и нужные display options;
их нельзя читать из FileSettings чужого manager. BuildArgs/defaults также передаются
по захваченному session context, без мутации глобальных Loaded* между вызовами.

Навигационные cursors и diagnostic reports получают владельца session + origin
key/generation/epoch. Host передаёт proxy весь ограниченный текущими caps remainder
отдельно от публичного краткого результата; proxy создаёт внешний cursor в своих stores.
Неполный transfer при смерти host завершает вызов ошибкой, без cursor для недоставленных bytes.
Старый cursor не инициирует поиск на новом graph и не удерживает retired workspace connection.
Уже локальный report CLI остаётся читаемым после switch/host crash до прежнего TTL.
Чужой token не читает payload и не двигает offset. Лимиты сохраняются per session.

Переключение A → B:

1. Сериализовать только намерения `load/reset` этой сессии. Захватить прежнюю привязку A и defaults.
2. Разрешить descriptor B и сделать attach/load **вне gate чтения/commit привязки**. Новые обычные вызовы пока принимаются на A.
3. Принятый вызов фиксирует connection/generation/epoch и увеличивает reference count. Его completion и progress не ждут загрузку B.
4. После успешной publication B коротко проверить session revision и cancellation, затем атомарно зафиксировать B и новые defaults.
5. После commit новые вызовы получают B; старые заканчивают на A. Последняя ссылка закрывает retired A connection вне commit gate.
6. Ошибка/cancel B освобождает candidate и сохраняет живой A и его defaults. Если A умер независимо, сообщается также недоступность A; rollback RAM не выдумывается.

Смена проекта инициатором выделенного A не меняет generation A и не перезагружает
его для оставшихся клиентов. Ролевые release/handoff/promotion/demotion — N/A:
ни один proxy не владеет workspace. Смерть proxy не передаёт роль другому proxy.
Reset/switch одной сессии упорядочены, но не блокируют выдачу уже готового результата.

Тяжёлые read-запросы используют snapshots и могут идти параллельно; write boundary
остаётся единственной точкой persist/preflight. Host admission запрещает смену graph
во время commit записи; reload/reset сначала прекращают admission старой epoch и
согласуют активные операции. Общий lock не держится на весь CLI или ожидание B.
Graph-stale reload того же key сериализован и создаёт новую WorkspaceEpoch; это не
переход на иной B. Если такой reopen уничтожил старый graph и затем отказал,
старый RAM graph не обещается всем клиентам: нужен явный retry/reset.
Недопустимо обойти write gate выдачей старого snapshot после reset/reload.

Plugins — отдельная граница пригодности. Текущий API допускает arbitrary services,
static state и обращения к manager ([IRoslynMcpPlugin](../../Plugins/IRoslynMcpPlugin.cs#L5)).
Сериализация plugin calls не изолирует его singleton state. Для task-02 допустимы
plugins=none и проверенный SamplePlugin; прочие plugins до индивидуального audit
дают явный unsupported shared, без скрытого per-proxy workspace fallback.
Metadata/DI API plugins не меняются; отдельный manifest чистого proxy не вводится.
Audit обязан подтвердить отсутствие load workspace в proxy startup/constructors,
session leaks и несогласованных manager mutations. Проверенные plugin tools с workspace
исполняются в host, неизвестные локально не запускаются по догадке.

Альтернативы держать session gate на весь load и делить activation/overflow singletons
отвергнуты по task-00 и F-01 ревью task-04. Риск — непроверенные plugins и общий CPU;
контроль изоляции и latency при N=10 получает task-02.

## 5. Размещение всех built-in tools — U-C-08

Ниже **54 имени** [каталога](../../Hosting/McpToolCatalog.cs#L165); стадии уточнены
по [инвентаризации](epoch-0-design-and-spike/evidence/task-00-state-inventory.md#инструменты-и-этапы-исполнения).
«Binding» — захваченный контекст исходной сессии, а не текущие Loaded* случайного host.
Простые disk tools не вызывают lazy load только ради маршрутизации.

| Инструменты | Место и граница |
|---|---|
| `list_tool_groups`, `get_tool_help`, `enable_tool_group` | Proxy: metadata и activation этого endpoint. |
| `load_workspace`, `reset_workspace` | Proxy: defaults/switch; host: load или общий retire по разделу 7. |
| `get_mcp_server_info`, `stop_mcp_server` | Proxy: локальные факты/stop; remote facts info явно подписаны. |
| `get_class_skeleton`, `get_diagnostics_for_file`, `find_symbol_definition`, `find_usages`, `find_symbol_references`, `find_implementations`, `get_call_graph` | Host: published snapshot; owned cursor continuation обслуживается по своему origin. |
| `add_using`, `add_method_to_class`, `update_method_body`, `get_code_fixes`, `apply_code_fix`, `rename_symbol`, `generate_test_method_stub` | Host: semantic/write boundary исходной epoch. |
| `list_projects`, `get_project_graph`, `get_test_list` | Host: опубликованный graph и disk sync. |
| `get_file_content`, `read_file_range`, `get_method_body` | Proxy: абсолютный путь от захваченного workspace directory либо cwd. |
| `get_code_skeleton`, `list_directory_tree` | Proxy: путь от cwd как сейчас, parser допустим. |
| `search_code`, `get_changed_files` | Host разрешает omitted graph roots/anchor; proxy исполняет disk/rg/git с готовыми путями. |
| `update_file_content`, `apply_patch` | При binding — host с прежним tracked write gate; unbound — прежний standalone disk path proxy. |
| `rename_project`, `add_package_reference`, `remove_package_reference` | При binding — host, включая graph invalidation; unbound disk path допустим лишь там, где он уже поддержан. |
| `explore_assembly`, `decompile_type`, `get_decompiled_class_skeleton`, `get_decompiled_method_body` | Explicit assemblyPath — proxy; assemblyName — host resolution и decompile, без переноса Roslyn handles. |
| `run_dotnet_build`, `run_dotnet_test`, `run_specific_test`, `run_test_by_filter` | Этапы раздела 6: session defaults в proxy, graph/proofs в host, CLI/result в proxy. |
| `run_dotnet_run`, `execute_dotnet_command`, `run_format` | Proxy: cwd/env и child lifetime исходного вызова; disk effects общие. |
| `list_nuget_packages`, `run_nuget_audit`, `list_outdated_packages`, `search_nuget_registry` | Proxy: прежние path/network/feeds semantics. |
| `read_log_tail`, `tail_tool_log`, `manage_agent_scratchpad` | Proxy: свой default log или explicit path, scratchpad исходного cwd. |

Все вызовы фиксируют пути/defaults до первого await, способного пересечь switch.
Успешные публичные результаты сохраняют нынешнее форматирование; источник host/proxy
добавляется к lifecycle/диагностике, а не в каждый результат анализа.
Альтернатива пересылать весь каталог отвергнута из-за cwd/CLI/session state;
перенести весь TestTools в proxy нельзя из-за semantic filter и DLL admission.
Риск выбора по стадиям — пропустить скрытый manager access; task-02 проверяет каждую строку.

## 6. Build/test, CLI и доказательства

Обычный build: proxy разрешает explicit target, Configuration/Platform/raw spelling
и buildArgs своей сессии; `SolutionProjectTargetResolver` может читать solution с диска.
Он запускает `DotNetBuildProbe` и возвращает результат в proxy. Этот маршрут не создаёт
DLL-route proof ни до, ни после host crash. TargetFramework load не добавляется автоматически.

Project test route: proxy фиксирует target/defaults/filter/timeout и прежние различия
noBuild. `run_specific_test` сначала запрашивает semantic filter у host, если graph
доступен или положен config lazy load; прежний fallback при отсутствии graph сохраняется.
Далее prebuild → test → parse/report выполняются в proxy с одним исходным бюджетом.
Фильтр и target во время выполнения не разрешаются заново на B.

DLL test route требует последовательности с ticket:

1. Host исходного binding делает disk sync, разрешает project/assembly и immutable BuildContext; сохраняет ProjectId внутри host.
2. Proxy получает plan с opaque ticket и путями. До CLI он получает OS gates для известных output paths плана в детерминированном порядке; их namespace общий для разных host generations/build identities того же user.
3. Host проверяет ticket/session/generation/epoch, делает свежий PullInputSnapshot и BeginBuild/admission. У каждой session свой `SessionBuildState`; output contention/admission общие между сессиями host.
4. Proxy запускает CLI с исходным cwd/env/args и держит OS gates до конца build/test/cleanup. Transport host не является cancellation token запущенного CLI.
5. Host подтверждает результат только в исходном контексте: повторный pull, output identity и существующие coverage/freshness правила. Движение inputs допускает прежний один rebuild в том же общем timeout.
6. Перед ещё не запущенными tests нужен разрешённый ticket исходной epoch. Известная потеря host/смена epoch до admission останавливает продолжение; уже стартовавшие tests заканчивают в proxy.
7. Proxy форматирует фактический CLI result и отдельно отказ/неполноту confirmation. Закрывает gates после завершения процесса и drain; новый host не получает старый proof.

OS output gates нужны потому, что host death освобождает его RAM semaphore, пока
старый proxy CLI ещё пишет bin/obj. Два решения с пересечением известных output
paths получают тот же gate. План охватывает outputs затрагиваемого project closure;
проверка полноты этого набора — обязательный эксперимент task-02. Неизвестные outputs
или внешние build/raw CLI не объявляются заблокированными: coverage/contested state
остаётся unknown, reuse не разрешается. Host tickets не хранятся после disconnect;
OS gates остаются у живого исполнителя до фактического конца CLI.

**Смерть proxy ограничивает эту гарантию.** SIGKILL/Job kill освобождает его OS gates,
а дочерний CLI на некоторых запусках может остаться жив и продолжить писать outputs
(task-05). Один proxy-owned lock не предотвращает такое пересечение.
До spawn под gates сохраняется bounded activity record с operation id и состоянием
`launching`, после spawn — известной PID/birth identity; normal completion помечается
только после process exit/drain. Хранится он под user root, вне immutable image и
без command/env secrets. Отсутствие записи clean completion после смерти proxy даёт
**abandoned writer / outcome-unknown**. Другой proxy, получив свободный gate, сначала
проверяет record и отказывается начинать DLL-route build/tests/выдавать подтверждение.
Этот record переживает также смерть host; новый generation не сбрасывает его.
Один исчезнувший PID не доказывает завершения descendants. Если завершение старого
writer/его дерева нельзя подтвердить, автоматический takeover outputs запрещён:
нужны остановка/проверка этих producers оператором и снятие abandoned record под gates.
Shared reset не стирает этот факт и не превращает потерянный вызов в новый proof.
Task-02 проверяет обе гонки — до сохранения child identity и с уже работающим orphan child.
Новый cross-platform supervisor/daemon ради этого здесь не вводится.

| Момент потери host | Результат |
|---|---|
| До plan/admission | CLI не запущен; явный отказ этой попытки. |
| После admission, до отправки команды CLI | При наблюдаемом disconnect команда не стартует. |
| Во время build | Build заканчивает в proxy; без confirmation tests не стартуют. |
| Между confirmation и tests | Известная invalidation запрещает старт; гонка уже выданного admission относится к старому ticket. |
| Во время tests / после CLI | Возвращается реально наблюдаемый результат; proof нового поколения отсутствует. |

Нет атомарной транзакции «host жив + OS CreateProcess»: потеря после выдачи admission
может быть замечена уже после запуска. Это не разрешение нового поколения и не повод
повторить CLI. Switch на B сохраняет call reference A до конца этого маршрута;
успех tests A нельзя подписать B. Отмена самого клиентского вызова по-прежнему
запускает обычный timeout/kill/drain DotNetCliRunner с его платформенными гарантиями.

В [SessionBuildState](../../Services/Build/SessionBuildState.cs#L8) pull не имеет input
content hashes: `auto` не skip, `never` не получает current proof только из exit=0.
Эта граница сохраняется; remote ticket не создаёт нового доказательства свежести.
Альтернатива запускать CLI в host отвергнута из-за его lifetime и cwd клиента.
Риск — дополнительные stage RPC и output collisions; task-02 обязан проверить реальные DLL routes.

## 7. Lifecycle, load attempts и восстановление — U-C-03/U-C-04/U-C-06

Host состояния: `Starting → ReadyEmpty → Loading(attempt) → Ready(epoch)` либо
`LoadFailed(attempt)`. `Closing` терминален для admission; следующее владение получает
новый generation. Graph reload — новая attempt/epoch внутри того же владения.
Readiness транспорта отличается от успешной загрузки workspace.

Один shared Task load принадлежит host lifetime, а не токену первого клиента.
Отмена одного waiter прекращает его ожидание. При отмене всех waiter, пока proxy
подключены, загрузка продолжается и новый waiter может присоединиться.
После ухода всех соединений grace отменяет load и завершает host.
Если proxy подключены, но запросов нет, workspace сохраняется без idle eviction;
выгрузку такого workspace и aggressive GC этот design не активирует (U-C-06).
Отказ хранится с failure/attempt id; все уже принятые waiter получают один результат.

Явный retry: proxy, получивший этот failure, сохраняет его id. **Следующий отдельный**
`load_workspace` того же key посылает retry-of=id. Host compare-and-swap меняет
`LoadFailed(id)` на единственную новую attempt. Конкурентные retry-of=id присоединяются
к ней; просроченный id не начинает ещё один load после её отказа. Новый клиент без
предыдущего failure сначала получает текущий отказ. Semantic lazy calls отказ не
сбрасывают. Неудачные retry не накапливаются в очередь; reset также может закрыть поколение.
Перед retry освобождаются ресурсы неудачного load; analyzer restart-required
сохраняется и требует reset host, а не бесконечного retry в том же CLR.

Нормальный EOF proxy закрывает его SDK sessions и отменяет принадлежащие ему запросы,
включая subscription listen. SIGKILL определяется transport EOF без finally proxy.
Host считает валидные подключения и активные call references. Уход инициатора при
других proxy не завершает host. После последнего подключения grace **1 с** даёт
новому attach отменить выход; решение admission/Closing атомарно.
Во время Closing подключение получает внутренний retry-after-release, но не admission;
новый launcher повторно проверяет lease до deadline и не начинает load рядом со старым.
После прекращения admission host отменяет/drain-ит работы, закрывает listener и manager,
затем освобождает lease. Зависший cleanup заканчивается выходом самого host, не ранним
освобождением lease с работающим старым workspace.

Windows host не выходит из Job harness и не использует breakaway. Unix запуск
через `setsid` допустим лишь вместе с самостоятельным exit после последнего proxy.
Harness может принудительно убить дерево/Job, включая host с чужими ещё живыми
подключениями: это crash/recovery, а не гарантия непрерывной доступности.
Возможность пережить EOF одного proxy не означает пережить полный выход harness.

**`reset_workspace` в shared** — explicit retire текущего общего host. До первого
load без binding он только очищает свою session. При binding он закрывает admission
ключа, уведомляет все proxy об invalidation, даёт активным операциям drain budget,
завершает host и ждёт освобождения lease. Это затрагивает все его сессии; ответ
называет key/generation и общий эффект. Инициатор очищает loaded defaults/buildArgs
и остаётся unbound, file defaults как сейчас допускают следующий lazy load.
Остальные сохраняют свои defaults/descriptor и восстанавливаются на следующем
явном вызове. Повторный retire уже закрывающегося generation идемпотентен и не
закрывает преемника. Повторно доставлять прерванные tool calls нельзя.

**`stop_mcp_server` в shared** — прежний ответ с последующим stop только своего
stdio proxy. Он освобождает connection; остальных не останавливает.
Restart-required analyzer указывает действие shared reset → новый load/host generation;
обычный restart proxy при живом host analyzer не обновляет. Default reset/stop сохраняют
нынешнюю in-process семантику. Выбор global retire вместо in-process clear упрощает
restart CLR и failure recovery, но сбрасывает общий прогрев; это решение владельца O-02.

Crash host: все привязанные in-flight host calls получают отказ; следующий отдельный
вызов проходит discovery/single-flight для **своего сохранённого descriptor**.
Восстановление не меняет внешний MCP initialize и не запускает workspace в каждом proxy.
Нет автономного background restart loop. При смерти launcher/host до readiness
одна группа уже ожидающих вызовов получает startup failure; следующий вызов после
cooldown может открыть следующую attempt. Если конкурирующий ребёнок просто проиграл
живому владельцу lease, это не crash failure этого ключа.

### Бюджеты, ошибки и cooldown

Значения ниже — **предлагаемые инженерные limits**, не результаты Roslyn-замера.
Task-02 проверяет их и приводит измерения; новые публичные параметры не добавляются.

| Стадия | Предлагаемый предел |
|---|---|
| OS connect | 5 с на попытку. |
| Preface handshake | Отдельные 5 с после connect. |
| Internal MCP initialize | Отдельные 15 с после handshake. |
| Discovery/start/readiness | Общие 30 с, включая ожидание launch/host locks. |
| Physical load/prepare | 10 минут на общую attempt, отдельно от discovery. |
| Semantic call | Прежний call cancellation; дополнительного общего 15 с / 2 мин IPC timeout нет. |
| CLI | Нынешний timeout инструмента включает stage RPC, gates, build/test и cleanup по прежним правилам. |
| Cancellation delivery | Отдельные 2 с с независимым токеном. |
| Retire/drain | 30 с, затем host завершает собственный процесс. |
| Нет первого / последнего клиента | 30 с startup allowance / 1 с closing grace. |
| Internal NDJSON | Те же logical payloads, что принимает внешний SDK; нового общего size cap нет. |

Стандартный SDK NDJSON не получает дополнительного ограничения целого сообщения.
Preface и private control DTO остаются ограниченными; существующие tool-specific
caps и schema/JSON validation сохраняются. Нельзя отказать принятому внешнему
`update_file_content` только из-за нового internal cap или усечь успешный response.
Неограниченный public content уже может потребовать много памяти ordinary процесса;
shared transport не объявляет этот прежний риск исправленным. Новый общий публичный
лимит требует отдельного изменения контракта. Task-02 проверяет parity ordinary/shared
на body больше 16 MiB и на меньшем body с JSON escaping, дающим сообщение больше 16 MiB.
Свой message chunking или data-file protocol ради этого не вводится.

Cooldown появляется **только после отказа** startup/load, а не при каждом успешном
launch. Предлагается последовательность 2/4/8/16/30 с, верхний предел 30 с; успешная
готовность/загрузка сбрасывает свой failure counter. Kill только что готового host
не наследует cooldown успешного старта. Одна admitted группа не запускает второй
процесс/attempt после истечения cooldown: нужен новый отдельный вызов.
Load failures координирует живой host, startup failures — metadata под launch lock.
Failure id завершённой load attempt также сохраняется под launch lock до выхода
host, чтобы уже ожидающая cohort не превратила его уход по grace в новый cold load.
Discovery фиксирует admission attempt/failure id до ожидания; уже принятый вызов
не переключается на новую попытку после её отказа или истечения cooldown.
Рекорд содержит attempt/failure id и OS-boot-scoped monotonic timestamp; timestamp
из прежней загрузки ОС или недопустимое future/corrupt значение не блокирует ключ.
UTC используется для журналирования. Никакой retry не продлевает общий deadline вызова.

Ошибка **not-sent** означает, что в этой попытке tool request не передан transport;
клиенту можно явно повторить вызов. После начала send без полученного результата —
**outcome-unknown**, включая частичную передачу, запись на диск до crash и потерю ответа.
Успешный send не доказывает исполнение; отсутствие ответа не доказывает его отсутствие.
Никакие tools/call автоматически не replay. Handshake/discovery до исполнения можно
повторять внутри бюджета; следующая явная tool operation — отдельная попытка.

Альтернатива infinite failed Task отвергнута по F-05; автоматическая цепочка перезапусков
и TTL процесса — по scope и task-04. Риск reset/recovery — общая потеря прогрева и
неопределённые disk effects; lifecycle ошибки должны это показывать без ложного успеха.

## 8. MCP-фасад и наблюдаемость — U-C-05

Внешний SDK endpoint всё время принадлежит proxy. Negotiated protocol/capabilities,
lite/full, enabled groups и SDK subscriptions не пересоздаются при switch/recovery.
Для 2025-11-25 и 2026-07-28 сохраняются разные правила list_changed/listen; host не
рассылает свой tools/list поверх коллекции клиента. Plugin schema mismatch отклоняется
до attach. Отсутствующая функция внешнего протокола не эмулируется по догадке.

Корреляция: external session/request → internal connection + generation + UUID request/
progress token. Совпадающие внешние id/token соседних клиентов не совпадают внутри.
Progress/response принимается только для живого call reference исходного connection;
ответ старого A разрешён его незавершённому запросу после switch, но не новому вызову B.
После terminal response/cancel/crash поздние progress отвергаются.
Completion транспорта состязается с ожиданием ответа и завершает его без ожидания
tool deadline. Cancellation отправляется явно с ожиданием отдельного send budget;
автоматический fire-and-forget SDK не считается подтверждением backend cancellation.
Ошибка доставки cancellation фиксируется; повтор записи из неё не следует.

Proxy пишет свои lifecycle/CLI/request логи в исходный `exe/logs`, как сейчас.
Host пишет process/start/load/lease события в `root/<key>/logs`, а не внутрь immutable image.
Запись host содержит собственный host id и переданный proxy harness session id,
internal call id, key digest, generation/epoch, startup/load attempt и стадию отказа.
Harness id исходного proxy определяется непосредственным parent по pitfall 26;
parent host — proxy, это не новый способ угадать harness.
Session-specific parameter/full-output logging остаётся в proxy; host по умолчанию
пишет технические события без копии чужих параметров и ответов.
GC/FirstChance counters процесса помечаются shared, не объявляются стоимостью сессии.
Полные env/command lines и значения credentials в discovery/evidence не сохраняются.

`get_mcp_server_info` отвечает даже при host failure: сначала факты proxy, затем
optional host section с source/key/generation/workspace/load state и причиной недоступности.
`read_log_tail` default остаётся proxy log; host log доступен через явный path из info.
Файлы логов не являются журналом завершённых side effects.
Альтернатива переподключить внешний клиент к новому SDK server отвергнута: потеряла бы сессию.
Риск — неполная доставка cancel/notifications; task-02 проверяет наблюдаемый backend outcome.

## 9. Shadow image, публикация и cleanup

Host работает только из проверенного immutable image под user root, вне repo/publish.
Уникальный staging → проверка полного manifest → атомарный rename → запуск.
Image публикуется под своим image digest, а не только workspace key; несколько keys
одной сборки могут использовать один image. Повреждённый existing image не используется:
под image creation lock готовится новая уникальная instance directory с тем же проверенным
содержимым. Уже исполняемый каталог не чинится/перезаписывается на месте.
Если исходный bundle больше недоступен или сам не совпадает, явная ошибка без fallback.
Новая image instance не меняет content identity и не разрешает второго владельца key.

Каждый proxy/host, использующий image, держит отдельный OS use lease на эту instance.
Cleanup получает exclusive creation/cleanup lock и подтверждает отсутствие всех use
leases, прежде чем удалять instance. Lock-файлы вынесены из удаляемых каталогов и
сами не удаляются. PID и возраст — только подсказки, не разрешение удалить живой image.
Staging без владельца можно убрать после той же проверки. Оставить файл при ошибке
доступа безопаснее удаления активного каталога. Analyzer/plugin generation cleanup
остаётся своим существующим контрактом; новый reaper не чистит их по возрасту.

Обычный proxy продолжает исполняться из исходного publish. На Windows он удерживает
managed DLL; копия одного host этого не исправляет. Рекомендация v1 — **плановая
остановка holders источника перед in-place publish**, как уже делает
[`publish-and-verify.ps1`](../../publish-and-verify.ps1#L48) по pitfall 13.
Процедура явно сообщает, что завершит соответствующие MCP-сессии; проверяет фактический
выход holders и только затем меняет файлы. `-SkipKill` не выдаёт locked publish за успех.
Shadow host не выбирается для kill только по общему имени executable: он может обслуживать
другой живой proxy. Если ушли все его proxy, host завершится по grace.
Перезапуск клиента после publish не сохраняет RAM старого host по обещанию этой темы.

Новый proxy закрепляет новый server manifest и получает иной key; старый host не
объявляется совместимым по одной версии 1.5.18. Живой старый proxy продолжает работу
со своей старой сборкой только пока его каталог не обновляется. E1 проверит publish
holder detection, expected tool count, protocol initialize и log origin из обеих директорий.
README и AGENTS.md.sample должны объяснять reset/stop/restart-required, opt-in,
совместимость и отсутствие hot update живого proxy; ARCHITECTURE получает shipped факты
после E1. Здесь пользовательская документация и скрипт не меняются.

Альтернатива стабильного launcher/versioned payload могла бы сохранить живые proxy
при обновлении исходного bundle, но добавляет packaging/process lifetime механизм.
Она не нужна для нынешнего publish с остановкой holders и пока отвергнута.
Single-file всего сервера запрещён действующей архитектурой dynamic loading.
Риск выбранного простого пути — потеря MCP-сессий при dogfood publish; принять его
должен владелец (O-03). Если требуется непрерывный hot publish, сначала пересматривается этот раздел.

## 10. Покрытие состояния task-00

Инвентарь не переносится целиком в singleton host. Решения для всех **S01–S42**:

| Строки | Владелец и решение |
|---|---|
| S01, S08, S09, S16, S23, S24, S29, S38 | Process/image/toolset/loader identity; reset host заканчивает его CLR history. |
| S02, S03, S07 | Отдельные DI границы host/session/request; effective provenance options в descriptor. |
| S04, S05, S06, S13, S14 | Proxy startup/defaults/CLI state; host получает только подтверждённые inputs. |
| S10, S11, S12, S15, S18, S36, S39 | Общий manager, publication/write/watch/prewarm/load; epoch invalidation сохраняет admission. |
| S17 | Per-session proof state в host и shared output admission; CLI/gates живут в proxy. |
| S19, S20, S21, S22, S25, S26, S40 | Сессия/вызов proxy; host correlation и process-wide counters подписаны явно. |
| S27, S28 | Owned payload/cursor; proxy TTL и происхождение сохраняются после switch. |
| S30 | Scratchpad исходного cwd в proxy; общий файл и межпроцессные эффекты прежние. |
| S31 | Shared reset retires host; stop завершает свой proxy. |
| S32 | Различены server image, plugin copy и analyzer generations; use leases до cleanup. |
| S33 | Request CLI/rg/git в proxy; host lifetime не является токеном их процессов. |
| S34, S35 | Fork settings и trace/window в descriptor; BuildHost не передаёт RAM cache новому host. |
| S37 | Persistent Roslyn index — общий диск; не workspace restore и не build proof. |
| S41 | Один manager/load owner в host; reaper трогает только его проверенных BuildHost children. |
| S42 | Decompiler refs — per-call; NuGet lookup cache не превращается в session proof. |

Неосуществимость universal plugin isolation не скрывается: gate пригодности раздела 4
получает audit конкретного plugin. Политика NuGet lookup freshness не меняется этой темой;
после host restart cache новый. Задержки search prewarm stop и parent/Job kill остаются
реальными ограничениями drain/recovery, а не обещанием выгрузки за 1 с.

## 11. Проверяемые последовательности и приёмка task-02

Это критерии design, а не уже полученные passes. Сначала воспроизводимый red нужного
поведения, затем минимальный green и обязательная validation AGENTS.md.

1. Десять cold proxy одного descriptor: lease до первого MSBuild open, одна physical attempt; разные cwd/env/config/TFM/analyzer policy дают предсказуемые отдельные keys.
2. A → B при удерживаемой загрузке B: вызов A отвечает до release B; оставшийся A proxy сохраняет PID/generation/epoch. Отказ/cancel B не меняет A/defaults.
3. Kill готового host сразу после startup, без 2,1 с задержки: нет ложного cooldown успешного запуска; десять следующих вызовов дают один новый owner/load. B продолжает работать.
4. Смерть launcher до/после lease и host до ready: один heavy owner; один failure для admitted cohort; после failure явный retry без очереди.
5. Shared load failure при подключённых клиентах: старые lazy calls получают тот же failure; новый explicit load с observed failure id даёт одну attempt. Cancel одного/всех waiter подтверждён backend counters.
6. EOF/SIGKILL последнего proxy, reconnect до Closing и во время Closing: нет двух владельцев, host сам выходит на Unix, lease освобождён только после завершения работ/процесса. PID reuse и stale/corrupt metadata не дают kill/takeover чужого процесса.
7. Shared reset при двух клиентах, активном read/write и proxy CLI: новое host generation, старые tickets/proofs запрещены, CLI не убит из-за host retirement. Stop одного proxy оставляет другого.
8. Analyzer V1 → V2 той же identity: restart proxy недостаточен; reset host и новый load исполняют V2 с прежним provenance/publication/write gate. Нет теневых путей на диске проекта.
9. Реальные build/project-test/DLL-test stages до/во время/после host kill и switch: фактический CLI result отдельно от confirmation, output gates переживают host crash, нет proof/replay другого generation.
10. Оба MCP protocols: activation/list_changed/listen, одинаковые IDs/progress tokens соседей, backend cancellation и поздний ответ A после switch. Effect-before-crash оставляет один side effect.
11. Plugin none/Sample: первое tools/list одинаково с ordinary; constructor/startup не грузит workspace proxy. Неаудированный plugin явно отклоняет shared attachment.
12. Полные server/native/plugin manifests, изменённый deps.json, partial/corrupt image, source publish holders и очистка активного image. Другой UID/SID отвергнут даже при ослабленных правах socket; macOS сначала проверяет lock/ACL, иначе disabled.
13. RAM reuse требует **другого живого proxy**, пока первый отключается; новый клиент в пределах grace — отдельная гонка. Saved `.cs` видны, graph stale/generated/write boundary прежние. После ухода всех клиентов повторный load cold.
14. Память N=1/N=10 и latency одинакового semantic query под тяжёлой работой соседей: не меньше пяти повторов сессии 2, все host/proxy/children, GC/runtime/index/prewarm и bases записаны. Отдельно сравнивается ordinary и shared.
15. До этих серий — точный descriptor двух настоящих MCP motivating harness и names-only проверка volatile inputs; результат go/no-go по разделу 2. Positive synthetic pair не заменяет этот контроль применимости.
16. Payload parity >16 MiB/JSON escaping и proxy SIGKILL с продолжающимся CLI: нет нового cap; abandoned activity блокирует DLL-route takeover/proof даже в новом host generation, пока завершение writers не подтверждено.

По коду task-02 обязательны SourceStructure, Release solution build, полная main suite
с актуальными исключениями `AnalyzerLifecycle`/`RequiresPinnedSdk` (на Linux также
`RequiresWindows`), затем AnalyzerLifecycle с `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`.
Unrun platform не становится pass. В эксперименте используется только выбранный
opt-in `ROSLYN_MCP_EXPERIMENTAL_WORKSPACE_HOST=1`; default не входит в shared ветку.
Флаг не означает production activation; E1 получает отдельный план после E0.

## 12. Решения владельца и оставшиеся проверки

| ID | Рекомендация / получатель |
|---|---|
| O-01 | Выбрать выделенный host против нулевого варианта для task-02; роли/HTTP остаются отложенными. |
| O-02 | Принять global retire shared reset и proxy-only stop; объявить reset способом обновить analyzer CLR. |
| O-03 | Принять controlled-stop publish holders; uninterrupted hot publish живых proxy не включать. |
| O-04 | До индивидуального audit ограничить shared plugins=none/Sample, с явным отказом для остальных. |
| V-01 | Task-02: настоящий Roslyn single-flight, session split, write/analyzer admission, graph reload и stage RPC. Fake load этого не доказывает. |
| V-02 | Task-02: macOS primitive/peer credentials и безопасные filesystem opens. Среды macOS здесь нет. |
| V-03 | Task-02: реальные бюджеты, payload parity, output closure/gates и abandoned writer, нагрузка/RAM. Числа не measured SLA. |
| V-04 | Task-02/E1: реконструкция полного server/plugin image и runtime/toolset facts из опубликованного RID bundle. |
| V-05 | После исследования: aggressive GC/остаточный heap/idle unload по отдельному решению владельца. |
| V-06 | Task-02 первым: точный descriptor настоящей MCP-пары; volatile preflight/mismatch возвращает применимость владельцу до механизма/rollout. |

U-C-01…U-C-08 имеют предложенные ответы в разделах 1–10; статус owner decision
до O-01…O-04 остаётся pending; 2026-10-29 владелец отложил этот выбор до отдельных ревью. Результат независимого ревью и исправления фиксируются
в [отчёте task-01](epoch-0-design-and-spike/evidence/task-01-architecture-decision.md).
Новых production bugs при этой docs-only работе не подтверждено. Дефекты spike из
ревью task-04 F-01–F-05/F-08 получают контракты разделов 2–4/7/9 и regressions раздела 11;
они не объявляются исправленным кодом задним числом.
