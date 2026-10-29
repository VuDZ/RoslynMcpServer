# Покрытие ревью C/E0/task-01 — раунды 1–2 и 4

## Раунд 4, 2026-10-29

Рецензент — Codex `/root/task01_research_review`, точный model ID **unavailable**. Source HEAD `d54a93adc781439ea586a1eff5016c5a51aef947`, production 1.5.18. На исходном срезе task-01 in progress, owner O-01…O-04 отложены, task-02 planned. Общие номера раундов: ранее Codex 1–2, внешний Grok review — 3, этот исправленный diff — 4. В этом раунде подтверждаются исходные **independent/** IDs, они не перенумерованы в прежние Codex/F-01…F-03.

### Прочитано и проверено

- Полностью внешний user-provided `review-task-01-independent-2026-10-29/result.md` и `coverage.md`; их SHA-256 сверены с ответом автора. Файлы не изменялись.
- Полностью [ответ автора](../evidence/2026-10-29-task-01-independent-review-response.md), обновлённый task-01 evidence; технический diff design §4/5/7/11 и перенесённые сценарии task-02. Task-01 header и index summaries проверены как pending owner; обновление counters после этого раунда остаётся обязанностью координатора.
- Source повторно: `UtilityTools.GetChangedFiles`, `ResolveGitAnchorPath`, `ListProjects`, `GetProjectGraph`; `ProjectTools` целиком; `SolutionManager.LoadAsync`, `ClearWorkspaceAsync`, fresh LoadCore и поле SessionBuildState; `SessionBuildState.Observe`; `GitChangedFilesHelper.SuggestTestProjects` и сохранение форматирования suggestions в UtilityTools.
- По `git diff --name-only b2a7b80 HEAD` между исходной базой и текущим HEAD только docs/data/review. Production, tests и project/build settings не менялись. Текущий авторский diff — документы и dependent plan; новые runtime experiments не выполнялись.

### Независимый completeness probe этого прохода

Повторён `rg` по `Tools/*.cs` на прямые `LoadAsync(`, `LoadAndPrepareAsync(`, `ClearWorkspaceAsync(`, `GetCurrentSolution(` и `SuggestTestProjects(`. Найдены две UtilityTools path-load ветви, явный load/reset WorkspaceTools, три ProjectTools clear sites, graph consumers Git/assembly/info/path helpers. Load/clear consumers исчерпываются уже рассмотренными ветвями; read-only metadata не использовалась как доказательство отсутствия mutation. Это целевой source probe достаточности трёх fixes, а не новый аудит всех транзитивных вызовов/произвольного plugin.

Source подтверждает claims внешнего review и не опровергает исправления. Runtime correctness новых state-machine/RPC interleavings остаётся проверкой task-02; пустой proof state в fixture не выдаётся за current content proof из exit=0.

### Docs validation раунда 4

- Ровно routing table §5 пересчитана по строкам, начинающимся с `|`, в сравнении с Core/Group entries `McpToolCatalog.Build`: **54/54**, unique 54, missing/extra 0. Повтор `get_changed_files` в пояснении вне таблицы не считается повторной регистрацией.
- State mapping §10: **42 unique S01…S42**, пропусков нет.
- Локальные links/anchors в **12 Markdown** (семь primary, свой result/coverage, внешний result/coverage, ответ автора): **323**, broken **0**. Проверены paths, Markdown/explicit anchors и диапазон source line references. Внешние web URL не открывались.
- `git diff --check` и whitespace checks собственных result/coverage — PASS; Git предупреждает только о возможной LF→CRLF нормализации Markdown.
- Внешние result/coverage hashes после своей записи совпали с исходными. Собственные result/coverage сохраняют историю раундов 1–2 и фиксируют hashes нового среза в [result](result.md). `.status` записан completed последним и означает завершение reviewer, а не задачи/эпохи.

### Что не выполнялось

Build, main suite, SourceStructure, AnalyzerLifecycle, новые SDK/transport/env/load/kill/CLI/payload/RAM/latency проверки — **not run в раунде 4**: C#/tests/build settings не менялись, запрос ограничен docs fixes. Прежний names-only probe раунда 2 сохранён как историческое наблюдение и не повторён. Новые сценарии 17–19 design/task-02 не объявлены passes. V-01…V-06 и O-01…O-04 не закрываются этим review.

Итог: independent/F-01 blocker, independent/F-02 major и independent/F-03 minor **закрыты в design**, 3 дополнительных подтверждённых исправления, новых must-fix по diff нет. Внешний Grok каталог не переписывается; Codex не приписывает ему новый verdict. Контракты primary документов этим reviewer не редактировались; commit/push не выполнялись.

## История покрытия раундов 1–2

### Раунд 2, 2026-10-29

Рецензент — Codex `/root/task01_research_review`, точный model ID **unavailable**. База source не изменилась: `b2a7b80`, 1.5.18. Design/evidence hash и результаты закрытия F-01…F-03 записаны в [result](result.md). Принят docs-only технический проект для выбора владельца; owner approval, выполнение task-01, task-02 и production activation не присваивались.

Перечитаны изменённые sections 2/4/6/7/8/11/12 design, полный обновлённый evidence task-01, task-02 и добавленные primary файлы `env-coordinator.json`, `env-reviewer.json`, `env-probe-source.txt`. Исходные отчёты task-00/03/04/05 и source spot-check первого раунда не повторялись без production drift. Стиль C# не оценивался, C# не создавался и не менялся.

F-01 сопоставлен с прежним `WriteFile` contract и удалением нового cap; parity остаётся сценарием task-02. F-02 проверен как явная ineligibility вместо включения volatile identity в key или молчаливого изменения semantic inputs; V-06 переносит точную проверку harness до серии механизмов. F-03 проверен по ограничениям gate и сохранению неизвестной activity через host restart/reset, с получателем двух orphan interleavings.

### Выполненный probe через настоящий MCP

Полностью прочитаны ignored `artifacts/workspace-host-process/task-01/EnvProbe.proj` и `EnvProbe.ps1`. Скрипт выводит только факт наличия трёх фиксированных имён, UTC и PID своего CLI child; environment values не печатает и не сохраняет. После `get_tool_help(execute_dotnet_command)` в reviewer MCP выполнено:

```text
msbuild "E:\Devel\RoslynMcpServer-workspace-host-process\artifacts\workspace-host-process\task-01\EnvProbe.proj" -t:Probe -p:ProbeLabel=reviewer -v:minimal -nologo
```

`workingDirectory` — корень данного checkout, timeoutSeconds=30. MCP вызов успешно вернул строку `Environment names-only probe completed.`, stderr empty. Сгенерирован `artifacts/workspace-host-process/task-01/reviewer.json`; координатор сохранил его как [primary env-reviewer.json](../epoch-0-design-and-spike/evidence/task-01-data/env-reviewer.json). Все три `present=false`. Скрипт не является production/test/build code, target Probe не загружает Roslyn workspace и не меняет MCP config.

Последующий `get_mcp_server_info` без lazy load: MCP 1.5.16.0 из соседнего publish, cwd этого checkout, full/54 tools, plugins=none, workspace loaded=no, обе RoslynMcp.jsonc отсутствуют. Версия live MCP не перенесена на source 1.5.18. Это child-presence probe, **не** точный descriptor pair, **не** сравнение всего parent environment, **не** проверка shared launch/load и **не** закрытие V-06.

### Повторные docs checks

- Семь primary Markdown: docs index, topic README, design, epoch README, task-01, task-02, task-01 evidence — **229 локальных ссылок/anchors, broken 0**. Проверяются local file paths, Markdown headings/explicit anchors и source line bounds; внешние URL заново не проверялись.
- Routing table пересчитана из `McpToolCatalog.Build`: catalog 54, routed 54, unique 54, missing 0. State mapping: 42 unique S01…S42.
- `git diff --check` — PASS; выводит только обычное Git LF→CRLF уведомление для docs index.
- Build/main suite/SourceStructure/AnalyzerLifecycle по-прежнему **not run**: source/tests/settings не менялись. Runtime payload/orphan/macOS/Roslyn/latency серии **not run**; они остаются task-02/E1.
- Primary evidence сейчас сохраняет pending до финального bookkeeping координатора. Его внесение технического вердикта/трёх fixes не означает выбор владельца.

История фактических проверок первого раунда сохранена ниже; её даты, начальный hash, числа и исходные ограничения не заменены задним числом. Итог двух раундов: **3 подтверждённых исправления, новых must-fix нет**.

### История покрытия раунда 1

Дата: **2026-10-29**. Раунд: **1**. Рецензент: Codex `/root/task01_research_review`, точный model ID **unavailable**. Версия — HEAD `b2a7b80c253b63ffe5259d0b3c307547550921d9` и рабочий docs diff, зафиксированный hash в [result](result.md).

#### Прочитано

- `AGENTS.md`; полностью `.agents/skills/research-task-review/SKILL.md`.
- Task-01, его evidence и design; README эпохи, README темы, раздел «Учёт задач, ревью и статистики» в `docs/README.md`.
- Полные primary evidence task-00, актуализация task-00 от 2026-10-28, task-03, task-04, task-05. Исторические raw JSON и все транзитивно связанные отчёты заново целиком не читались: они не выдаются за повторённые эксперименты.
- `docs/ARCHITECTURE.md`; pitfalls 13, 20, 26, 27; входной review task-04 round 3. Чужие review task-01 не читались.
- Обновлённый task-02 и documentation diff пяти tracked файлов; два новых primary Markdown проверены отдельно.
- По source: Program, DI, InputPathCanon, plugin API/registration; load/defaults/fresh-open участки SolutionManager; build proof и DLL-route участки TestTools, TestDllEnsure, SessionBuildState, DotNetCliRunner; routing decompile tools и resolver, publish holder detection.

C# не менялся, стиль C# не оценивался; чтение `docs/code-style.md` для этого research review не требовалось. Изменены только собственные result/coverage/.status.

#### База и дрейф

`git log -1`: HEAD совпадает с базой evidence `b2a7b80`. `RoslynMcpServer.csproj:27` содержит 1.5.18. Между описанным автором исходным `33b1d8d` и текущим HEAD один commit документации/review task-04; новых production изменений на этом переходе нет. Рабочий diff охватывает только Markdown. Версия исследующего запущенного MCP не используется как версия исходников.

SHA-256 `bin/Release/net10.0/ModelContextProtocol.Core.dll` независимо совпал с авторским: `B1752096EA9D2F38BA88E467F04AEA43FB300D9A54D881C165F62B69028EE3AB`.

#### Независимый completeness probe

В дополнение к описанному автором поиску SetCurrentDirectory/Environment/Console/ThreadStatic/AsyncLocal выполнен отдельный `rg` по production roots `Program.cs`, `Hosting`, `Services`, `Tools`, `Config`, `Diagnostics`, `Plugins` с patterns `Task.Run`, Timer/PeriodicTimer, ThreadPool, ProcessExit/UnhandledException, AssemblyLoadContext, FirstChanceException, AssemblyResolve; tests, spikes, samples и forks не включались.

Найдены plugin ALC, analyzer resolver hook, FirstChance search recorder, Task.Run provenance replay и search prewarm. Они уже укладываются в S16/S23/S33/S36/S40. Независимого неучтённого mutable process механизма этот поиск не выявил; совпадения имён свойств `ProcessExited` не приняты за process event. Скан static mutable коллекций подтвердил known navigation/report stores, NuGet cache и plugin reservations; immutable catalog/lookup и static методы не считались session state. Это текстовый probe, не доказательство поведения произвольного plugin.

Дополнительный probe границ был направлен на владельцев живых дочерних процессов: сравнение lifetime gate из design с DotNetCliRunner и уже измеренным child survival task-05 дало F-03.

Отдельно выполнен **names-only** probe текущего shell environment (`CODEX|CURSOR|SESSION|THREAD|PID|TERM`). Найдены `CODEX_THREAD_ID`, `CODEX_SESSION_ID`, `CODEX_TOOL_CALL_ID` и другие имена. Значения не выводились/не сохранялись; process environment MCP не читалось. Это evidence возможного внешнего volatile input для формулы descriptor, **не** pass/fail настоящей MCP-пары и **не** измеренный факт, что текущие MCP не схлопнутся. Этот предел явно сохранён в F-02.

#### Что выполнено

1. Независимый пересчёт каталога по `Core/Group` entries `McpToolCatalog.Build` и имён таблицы routing: **54 / 54**, unique routed **54**, missing **0**, extra **0**. Withheld методы и plugin tools не включены в public built-in count.
2. Пересчёт S-идентификаторов раздела покрытия design: **42 unique**, S01…S42 без пропусков.
3. Проверка локальных Markdown paths/heading anchors/explicit anchors/source line anchors семи документов — `docs/README.md`, topic README, design, epoch README, task-01, task-02, task-01 evidence: **222 links, broken 0**. Внешние web URL не проверялись.
4. `git diff --check`: **PASS**, только уведомление Git о возможной LF→CRLF нормализации `docs/README.md`. Ошибок whitespace нет.
5. Source spot-check пяти существенных утверждений записан в result; runtime свойства проектируемого shared-режима не объявлены подтверждёнными.
6. Сверка bookkeeping: task-01 in progress / 2026-10-29 / docs-only / pending owner согласуется с epoch/topic/docs index; task-02 planned. Независимое review само не завершает task-01 и не принимает эпоху.

#### Что не запускалось и почему

- Build, main suite, SourceStructure, AnalyzerLifecycle: **not run**, поскольку review/docs-only не меняет C#, tests, attributes либо project/build settings. Исторические 1432/1432 — результаты прежних задач, не результат этого раунда.
- Новые OS transport/ACL/locks, launch/kill, SDK forwarding/cancellation, Roslyn multi-process и memory/latency серии: **not run**. Они входят в task-02/E1 либо уже имеют исторический standalone scope. macOS среды нет; pass ей не присвоен.
- Повторная декомпиляция SDK: **not run**; hash сверён, API evidence автора и существующий spike прочитаны без заявления независимого runtime pass.
- Не проверялись реальные CLI outputs после proxy SIGKILL и точный environment двух MCP harness. F-02/F-03 требуют явно закрепить такие проверки/ограничения в следующем design.
- C# style и code diff quality не входят в этот research review. Ничего не коммитилось и не пушилось.

Новые production bugs не подтверждены. Verdict и stable finding IDs — [result](result.md). `.status` относится только к завершению reviewer round 1; не означает принятия design или выбора владельца.
