# Ревью результата C/E0/task-01 — раунды 1–2

## Главное

**Раунд 2: техническое содержимое проекта design принято; F-01, F-02 и F-03 закрыты. Новых must-fix нет.** Принят только docs-only проект архитектурного решения для выбора владельца. Task-01 остаётся in progress / pending owner, O-01…O-04 не приняты этим ревью; task-02, реализация эпохи и production activation не разрешены.

Подтверждено **3 исправления** по стабильным IDs: два major и одно minor. История первого раунда ниже сохранена целиком. Рецензент обоих раундов — отдельная сессия Codex `/root/task01_research_review`, точный model ID **unavailable**. Дата: **2026-10-29**. HEAD по-прежнему `b2a7b80c253b63ffe5259d0b3c307547550921d9`, сервер source 1.5.18.

Проверенный срез раунда 2: design SHA-256 `E1DE46C90105DB9B1C9EFDE5C52729A2373984578DF80C58BB6F0799BD27E70D`; evidence SHA-256 `4EFD1DBCCD5552CF13592B299030A5F1CB81A5EF9849F812653782142A35EF66`. Последующее внесение координатором вердикта, счётчиков и финальной docs validation в evidence/индексы не является новым техническим контрактом.

## Перепроверка исправлений

| ID | Исходная severity | Исправление и проверка | Итог |
|---|---|---|---|
| F-01 | major | [Design:475–485](../design.md#L475) сохраняет logical payloads внешнего SDK и прежние tool-specific caps; новый generic 16 MiB предел удалён. Preface/control DTO caps отделены от tool body. Ordinary/shared parity для body >16 MiB и JSON escaping назначена task-02 ([сценарий 16](../design.md#L639)). Остаточная нормативная фраза о необходимости generic cap исправлена и в SDK evidence. Реальный payload test пока не выполнен и не объявлен pass. | **Закрыто в документации.** |
| F-02 | major | [Design:76–106](../design.md#L76) отвергает presence известных трёх volatile IDs до key/launch, включая пустое значение, без N host, изменения MSBuild environment и скрытого fallback. Проекты с зависимостью от этих inputs остаются ordinary до явного решения. Точный descriptor двух настоящих MCP — обязательный go/no-go task-02, отражён и в [его сценариях](../epoch-0-design-and-spike/task-02-bridge-spike-hi.md#сценарии). Несовместимость возвращается владельцу, synthetic N=10 её не закрывает. | **Закрыто в design; V-06 остаётся обязательной будущей проверкой.** |
| F-03 | minor | [Design:355–370](../design.md#L355) явно ограничивает proxy-owned gate. `launching` activity record сохраняется до spawn; незавершённая activity после потери proxy блокирует DLL-route takeover/confirmation также в новом host generation. Один исчезнувший PID и shared reset её не очищают; при неизвестном завершении writers нужен fail-closed и проверка оператором. Гонки до child identity и с живым orphan child переданы task-02. Новый supervisor не объявлен существующим. | **Закрыто в документации.** |

Дополнительные уточнения session max-results/preview, владения локальным cursor remainder, Job/tree kill и attached-but-idle workspace согласуются с исходным scope. Они не принимают deferred idle unload, произвольные plugins либо обход Job.

## Независимое наблюдение среды

После чтения полного `.proj`/`.ps1` выполнен names-only probe **через собственный Roslyn MCP `execute_dotnet_command`**, а не shell executor. Probe возвратил `present=false` для `CODEX_THREAD_ID`, `CODEX_SESSION_ID`, `CODEX_TOOL_CALL_ID`; значения не выводились и не сохранялись. [Данные рецензента](../epoch-0-design-and-spike/evidence/task-01-data/env-reviewer.json) сохранены координатором из ignored artifacts.

`get_mcp_server_info` подтвердил MCP **1.5.16.0**, cwd данного checkout, plugins=none и workspace loaded=no. Load/config изменений не было. Это узкий результат дочернего CLI двух независимых сессий на старом MCP; он не устанавливает полный environment родительских процессов, точный descriptor пары либо применимость shared 1.5.18. **V-06 этим наблюдением не закрыт.**

## Validation и оставшиеся решения

Повторный независимый пересчёт: 54/54 tools, unique routing 54, missing 0; S01–S42 — 42/42. В семи primary Markdown проверено **229 локальных links/anchors, broken 0**. `git diff --check` — PASS; код, tests, конфигурация и build settings не менялись. Полный охват двух раундов — [coverage](coverage.md).

O-01…O-04 остаются выбором владельца. Roslyn integration, payload parity, output closure/abandoned-writer behavior, настоящий descriptor pair, macOS/ACL, память и latency пока не проверены; у них есть явные получатели task-02/E1. Следующий шаг — решение владельца по проекту, затем работа только в разрешённом им scope. Исторические passes spike и узкий env probe не подменяют эти проверки.

## История раунда 1 — до исправлений

### Главное

**Вердикт: требуются исправления до технического принятия проекта design.** Найдены два major и одно minor замечание. Выбор владельца остаётся pending: это ревью не принимает O-01…O-04, не запускает task-02 и не разрешает production activation.

- Внутренний предел 16 MiB меняет допустимость публичных вызовов, хотя task-01 требует сохранить их параметры и успешное поведение (F-01).
- Полный inherited environment может снова сделать идентификатор внешней сессии причиной отдельного host; обещание исключить чат/PID из причин дублирования пока не согласовано с формулой ключа (F-02).
- Output lease принадлежит proxy и исчезает при его смерти; переживший proxy CLI остаётся за пределами этой гарантии. Этот известный предел нужно явно связать с отказом подтверждения и проверкой task-02 (F-03).
- По полноте: 54/54 public built-in tools и S01–S42 покрыты; пропущенных имён и повторов в routing table нет. Все U-C-01…08 имеют предложенные ответы. Документы ясно различают контракт проекта, результаты spike и будущие проверки.
- Новых production bugs этим ревью не подтверждено. Замечания относятся к будущему shared design. Для нулевого/default варианта их исправление не требует менять runtime. Роли и HTTP остаются отложенными.

Дата: **2026-10-29**. Рецензент: отдельная сессия Codex `/root/task01_research_review`; точный model ID **unavailable**. Проверяемая база: `b2a7b80c253b63ffe5259d0b3c307547550921d9`, сервер 1.5.18. Рабочий docs diff: design/evidence task-01 и синхронизация task/индексов/task-02. На момент фиксации findings design SHA-256 `0A6A412ABA3F909A23DDE67B1EFFF457BAA2E891FAB2A03934853E851DA305DA`; evidence SHA-256 `8327FC67006CA5436652B37D6CE0B36638F5B9834E69B55EAC43771B89D20EF9`.

### Замечания

Все три замечания открыты. Источник — этот раунд; ответ автора и результат повторной проверки пока отсутствуют. Исправлений, подтверждённых рецензентом: **0**.

| ID | Severity | Где | Что не так | Что сделать |
|---|---|---|---|---|
| F-01 | major | [design:422–427](../design.md#L422), [routing:281](../design.md#L281); [EditingTools.cs:22–39](../../../Tools/EditingTools.cs#L22) | Предел 16 MiB задан на целое внутреннее NDJSON-сообщение. Bound `update_file_content` обязан идти через host, а существующий `WriteFile` принимает полный body без такого ограничения. Допустимый внешний вызов с большим content станет новой ошибкой shared-режима; JSON escaping может пересечь предел и при меньшем исходном body. Обозначение числа как proposed и будущий cap-test не устраняют противоречия требованию сохранить публичное поведение. В O-01…O-04 отдельного отклонения по размерам нет. | Убрать новый предел целого logical RPC либо сохранить допустимые logical payloads через транспортное решение ниже tool semantics. Сохранить прежние tool-specific limits. Добавить parity-проверку ordinary/shared с body больше 16 MiB и с JSON escaping; результат не усекать. Если нужен новый публичный предел, оформить его отдельным отклонением для владельца до принятия, а не считать сохранением нынешнего контракта. |
| F-02 | major | [descriptor:72–83](../design.md#L72), [требование U-C-01](../epoch-0-design-and-spike/task-01-architecture-decision-hi.md#L67) | Digest полного унаследованного environment включает и идентификаторы, введённые внешним harness. Исключение только IDs, которые само приложение не внедряет в env, этот случай не решает. Например, различие одного `CODEX_THREAD_ID` уже меняет key по формуле, даже при одинаковых workspace/настройках и отсутствии чтения этой переменной проектом. В текущем исследовательском shell найдены имена `CODEX_THREAD_ID`, `CODEX_SESSION_ID`, `CODEX_TOOL_CALL_ID`; это **не** измерение среды запуска MCP и не доказательство фактического расхождения MCP. Но нормативная гарантия «чат/PID не причина дублирования» сейчас не обеспечена, а проверка точного descriptor на реальной паре motivating harness отсутствует. Это может обнулить основной эффект C при технически зелёном synthetic N=10. | Явно решить, какая effective graph environment передаётся MSBuild и как обрабатываются внешние volatile IDs. Нельзя просто игнорировать переменную в key, оставив различающиеся значения доступными imports/conditions. Допустимы проверенная нормализация исполнения либо честная ineligibility/owner decision для такого контекста. До вывода о применимости провести names-only/digest проверку точного descriptor для двух настоящих совместимых MCP harness; отрицательный результат должен останавливать заявление об их схлопывании. Обновить summary и критерии task-02. |
| F-03 | minor | [CLI/gates:312–325](../design.md#L312), [сценарии:571–574](../design.md#L571); [task-05:102–121](../epoch-0-design-and-spike/evidence/task-05-applicability.md#L102) | Gate в proxy переживает смерть host, но не смерть proxy. Task-05 уже показал живого обычного CLI child после kill родителя в Windows runner и после SIGKILL в Linux. Тогда OS lock proxy снимается, а старый child всё ещё может писать outputs рядом с новым исполнителем. Ограничение внешних raw CLI в design есть; этот внутренний путь потери владельца и его последствия не названы. Это не подтверждённый production defect и не требование вводить новый daemon. | Явно ограничить гарантию gate временем жизни proxy; описать outcome-unknown и запрет получать новое подтверждение из потерянного вызова. Назначить task-02 управляемую проверку proxy death с продолжающимся child и записать, какое fail-closed/unknown поведение допустимо до доказательства завершения старого writer. Не обещать, что один OS gate сам предотвращает это пересечение. |

### Проверенные утверждения

| Утверждение | Итог | Проверка по текущему коду / предел |
|---|---|---|
| Proxy не может получить изоляцию простым повторением нынешнего core DI. | Подтверждено. | [RegisterCore:60–86](../../../Hosting/RoslynMcpServiceCollectionExtensions.cs#L60) регистрирует общие activation/collection, provenance и `SolutionManager` как singleton. Предлагаемый split требуется обеим shared-моделям. |
| Omitted load defaults и CLI-only build suffix нужно сохранять за исходной сессией. | Подтверждено. | [SolutionManager:893–915](../../../Services/Workspace/SolutionManager.cs#L893) вычисляет passed → Loaded* → file defaults; buildArgs вычисляется отдельно. У fresh load [2076–2097](../../../Services/Workspace/SolutionManager.cs#L2076) старый graph и loaded state освобождаются до нового open. |
| Перенести весь DLL test route в proxy без stage RPC нельзя; exit=0 не является freshness proof. | Подтверждено. | [TestTools:741–834](../../../Tools/TestTools.cs#L741) использует published Solution, ProjectId, BuildContext, manager state и PullSnapshot. [SessionBuildState:8–12](../../../Services/Build/SessionBuildState.cs#L8), [TestDllEnsure:147–174](../../../Services/Build/TestDllEnsure.cs#L147) прямо запрещают reuse без input content hashes. Proposed tickets не объявлены работающей реализацией. |
| Произвольный plugin singleton нельзя изолировать только сериализацией вызовов. | Подтверждено. | [IRoslynMcpPlugin:5–30](../../../Plugins/IRoslynMcpPlugin.cs#L5) и [RoslynMcpPluginContext:26–29](../../../Plugins/RoslynMcpPluginContext.cs#L26) разрешают произвольные services. Ограничение none/Sample честно вынесено в O-04; подтверждённого аудита всех plugins нет. |
| Обычный CLI runner не гарантирует уничтожить ребёнка после внезапной смерти исполнителя. | Подтверждено по source + существующему probe; заново не запускалось. | [DotNetCliRunner:175–230](../../../Services/Build/DotNetCliRunner.cs#L175) пытается kill только на работающем control flow timeout/catch; [CreateProcessStartInfo:326–343](../../../Services/Build/DotNetCliRunner.cs#L326) не задаёт отдельного lifetime supervisor. Наблюдения task-05 подтверждают различие runner и Job harness; см. F-03. |

SDK hash совпал с evidence: `B1752096EA9D2F38BA88E467F04AEA43FB300D9A54D881C165F62B69028EE3AB`. Сигнатуры приняты по авторской локальной декомпиляции и имеющемуся SDK spike; независимая декомпиляция и новый runtime pass в этом раунде не выполнялись. Исторические числа task-03/05 сохранены как измерения/оценки прежних контролей; они не выданы за замер production shared.

### Полнота, неизвестные и разрешения

- U-C-01: состав descriptor, пути, omission/lazy load, sticky overlay и manifests описаны; F-02 остаётся существенной границей применимости.
- U-C-02/03: proxy/session split, owned stores, short commit switch, старые call references, shared failure/retry и session defaults описаны. Switch/cancel/retry — proposed контракты, не утверждение об исправленном spike.
- U-C-04: lease до heavy initialization, failure-only cooldown, monotonic budgets, closing/reprobe и cleanup описаны; macOS explicitly disabled до V-02. Job/tree-kill нельзя трактовать как гарантированное выживание host/CLI.
- U-C-05: SDK endpoint, protocol revisions, ID/progress mapping, явная cancellation delivery и subscriptions остаются в proxy. Новых passes в этом review нет.
- U-C-06: host grace и сохранение workspace при подключённых idle клиентах следует из предложенной модели; idle unload/GC отложены владельцем и не объявлены реализованными.
- U-C-07: нулевой контроль, idle unload, dedicated host и deferred роли сравниваются без измеренных production выгод. O-01 pending.
- U-C-08: все 54 имён имеют место; сложные CLI маршруты разбиты на стадии. F-01/F-03 требуют уточнения границ.
- O-01…O-04 — вопросы владельца, а не недостающие технические passes и не решения reviewer. Текущее in progress/pending owner согласовано с task и тремя индексами. Дополнительные правки task-02 только применяют уже принятые scope/validation уточнения и не активируют его.
- V-01…V-05 имеют получателей. Произвольные plugins, macOS, output closure, budgets и Roslyn integration остаются непроверенными; task-02/E1 не получают приёмку из этого отчёта.

### Рекомендация

Автору исправить F-01/F-02 и явно оформить предел F-03, затем вернуть следующую версию этому же reviewer. После повторной технической проверки координатор обновляет task/индексы и статистику; выбор владельца фиксируется отдельно. Подробности выполненных проверок и ограничений — [coverage](coverage.md).
