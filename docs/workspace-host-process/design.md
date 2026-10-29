# Выделенный workspace host: проект архитектурного решения

Дата: **2026-10-29**. База: `b2a7b80`, сервер **1.5.18**, MCP SDK **2.2.0**.
Статус: **proposed; O-05 выбран владельцем 2026-10-29: небольшой WorkspaceKey, приватный fresh и reuse-only диагностика; редакция перепроверена в раундах 8–9. O-01…O-04 отложены, task-06 planned**.
Результат [E0/task-01](epoch-0-design-and-spike/task-01-architecture-decision-hi.md).
Границы темы и решения владельца — в [README](README.md).
Факты действующего сервера ниже отделены от предлагаемого контракта shared-режима.

## Главное

- Кандидат — **opt-in выделенный host**; default остаётся автономным stdio. До окончательного O-01 нужен preflight task-06. O-05 выбран владельцем: общий контекст host и явный private fresh. Роли, handoff, promotion/demotion, HTTP и restore RAM отложены.
- Дублирование настоящего workspace подтверждено task-05. Экономия production shared-режима ещё не измерена; task-02 сравнит все процессы при N=1/N=10, включая цену отдельного host.
- Proxy — обычный сервер с Roslyn/plugins, но без загруженного workspace. Его MCP-сессия, activation, defaults, CLI и отчёты остаются локальными.
- WorkspaceKey содержит path/Configuration/Platform/TFM; environment не хешируется для выбора workspace. Image/plugin/protocol admission проверяется отдельно. `--force_fresh`/`load_workspace(forceFresh=true)` создаёт отдельный host только для инициатора. Reused load сообщает actual context и инструкцию; создатель нового host дополнительный блок не получает.
- Switch, включая path-варианты `list_projects`/`get_project_graph`, готовит B вне gate привязки. Отказ B сохраняет живой A; metadata writes общего graph меняют epoch и запрещают прежние tickets/proofs всех сессий.
- Shared load имеет один attempt и независимых waiter. После отказа следующий явный `load_workspace` клиента, уже получившего этот отказ, может открыть одну новую попытку без отключения всех клиентов.
- Предлагается обычный reset через общую epoch invalidation, с сохранением процесса host; retire — при подтверждённом restart-required или неисправимом admission. Перезагрузка graph всё равно стоит ordinary load; вариант O-02 требует выбора владельца.
- Падение host не убивает CLI в живом proxy. Успех CLI и подтверждение build proof — разные результаты; доказательства не переносятся в новое поколение.
- Socket/locks отделены от storage image/logs: Unix images не помещаются в `/tmp`. Проверенный launcher snapshot может переиспользоваться после audit, иначе создаётся проверенная копия в storage; Windows publish останавливает holders источника.
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
| Выделенный host | N proxy и один workspace на ключ. | Нужны IPC, session split и recovery. | Кандидат после task-06 и выбора O-02/O-05. |
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

Решение владельца **2026-10-29, O-05**: обычное обнаружение использует небольшой
ключ workspace, без хеша inherited environment и без автоматического разделения
по session/terminal metadata. Все факты ниже — контракт будущей реализации.

`WorkspaceKey = (absolute workspace path, Configuration, canonical Platform, TFM)`.
Путь допускает `.sln`, `.slnx` и `.csproj`; defaults разрешаются по правилам ниже.
`null` остаётся «global property не передано», не подменяется предполагаемым Debug.
Каноническая сериализация задаёт порядок полей, длины UTF-8 строк, явный `null` и
порядок списков. Разные checkout имеют разные абсолютные пути.

От ключа workspace отделены namespace и идентичность исполняемого кода:
текущий SID/effective UID, версия внутреннего протокола и полный verified server/
plugin image contract проверяются до attach. Совпадение WorkspaceKey не разрешает
вызовы между разными сборками/схемами tools или обход ACL. MSBuild/runtime facts,
cwd, environment и host settings относятся к зафиксированному контексту host,
а не создают дополнительные workspace keys. Image proof §2/§9 и analyzer/write
admission сохраняются; O-05 не отменяет их.

`HostKey = SHA256(compatibility namespace, WorkspaceKey, instance scope)`.
Обычный scope — `shared`. Для явного отдельного host scope содержит новый случайный
owner-bound instance id. Ниже «один host на key» означает HostKey: несколько
приватных экземпляров одного WorkspaceKey разрешены только по явному fresh.
При handshake сравниваются WorkspaceKey, namespace, scope и HostGeneration;
PID/имя канала не являются доказательством совпадения.

<a id="политика-environment--o-05"></a>
### Контекст host и O-05

Host наследует среду, cwd и effective host settings инициатора создания.
Последующее подключение не меняет этот контекст. Proxy/его CLI сохраняют собственную
среду. Автоматическая очистка session/terminal variables (`canonical-v1`) и полный
environment digest (`strict`) **не выбраны владельцем** и не являются контрактом v1.
Имена чатов, PID и terminal/login metadata не дробят обычный WorkspaceKey.

Цена решения: общий workspace не обещает воспроизвести индивидуальную среду каждого
proxy. Проект/custom task/plugin может читать произвольные переменные; отсутствие
различий в показанных параметрах не доказывает environment parity. CLI proxy и
Roslyn host могут различаться по скрытому input. Это осознанный предел O-05:
агент видит фактический контекст при reuse и при необходимости запрашивает fresh.
Журналы MSBuild не объявляются универсальным detector прямых Environment reads.
Успех CLI по-прежнему не создаёт build proof без проверок host (§6).

В [раунде 5](review-task-01-round-5-2026-10-29/result.md) наблюдались 11 различий среды
двух MCP Claude Code/Linux в разных checkout. Эти данные не доказывают совпадение
workspace keys; прежние варианты canonical/strict сохранены в историческом ответе
на ревью, но заменены настоящим решением владельца.

### Явный отдельный host — `--force_fresh`

В экспериментальном shared-режиме `--force_fresh` при запуске MCP выбирает
**новый приватный host**, а не автономную загрузку workspace внутри proxy.
Host доступен только инициирующему proxy, не публикуется как общий кандидат и не
заменяет существующий shared host. Другие клиенты остаются на прежнем экземпляре.
Ключ workspace прежний; новый instance scope отделяет ownership/discovery/locks.
Private endpoint проверяет owner-bound capability, а не только PID.

Чтобы агент мог выполнить инструкцию без изменения harness config/перезапуска MCP,
планируется аргумент `load_workspace(forceFresh=true)`. Он создаёт новый private
instance и переключает только вызывающий proxy после успешного load. Отказ/отмена
сохраняет прежнюю живую привязку. Один explicit fresh intent имеет один instance id
на все свои startup retries; следующий explicit fresh создаёт другой instance.
Это дополнительный opt-in аргумент будущего spike, сейчас его в production нет.

После успешного fresh omitted/false не возвращает тот же proxy обратно в shared:
повторный load текущего WorkspaceKey переиспользует его private host. Startup flag
делает private также новые workspace bindings этой сессии; он не пересоздаёт host
на каждом cached load. Crash recovery сохраняет private scope/owner и создаёт новое
HostGeneration, не присоединяется к общему host. Другие proxy не восстанавливают
этот private instance. После ухода owner действуют startup allowance/closing grace §7.
Несколько fresh увеличивают RAM; общие файлы/outputs всё равно защищаются gates §6.

Private intent хранится в proxy **отдельно от binding и SessionLoadContext**:
`PrivateScopeIntentByWorkspaceKey` содержит owner/instance scope и capability для
успешно выбранных private keys. Запись появляется только при успешном fresh commit;
новый successful fresh того же key заменяет её после сохранения старых call references.
Reset defaults/unbind, soft reset, hard retire и cleanup host эту запись не удаляют.
Она живёт до завершения proxy session; секрет capability не пишется в discovery/logs.
При следующем explicit/config lazy load, разрешившемся в этот же WorkspaceKey,
proxy использует private intent, даже если ни прежнего binding, ни host уже нет.
Новый generation создаётся только для owner. Intent не восстанавливает очищенные
load defaults и не подменяет правила разрешения аргументов: если они дают другой
WorkspaceKey, это другой выбор; startup flag требует private для всех новых keys.
Reset не является командой возврата в shared. Failed fresh не меняет прежний intent.

### Ответ `load_workspace` при подключении к существующему host

**Уточнение владельца:** дополнительный блок параметров и инструкция выводятся
только если текущий proxy **переиспользовал host**, созданный другим инициатором.
Создатель нового shared/private host этот дополнительный блок не получает.
Обычный результат load сохраняется в обоих случаях; речь только о новом reuse block.

Признак `created`/`reused` относится к происхождению текущей привязки этого proxy,
подтверждённому startup attempt/handshake, а не к `cached=true` и не к факту MSBuild
load. Проигравший гонку создания — reused. У proxy, присоединившегося к чужому host,
блок присутствует при каждом успешном explicit load текущей привязки, включая cache
hit; `briefOutput=true` его не скрывает. После recovery признак определяется заново.

Блок берётся из фактического host receipt той же generation/epoch, что ответ load:

- абсолютный workspace path, effective Configuration/Platform/TFM global properties;
- для отсутствующего global property — явное «не передано MSBuild; project defaults»,
  без выдуманного единого Debug/TFM для всех проектов;
- server image/version, runtime/SDK/MSBuild, startup cwd и config origin;
- действующие analyzer/overlay policy и host settings, влияющие на load;
- HostGeneration/WorkspaceEpoch, reuse mode и факт, что inherited environment
  принадлежит создателю host и не был заменён средой этого proxy;
- отдельно session-only buildArgs, чтобы не представить их общим параметром host.

Сырые env values, credentials и полные command lines не выводятся. Блок не утверждает
полное совпадение environments. Инструкция агенту на английском: проверить actual
context против ожидаемого; при различии вызвать `load_workspace` с теми же нужными
path/Configuration/Platform/TFM и `forceFresh=true`. Он создаст отдельный host только
для этой сессии; общий host/соседи не сбрасываются. Нельзя советовать reset общего
host или только повторный обычный load как способ гарантировать изоляцию.

При reuse явно запрошенную несовместимую host policy нельзя молча проигнорировать: attach/load
возвращает context-mismatch с actual context и той же инструкцией, не меняя прежнюю
привязку. False/omitted overlay после sticky true своей сессии трактуется по правилам
ниже, а не как новое требование отключить overlay. Schema/plugin/image mismatch
остаётся отказом admission; fresh не является разрешением обойти эти проверки.

**Go/no-go до O-01:** [task-06](epoch-0-design-and-spike/task-06-environment-and-deployment-preflight-med.md)
проверяет простые keys/reuse разных session environments, отдельный private fresh,
created/reused ветви ответа на Codex/Windows и Claude Code/Linux, а также storage/
startup identity. Это не доказательство универсальной parity проектов.

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

Shared допускается только при доказанной immutable publication/use lease bundle,
непрерывно действующей **до OS CreateProcess**, включая первоначальную загрузку
exe/server assemblies. Startup guard до MSBuild/Roslyn/plugins закрепляет image
identity **один раз на процесс/образ** из замороженного bundle или audited publisher
manifest (§9), без полного повторного hash на каждом attach/RPC. Guard должен связать
manifest с фактически запускаемым image; lock, взятый лишь в managed Program после
загрузки exe/server assemblies, не доказывает эту границу.

Mutable source без такого доказательства получает `deployment-unverified` при
попытке shared и требует restart через verified image; default ordinary сохраняется.
Поздний hash при первом shared call не может присвоить новым файлам identity уже
исполняемого старого CLR кода. mtime/size и MVID также не заменяют content proof.
Изменение закреплённого source требует restart proxy. При создании копии hash
считается вместе с копированием и сверяется с закреплённым manifest.
Действующий Windows controlled-stop publish сам по себе не доказывает prelaunch
lease: task-06 проверяет наличие этой границы или фиксирует no-go до O-01.

Новый host запускается с cwd, исходной средой и effective host options инициатора.
Он проверяет фактические MSBuild/runtime/plugin facts до readiness. Поздний клиент
принимает закреплённый контекст host по O-05, а не меняет SDK под себя. Изменение
global.json на диске не превращает уже закреплённый SDK в новый; actual toolset
сообщается в reused load. Для иного стартового контекста нужен fresh/restart;
никакой host не объявляет несовпавший фактический toolset успешно воспроизведённым.
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

Overlay — неизменяемая политика созданного host, не дополнительное поле
WorkspaceKey. True при создании включает overlay; false/omitted после sticky true
той же сессии/graph сохраняет его. На другом workspace запрос начинает policy
явного аргумента, default false. Если выбранный общий host не может выполнить
запрошенную policy, нужен явный mismatch/fresh, а не изменение policy соседей.
Reset очищает sticky request своей сессии, но не переключает политику живого host
для других клиентов. Mapping/provenance GUID не передаются как готовое разрешение.

Полный inherited descriptor отвергнут владельцем как правило выбора workspace:
он дробит общий host по служебной среде. Риск небольшого ключа — иной host context;
reuse block делает известные параметры явными, fresh даёт изоляцию, но не detector
всех скрытых inputs. Эти границы проверяют task-06/task-02.

## 3. Владение, транспорт и безопасность — U-C-04/U-C-05

Есть два корня. **RendezvousRoot** на Unix — `/tmp/roslyn-mcp-<euid>/v1`, независимо
от TMPDIR: только locks, socket, bounded discovery/activity metadata. Image/staging/logs
туда не попадают. **StorageRoot** на Linux — `${XDG_CACHE_HOME}/roslyn-mcp/workspace-hosts/v1`,
по умолчанию `~/.cache/roslyn-mcp/workspace-hosts/v1`; относительный XDG_CACHE_HOME
игнорируется по [XDG specification](https://specifications.freedesktop.org/basedir/latest/).
На macOS StorageRoot — user cache `~/Library/Caches/RoslynMcpServer/WorkspaceHosts/v1`.
На Windows оба namespace находятся под `LocalApplicationData/RoslynMcpServer/WorkspaceHosts/v1`,
с отдельными runtime/storage подкаталогами. Путь StorageRoot сам по себе не меняет
graph identity; используемый host сообщает фактическую image/log location.

Оба root создаются с owner-only доступом: DACL SID владельца на Windows, mode 0700 и
проверенный UID на Unix. Lock/config files — 0600 на Unix. Проверяются существующие
roots, владельцы и отсутствие reparse/symlink подмены; открытия фиксируют проверенные
объекты, а не повторяют незащищённый path check. При невозможности проверки — отказ shared.
Сетевой filesystem не поддерживается. Storage проверяет actual backing FS: tmpfs/ramfs
и noexec для executable image отклоняются до копирования. Одно имя `.cache` не доказывает
дисковое размещение. До staging проверяется available space относительно размера
планируемого образа; ENOSPC во время copy остаётся явным отказом с cleanup только своего
staging, без partial launch и без fallback в `/tmp`. Успешный preflight не резервирует место.
Rendezvous допускает memory-backed/noexec FS: исполняемых образов там нет; очистка
его активных lock/metadata запрещена до завершения владельцев, boot-old записи не reused.
Непроверенная platform FS проверка остаётся task-06/task-02, не pass по названию каталога.
Угроза — другой OS user; код того же
пользователя уже имеет доступ к исходникам/процессам и не считается sandboxed.

Под RendezvousRoot лежат `<full-key>/launch.lock`, `host.lock`, bounded metadata и socket.
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

До SDK идёт length-prefixed preface v1: полный HostKey/WorkspaceKey, namespace/image
facts, instance scope, generation ответа, session correlation и limits. Environment
digest в нём не проверяется. Максимум 16 KiB; сырые environment
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

### Инструменты с аргументом workspacePath

`list_projects` и `get_project_graph` сейчас вызывают `LoadAsync`, если передан
непустой workspacePath ([UtilityTools](../../Tools/UtilityTools.cs#L1412)). Поэтому
их нельзя пересылать целиком в текущий host как безусловные read-only операции.
В shared proxy сначала разрешает путь и defaults своей сессии по разделу 2.
Если полученный key отличается от текущего, он проходит тот же attach/load/commit
A → B, что `load_workspace`, включая успешную загрузку до commit и сохранение A
при отказе/cancel B. Только после этого read-часть исполняется на B по закреплённому
call reference. У оставшегося на A клиента сохраняются path, generation, epoch и policy.

Если key совпал, разрешён coordinated load/refresh этого key: cache hit читает
текущий published graph, graph-stale reopen проходит single-flight раздела 7.
Переключение policy по чужому Loaded* запрещено, sticky правила раздела 2 сохраняются.
На host вызывается read-часть без повторного `LoadAsync(workspacePath)` и без
возможности заменить его descriptor. Omitted workspacePath читает только уже
выбранный graph; unbound без явного path сохраняет нынешний ответ «No workspace loaded»,
не начинает новую lazy load ради этих двух инструментов. `get_test_list` остаётся
отдельным semantic reader со своей прежней lazy/disk-sync семантикой.

### Общая admission и plugins

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
| `load_workspace`, `reset_workspace` | Proxy: defaults/switch; host: load, soft reset или reason-bound hard retire по разделу 7. |
| `get_mcp_server_info`, `stop_mcp_server` | Proxy: локальные факты/stop; remote facts info явно подписаны. |
| `get_class_skeleton`, `get_diagnostics_for_file`, `find_symbol_definition`, `find_usages`, `find_symbol_references`, `find_implementations`, `get_call_graph` | Host: published snapshot; owned cursor continuation обслуживается по своему origin. |
| `add_using`, `add_method_to_class`, `update_method_body`, `get_code_fixes`, `apply_code_fix`, `rename_symbol`, `generate_test_method_stub` | Host: semantic/write boundary исходной epoch. |
| `list_projects`, `get_project_graph` | Proxy: optional-path attach/switch или same-key refresh по §4; host: read-часть выбранного graph без неявного LoadAsync. |
| `get_test_list` | Host: опубликованный graph и disk sync. |
| `get_file_content`, `read_file_range`, `get_method_body` | Proxy: абсолютный путь от захваченного workspace directory либо cwd. |
| `get_code_skeleton`, `list_directory_tree` | Proxy: путь от cwd как сейчас, parser допустим. |
| `search_code` | Host разрешает omitted graph roots; proxy исполняет disk/rg с готовыми путями. |
| `get_changed_files` | Proxy исполняет git; host исходного call snapshot разрешает omitted anchor и вычисляет test suggestions по полученным путям. |
| `update_file_content`, `apply_patch` | При binding — host с прежним tracked write gate; unbound — прежний standalone disk path proxy. |
| `rename_project`, `add_package_reference`, `remove_package_reference` | При binding — host с общей epoch invalidation по §7; unbound disk path допустим лишь там, где он уже поддержан. |
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

`get_changed_files` сохраняет и Git paths, и **Suggested test projects (heuristic)**:
сейчас [SuggestTestProjects](../../Services/GitChangedFilesHelper.cs#L93) читает
`solution.Projects` и документы. При приёме вызова host закрепляет snapshot/call
reference исходного binding; proxy выполняет git с захваченным anchor, затем передаёт
разобранные относительные пути этому же host. Host вычисляет heuristic по закреплённому
snapshot; proxy форматирует готовый список прежним способом. Switch proxy на B во
время git не заменяет snapshot A. Явный git workspacePath другого repo сам по себе
не запускает attach/switch: heuristic использует graph исходного binding, как сейчас.
Без graph список пуст и host не создаётся. Потеря host до получения heuristic при
ранее закреплённом graph даёт явную ошибку полноты с сохранённым результатом git,
без молчаливой замены неизвестного списка пустым и без повторного запуска git.

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

### Очистка graph после project/package mutations

`add_package_reference`, `remove_package_reference` и применённый `rename_project`
сейчас вызывают [ClearWorkspaceAsync](../../Tools/ProjectTools.cs#L86). Это общий
эффект при binding, даже если изменённый projectPath не входит в активный graph;
dry-run rename его не вызывает. Private исполнение этих методов проходит host
coordinator, а не обходит его прямым вызовом нынешнего adapter целиком.

1. До возможной записи coordinator закрывает admission старой epoch и согласует другие уже принятые semantic/write операции; proxy CLI не ждёт/не получает cancellation от этого drain.
2. Перед первым возможным disk effect увеличивается `WorkspaceEpoch`. Все session tickets, write contexts и DLL-proof state прежней epoch становятся недействительными; per-session `SessionBuildState` заменяются пустыми экземплярами. Одного последующего `Observe` или очистки полей manager недостаточно.
3. Host выполняет mutation и clear под этим admission. Всем привязанным proxy посылается invalidation с key, generation, новой epoch и причиной. Потерянное уведомление не разрешает старый ticket: каждый stage RPC проверяет epoch на host.
4. Успешный clear переводит key в `Invalidated(epoch)`: path/Loaded*/mapping manager очищены, но descriptor host и defaults каждой proxy session сохранены отдельно. Следующая допустимая load/prepare этого key — одна общая attempt через раздел 7, с его закреплённой analyzer policy, а не тихий `LoadAsync` первого клиента.
5. Старые semantic/build-proof tickets отклоняются как workspace-invalidated. До исполнения нового вызова bound proxy может присоединиться к одной prepare/load своего сохранённого key; новые tickets выдаются после publication. Unbound использует только собственный explicit/config lazy load. Это восстановление до исполнения, без replay и смены descriptor остальных сессий.

Уже запущенный proxy CLI может завершиться; подтверждение его старого ticket после
шага 2 запрещено, в том числе после следующего load и в том же HostGeneration.
Output gates и abandoned-writer records не очищаются от этой invalidation.
После частичной записи, cancellation или ошибки clear старый graph/proof не
объявляется пригодным: admission остаётся закрытым до завершённого cleanup/reload
либо retire host. Эпоха не откатывается, disk effects не replay.

HostGeneration при такой очистке не меняется, CLR analyzer assemblies не выгружаются;
это не hard retire по `reset_workspace` ниже. Новый load по-прежнему
проверяет analyzer admission; restart-required требует retire host.
После успешной mutation/clear ответ вызывающему сохраняет прежнее указание вызвать
`load_workspace`. Отказ очистки не выдаётся за успешно очищенный cache.
Если rename переместил сам loaded `.csproj`, исходный key не меняет путь на месте:
load нового пути проходит attach/switch к другому key; вызов старого пути может
отказать. Rename только проекта внутри сохранённой `.sln` допускает reload её
прежнего key с новой epoch. Остальные сессии не перенаправляются на новый путь молча.

### Общая загрузка и lifetime

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

**`reset_workspace` в shared — предлагаемый вариант O-02: обычный soft reset.**
Он использует общую epoch invalidation выше: закрыть admission, дождаться других
принятых semantic/write calls, clear graph и proofs, уведомить proxy, оставить
`Invalidated(new epoch)`. HostGeneration/CLR сохраняются, пока есть подключённые
клиенты. Если drain за 30 с не завершён, reset возвращает busy до clear/epoch change;
он не убивает работающий host только ради deadline. После начала clear ошибка
оставляет admission fail-closed по общему протоколу.

Reset не запускает eager load. Следующий bound workspace request или explicit load
входит в одну shared attempt. Reset со ссылкой на уже invalidated исходную epoch либо
на её ещё не опубликованного преемника присоединяется к тому же сбросу, без очереди
из N reload; запоздавший reset не очищает уже опубликованного преемника.
Новый reset, принятый после новой publication и с её epoch, может потребовать ещё одну
загрузку: её ненужность не доказана.
Инициатор после успешного reset очищает свои loaded defaults/buildArgs и становится
unbound; file defaults по-прежнему допускают его config lazy load. Остальные сохраняют
свои descriptor/defaults и заново проходят prepare. Если после detach инициатора
подключений не осталось, обычная grace может завершить host до следующего load.

**Hard retire** тем же reset допускается при подтверждённом host restart-required
analyzer либо неисправимом состоянии admission/cleanup. Host хранит этот reason до
выхода процесса; ответ/info явно указывают общий эффект и новое поколение после restart.
Обычный отказ загрузки проекта остаётся shared LoadFailed с explicit retry, а не
поводом автоматически менять процесс. Если same-identity update обнаружен лишь при
следующем load, он сначала возвращает restart-required; повторный reset выполняет retire.
Retire закрывает admission, уведомляет proxy, drain-ит работы и завершает host;
повторный retire относится только к исходному generation и не закрывает преемника.
Hard retire может прервать calls с неопределённым исходом; replay запрещён.

**`stop_mcp_server` в shared** — прежний ответ с последующим stop только своего
stdio proxy. Он освобождает connection; остальных не останавливает.
Обычный restart proxy при живом host analyzer не обновляет. Default reset/stop сохраняют
нынешнюю in-process семантику. Soft reset сохраняет процесс, **не обещает сохранения
прогрева уничтоженного graph**: следующий ordinary load может стоить минуты.
В AGENTS.md.sample reset рекомендуют после rename, изменения состава `.cs` и generated
outputs, но не после каждого build/обычного save. Частота реальных reset не измерена.
Альтернатива O-02 — прежний global retire на каждый reset: проще, но сбрасывает также
CLR и может прервать соседние calls. Task-02 измеряет частый reset→load при N=10,
latency соседей, число physical load/retire; цена вариантов не считается измеренной заранее.

Crash host: все привязанные in-flight host calls получают отказ; следующий отдельный
вызов проходит discovery/single-flight для **своего сохранённого descriptor**.
Здесь сохраняются WorkspaceKey, namespace и instance scope, но не обещание восстановить
чужой environment/cwd/settings после смерти процесса. Для shared новый host получает
контекст proxy, **выигравшего новый startup attempt**. Если создатель прежнего host A
ушёл, B(E2) или C(E3) могут стать новым создателем; их контекст не выдаётся за старый E1.
Победитель сохраняет created receipt; остальные получают reused receipt нового
generation. Перед возобновлением допустимого вызова каждый proxy проверяет actual
receipt и свои explicit/sticky host-policy требования; несовместимость даёт
context-mismatch/fresh guidance до исполнения. Старые tickets/proofs не переносятся.
Изменение неключевого environment принимается по O-05, без обещания detector всех
inputs. Actual context обновляется в binding/get_mcp_server_info; дополнительный
reuse block появляется только в следующем explicit load_workspace у reused клиента,
не добавляется в semantic response и не выводится новому создателю.
Private recovery использует сохранённый owner intent §2, не общий discovery slot.
Восстановление не меняет внешний MCP initialize и не запускает workspace в каждом proxy.
Нет автономного background restart loop. При смерти launcher/host до readiness
одна группа уже ожидающих вызовов получает startup failure; следующий вызов после
cooldown может открыть следующую attempt. Если конкурирующий ребёнок просто проиграл
живому владельцу lease, это не crash failure этого ключа.

### Бюджеты, ошибки и cooldown

Значения ниже — **предлагаемые инженерные limits**, не результаты Roslyn-замера.
Task-02 проверяет их и приводит измерения; публичные настройки этих timeout не
добавляются. Выбранный forceFresh описан отдельно в §2.

| Стадия | Предлагаемый предел |
|---|---|
| Guarded startup identity | До 30 с на pinned manifest/validation, до SDK facade start; результат кешируется на процесс/образ. |
| OS connect | 5 с на попытку. |
| Preface handshake | Отдельные 5 с после connect. |
| Internal MCP initialize | Отдельные 15 с после handshake. |
| Discovery/start/readiness | Общие 30 с на shared call, включая locks, image copy/hash/verify и MCP readiness. |
| Physical load/prepare | 10 минут на общую attempt, отдельно от discovery. |
| Semantic call | Прежний call cancellation; дополнительного общего 15 с / 2 мин IPC timeout нет. |
| CLI | Нынешний timeout инструмента включает stage RPC, gates, build/test и cleanup по прежним правилам. |
| Cancellation delivery | Отдельные 2 с с независимым токеном. |
| Soft reset drain / hard retire | 30 с; soft reset до clear возвращает busy, hard retire после budget завершает свой host. |
| Нет первого / последнего клиента | 30 с startup allowance / 1 с closing grace. |
| Internal NDJSON | Те же logical payloads, что принимает внешний SDK; нового общего size cap нет. |

Startup identity должна уложиться также в budget MCP initialize конкретного harness;
30 с не разрешают превысить его. Она включается в time-to-first-useful от запуска
процесса. Discovery/copy/verify ограничены ещё и **оставшимся бюджетом вызова**;
существующий CLI timeout не продлевается. Отдельно логируются identity, lock wait,
image copy/verify, initialize и cached reuse: elapsed, files, bytes, cache mode.
Task-06 измеряет guarded startup и cold/warm identity, task-02 — весь attach и
time-to-first-useful. Непроверенный запуск нельзя легализовать поздним hash.
Ни stat cache, ни истечение бюджета не разрешают непроверенную identity/partial image.

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
Host пишет process/start/load/lease события в `StorageRoot/logs/<key>`, вне immutable image;
RendezvousRoot log payload не хранит. Retention сохраняет ограничение rolling logs.
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

Host работает из проверенного immutable image в **StorageRoot**, вне изменяемого
repo/publish; rendezvous `/tmp` используется только для малого runtime state (§3).
Уникальный staging → проверка полного manifest → атомарный rename → запуск.
Image публикуется под своим image digest, а не только workspace key; несколько keys
одной сборки могут использовать один image. Повреждённый existing image не используется:
под image creation lock готовится новая уникальная instance directory с тем же проверенным
содержимым. Уже исполняемый каталог не чинится/перезаписывается на месте.
Если исходный bundle больше недоступен или сам не совпадает, явная ошибка без fallback.
Новая image instance не меняет content identity и не разрешает второго владельца key.

Проверка source identity выполняется по pinned manifest (§2). Новая копия хешируется
при чтении/копировании, до публикации сверяется её полный manifest; повторный полный
hash на каждом attach не нужен для уже проверенного immutable image под use lease.
Обычный каталог с одними совпавшими stat stamps таким образом не признаётся trusted.
Все эти фазы входят в identity/discovery budgets §7; превышение даёт явный отказ.
Изменённые bytes с прежними size/mtime должны быть пойманы при первоначальной проверке/
создании image; проверенный image далее нельзя менять в обход immutable/use-lease контракта.

Каждый proxy/host, использующий image, держит отдельный OS use lease на эту instance.
Cleanup получает exclusive creation/cleanup lock и подтверждает отсутствие всех use
leases, прежде чем удалять instance. Lock-файлы вынесены из удаляемых каталогов и
сами не удаляются. PID и возраст — только подсказки, не разрешение удалить живой image.
Staging без владельца можно убрать после той же проверки. Оставить файл при ошибке
доступа безопаснее удаления активного каталога. Analyzer/plugin generation cleanup
остаётся своим существующим контрактом; новый reaper не чистит их по возрасту.

### Варианты развёртывания

Обычный Windows proxy исполняется из исходного publish. Он удерживает managed DLL;
копия одного host этого не исправляет. Допуск такого proxy к shared требует ещё
доказанной startup boundary §2; controlled-stop script не заменяет prelaunch lease.
Рекомендация по обновлению — **плановая
остановка holders источника перед in-place publish**, как уже делает
[`publish-and-verify.ps1`](../../publish-and-verify.ps1#L48) по pitfall 13.
Процедура явно сообщает, что завершит соответствующие MCP-сессии; проверяет фактический
выход holders и только затем меняет файлы. `-SkipKill` не выдаёт locked publish за успех.
Shadow host не выбирается для kill только по общему имени executable: он может обслуживать
другой живой proxy. Если ушли все его proxy, host завершится по grace.
Перезапуск клиента после publish не сохраняет RAM старого host по обещанию этой темы.

На Linux у владельца уже есть внешний launcher `~/.local/share/roslyn-mcp/launch.sh`:
по [замеру раунда 5](review-task-01-round-5-2026-10-29/linux-env-and-image-probe.json)
proxy стартует из snapshot в `~/.cache/roslyn-mcp/snapshots/<hash>` с shared flock.
Тогда вторая копия **не обязательна**: task-06/E1 проверяют полный publisher manifest,
owner/FS/permissions, immutable publication от момента до CreateProcess, content identity, config origin и
совместимый use-lease/cleanup протокол для launcher/proxy/host. В частности, flock
нельзя считать совместимым с независимым fcntl lock на другом файле. При подтверждении
контракта host использует ту же instance и берёт свой lease до потери lease launcher.
Имя каталога `<hash>` и наличие любого flock сами по себе доказательством не являются.
Если reuse audit не пройден, собственная копия в StorageRoot допустима только при
доказанном происхождении proxy по §2. Иначе shared отклоняется до restart через
verified image; копия host не легализует неизвестный proxy. Чужие snapshot/launcher
cleanup не изменяются и не удаляются новым reaper.

Замеренный snapshot включает вложенные RID outputs: 2.0G/4868 файлов против
93M/392 файлов выбранного build output. Это отдельный [L-01](README.md#l-01-launcher),
владелец — сопровождающий Linux launcher. Исправление скрипта вне этого checkout
сюда не включается. Task-06 проверяет deployment file set по runtime/publish dependencies,
а не исключает все подкаталоги: native/BuildHost/plugin зависимости должны сохраниться.

Новый verified proxy закрепляет новый server manifest и получает иной key; старый host не
объявляется совместимым по одной версии 1.5.18. Живой старый proxy продолжает работу
со своей старой сборкой только пока его каталог не обновляется. E1 проверит publish
holder detection, expected tool count, protocol initialize и log origin из обеих директорий.
README и AGENTS.md.sample должны объяснять reset/stop/restart-required, opt-in,
совместимость и отсутствие hot update живого proxy; ARCHITECTURE получает shipped факты
после E1. Здесь пользовательская документация и скрипт не меняются.

Альтернатива стабильного launcher/versioned payload могла бы сохранить живые proxy
при обновлении исходного bundle, но добавляет packaging/process lifetime механизм.
Ради hot update она пока не включена в scope. Отдельно task-06 обязан доказать
startup identity обычного deployment: если prelaunch boundary отсутствует,
потребуется пересмотр deployment/O-03 до выбора O-01, а не позднее хеширование DLL.
Single-file всего сервера запрещён действующей архитектурой dynamic loading.
Риск выбранного простого пути — потеря MCP-сессий при dogfood publish; принять его
должен владелец (O-03). Если требуется непрерывный hot publish, сначала пересматривается этот раздел.

## 10. Покрытие состояния task-00

Инвентарь не переносится целиком в singleton host. Решения для всех **S01–S42**:

| Строки | Владелец и решение |
|---|---|
| S01, S08, S09, S16, S23, S24, S29, S38 | Process/image/toolset/loader identity; только hard retire заканчивает CLR history. |
| S02, S03, S07 | Отдельные DI границы host/session/request; effective provenance options — закреплённый host context. |
| S04, S05, S06, S13, S14 | Proxy startup/defaults/CLI state; host получает только подтверждённые inputs. |
| S10, S11, S12, S15, S18, S36, S39 | Общий manager, publication/write/watch/prewarm/load; epoch invalidation сохраняет admission. |
| S17 | Per-session proof state в host и shared output admission; CLI/gates живут в proxy. |
| S19, S20, S21, S22, S25, S26, S40 | Сессия/вызов proxy; host correlation и process-wide counters подписаны явно. |
| S27, S28 | Owned payload/cursor; proxy TTL и происхождение сохраняются после switch. |
| S30 | Scratchpad исходного cwd в proxy; общий файл и межпроцессные эффекты прежние. |
| S31 | Soft reset инвалидирует epoch; hard retire по restart-required/неисправимому admission, stop завершает свой proxy. |
| S32 | Различены server image, plugin copy и analyzer generations; use leases до cleanup. |
| S33 | Request CLI/rg/git в proxy; host lifetime не является токеном их процессов. |
| S34, S35 | Fork settings и trace/window в host context/reuse receipt; BuildHost не передаёт RAM cache новому host. |
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

1. Десять cold proxy одного WorkspaceKey/namespace: lease до первого MSBuild open, одна physical attempt независимо от session env. Другие path/Configuration/Platform/TFM выбирают другой WorkspaceKey; несовместимая image/schema не проходит admission. Host context не меняется при attach; policy mismatch даёт actual context/fresh guidance без скрытой смены graph.
2. A → B при удерживаемой загрузке B: вызов A отвечает до release B; оставшийся A proxy сохраняет PID/generation/epoch. Отказ/cancel B не меняет A/defaults.
3. Kill готового host сразу после startup, без 2,1 с задержки: нет ложного cooldown успешного запуска; десять следующих вызовов дают один новый owner/load. B продолжает работать.
4. Смерть launcher до/после lease и host до ready: один heavy owner; один failure для admitted cohort; после failure явный retry без очереди.
5. Shared load failure при подключённых клиентах: старые lazy calls получают тот же failure; новый explicit load с observed failure id даёт одну attempt. Cancel одного/всех waiter подтверждён backend counters.
6. EOF/SIGKILL последнего proxy, reconnect до Closing и во время Closing: нет двух владельцев, host сам выходит на Unix, lease освобождён только после завершения работ/процесса. PID reuse и stale/corrupt metadata не дают kill/takeover чужого процесса.
7. Soft reset при двух клиентах, активном read/write и proxy CLI: прежний HostGeneration при живом соседе, новая WorkspaceEpoch, старые tickets/proofs запрещены. Другие принятые calls завершаются до clear; drain timeout до clear возвращает busy без убийства host. Stop одного proxy оставляет другого.
8. Analyzer V1 → V2 той же identity: restart proxy и soft reset не объявляются CLR unload. После подтверждённого restart-required hard reset retires host, новый generation исполняет V2 с прежним provenance/publication/write gate. Нет теневых путей на диске проекта.
9. Реальные build/project-test/DLL-test stages до/во время/после host kill и switch: фактический CLI result отдельно от confirmation, output gates переживают host crash, нет proof/replay другого generation.
10. Оба MCP protocols: activation/list_changed/listen, одинаковые IDs/progress tokens соседей, backend cancellation и поздний ответ A после switch. Effect-before-crash оставляет один side effect.
11. Plugin none/Sample: первое tools/list одинаково с ordinary; constructor/startup не грузит workspace proxy. Неаудированный plugin явно отклоняет shared attachment.
12. Полные server/native/plugin manifests, изменённый deps.json, partial/corrupt image, source publish holders и очистка активного image. Другой UID/SID отвергнут даже при ослабленных правах socket; macOS сначала проверяет lock/ACL, иначе disabled.
13. RAM reuse требует **другого живого proxy**, пока первый отключается; новый клиент в пределах grace — отдельная гонка. Saved `.cs` видны, graph stale/generated/write boundary прежние. После ухода всех клиентов повторный load cold.
14. Память N=1/N=10 и latency одинакового semantic query под тяжёлой работой соседей: не меньше пяти повторов сессии 2, все host/proxy/children, GC/runtime/index/prewarm и bases записаны. Отдельно сравнивается ordinary и shared.
15. До выбора модели — preflight task-06 для пар Codex/Windows и Claude Code/Linux в одинаковом checkout; task-02 повторяет этот контроль на реализации. Positive synthetic pair и пара разных checkout не заменяют контроль применимости.
16. Payload parity >16 MiB/JSON escaping и proxy SIGKILL с продолжающимся CLI: нет нового cap; abandoned activity блокирует DLL-route takeover/proof даже в новом host generation, пока завершение writers не подтверждено.
17. Для обоих `list_projects`/`get_project_graph`: два клиента A; один вызывает инструмент с path B. При удерживаемом load B другой продолжает A с прежними generation/epoch/policy. После успешного commit только инициатор использует B; failed/cancel B сохраняет A. Same-key path проверяет coordinated cache hit/graph-stale refresh, omitted unbound не запускает load. На A не было LoadAsync чужого path.
18. Для add/remove package и применённого rename при двух клиентах: до mutation подготовлен ticket/proof state клиента 2 и идёт его proxy CLI. После mutation epoch увеличена, оба клиента invalidated, прежние tickets/proofs отклонены даже при потерянном notification; завершившийся старый CLI не подтвердил новый graph. Конкурентные следующие load дали одну attempt; rename loaded path не подменил старый key. Dry-run rename epoch не меняет; partial write/clear failure оставляют admission fail-closed.
19. `get_changed_files` при загруженном A и известных изменённых файлах сохраняет heuristic tests как ordinary. Switch на B во время git не меняет snapshot подсказок; без graph список пуст. Kill host между git result и heuristic не скрывает недоступность под видом пустого списка.
20. Рецепты AGENTS.md.sample reset→load после rename/изменения состава/generated при N=10: latency соседнего запроса, physical-load/retire counts, join одинаковых reset и новый reset после publication. Soft reset не выдаётся за бесплатный refresh и не replay-ит записи.
21. Storage отдельно от rendezvous: `/tmp` tmpfs/noexec не получает image/log payload; StorageRoot tmpfs/noexec, ENOSPC и source mutation дают bounded failure без partial launch. Обновление source между CreateProcess/bootstrap и первым shared call запрещено verified lease; raw mutable worker без доказательства отвечает deployment-unverified и не присваивает старому коду новый disk hash. Отдельно измерить guarded startup/identity/hash/copy/cache-hit и проверить reused launcher image с совместимым lease и полным file set.
22. Proxy A создаёт shared host — reuse block отсутствует. B присоединяется — actual context и actionable forceFresh инструкция присутствуют, также на cached/briefOutput load. Проигравший launch race получает reused block независимо от cached flag. Для отсутствующих global properties не выдумываются Debug/TFM; env secrets не попадают в ответ.
23. B вызывает load_workspace(forceFresh=true): создаётся private host с тем же WorkspaceKey, новый instance scope и одна attempt на explicit intent. A и обычный C остаются на shared. Обычный следующий load B сохраняет private; следующий explicit fresh создаёт новый private. Failure/cancel до commit сохраняет B на прежнем живом host. Private owner/capability исключают attach C, crash recovery не уходит в shared, EOF owner завершает private по grace. Повторить для startup --force_fresh; proxy не получает in-process workspace.
24. Tool-only fresh без startup flag: reset очищает defaults и делает proxy unbound; прежний private host может завершиться. Следующий explicit load с тем же resolved WorkspaceKey и отдельно config lazy load того же key создают/reuse только private по сохранённому intent. Повторить после hard retire/host cleanup. Failed fresh intent не меняет предыдущий выбор; другой resolved WorkspaceKey не получает старые load defaults.
25. Shared A(E1) создал host, B(E2)/C(E3) attached. A отключается, затем host crash. Управляемо дать B или C выиграть startup: один новый owner/load, его фактический контекст нового generation, created у победителя/reused у остальных. До исполнения проверить sticky/explicit policy mismatch, отсутствие старых proofs, актуальный info и reuse-only load block. Ни semantic call, ни creator load не получают новый reuse block.

По коду task-02 обязательны SourceStructure, Release solution build, полная main suite
с актуальными исключениями `AnalyzerLifecycle`/`RequiresPinnedSdk` (на Linux также
`RequiresWindows`), затем AnalyzerLifecycle с `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`.
Unrun platform не становится pass. В эксперименте используется только выбранный
opt-in `ROSLYN_MCP_EXPERIMENTAL_WORKSPACE_HOST=1`; default не входит в shared ветку.
Флаг не означает production activation; E1 получает отдельный план после E0.

## 12. Решения владельца и оставшиеся проверки

| ID | Рекомендация / получатель |
|---|---|
| O-01 | После task-06 выбрать выделенный host против нулевого варианта с учётом выбранного O-05; роли/HTTP остаются отложенными. |
| O-02 | Рекомендуется soft reset + coordinated reload; hard retire только при restart-required/неисправимом admission. Альтернатива — global retire на каждый reset с ценой для соседей (§7). Stop — свой proxy. |
| O-03 | Принять controlled-stop publish holders; uninterrupted hot publish живых proxy не включать. |
| O-04 | До индивидуального audit ограничить shared plugins=none/Sample, с явным отказом для остальных. |
| O-05 | **Выбрано владельцем 2026-10-29:** WorkspaceKey=path/Configuration/Platform/TFM, контекст создателя host; fresh создаёт private только для инициатора. Actual context/инструкция в load — только для reused attachment, не для создателя. Namespace/image/admission проверки сохраняются; production activation не разрешена. |
| V-01 | Task-02: настоящий Roslyn single-flight, session split, write/analyzer admission, graph reload и stage RPC. Fake load этого не доказывает. |
| V-02 | Task-06/task-02: FS/permissions/noexec/space двух roots; macOS primitive/peer credentials остаются непроверенными до среды. |
| V-03 | Task-06: identity costs; task-02: attach/reset latency, payload parity, output gates/abandoned writer, нагрузка/RAM. Числа не SLA. |
| V-04 | Task-06/E1: полный server/plugin file set, launcher reuse и совместимый lease; runtime/toolset проверяются task-02. |
| V-05 | После исследования: aggressive GC/остаточный heap/idle unload по отдельному решению владельца. |
| V-06 | Task-06 до O-01: небольшой key при разных session env, actual context/reuse-only output и private fresh на реальных парах Codex/Windows и Claude Code/Linux. Нет заявления универсальной parity; task-02 подтверждает на реализации. |

U-C-01…U-C-08 имеют предложенные ответы в разделах 1–10; статус owner decision
по O-01…O-04 остаётся отложенным с 2026-10-29; O-05 выбран владельцем в scope §2. Результат независимого ревью и исправления фиксируются
в [отчёте task-01](epoch-0-design-and-spike/evidence/task-01-architecture-decision.md).
Новых production bugs при этой docs-only работе не подтверждено. Дефекты spike из
ревью task-04 F-01–F-05/F-08 получают контракты разделов 2–4/7/9 и regressions раздела 11;
они не объявляются исправленным кодом задним числом.
