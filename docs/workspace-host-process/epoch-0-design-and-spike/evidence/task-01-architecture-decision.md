# Task-01 — архитектурное решение

## Главное

- **In progress, 2026-10-29, docs-only.** O-05 выбран владельцем: небольшой key/private fresh/reuse-only output. [Новая редакция и validation](../../evidence/2026-10-29-task-01-owner-context.md) перепроверена в раундах 8–9; O-03 dev publish выбран, O-01/O-02/O-04 открыты.
- Выделенный host рекомендован против нулевого варианта; idle unload и роли сопоставлены отдельно без обещания измеренной экономии production RAM.
- Описаны descriptor/key, session/workspace split, switch вне gate, shared retry, no replay, reset/stop, CLI/proof boundaries, shadow image и controlled-stop publish.
- Все S01–S42 и 54 public built-in tools имеют размещение/решение; счёт и локальные ссылки независимо проверены.
- Новых production defects этой задачей не подтверждено. Найденные в task-04 дефекты не объявлены исправленным кодом: их принимающие контракты и проверки включены в design.
- **9 раундов, 14 подтверждённых исправлений**: прежние 6, пять r5/F-*, R6-N01 и R8-F01/R8-F02. [Ответы и validation](../../evidence/2026-10-29-task-01-round-5-response.md). Task-02 и production activation не начаты.
- Для окончательного O-01 добавлен planned task-06: реальные совместимые пары Codex/Windows и Claude Code/Linux, roots/identity/launcher preflight. O-05 выбран владельцем позже в тот же день; пересмотренный O-02 не принят.

## База и scope

До работы: чистая ветка `research/workspace-host-process`, `33b1d8d`.
`git pull --ff-only origin research/workspace-host-process` обновил её до
**`b2a7b80c253b63ffe5259d0b3c307547550921d9`**.
Production версия **1.5.18**, SDK ModelContextProtocol **2.2.0**, target .NET 10.
Между этими commit изменились документы и evidence/review task-04, production не менялся.
Перечитаны изменённые task/README/evidence и новые findings F-01…F-08.

Исполнитель — GPT Sol-6.1 (`gpt-6.1-sol`), Codex; уточнено владельцем 2026-10-29.
Это проект решения для владельца, без C#, тестов, версии, publish или конфигурации MCP.
Текущий MSBuild/Roslyn process исследователя не использован как production shared-реализация.

## Проверка SDK и исходников

Проверена установленная сборка
`bin/Release/net10.0/ModelContextProtocol.Core.dll`, SHA-256:
`B1752096EA9D2F38BA88E467F04AEA43FB300D9A54D881C165F62B69028EE3AB`.
Через Roslyn MCP выполнены `list_tool_groups`, `get_tool_help(decompile_type)`,
`explore_assembly`, `decompile_type`, `get_decompiled_class_skeleton`,
`get_decompiled_method_body`. Load workspace для absolute assemblyPath не требовался.

Подтверждены:

- `McpServer.Create(ITransport, McpServerOptions, ILoggerFactory?, IServiceProvider?)` создаёт SDK server с переданными options/provider.
- `ModelContextProtocol.Protocol.StreamClientTransport` принимает два Stream и logger factory; начальный неверный namespace `Client` исправлен после inventory.
- `StreamServerTransport` читает NDJSON и имеет отдельный send lock; новый generic tool-level cap не добавляется, preface/control DTO ограничиваются приложением отдельно.
- `DelegatingMcpServerTool` сохраняет ProtocolTool/Metadata и позволяет переопределить `InvokeAsync(RequestContext<CallToolRequestParams>, CancellationToken)`.
- `McpSession.SendRequestAsync<TParameters,TResult>` принимает явный RequestId и cancellation token. Корреляция остаётся на SDK, а session/generation guard — у приложения.

Полный `decompile_type(McpServer)` отказал по лимиту 815 строк; вместо этого
проверен нужный метод Create. Это не отказ исполнения или pass runtime-сценария.
Автоматический cancel SDK заново не трассировался: факт fire-and-forget и наблюдаемые
ограничения взяты с их оговорками из task-03/04, design требует явную доставку.

Ключевые текущие факты перепроверены в коде:

| Факт | Исходник |
|---|---|
| Старт регистрирует MSBuild, Roslyn, cwd/config и plugins до stdio. | [Program.cs:15](../../../../Program.cs#L15), [Program.cs:96](../../../../Program.cs#L96) |
| Core DI смешивает activation и singleton manager. | [RegisterCore:60](../../../../Hosting/RoslynMcpServiceCollectionExtensions.cs#L60) |
| Omitted defaults берутся из прежних Loaded*, build args отдельно. | [SolutionManager:893](../../../../Services/Workspace/SolutionManager.cs#L893) |
| Fresh load освобождает старый graph до нового open. | [SolutionManager:2076](../../../../Services/Workspace/SolutionManager.cs#L2076) |
| DLL test зависит от graph, BuildContext и PullSnapshot delegate. | [TestTools:741](../../../../Tools/TestTools.cs#L741), [TestDllEnsureRequest](../../../../Services/Build/Models/TestDllEnsureRequest.cs) |
| Exit=0 не даёт input-content proof. | [SessionBuildState:8](../../../../Services/Build/SessionBuildState.cs#L8) |
| Plugin API разрешает собственные singleton services. | [IRoslynMcpPlugin](../../../../Plugins/IRoslynMcpPlugin.cs), [context](../../../../Plugins/RoslynMcpPluginContext.cs) |
| Publish уже завершает source holders. | [publish-and-verify.ps1:48](../../../../publish-and-verify.ps1#L48) |

Дополнительный поиск вне таблиц: `SetCurrentDirectory`, `SetEnvironmentVariable`,
`AppContext.SetSwitch`, `Console.Set*`, `[ThreadStatic]`, `AsyncLocal`, `AssemblyResolve`
в production C# без forks/spikes/tests/samples. Найдены startup cwd, search recorder
и analyzer resolver; нового неучтённого process-state механизма не обнаружено.
Этот текстовый поиск не доказывает изоляцию произвольного plugin кода.

Непроверенные OS API сверены по первичным источникам:
[FileStream.Lock](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.lock?view=net-10.0),
[Win32 byte-range locks](https://learn.microsoft.com/en-us/windows/win32/fileio/locking-and-unlocking-byte-ranges-in-files),
[Linux flock manual](https://www.man7.org/linux/man-pages/man2/flock.2.html).
Apple archived manual недоступен через web timeout; macOS не заявлена проверенной.
Design не подменяет Linux `fcntl` семантикой `flock`.

## Соответствие и известные ограничения

U-C-01 — descriptor/defaults; U-C-02 — DI/stores; U-C-03 — switch/load/retry;
U-C-04 — locks/start/closing/publish; U-C-05 — facade/protocol/cancel;
U-C-06 — host grace и отложенная выгрузка; U-C-07 — сравнение;
U-C-08 — 54 tools и build/test stages. Каждый раздел design даёт рекомендацию,
альтернативу и риск. Проверяемые последовательности перечислены в разделе 11.

Из ревью task-04 раунда 3 приняты: F-01 load вне binding gate; F-02 раздельные
connect/handshake/initialize budgets; F-03 failure-only cooldown и not-sent/unknown;
F-04 один lock descriptor на process/key Linux; F-05 explicit observed-failure retry;
F-08 manifest всех файлов и новая image instance вместо повреждённой.
F-07 об удалении runner temp остаётся прежним неприменённым suggestion task-04;
его исправление не требуется для docs-only design и не засчитывается в fixes.

V-01…V-04 design имеют получателей task-02/E1: настоящий Roslyn, macOS/ACL,
payload/budget/output gates, manifest/runtime toolset. V-05 — отложенное исследование
GC/idle после выбора модели. Никакая из этих проверок не объявлена выполненной.
Свежих memory/latency измерений нет; числа task-03/05 явно сохранены на старой базе.

## Независимое ревью

Рецензент — отдельная сессия Codex `/root/task01_research_review`, точный model ID
недоступен. [Результат](../../review-task-01-2026-10-29/result.md),
[coverage](../../review-task-01-2026-10-29/coverage.md).
Раунд 1: **требуются исправления**, два major и одно minor.
Раунд 2: **техническое содержимое проекта принято**, F-01…F-03 закрыты,
новых must-fix нет. Подтверждены **3** исправления; выбор владельца не подменён.

| ID | Ответ автора / исправление | Перепроверка |
|---|---|---|
| F-01 | Убран новый generic 16 MiB logical cap; сохранены SDK/tool-specific ограничения и добавлен >16 MiB/escaping parity test task-02. | Закрыто, раунд 2. |
| F-02 | Exact volatile-name preflight отклоняет context до key/launch; MSBuild env не меняется молча. Task-02 получает настоящий descriptor-pair go/no-go до серий механизмов. | Закрыто, раунд 2; V-06 не выполнен. |
| F-03 | Gate ограничен lifetime proxy; abandoned activity record блокирует takeover/proof до проверки writers, даже после host restart. Назначен SIGKILL/orphan regression task-02. | Закрыто, раунд 2. |

После этих исправлений design и отчёт оформлены по **research-task-authoring**:
go/no-go перед механизмами, нулевой вариант и простое улучшение отдельно,
известные OS/SDK primitives, «Главное», traceable findings и получатели V-01…V-06.
Дополнительно уточнены session display options, локальное владение cursor remainder,
Job/tree kill и явное сохранение workspace при idle подключённых proxy.
Приёмка владельцем O-01…O-04 фиксируется отдельно; техническое ревью её не заменяет.

## Names-only MCP probe после F-02

2026-10-29 выполнен `execute_dotnet_command` реально подключённого MCP **1.5.16.0**,
без load workspace и без изменения его конфигурации. В отдельном ignored `.proj`
target Exec вызвал PowerShell, читающий только факт наличия трёх имён. Значения не
читались в вывод, не хешировались и не сохранялись. Probe не является проектом
production, тестом или новым runtime механизмом.

```text
msbuild artifacts/workspace-host-process/task-01/EnvProbe.proj -t:Probe -p:ProbeLabel=coordinator -v:minimal -nologo
```

У child CLI координатора и независимо у child CLI рецензента
**CODEX_THREAD_ID/SESSION_ID/TOOL_CALL_ID отсутствуют**. В обеих сессиях
`get_mcp_server_info` сообщил 1.5.16.0 и отсутствие workspace; это два names-only
наблюдения из разных сессий Codex, не инвентаризация PID MCP-пары.
Исходники runner сохраняют эти env names; его специальная SDK sanitation касается
других имён ([DotNetSdkEnvironment](../../../../Services/Build/DotNetSdkEnvironment.cs#L33)).
Это узкое наблюдение дочернего CLI реального MCP, а не shell Codex.
Оно не измеряет полный environment родителя, точный descriptor пары либо shared
загрузку 1.5.18. V-06 не закрыт этим probe.
[Данные координатора](task-01-data/env-coordinator.json),
[данные рецензента](task-01-data/env-reviewer.json),
[полный source probe для воспроизведения](task-01-data/env-probe-source.txt).

## Validation

Предварительная проверка 2026-10-29: **214** локальных links/anchors в шести
созданных/изменённых Markdown, broken=0. Имена пересчитаны непосредственно из
`McpToolCatalog.Build`: **54/54**, пропущено/дубликатов 0. Mapping task-00 — **42/42**
S01–S42. `git diff --check` — PASS. После fixes независимый reviewer проверил
**229** локальных links/anchors семи primary Markdown, broken=0, и подтвердил
54/54 tools и 42/42 states. Финальная проверка всего documentation пакета — ниже.
Зависимости task-01/task-02 не изменены; task-01 in progress, owner choice pending.
В task-02 исправлены прежние противоречия уже принятым решениям владельца:
обычный proxy может загружать assemblies, RAM reuse требует живого контрольного
клиента, validation filter приведён к актуальному AGENTS.md.
Build/main suite/SourceStructure/AnalyzerLifecycle не запускаются: код не меняется.
Это соответствует docs-only проверкам task-01 и AGENTS.md.

Проверка среза до отдельного ревью Grok после review/fixes и bookkeeping: **267** локальных ссылок/anchors
в **9** Markdown, broken=0; **54/54** имён без пропусков/повторов, **42/42** S-строк.
Проверены также существование source line anchors, три primary data файла и отсутствие
изменений вне `docs/`; [машинная сводка](task-01-data/validation.json).
`git diff --check` — PASS; стадия перед commit проверяется также staged diff.
На том срезе Task/index/topic/docs статусы были согласованы: проект прошёл 2 раунда/3 fixes,
задача **in progress**, task-02 planned. Выполнения task-02,
приёмки эпохи и production activation нет. Далее — отдельные ревью по решению
владельца ниже, затем его явный выбор O-01…O-04; за владельца он не записан.

## Решение владельца, 2026-10-29

На предложение принять O-01…O-04 владелец ответил: «пока отложим, проведём отдельные ревью».
Пакет не принят и не отклонён; выбор отложен до этих ревью. Task-01 остаётся
незавершённой, task-02 — planned и не начинается. Технические контракты design,
история двух раундов и счётчик трёх исправлений не меняются. На момент этого решения
отдельные ревью ещё не выполнены и в статистику не включены. Эта правка фиксирует решение владельца
и статусы; нового независимого технического ревью самой статусной правки нет.

Validation этой статусной правки на базе `d8c15f6`: 267 локальных ссылок/anchors
в девяти Markdown — PASS; `git diff --check` — PASS. Изменены только шесть
Markdown-файлов; build/test и runtime probes не запускались.

## Независимое ревью Grok 4.7 и исправления, 2026-10-29

На базе `d54a93a` получено [отдельное ревью](../../review-task-01-independent-2026-10-29/result.md).
Independent/F-01 blocker, F-02 major, F-03 minor — **ACCEPT** после сверки с кодом;
возражений нет. Исправлены implicit load в list/graph, общая epoch/proof invalidation
после project/package clear и heuristic этап get_changed_files.
Полные основания, последствия и сценарии 17–19 — в
[ответе автора](../../evidence/2026-10-29-task-01-independent-review-response.md).
Это раунд 3 по опубликованному `d54a93a`, следующий срез после раундов Codex 1–2.
Повторный [research-task-review Codex, раунд 4](../../review-task-01-2026-10-29/result.md)
подтвердил все три исправления; новых must-fix по этому diff нет. Grok повторно
эту версию не проверял. Полный учёт task-01: **4 раунда / 6 fixes**, исходные ID
различены префиксами каталогов. Выбор O-01…O-04 остаётся отложенным, task-02 planned.

## Раунд 5: среда, reset и deployment

`git pull --ff-only origin research/workspace-host-process` обновил ветку
с `e9de5ee` до **`6abad7c`**, добавив только каталог review раунда 5;
production по-прежнему 1.5.18. Все четыре файла каталога прочитаны, не меняются.

[Ответ на F-01…F-05 и validation](../../evidence/2026-10-29-task-01-round-5-response.md):
ACCEPT F-02/F-05; PARTIALLY ACCEPT F-01/F-03/F-04 с конкретными границами доказательств
и рецептов. Proposed O-05 задаёт actual host environment; O-02 рекомендует soft reset
с сохранением процесса, не бесплатный reload graph; roots и identity budgets разделены.
[L-01](../../README.md#l-01-launcher) сохраняет owner внешнего launcher/RID bloat.

[Task-06](../task-06-environment-and-deployment-preflight-med.md) добавлен перед
окончательным выбором task-01: он использует draft как гипотезу и не зависит от его
завершения. Это изменение плана после раунда 5, а не подтверждение preflight или
повторная приёмка всего плана E0. Task-06 и task-02 не запущены. Владельцем ещё
не выбраны O-01…O-04 и новый O-05.

Codex `/root/task01_research_review` в раунде 6 подтвердил все пять r5/F-* fixes
и нашёл новый R6-N01: hash при первом shared call не доказывал identity уже
загруженного proxy. ACCEPT; теперь требуется immutable publication/use lease до
OS CreateProcess, а неподтверждённый mutable deployment отклоняет shared.
Windows controlled-stop ещё не доказывает эту границу; её проверяет task-06.
Раунд 7 подтвердил исправление R6-N01, новых must-fix в проверенном diff нет.
[Срезы ревью](../../review-task-01-2026-10-29/result.md),
[финальная docs validation](task-01-data/validation-round-5.json).
Итог task-01 — **7 раундов / 12 fixes**; `claude-opus-5-5` повторно эту версию
не проверял. После fixes применён research-task-authoring: go/no-go до выбора,
нулевой вариант сохранён, L-01 имеет owner, task/index/report согласованы.

## Решение владельца O-05 после раунда 7, 2026-10-29

На базе `9f1fe65` владелец выбрал WorkspaceKey path/Configuration/Platform/TFM,
контекст создателя host и новый private host только для инициатора по force_fresh.
Последнее уточнение: actual context/инструкция в load выводятся дополнительно
только при подключении к существующему host, а не у создателя нового.
[Выбор, последствия и validation](../../evidence/2026-10-29-task-01-owner-context.md).
Прежние отчёты раундов 5–7 описывают срез до этого решения. Раунд 8 выявил
R8-F01/R8-F02 major, исправления подтверждены в раунде 9; общий счёт 9/14. Task-01 in progress, task-06/task-02 planned. Новый output и
forceFresh не реализованы в production; O-01…O-04 и epoch acceptance не выбраны.

## O-02 оставлен открытым, 2026-10-29

На базе `5346559` владелец попросил сохранить локальный reset как дополнительный
вариант: вызывающий MCP покидает host, остальные работают с текущим workspace,
следующий load инициатора создаёт private host. Detach против завершения MCP и
freshness/discovery/RAM ещё не выбраны. [Запись и validation](../../evidence/2026-10-29-task-01-reset-options.md).
Это каталогизация альтернативы после раунда 9, не выбранная семантика и не новый
reviewed slice. Task-01 in progress, task-02/task-06 planned; counters 9/14 прежние.

## O-03 выбран владельцем, 2026-10-29

На базе `4cdc1b7` владелец выбрал текущую схему dev publish: завершать копии MCP из
текущего каталога разработки, пытаться обновить, при respawn harness допускать
отказ и ручную остановку. Непрерывное обновление/новый launcher не требуются.
[Решение, source сверка и validation](../../evidence/2026-10-29-task-01-dev-publish.md).
Изменений скрипта и запуска publish/kill нет. Это запись после раунда 9; counters
9/14 прежние. O-03/O-05 выбраны, O-01/O-02/O-04 открыты; task-01 in progress,
task-02/task-06 planned. Startup identity task-06 остаётся отдельной проверкой.
