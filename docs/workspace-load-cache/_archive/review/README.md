# Review: falsification of workspace-load-cache_v2

Каталог: замечания к спецификациям, не к тону документов.
Реализация серии не начиналась. Сверка с кодом на момент ревью: `SolutionManager`,
`MsBuildWorkspaceProperties`, `WorkspaceDiskPathFilter`, `WorkspaceDocumentDiskSync`,
`LoadAndPrepareAsync` (U-ARB-04), write boundary; runtime `get_mcp_server_info` =
**1.3.20**, не 1.3.5 из плана.

Цель ревью — опровергнуть предложенную архитектуру, а не улучшить её изложение.

| ID | Sev | Spec |
| --- | --- | --- |
| R-01 | Blocker | README.md |
| R-02 | Blocker | README.md |
| R-03 | High | README.md |
| R-04 | High | README.md |
| R-05 | High | README.md |
| R-06 | High | README.md |
| C-01 | Blocker | cache-contract.md |
| C-02 | High | cache-contract.md |
| C-03 | High | cache-contract.md |
| C-04 | High | cache-contract.md |
| C-05 | High | cache-contract.md |
| C-06 | High | cache-contract.md |
| C-07 | High | cache-contract.md |
| C-08 | Medium | cache-contract.md |
| C-09 | High | cache-contract.md |
| E0-01 | Blocker | epoch-0 |
| E0-02 | High | epoch-0 |
| E0-03 | High | epoch-0 |
| E0-04 | High | epoch-0 |
| E0-05 | Medium | epoch-0 |
| E1-01 | Blocker | epoch-1 |
| E1-02 | Blocker | epoch-1 |
| E1-03 | High | epoch-1 |
| E1-04 | High | epoch-1 |
| E1-05 | High | epoch-1 |
| E1-06 | High | epoch-1 |
| E1-07 | Medium | epoch-1 |
| E2-01 | Blocker | epoch-2 |
| E2-02 | High | epoch-2 |
| E2-03 | High | epoch-2 |
| E2-04 | High | epoch-2 |
| E2-05 | High | epoch-2 |
| E2-06 | High | epoch-2 |
| E3-01 | Blocker | epoch-3 |
| E3-02 | High | epoch-3 |
| E3-03 | High | epoch-3 |
| E3-04 | High | epoch-3 |
| E3-05 | High | epoch-3 |
| E3-06 | High | epoch-3 |
| E3-07 | High | epoch-3 |
| E3-08 | High | epoch-3 |
| E4-01 | High | epoch-4 |
| E4-02 | High | epoch-4 |
| E4-03 | High | epoch-4 |
| E4-04 | Medium | epoch-4 |
| V-01 | Blocker | verification.md |
| V-02 | High | verification.md |
| V-03 | High | verification.md |
| V-04 | Medium | verification.md |
| H-01 | High | handoff-template.md |

---

ID: R-01
Severity: Blocker
Category: Hydration

Target:
README.md / «Цель и критерий корректности» / абзац 1; «Порядок работ» / строка про эпоху 1

Claim:
Дисковый hit восстанавливает ту же семантику, что свежая загрузка MSBuild. Эпоха 1 даёт эквивалентную гидрацию публичными API. Сериализация `Solution` / внутренних Roslyn storage API запрещена.

Evidence:
Публичный `MSBuildWorkspace` наполняет граф через `OpenSolutionAsync` / `OpenProjectAsync` (это и есть DTB). Публичного API «применить DTO compiler inputs к уже созданному `MSBuildWorkspace` без Open» нет. `AdhocWorkspace` + `ProjectInfo` гидратируется, но это другой host: другой `TryApplyChanges` (не пишет `.csproj`), другой analyzer host, другой lifetime loader. План не выбирает host и одновременно требует семантической идентичности со свежим MSBuild и безопасных записей эпохи 1.

Failure scenario:
1. Epoch 0 «восстанавливает» DTO в `AdhocWorkspace` и ставит go.
2. Epoch 1 объявляет edit/refactoring эквивалентными; `rename_project` / code fix с AddDocument меняют только память.
3. Disk-hit в Epoch 2 считается успешным (0 вызовов `OpenSolutionAsync`), а семантика записи и генераторов расходится со свежим MSBuild. Либо наоборот: гидрация всё же зовёт Open — тогда диск-кэш не пропускает DTB и цель README ложна.

Suggested change:
Зафиксировать host до Epoch 0 и признать одно из трёх: (a) только read-only hydrate в `AdhocWorkspace` — тогда Epoch 1 edit-equivalence ложна; (b) hydrate невозможен публично — stop; (c) разрешить внутренний API, что запрещено этим же README. Нельзя удерживать все три ограничения сразу.

Confidence:
High

---

ID: R-02
Severity: Blocker
Category: Support profile

Target:
README.md / «Цель» / абзац 2; «Ограничения» / «Не реализовывать собственный общий интерпретатор MSBuild»

Claim:
Проект нельзя считать безопасным для кэша только потому, что `.csproj` не изменился. Для непроверяемой зависимости — обычный MSBuild load. Собственный интерпретатор MSBuild строить нельзя.

Evidence:
`cache-contract.md` §4 требует документировать *все* влияющие входы, включая отсутствие файлов в условных `Exists`/imports и появление файлов в wildcard-областях. Полный набор отрицательных зависимостей есть только у evaluation. Публичный граф `Project.Documents` / `AdditionalDocuments` показывает уже принятые входы, не неудавшиеся `Exists`. Без интерпретатора (или без бинарного лога MSBuild, который план не вводит) распознавание `unsupported` сводится к имени SDK и уже разрешённым imports — ровно то, что README запрещает считать достаточным.

Failure scenario:
1. Epoch 0 составляет allowlist «Microsoft.NET.Sdk + известные imports».
2. В `Directory.Build.props` есть `Condition="Exists('Local.props')"`. Файла нет, snapshot сохранён.
3. Появился `Local.props`, `.csproj` и известные imports не изменились, hashes совпали, disk-hit отдаёт старый граф. Либо любой неизвестный import → unsupported на всём запросе, и большие решения из цели README никогда не попадают в hit.

Suggested change:
Либо ввести *полный* источник provenance (binlog / evaluation API) и снять запрет «не интерпретировать MSBuild», либо сузить контракт: отрицательные `Exists` вне статически перечисленного набора = всегда unsupported, и тогда честно вычеркнуть «большие произвольные C# решения» из цели.

Confidence:
High

---

ID: R-03
Severity: High
Category: Baseline

Target:
README.md / «Текущее основание» / строка «версия репозитория 1.3.5»

Claim:
Основание плана — runtime 1.3.5: один `MSBuildWorkspace`, RAM-ключ path+Configuration+Platform+TFM, watcher `.cs`, overlay только в памяти.

Evidence:
`get_mcp_server_info` на момент ревью: 1.3.20. С 1.3.5 закрыты U-ARB-04 (`LoadAndPrepareAsync`, один acquire, published `_solution` only), write boundary + freshness gate, session-sticky overlay, restart-required generator identity. `docs/ARCHITECTURE.md` описывает этот lifecycle и всё ещё указывает план в `docs/workspace-load-cache/` (v1), не v2. `FindDocumentAsync` берёт `FirstOrDefault` по path. Watcher стартует на каталоге `.sln`/`.csproj` и игнорирует `obj`/`bin`.

Failure scenario:
1. Исполнитель эпохи 1 аудирует write-path по тексту 1.3.5 и заново вводит `TryApplyChanges` overlay или dispose-before-open.
2. Epoch 3 вставляет refresh внутрь semantic call и ломает U-ARB-04.
3. `ARCHITECTURE.md` читают как указатель на v1 Merkle/size+mtime, не на этот контракт.

Suggested change:
Перебазировать план на текущий shipped lifecycle (Lock / publication / overlay / watcher root). Явно запретить опираться на 1.3.5 и на соседний каталог v1 как на base truth.

Confidence:
High

---

ID: R-04
Severity: High
Category: Overlay

Target:
README.md / «Текущее основание» / пункт про overlay; cache-contract §2 «Overlay хранится как состояние сессии»

Claim:
Overlay нельзя передавать в `TryApplyChanges` и нельзя класть temp-пути в snapshot. После гидрации overlay повторно применяется к базовой solution.

Evidence:
Точка capture не названа. Production semantic snapshot — `GetCurrentSolution()` / `_solution` после overlay (`SolutionManager`). Сырой `workspace.CurrentSolution` — база без shadow. `shadowCopyInSolutionAnalyzers` не входит в RAM-ключ и не входит в `RequestIdentity`. Prepare после load — отдельная фаза `LoadAndPrepareAsync`, с fail-closed и restart-required.

Failure scenario:
1. Capture читает `_solution` → в DTO попадают shadow-пути из `%TEMP%`. Новый процесс hydrates, файлов нет → пустая генерация или miss, который выглядит как «кэш сломан».
2. Capture читает сырой workspace → hit без overlay. Агент не передаёт флаг (или передаёт `false` на cache-hit) — session-sticky в *новом* PID не действует. Real analyzer path снова лочится / не находится, ради чего overlay и делали.
3. Флаг включают после disk-hit: prepare копирует DLL, first-semantic снова дорогой, критерий README не измеряется.

Suggested change:
Ввести overlay-режим в identity сессии и в ответ load. Capture только с stripped base. Disk-hit сам по себе не восстанавливает generation; first-semantic budget обязан включать optional prepare. Без этого «та же семантика, что MSBuild» ложна для in-solution generators.

Confidence:
High

---

ID: R-05
Severity: High
Category: Scope

Target:
README.md / «Порядок работ» / «Эпохи 0–3 образуют основной маршрут»

Claim:
Основной маршрут — 0–3. Merkle, partial load, metadata references, индекс символов не нужны для диск-кэша. Stop сохраняет обычный MSBuild load.

Evidence:
Epoch 2 на любом изменении `.cs` делает полный miss. Epoch 3 перед каждым semantic call требует strict probes + membership и при `graph-dirty` — обычный load. Цель README — большие решения после рестарта MCP в агентском цикле (правки `.cs`, `dotnet build`, reload). 4C (членство без DTB) optional и сам требует модели Include/Exclude, запрещённой R-02.

Failure scenario:
1. Epoch 2 shipped, default `useDiskCache=false`, hit только на полностью неизменном allowlist-графе.
2. Агент правит один `.cs` или собирает solution (меняются входы в `obj`) — следующего процесса hit нет.
3. Epoch 3 на `Created`/build делает DTB *внутри* живой сессии. Основной маршрут либо не ускоряет заявленный workload, либо делает его медленнее baseline.

Suggested change:
Вынести Epoch 3 из основного маршрута или признать, что Epoch 2 не выполняет цель README без 4C. Не называть 0–3 «путём к ускорению», пока miss-on-any-`.cs` и rebuild-invalidates-obj остаются нормативными.

Confidence:
High

---

ID: R-06
Severity: High
Category: Multi-targeting

Target:
README.md / цель «большие C# решения»; cache-contract §2 inner TFM / outer evaluation

Claim:
Пока корректное сопоставление project instances не доказано, multi-targeting запрос не поддерживает disk-hit. Outer evaluation не гидратируется.

Evidence:
Pitfall 17 и `load_workspace` `targetFramework`: `Directory.Build.props` с `TargetFrameworks` (даже из одного TFM) даёт CrossTargeting outer без `Compile`. Это обычный вид «большого» репозитория, ради которого фича заявлена. Контракт откладывает disk-hit до доказательства mapping, но Epoch 0 не делает mapping предварительным условием go для эпохи 2.

Failure scenario:
1. Epoch 0 go на двух одно-TFM SDK fixture (V01).
2. Целевая монорепа с repo-level `TargetFrameworks` получает `unsupported` на весь граф (контракт §4: один неподдерживаемый проект отключает hit).
3. README цель недостижима при выполнении собственного контракта.

Suggested change:
Либо multi-target/CrossTargeting — blocker Epoch 0 (нет go без него), либо цель сузить до «одно-TFM SDK fixture / этот репозиторий» и не обещать большие решения.

Confidence:
High
