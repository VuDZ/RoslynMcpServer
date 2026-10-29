# Ревью результата C/E0/task-01 — раунды 1–2, 4 и 6–7

## Главное

**Раунд 7: исправление R6-N01 принято в scope затронутого startup/deployment контракта. R6-N01 закрыто; новых must-fix по этой правке нет.** Это не приёмка task-01 целиком, не решение владельца O-01…O-05 и не разрешение task-06/task-02 либо production activation.

Подтверждено **1 дополнительное исправление**. Сводка task-01: **7 раундов / 12 уникальных закрытых замечаний** (6 прежних + 5 r5 + R6-N01). История раундов 1–2, 4 и 6 ниже сохранена как проверенные срезы. Reviewer — прежняя отдельная сессия Codex `/root/task01_research_review`; точный model ID **unavailable**. Уточнение владельца об исполнителе task-01/правок — **GPT Sol-6.1 (`gpt-6.1-sol`)** — не меняет идентичность рецензентов.

Дата: **2026-10-29**; HEAD `6abad7ce3e35f269c54f6b84d653792a20f47b08`, production **1.5.18**. Проверены design SHA-256 `110F0A63DB59A96685B82E8A6B0B88B59371F97D622DF6AD4B0C581B3B841389`, task-06 SHA-256 `A11753BB7396DA6971109521BAE48400943D2B0D225437CB25847423CDAA0724`, author response SHA-256 `217BFF419FD0A43D7E41C04A894344AEF01D342F538DD42E610FC760C129B239`. Evidence/индексы параллельно финализируются координатором; их bookkeeping не подменяет технический срез.

## Перепроверка R6-N01

| ID | Severity | Исправление и его границы | Итог |
|---|---|---|---|
| R6-N01 | major | [Design §2:146–161](../design.md#L146) требует непрерывную immutable publication/use lease **до OS CreateProcess**, включая первоначальные exe/server assemblies; early guard до MSBuild/Roslyn/plugins связывает manifest с фактическим image. Lock лишь внутри managed Program, mtime/size/MVID и late hash исключены как доказательство. Raw mutable без proof получает deployment-unverified/restart-through-verified-image, default ordinary сохраняется. [Deployment:733–777](../design.md#L733) не признаёт Windows controlled-stop готовым guard и не легализует неизвестный proxy через копию host. Отсутствие доказуемой startup boundary — no-go task-06 до O-01, возможный пересмотр deployment/O-03 остаётся владельцу. [Budgets:609–628](../design.md#L609) учитывают guarded startup ≤30 с и меньший initialize budget harness, discovery отдельно внутри caller budget, time-to-first-useful от launch. [Сценарий 21](../design.md#L834), [task-06 пункты 5–7](../epoch-0-design-and-spike/task-06-environment-and-deployment-preflight-med.md#что-сделать) и deployment task-02 требуют source-update race до первого shared вызова, prelaunch lease и fallback отказ. [Ответ автора](../evidence/2026-10-29-task-01-round-5-response.md#r6-n01--accept) прямо оставляет bootstrap непроверенным/нереализованным. | **Закрыто в docs-only контракте; runtime feasibility не подтверждена.** |

Связь с исходным source контрпримером сохранена: [Program:15–24](../../../Program.cs#L15) и [plugin startup:96](../../../Program.cs#L96) исполняются раньше любого tool call. Теперь контракт требует guard, охватывающий эту границу и загрузку entry image, а не позднюю проверку поверх уже исполненного кода. Запрет late identity нельзя обойти audited-manifest reuse либо cache-hit: их начальный trust всё равно связан с immutable prelaunch image.

Открытые O-02/O-05 и environment/FS/reset контракты предыдущего среза не переопределены этим исправлением. Task-06 обязан доказать возможность deployment; сам факт наличия новой нормы не объявляет Windows или Linux startup поддержанным. Ничего не реализовано и не измерено этим review.

## Validation и дальнейший шаг

Source между `b2a7b80` и HEAD по production/tests roots не менялся; новые runtime/build/tests не запускались. Локальные links/anchors и неизменность исходного каталога раунда 5 проверены; подробности — [coverage](coverage.md).

Дальше координатор завершает status/counters/validation. Task-01 **in progress**, task-06/task-02 **planned**; O-01…O-04 отложены, пересмотренный O-02 и новый O-05 остаются выбором владельца. Приёмки task-01, эпохи и activation этим узким вердиктом нет.

## История раунда 6 и предыдущих проходов — ранее записанный отчёт

### Главное

**Раунд 6: требуются исправления.** Пять исходных замечаний раунда 5 закрыты в scope docs-only предложений, но выявлено новое **R6-N01 (major)**: поздний pin полного манифеста не доказывает identity кода уже запущенного proxy. Автор согласился исправить startup boundary; новый diff проверяется отдельно в раунде 7.

Исходные IDs здесь — **r5/F-01…r5/F-05**, источник [review `claude-opus-5-5`](../review-task-01-round-5-2026-10-29/result.md). Они не продолжают Codex/F-01…F-03 или independent/F-01…F-03. В этом раунде подтверждены **5 дополнительных исправлений**: 3 major, 1 minor, 1 suggestion. Вместе с прежними шестью — **11 уникальных закрытых замечаний**; **R6-N01 остаётся открытым**, его будущая правка пока не входит в число исправлений.

Это не принятие O-01…O-04, пересмотренного O-02 или нового O-05. Task-01 остаётся in progress; task-06/task-02 planned и не запущены. Новых production defects не подтверждено: найден пробел будущего design. Точный model ID reviewer Codex `/root/task01_research_review` **unavailable**.

### Проверенный срез

Дата: **2026-10-29**; HEAD `6abad7ce3e35f269c54f6b84d653792a20f47b08`, production **1.5.18**. До принятия автором R6-N01 проверен рабочий docs diff:

- design SHA-256 `0AC7038E8970E33A382E7FC19CF6A0925273C606692067F157231C82B54492C3`;
- task-01 evidence SHA-256 `C77F9D359BC0E2AA9DB0BA454CB7B9DAD5C396E10500B3003FC43873B1663764`;
- [ответ автора раунду 5](../evidence/2026-10-29-task-01-round-5-response.md) SHA-256 `960428C760490954238F25AE2BC399080E39A36919B1326E2D6BC87F0F640A3C`.

Новые правки startup guard, внесённые после фиксации R6-N01, этим срезом не приняты. История раундов 1–2 и 4 сохранена ниже; их старые verdict/hash не перезаписаны.

### Новое замечание

| ID | Severity | Где | Что не так | Что сделать |
|---|---|---|---|---|
| R6-N01 | major | [design §2:146–153](../design.md#L146), [бюджет:601](../design.md#L601); [Program:15–24](../../../Program.cs#L15), [plugin startup:96](../../../Program.cs#L96) | Identity закрепляется при первом shared обращении, хотя к этому моменту процесс уже исполняет сервер, загрузил Roslyn и plugins. Допускаемый обычный mutable source bundle мог измениться между запуском и первым shared вызовом; полный hash тогда закрепит новые bytes за proxy со старым загруженным кодом. Проверка hash копии и запрет смены source **после** pin не закрывают это окно, stat stamps также не являются доказательством. Даже неизменившаяся schema не доказывает одинаковую реализацию proxy/host. | Определить проверяемую startup boundary, связывающую identity со всем фактически запущенным image до загрузки зависимостей, и сохранить её до shared use. Нельзя поздно присвоить identity текущих файлов неизвестному уже работающему процессу. При недоказуемом происхождении — fail-closed/shared отказ и restart через verified image; default ordinary не менять молча. В task-06/task-02 нужен сценарий обновления source между запуском/bootstrap и первым shared обращением, включая mutable и audited immutable deployment. |

Ответ автора: **ACCEPT**, предложен непрерывный guard до OS CreateProcess/immutable bundle и явный deployment-unverified для старого mutable процесса. На завершение раунда 6 правка **не перепроверена**; ID остаётся open до раунда 7. Это новая причина, отдельная от r5/F-04 о стоимости и кешировании hash.

### Проверка исправлений раунда 5

| ID | Решение автора | Подтверждение достаточности | Итог раунда 6 |
|---|---|---|---|
| r5/F-01 — major | PARTIALLY ACCEPT | [Design §2](../design.md#политика-environment--o-05) теперь предлагает canonical-v1, которая меняет actual host/MSBuild environment, а не только hash. Описаны families, PWD от captured cwd, сохранение credential/config inputs, исходная среда CLI и names-only diagnostics. Отклонение от ordinary parity и неполнота MSBuild logging явно входят в O-05. Raw JSON действительно указывает **разные checkout**; эта пара не является positive совместимым контролем. [Task-06](../epoch-0-design-and-spike/task-06-environment-and-deployment-preflight-med.md) до O-01 требует SAME checkout пары Codex/Windows и Claude Code/Linux и fixture прямых Environment reads; результат пока не получен. | **Закрыто как исправление design/плана; O-05 и preflight не приняты.** |
| r5/F-02 — major | ACCEPT | [Два roots](../design.md#3-владение-транспорт-и-безопасность--u-c-04u-c-05) отделяют малый rendezvous от image/staging/log storage. Linux XDG абсолютный cache/default, macOS Caches, Windows LocalAppData заданы; actual tmpfs/ramfs/noexec storage отвергается. Space check не резервирует место; ENOSPC не даёт partial launch либо fallback в /tmp. Проверки FS/owner/symlink/space переданы task-06/task-02, не объявлены pass. | **Закрыто в design.** |
| r5/F-03 — major | PARTIALLY ACCEPT | [Soft reset:546–584](../design.md#L546) меняет общую epoch, сохраняет CLR/HostGeneration при живых клиентах, drain до clear может вернуть busy, одинаковые invalidation coalesce, следующий load single-flight. Старые proof/tickets недействительны, неуспешный clear fail-closed; hard retire reason-bound restart-required/неисправимый admission. Sticky policy остаётся свойством descriptor; инициатор unbound выбирает последующий key из собственных defaults, соседи сохраняют свой. Clear не назван сохранением прогрева либо дешёвым reload. Рецепты reset подтверждены source docs, их фактическая частота не измерялась; сценарий 20/task-02 измеряет цену. | **Закрыто как вариант O-02; выбор владельца открыт.** |
| r5/F-04 — minor | PARTIALLY ACCEPT | Стоимость identity/hash/copy/verify выделена в budgets и metrics, cache раз на process/image и trusted publisher manifest под lease не заменены mtime/size proof. 30 с identity + 30 с discovery остаются внутри caller budget. Исходные 0,98/0,26/15,11 с подписаны разными single-run bundle/cache условиями. Рецепт stat-only caching обоснованно не принят. Обнаруженная при этой правке другая ошибка момента pin вынесена отдельно в R6-N01. | **Исходное замечание о стоимости закрыто; R6-N01 блокирует общий verdict.** |
| r5/F-05 — suggestion | ACCEPT | [Deployment §9](../design.md#варианты-развёртывания) допускает reuse launcher snapshot только после audit полного file set, immutable publisher, config origin и совместимого lease/cleanup. Произвольный flock не признан совместимым с fcntl другого файла; непроверенные snapshots не чистятся новым reaper. [L-01](../README.md#l-01-launcher) имеет owner и ссылку на чужой замер RID bloat; внешний скрипт не изменяется и его repair не объявлен выполненным. | **Закрыто в documentation scope.** |

### Полнота и дальнейший шаг

Независимо пересчитаны routing table §5 — **54/54 unique**, missing/extra 0, и state mapping §10 — **42/42**. Прямой Depends-on graph семи задач E0 не содержит циклов: task-06 → task-04/task-05, окончательный task-01 → task-06, task-02 → task-01/task-06; чтение draft design task-06 является входом данных и не требует завершённого task-01. На проверенном срезе **16 Markdown / 412 local links, broken 0**; подробности — [coverage](coverage.md).

Автору завершить R6-N01 и вернуть новый срез этому же reviewer на раунд 7. Пока принимаются пять исходных documentation fixes, но техническое принятие всего обновлённого пакета отложено. Координатор синхронизирует counters/indices, не меняя выбор владельца и не запуская task-06/task-02.

### История раундов 1–2 и 4 — ранее записанный отчёт

#### Главное

**Раунд 4: исправления независимого ревью приняты. `independent/F-01` (blocker), `independent/F-02` (major), `independent/F-03` (minor) закрыты; новых must-fix по исправленному diff нет.** Принят только технический docs-only проект. Решение владельца O-01…O-04 отложено; task-01 остаётся in progress, task-02 — planned. Этот вердикт не разрешает реализацию или production activation.

Исходные замечания — [отдельный каталог Grok 4.7](../review-task-01-independent-2026-10-29/result.md), [его coverage](../review-task-01-independent-2026-10-29/coverage.md). Они не являются прежними Codex/F-01…F-03. В общей серии: Codex — раунды 1–2; независимый review среза `d54a93a` — раунд 3; этот diff исправлений — **раунд 4**, проверен той же сессией Codex `/root/task01_research_review`, точный model ID **unavailable**. Исправления Grok findings подтверждает Codex; отдельной повторной приёмки Grok этим не заявлено.

В этом раунде подтверждены **3 дополнительных исправления**, исходные ID и severity сохранены. Вместе с тремя Codex findings, закрытыми в раунде 2, — **6 уникальных подтверждённых исправлений**; это не шесть новых findings этого reviewer. История раундов 1–2 ниже сохранена.

#### Версия и неизменность внешнего review

Дата: **2026-10-29**. HEAD `d54a93adc781439ea586a1eff5016c5a51aef947`, production **1.5.18**, рабочий docs diff. Source между `b2a7b80` и HEAD не менялся. Проверенный срез:

- design SHA-256 `2671EC9DA2B0096FF87BE643778C02D8C5AC0F4397C4B08EC8C7AAB37DFC4B6B`;
- evidence task-01 SHA-256 `2BFF9BB86BD142E7F6870019F1315DCE12DAD32B96B2F8BF344C8753561A56FD`;
- [ответ автора](../evidence/2026-10-29-task-01-independent-review-response.md) SHA-256 `589CA25E1CBB6A2D6B9B20DC8DA3297DAB33DA5858E0A7A1825F599B475BE97D`.

Внешние user-provided файлы не редактировались: result SHA-256 `0BC8659DA6597551F2B6D49EAEF72EB43D5046190DD5A3887548806482757654`, coverage SHA-256 `322EF135CF913DDF795A0DA947D6E406832CD05E0197FE5EDB71B6518EBABACB`. Совпадение проверено до и после записи своего review. Координатор после вердикта обновляет только status/counters/validation; такое bookkeeping не считается новой технической версией.

#### Перепроверка independent findings

| ID | Исходная severity | Что подтверждено в коде и исправленном контракте | Итог раунда 4 |
|---|---|---|---|
| independent/F-01 | blocker | [UtilityTools:1418–1423](../../../Tools/UtilityTools.cs#L1418) и [1483–1488](../../../Tools/UtilityTools.cs#L1483) действительно вызывают LoadAsync при явном path. [LoadAsync:242–259](../../../Services/Workspace/SolutionManager.cs#L242) ведёт в load/prepare, а [fresh open:2076–2119](../../../Services/Workspace/SolutionManager.cs#L2076) освобождает прежний graph/policy. В [design:268–286](../design.md#L268) foreign key теперь проходит proxy attach/load/commit с сохранением A при отказе/cancel B; на host A чужой LoadAsync запрещён. Same-key refresh — coordinated single-flight; omitted unbound не создаёт новую lazy load. Routing table разделяет эти стадии. [Сценарий 17](../design.md#L709) и task-02 охватывают обе tools и ветви. | **Закрыто в design.** |
| independent/F-02 | major | [ProjectTools:58](../../../Tools/ProjectTools.cs#L58), [:86](../../../Tools/ProjectTools.cs#L86), [:114](../../../Tools/ProjectTools.cs#L114), [:128](../../../Tools/ProjectTools.cs#L128) подтверждают общий clear после package/apply rename и отсутствие clear в dry-run. [Clear:768–806](../../../Services/Workspace/SolutionManager.cs#L768) очищает manager, сохраняя объект [SessionBuildState:66](../../../Services/Workspace/SolutionManager.cs#L66); его [Observe:145–169](../../../Services/Build/SessionBuildState.cs#L145) не заменяет внешний ticket guard. [Design:435–464](../design.md#L435) теперь закрывает admission, увеличивает WorkspaceEpoch до disk effects, инвалидирует tickets/write contexts/proofs всех сессий и заменяет session build states. Каждый stage RPC проверяет epoch независимо от notification. Следующая загрузка того же descriptor — одна attempt; error/partial write не возвращает старую пригодность. CLI может завершиться без confirmation новой epoch; output gates/abandoned records сохранены. Loaded csproj rename не подменяет key на месте. [Сценарий 18](../design.md#L710) и task-02 фиксируют эти случаи. | **Закрыто в design.** |
| independent/F-03 | minor | [UtilityTools:160–163](../../../Tools/UtilityTools.cs#L160) и [SuggestTestProjects:93–134](../../../Services/GitChangedFilesHelper.cs#L93) подтверждают зависимость успешного Git response от graph, не только anchor. [Design:348–358](../design.md#L348) закрепляет исходный snapshot/call reference; proxy получает Git paths, тот же host вычисляет heuristic, proxy сохраняет прежнее форматирование. Switch не подменяет graph; explicit Git path сам не инициирует switch; без graph остаётся пустой список. Потеря исходного host даёт явную неполноту вместе с уже полученным Git result, без молчаливого пустого списка или replay. [Сценарий 19](../design.md#L711) и task-02 проверяют parity/switch/host loss. | **Закрыто в design.** |

Это приёмка достаточности контракта и назначенных проверок. Настоящие interleavings, epoch RPC и tool parity ещё не реализованы/не запущены; старый production код не объявлен исправленным задним числом. Независимых новых production defects этим проходом не подтверждено.

#### Полнота и итог

Повторный probe всех прямых `LoadAsync/LoadAndPrepareAsync/ClearWorkspaceAsync` consumers в `Tools/` подтвердил найденные внешним review скрытые load/clear и уже покрытый WorkspaceTools load/reset; новых прямых consumers вне этих категорий не найдено. Пересчёт **только строк routing table §5**, без повторов имён в поясняющих абзацах: **54/54**, unique 54, missing/extra 0. State coverage — **42/42**. Статусы и разрешения по-прежнему отделены от технической проверки; финальную синхронизацию истории четырёх раундов и шести fixes выполняет координатор.

Подробности docs checks и границ этого прохода — [coverage](coverage.md). Следующий шаг — финализировать учёт review, затем ждать отдельного решения владельца O-01…O-04; task-02 этим вердиктом не начинается.

#### История раундов 1–2 — ранее записанный отчёт

##### Главное

**Раунд 2: техническое содержимое проекта design принято; F-01, F-02 и F-03 закрыты. Новых must-fix нет.** Принят только docs-only проект архитектурного решения для выбора владельца. Task-01 остаётся in progress / pending owner, O-01…O-04 не приняты этим ревью; task-02, реализация эпохи и production activation не разрешены.

Подтверждено **3 исправления** по стабильным IDs: два major и одно minor. История первого раунда ниже сохранена целиком. Рецензент обоих раундов — отдельная сессия Codex `/root/task01_research_review`, точный model ID **unavailable**. Дата: **2026-10-29**. HEAD по-прежнему `b2a7b80c253b63ffe5259d0b3c307547550921d9`, сервер source 1.5.18.

Проверенный срез раунда 2: design SHA-256 `E1DE46C90105DB9B1C9EFDE5C52729A2373984578DF80C58BB6F0799BD27E70D`; evidence SHA-256 `4EFD1DBCCD5552CF13592B299030A5F1CB81A5EF9849F812653782142A35EF66`. Последующее внесение координатором вердикта, счётчиков и финальной docs validation в evidence/индексы не является новым техническим контрактом.

##### Перепроверка исправлений

| ID | Исходная severity | Исправление и проверка | Итог |
|---|---|---|---|
| F-01 | major | [Design:475–485](../design.md#L475) сохраняет logical payloads внешнего SDK и прежние tool-specific caps; новый generic 16 MiB предел удалён. Preface/control DTO caps отделены от tool body. Ordinary/shared parity для body >16 MiB и JSON escaping назначена task-02 ([сценарий 16](../design.md#L639)). Остаточная нормативная фраза о необходимости generic cap исправлена и в SDK evidence. Реальный payload test пока не выполнен и не объявлен pass. | **Закрыто в документации.** |
| F-02 | major | [Design:76–106](../design.md#L76) отвергает presence известных трёх volatile IDs до key/launch, включая пустое значение, без N host, изменения MSBuild environment и скрытого fallback. Проекты с зависимостью от этих inputs остаются ordinary до явного решения. Точный descriptor двух настоящих MCP — обязательный go/no-go task-02, отражён и в [его сценариях](../epoch-0-design-and-spike/task-02-bridge-spike-hi.md#сценарии). Несовместимость возвращается владельцу, synthetic N=10 её не закрывает. | **Закрыто в design; V-06 остаётся обязательной будущей проверкой.** |
| F-03 | minor | [Design:355–370](../design.md#L355) явно ограничивает proxy-owned gate. `launching` activity record сохраняется до spawn; незавершённая activity после потери proxy блокирует DLL-route takeover/confirmation также в новом host generation. Один исчезнувший PID и shared reset её не очищают; при неизвестном завершении writers нужен fail-closed и проверка оператором. Гонки до child identity и с живым orphan child переданы task-02. Новый supervisor не объявлен существующим. | **Закрыто в документации.** |

Дополнительные уточнения session max-results/preview, владения локальным cursor remainder, Job/tree kill и attached-but-idle workspace согласуются с исходным scope. Они не принимают deferred idle unload, произвольные plugins либо обход Job.

##### Независимое наблюдение среды

После чтения полного `.proj`/`.ps1` выполнен names-only probe **через собственный Roslyn MCP `execute_dotnet_command`**, а не shell executor. Probe возвратил `present=false` для `CODEX_THREAD_ID`, `CODEX_SESSION_ID`, `CODEX_TOOL_CALL_ID`; значения не выводились и не сохранялись. [Данные рецензента](../epoch-0-design-and-spike/evidence/task-01-data/env-reviewer.json) сохранены координатором из ignored artifacts.

`get_mcp_server_info` подтвердил MCP **1.5.16.0**, cwd данного checkout, plugins=none и workspace loaded=no. Load/config изменений не было. Это узкий результат дочернего CLI двух независимых сессий на старом MCP; он не устанавливает полный environment родительских процессов, точный descriptor пары либо применимость shared 1.5.18. **V-06 этим наблюдением не закрыт.**

##### Validation и оставшиеся решения

Повторный независимый пересчёт: 54/54 tools, unique routing 54, missing 0; S01–S42 — 42/42. В семи primary Markdown проверено **229 локальных links/anchors, broken 0**. `git diff --check` — PASS; код, tests, конфигурация и build settings не менялись. Полный охват двух раундов — [coverage](coverage.md).

O-01…O-04 остаются выбором владельца. Roslyn integration, payload parity, output closure/abandoned-writer behavior, настоящий descriptor pair, macOS/ACL, память и latency пока не проверены; у них есть явные получатели task-02/E1. Следующий шаг — решение владельца по проекту, затем работа только в разрешённом им scope. Исторические passes spike и узкий env probe не подменяют эти проверки.

##### История раунда 1 — до исправлений

###### Главное

**Вердикт: требуются исправления до технического принятия проекта design.** Найдены два major и одно minor замечание. Выбор владельца остаётся pending: это ревью не принимает O-01…O-04, не запускает task-02 и не разрешает production activation.

- Внутренний предел 16 MiB меняет допустимость публичных вызовов, хотя task-01 требует сохранить их параметры и успешное поведение (F-01).
- Полный inherited environment может снова сделать идентификатор внешней сессии причиной отдельного host; обещание исключить чат/PID из причин дублирования пока не согласовано с формулой ключа (F-02).
- Output lease принадлежит proxy и исчезает при его смерти; переживший proxy CLI остаётся за пределами этой гарантии. Этот известный предел нужно явно связать с отказом подтверждения и проверкой task-02 (F-03).
- По полноте: 54/54 public built-in tools и S01–S42 покрыты; пропущенных имён и повторов в routing table нет. Все U-C-01…08 имеют предложенные ответы. Документы ясно различают контракт проекта, результаты spike и будущие проверки.
- Новых production bugs этим ревью не подтверждено. Замечания относятся к будущему shared design. Для нулевого/default варианта их исправление не требует менять runtime. Роли и HTTP остаются отложенными.

Дата: **2026-10-29**. Рецензент: отдельная сессия Codex `/root/task01_research_review`; точный model ID **unavailable**. Проверяемая база: `b2a7b80c253b63ffe5259d0b3c307547550921d9`, сервер 1.5.18. Рабочий docs diff: design/evidence task-01 и синхронизация task/индексов/task-02. На момент фиксации findings design SHA-256 `0A6A412ABA3F909A23DDE67B1EFFF457BAA2E891FAB2A03934853E851DA305DA`; evidence SHA-256 `8327FC67006CA5436652B37D6CE0B36638F5B9834E69B55EAC43771B89D20EF9`.

###### Замечания

Все три замечания открыты. Источник — этот раунд; ответ автора и результат повторной проверки пока отсутствуют. Исправлений, подтверждённых рецензентом: **0**.

| ID | Severity | Где | Что не так | Что сделать |
|---|---|---|---|---|
| F-01 | major | [design:422–427](../design.md#L422), [routing:281](../design.md#L281); [EditingTools.cs:22–39](../../../Tools/EditingTools.cs#L22) | Предел 16 MiB задан на целое внутреннее NDJSON-сообщение. Bound `update_file_content` обязан идти через host, а существующий `WriteFile` принимает полный body без такого ограничения. Допустимый внешний вызов с большим content станет новой ошибкой shared-режима; JSON escaping может пересечь предел и при меньшем исходном body. Обозначение числа как proposed и будущий cap-test не устраняют противоречия требованию сохранить публичное поведение. В O-01…O-04 отдельного отклонения по размерам нет. | Убрать новый предел целого logical RPC либо сохранить допустимые logical payloads через транспортное решение ниже tool semantics. Сохранить прежние tool-specific limits. Добавить parity-проверку ordinary/shared с body больше 16 MiB и с JSON escaping; результат не усекать. Если нужен новый публичный предел, оформить его отдельным отклонением для владельца до принятия, а не считать сохранением нынешнего контракта. |
| F-02 | major | [descriptor:72–83](../design.md#L72), [требование U-C-01](../epoch-0-design-and-spike/task-01-architecture-decision-hi.md#L67) | Digest полного унаследованного environment включает и идентификаторы, введённые внешним harness. Исключение только IDs, которые само приложение не внедряет в env, этот случай не решает. Например, различие одного `CODEX_THREAD_ID` уже меняет key по формуле, даже при одинаковых workspace/настройках и отсутствии чтения этой переменной проектом. В текущем исследовательском shell найдены имена `CODEX_THREAD_ID`, `CODEX_SESSION_ID`, `CODEX_TOOL_CALL_ID`; это **не** измерение среды запуска MCP и не доказательство фактического расхождения MCP. Но нормативная гарантия «чат/PID не причина дублирования» сейчас не обеспечена, а проверка точного descriptor на реальной паре motivating harness отсутствует. Это может обнулить основной эффект C при технически зелёном synthetic N=10. | Явно решить, какая effective graph environment передаётся MSBuild и как обрабатываются внешние volatile IDs. Нельзя просто игнорировать переменную в key, оставив различающиеся значения доступными imports/conditions. Допустимы проверенная нормализация исполнения либо честная ineligibility/owner decision для такого контекста. До вывода о применимости провести names-only/digest проверку точного descriptor для двух настоящих совместимых MCP harness; отрицательный результат должен останавливать заявление об их схлопывании. Обновить summary и критерии task-02. |
| F-03 | minor | [CLI/gates:312–325](../design.md#L312), [сценарии:571–574](../design.md#L571); [task-05:102–121](../epoch-0-design-and-spike/evidence/task-05-applicability.md#L102) | Gate в proxy переживает смерть host, но не смерть proxy. Task-05 уже показал живого обычного CLI child после kill родителя в Windows runner и после SIGKILL в Linux. Тогда OS lock proxy снимается, а старый child всё ещё может писать outputs рядом с новым исполнителем. Ограничение внешних raw CLI в design есть; этот внутренний путь потери владельца и его последствия не названы. Это не подтверждённый production defect и не требование вводить новый daemon. | Явно ограничить гарантию gate временем жизни proxy; описать outcome-unknown и запрет получать новое подтверждение из потерянного вызова. Назначить task-02 управляемую проверку proxy death с продолжающимся child и записать, какое fail-closed/unknown поведение допустимо до доказательства завершения старого writer. Не обещать, что один OS gate сам предотвращает это пересечение. |

###### Проверенные утверждения

| Утверждение | Итог | Проверка по текущему коду / предел |
|---|---|---|
| Proxy не может получить изоляцию простым повторением нынешнего core DI. | Подтверждено. | [RegisterCore:60–86](../../../Hosting/RoslynMcpServiceCollectionExtensions.cs#L60) регистрирует общие activation/collection, provenance и `SolutionManager` как singleton. Предлагаемый split требуется обеим shared-моделям. |
| Omitted load defaults и CLI-only build suffix нужно сохранять за исходной сессией. | Подтверждено. | [SolutionManager:893–915](../../../Services/Workspace/SolutionManager.cs#L893) вычисляет passed → Loaded* → file defaults; buildArgs вычисляется отдельно. У fresh load [2076–2097](../../../Services/Workspace/SolutionManager.cs#L2076) старый graph и loaded state освобождаются до нового open. |
| Перенести весь DLL test route в proxy без stage RPC нельзя; exit=0 не является freshness proof. | Подтверждено. | [TestTools:741–834](../../../Tools/TestTools.cs#L741) использует published Solution, ProjectId, BuildContext, manager state и PullSnapshot. [SessionBuildState:8–12](../../../Services/Build/SessionBuildState.cs#L8), [TestDllEnsure:147–174](../../../Services/Build/TestDllEnsure.cs#L147) прямо запрещают reuse без input content hashes. Proposed tickets не объявлены работающей реализацией. |
| Произвольный plugin singleton нельзя изолировать только сериализацией вызовов. | Подтверждено. | [IRoslynMcpPlugin:5–30](../../../Plugins/IRoslynMcpPlugin.cs#L5) и [RoslynMcpPluginContext:26–29](../../../Plugins/RoslynMcpPluginContext.cs#L26) разрешают произвольные services. Ограничение none/Sample честно вынесено в O-04; подтверждённого аудита всех plugins нет. |
| Обычный CLI runner не гарантирует уничтожить ребёнка после внезапной смерти исполнителя. | Подтверждено по source + существующему probe; заново не запускалось. | [DotNetCliRunner:175–230](../../../Services/Build/DotNetCliRunner.cs#L175) пытается kill только на работающем control flow timeout/catch; [CreateProcessStartInfo:326–343](../../../Services/Build/DotNetCliRunner.cs#L326) не задаёт отдельного lifetime supervisor. Наблюдения task-05 подтверждают различие runner и Job harness; см. F-03. |

SDK hash совпал с evidence: `B1752096EA9D2F38BA88E467F04AEA43FB300D9A54D881C165F62B69028EE3AB`. Сигнатуры приняты по авторской локальной декомпиляции и имеющемуся SDK spike; независимая декомпиляция и новый runtime pass в этом раунде не выполнялись. Исторические числа task-03/05 сохранены как измерения/оценки прежних контролей; они не выданы за замер production shared.

###### Полнота, неизвестные и разрешения

- U-C-01: состав descriptor, пути, omission/lazy load, sticky overlay и manifests описаны; F-02 остаётся существенной границей применимости.
- U-C-02/03: proxy/session split, owned stores, short commit switch, старые call references, shared failure/retry и session defaults описаны. Switch/cancel/retry — proposed контракты, не утверждение об исправленном spike.
- U-C-04: lease до heavy initialization, failure-only cooldown, monotonic budgets, closing/reprobe и cleanup описаны; macOS explicitly disabled до V-02. Job/tree-kill нельзя трактовать как гарантированное выживание host/CLI.
- U-C-05: SDK endpoint, protocol revisions, ID/progress mapping, явная cancellation delivery и subscriptions остаются в proxy. Новых passes в этом review нет.
- U-C-06: host grace и сохранение workspace при подключённых idle клиентах следует из предложенной модели; idle unload/GC отложены владельцем и не объявлены реализованными.
- U-C-07: нулевой контроль, idle unload, dedicated host и deferred роли сравниваются без измеренных production выгод. O-01 pending.
- U-C-08: все 54 имён имеют место; сложные CLI маршруты разбиты на стадии. F-01/F-03 требуют уточнения границ.
- O-01…O-04 — вопросы владельца, а не недостающие технические passes и не решения reviewer. Текущее in progress/pending owner согласовано с task и тремя индексами. Дополнительные правки task-02 только применяют уже принятые scope/validation уточнения и не активируют его.
- V-01…V-05 имеют получателей. Произвольные plugins, macOS, output closure, budgets и Roslyn integration остаются непроверенными; task-02/E1 не получают приёмку из этого отчёта.

###### Рекомендация

Автору исправить F-01/F-02 и явно оформить предел F-03, затем вернуть следующую версию этому же reviewer. После повторной технической проверки координатор обновляет task/индексы и статистику; выбор владельца фиксируется отдельно. Подробности выполненных проверок и ограничений — [coverage](coverage.md).
