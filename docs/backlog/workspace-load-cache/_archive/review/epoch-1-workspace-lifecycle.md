ID: E1-01
Severity: Blocker
Category: WritePath

Target:
epoch-1-workspace-lifecycle.md / «Работа» / пункт про семантику записи и `.csproj`

Claim:
Семантика записи не должна случайно зависеть от того, применяет ли конкретный `Workspace` изменения к `.csproj` на диске. Приёмка V07: одинаковое поведение редактирования на обычном и гидратированном workspace; последующая обычная сборка и неизменность analyzer refs.

Evidence:
`MSBuildWorkspace.TryApplyChanges` для project changes пишет `.csproj` (уже доказано на overlay: `AddAnalyzerReference` → `<Analyzer Include>`). `AdhocWorkspace` этого не делает. Требование «не зависеть от типа Workspace» + «эквивалентные записи» взаимоисключающие, пока host не выбран (R-01). Кастомный writer `.csproj` — новая подсистема, в эпохе её нет.

Failure scenario:
1. Hydrate в Adhoc; V07 тестирует `apply_patch` текста — зелёный.
2. Агент вызывает `rename_project` / code fix, добавляющий файл — `.csproj` не обновлён, `dotnet build` не видит файл.
3. Либо hydrate в MSBuildWorkspace через Open — тогда «без disk-hit» эпохи 1 держится на том же DTB, который кэш должен пропускать.

Suggested change:
Снять требование независимости от host. Зафиксировать: гидратированная сессия либо read-only для project mutations, либо это всё ещё `MSBuildWorkspace` после Open (нет пропуска DTB). Иначе эпоха не имеет реализуемого acceptance.

Confidence:
High

---

ID: E1-02
Severity: Blocker
Category: Lifecycle

Target:
epoch-1-workspace-lifecycle.md / «Кандидат публиковать целиком; cancellation сохраняет прежнюю сессию; stale не очищается»

Claim:
Временный loader имеет отдельный lifetime. Cancellation/failure освобождает временные ресурсы и сохраняет прежнюю сессию. Stale при этом не очищается.

Evidence:
`LoadCoreAsync` при cache miss *сначала* `StopDiskWatcher` + `Dispose` + обнуление `_solution` / overlay / provenance / `_projectGraphStale = false`, *потом* `MSBuildWorkspace.Create` и Open (`SolutionManager` ~1498–1510). U-ARB-04 держит один acquire вокруг load+prepare; после dispose старой сессии уже нет. «Сохранить прежнюю» требует инверсии: не dispose до publish. Это конфликт с текущим fail-closed opt-in (при ошибке нового load старый overlay/publication уже сброшен).

Failure scenario:
1. Исполнитель оборачивает текущий `LoadCoreAsync` — cancel после dispose оставляет `_workspace == null`, «прежняя сессия» уничтожена, требование ложно.
2. Исполнитель держит два workspace до publish и отпускает `_workspaceLock` на время DTB — semantic reader видит старый `_solution` или null; U-ARB-04 сломан.
3. Failure нового load сохраняет *stale* старую сессию; следующий `find_symbol_*` без явного stale в ответе (stale сброшен в текущем коде при входе в reopen).

Suggested change:
Описать инверсию dispose-before-open как обязательную работу эпохи и её взаимодействие с U-ARB-04 / Banned publication. Пока этого нет, пункт «сохранить прежнюю сессию» несовместим с base truth.

Confidence:
High

---

ID: E1-03
Severity: High
Category: Snapshot

Target:
epoch-1-workspace-lifecycle.md / «Сохранять несколько членств linked file и TFM contexts»

Claim:
Не выбирать только первый `DocumentId` или ближайший каталог проекта как доказательство членства.

Evidence:
`FindDocumentAsync` (`SolutionManager` ~434–437): `FirstOrDefault` по нормализованному `FilePath`. Большинство file-scoped semantic tools идут через этот метод. Контракт §3 прямо запрещает карту `path → один DocumentId`. Эпоха 1 делает это production-изменением всех tools, не тестовым hydrator.

Failure scenario:
1. Тесты гидрации проверяют оба membership в DTO — зелёные.
2. `get_diagnostics_for_file` / code fix идут в первый project/TFM.
3. Второй TFM молча не обновляется; V06 «все членства» ложно для реального tool path. Либо меняют `FindDocumentAsync` — diagnostics/fixes начинают применяться к другому instance, регресс без disk-кэша.

Suggested change:
Либо сузить эпоху: DTO хранит все членства, tools остаются FirstOrDefault (тогда V07/V06 через MCP ложны), либо вынести смену FindDocument в отдельную shipped-работу с матрицей TFM. Нельзя прятать breaking change в «внутренний механизм без новых tool names».

Confidence:
High

---

ID: E1-04
Severity: High
Category: Overlay

Target:
epoch-1-workspace-lifecycle.md / «Все semantic tool paths получают overlay»

Claim:
Все semantic пути получают overlay; все пути применения изменений соблюдают защиту shadow refs. Production load остаётся обычным MSBuild. Нет изменения defaults.

Evidence:
Overlay opt-in и session-sticky (`load_workspace` Description, `ARCHITECTURE.md`). «Все пути получают overlay» либо включает overlay без флага (смена default), либо верно только при флаге (тогда hydrate path без флага не покрыт). Write boundary уже существует (v1.3.8+); аудит «как в 1.3.5» рискует переписать её.

Failure scenario:
1. Эпоха включает overlay на гидратированном workspace всегда — default изменился, вопреки приёмке.
2. Эпоха применяет overlay только при флаге; V08 на hydrate без флага не проверяет shadow write; Epoch 2 hit без флага пишет/лочит original analyzer (C-05).
3. Аудит заменяет exact-inverse mapping на новый «универсальный» revert.

Suggested change:
Явно: overlay остаётся opt-in; hydrate обязан проходить *существующую* write boundary, не заменять её. V08 гонять с флагом on и off.

Confidence:
High

---

ID: E1-05
Severity: High
Category: Scope

Target:
epoch-1-workspace-lifecycle.md / «Обнаружение нового файла scanner не разрешает переписывать `.csproj`»

Claim:
Явно разделить отражение уже произошедшего disk change и пользовательскую правку проекта. Scanner не переписывает `.csproj` через `TryApplyChanges`.

Evidence:
Scanner/membership — Epoch 2/3. В Epoch 1 его нет. Пункт либо мёртв (vacuous pass), либо тянет scanner в эпоху «без disk-hit». `WorkspaceDocumentDiskSync` при `refreshAll` работает по *известным* documents, не добавляет членство в csproj — текущий код уже не пишет csproj от нового файла. Риск в будущем `AddDocument` на MSBuildWorkspace.

Failure scenario:
1. Исполнитель считает пункт выполненным, потому что scanner ещё нет.
2. Epoch 2/3 добавляет Created → `AddDocument` на `MSBuildWorkspace` → правка `.csproj`.
3. Запрет эпохи 1 не перенесён, handoff эпохи 1 «write-safe» устарел.

Suggested change:
Сформулировать как инвариант *серии*, проверяемый в Epoch 3 V19, не как работу Epoch 1. В Epoch 1 — только запрет `AddDocument`/`TryApplyChanges` project mutations на hydrate-host, если host это MSBuild.

Confidence:
High

---

ID: E1-06
Severity: High
Category: Lifecycle

Target:
epoch-1-workspace-lifecycle.md / «Production load остаётся обычным MSBuild»; «внутренний механизм с проверками»

Claim:
Результат эпохи — внутренний механизм. Нет чтения production кэша, новых tool names, изменения defaults. Production load остаётся обычным MSBuild.

Evidence:
Граница loader/session, notification собственных записей, смена membership, инверсия dispose, аудит всех write path — это production `SolutionManager`. Тесты V07–V09 на hydrate, который в production не вызывается, не защищают путь, который вызывается. Симметрично: поломка production MSBuild-path тестами hydrate не ловится.

Failure scenario:
1. Green на изолированном hydrator.
2. Notification path / двойной workspace меняют lock и publication в production load.
3. Epoch 2 включает hydrate в `load_workspace`; первый раз production видит путь, который тестировали только как «внутренний».

Suggested change:
Если меняется `SolutionManager` lifecycle — это shipped поведение, даже без disk-hit. Приёмка должна гонять *оба* production load (MSBuild) и hydrate на одних write tools. Не называть изменение ядра «не production».

Confidence:
High

---

ID: E1-07
Severity: Medium
Category: Overlay

Target:
epoch-1-workspace-lifecycle.md / Handoff «доказательство отсутствия записанных overlay paths»

Claim:
Handoff доказывает, что overlay paths не записаны. Любой потерянный compiler input блокирует переход к Epoch 2.

Evidence:
Доказательство «нет shadow в csproj» уже есть у write boundary. Потерянный compiler input на hydrate (пропущенный AdditionalFile / editorconfig / linked membership) блокирует Epoch 2 — правильно, но Epoch 1 не имеет независимого oracle полного input set (C-01). Handoff может приложить «все documents из DTO == documents из MSBuild project» и пропустить imports, которых в `Project` нет.

Failure scenario:
1. Сравнивают только `project.Documents`.
2. Пропавший `Directory.Build.props` как вход не виден.
3. Epoch 2 получает go по неполному DTO.

Suggested change:
Список обязательных категорий сравнения взять из контракта §3 *включая* non-document inputs (props, assets, analyzer DLL). Не сводить «compiler input» к `Documents`.

Confidence:
High
