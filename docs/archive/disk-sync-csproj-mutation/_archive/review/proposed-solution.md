ID: C-01
Severity: Blocker
Category: WritePath

Target:
proposed-solution.md / §1 C-1; §2 вариант C; §3 «A + C»; §4 T-2; §7.3

Claim:
`MSBuildWorkspace.TryApplyChanges` не должен получать candidate с изменившимся
составом документов. Гейт в `WorkspaceWriteBoundary.Preflight`
(`DocumentIds` cleaned ≠ workspaceCurrent → `document-set-diff`) закрывает
**все** пути к `TryApplyChanges`, не только FSW, включая будущие AST-тулы.
T-2 кодирует это: любой candidate с добавленным документом → Preflight `false`.

Evidence:
`WorkspaceWriteBoundary.Preflight` / `ClassifyAndInvert` — единственный гейт
перед `TryApplyWorkspaceChanges` (`Services/WorkspaceWriteBoundary.cs`, цикл
по проектам сейчас сравнивает только `AnalyzerReferences`). Тот же
`ApplyWorkspaceWriteUnderLockAsync` → Preflight → `TryApplyChanges`
(`Services/SolutionManager.cs`) вызывают `extract_interface` и
`move_type_to_new_file` (`Tools/RefactoringTools.cs` →
`ApplySolutionChangesToDiskAsync`). Они делают `Solution.AddDocument`
(`Services/StructuralRefactoringHelper.cs`) и пишут `.cs` через
`PersistDocumentChangesAsync`, после чего `cleaned` всё ещё содержит новый
`DocumentId`. `docs/workspace-load-cache/epoch-1-workspace-lifecycle.md`:
external disk reconciliation не пишет `.csproj`; **намеренный** AddDocument
проходит capability/A-WRITE — это прямо запрещает глобальный C-1.
Для SDK-style те же тулы уже дают тот же NETSDK1022; §8 выносит «другой способ
представления новых файлов» за рамки, а C обещает закрыть все пути.

Failure scenario:
1. Исполнитель вносит C-1/C/T-2 в `ClassifyAndInvert` без флага намерения.
2. `extract_interface(createNewFile=true)` проходит persist `.cs`, Preflight
   отвергает `document-set-diff`; тул возвращает «not fully applied».
3. T-2 зелёный, A+C принимается; публичные refactoring-тулы сломаны. Либо C
   не внедряют, а C-1 читают как уже выполненную норму и считают D-01 закрытым
   по всем `TryApplyChanges`, хотя write-тулы продолжают мутировать `.csproj`.

Suggested change:
C-1 сузить до **external disk reconciliation** (disk-sync candidate). Вариант C
и T-2 — только этот вход (или `persistDocuments: false` + composition-diff),
не весь `Preflight`. Намеренный AddDocument — отдельный follow-up (persist `.cs`
+ `_projectGraphStale` + reload, либо A-WRITE). §3 рекомендовать **A**, не A+C.
§7.3: гейт только disk-sync.

Confidence:
High

---

ID: C-02
Severity: High
Category: Lifecycle

Target:
proposed-solution.md / §2 вариант A, шаги 1–2

Claim:
При непустом `Unrepresentable` `FlushDirtyDocumentsUnderLockAsync` взводит
`_projectGraphStale`, логирует пути, и `TryApplyChanges` не выполняется,
потому что solution без изменений состава.

Evidence:
После `WorkspaceDocumentDiskSync.ApplyAsync` flush уже делает early-return,
если `Updated/Added/Removed == 0` и `ReferenceEquals(result.Solution,
workspace.CurrentSolution)` (`Services/SolutionManager.cs`, блок сразу после
`ApplyAsync`). В этом return `_projectGraphStale` не трогается. Если шаг 1
оставляет тот же `Solution` и `Added == 0`, шаг 2 **не достигается**, пока
спека явно не поставит обработку `Unrepresentable` **выше** этого return.

Failure scenario:
1. Исполнитель убирает `AddDocument`, `Added=0`, solution — тот же экземпляр.
2. Flush выходит по существующему early-return; stale не взводится; note нет.
3. T-1 зелёный (состав не вырос, `.csproj` цел), D-04 жив: агент видит успех
   семантического тула без hint. T-5, если тестирует только `ApplyAsync`,
   тоже зелёный.

Suggested change:
Норма A: `Unrepresentable` обрабатывается в flush **до** no-op return
(stale + warning + не вызывать write boundary). T-5 обязан идти через flush,
не через один `ApplyAsync`.

Confidence:
High

---

ID: C-03
Severity: High
Category: Lifecycle

Target:
proposed-solution.md / §2 вариант D; §3 «D опционально»; §7.4

Claim:
D — упрощённая редакция A: watcher не кладёт в dirty пути вне документов
загруженного решения, тогда `WorkspaceDocumentDiskSync` не видит add-кейс.
После A add-ветка становится мёртвой; D можно взять как упрощение контракта.

Evidence:
`QueueDiskPath` добавляет любой `.cs` вне ignore/bin (`SolutionManager.cs`).
Stale в A возникает только из `Unrepresentable` после `ApplyAsync` (шаг 2).
Если D отфильтрует неизвестный путь на watcher, dirty пуст, `ApplyAsync` не
вызывается или не видит путь, `Unrepresentable` пуст. Спека не требует
ставить `_projectGraphStale` в `QueueDiskPath`. Минус D («теряется waitDirty»)
не покрывает потерю единственного сигнала «нужен reload».

Failure scenario:
1. Берут D вместо A, или A затем D «вычистить мёртвую ветку», без stale в
   `QueueDiskPath`.
2. Новый `.cs` не попадает в dirty; composition и `.csproj` не меняются.
3. Нет mutation (D-01 «закрыт»), нет hint (D-04), `waitDirty` молчит; агент
   считает файл частью workspace. A и D не эквивалентны.

Suggested change:
D не альтернатива и не упрощение A. Если сужать dirty — stale ставить в
`QueueDiskPath` при пути без `DocumentId`. §7.4: add/remove-ветки оставляют
и возвращают `Unrepresentable`, не удаляют ради D.

Confidence:
High

---

ID: C-04
Severity: High
Category: Contract

Target:
proposed-solution.md / §1 C-5; §2 минусы A; §7.2

Claim:
Нельзя терять файл из проекта, где `<Compile>` задан явно. Минус A: для таких
проектов новый файл тоже требует reload — формально корректно, но надо
проверить, что после reload он не потеряется. §7.2 допускает отдельную ветку
«файл уже в project graph, но Roslyn его не знает».

Evidence:
`FindContainingProject` выбирает проект по **каталогу** `.csproj`, не по glob /
`Compile Include` / `Compile Remove` (`WorkspaceDocumentDiskSync.cs`). Для
SDK-style новый файл уже в графе через default items; `AddDocument` его не
«сохраняет», а дублирует item (D-01). Для old-style файл не в графе, пока нет
явного item: не-`AddDocument` ничего не удаляет из `.csproj`. Reload
восстанавливает только то, что MSBuild реально компилирует; нового файла без
item там не будет — это не регрессия A.

Failure scenario:
1. Исполнитель откладывает A из‑за C-5 / ветки 7.2 «уже в graph».
2. Membership выводят из папки или неполного XML (без `Choose`/условий/Remove).
3. Снова `AddDocument` для файла под каталогом, либо `Compile Remove`
   оказывается в `Solution`; NETSDK1022 / ложная семантика. Либо A не ship'ят,
   пока не напишут парсер project graph.

Suggested change:
C-5 снять как ограничение этого патча. §7.2: только «reload обязателен», без
ветки «уже в graph». Membership без ре-эвалютации не восстанавливается.

Confidence:
High

---

ID: C-05
Severity: Medium
Category: Observability

Target:
proposed-solution.md / §2 вариант A, шаг 3; `GetProjectGraphStaleHint`

Claim:
Агенту достаточно дополнить существующий `GetProjectGraphStaleHint` случаем
«новый файл на диске не входит в загруженный project graph».

Evidence:
Текущий hint безусловно говорит, что изменились `.csproj` / `.sln` /
`Directory.Build.props` (`SolutionManager.GetProjectGraphStaleHint`). После A
при `Unrepresentable` `.csproj` как раз **не** меняется; `_projectGraphStale`
взводится без FSW на project file. Дописывание второго предложения к первому
даёт ложную причину (диск проекта якобы изменён).

Failure scenario:
1. Реализуют шаг 3 как конкатенацию к нынешнему тексту.
2. Flush по новому `.cs` ставит stale, hint сообщает про изменение `.csproj`.
3. Агент ищет diff проекта, не находит, считает note ложным / чинит
   `global.json`. D-04 формально «есть note», oracle вводит в заблуждение.

Suggested change:
Текст hint различать: graph file changed vs unrepresentable source (новый /
удалённый `.cs` вне snapshot). Один флаг допустим, формулировка — нет.

Confidence:
High

---

ID: C-06
Severity: Medium
Category: Contract

Target:
proposed-solution.md / §5 влияние на docs; корневой README v1.1.0

Claim:
При ship достаточно patch bump, «Agent tools by version», pitfalls про
`TryApplyChanges`+`AddDocument`, и AGENTS только если меняется session policy.

Evidence:
Корневой README, блок v1.1.0, до сих пор нормирует: «New `.cs` under a project
folder are `AddDocument`’d; deleted files are removed.» Это агентский контракт
текущего поведения, не историческая сноска. §5 не требует снять это обещание.
Pitfall про NETSDK1022 не отменяет инструкцию 1.1.0 «файл появляется в
workspace без reload».

Failure scenario:
1. Патч A ship'ят, pitfalls/version history обновляют, абзац 1.1.0 оставляют.
2. Агент по README ждёт, что новый сохранённый `.cs` сразу в symbol-тулах.
3. После A его нет до reload; повторный `AddDocument` через обходной путь или
   ложный «регресс MCP».

Suggested change:
§5: обязательная ретракция поведения 1.1.0 (disk-sync = известные документы;
новый/удалённый `.cs` → stale + reload). AGENTS — если в session policy ещё
есть «создал файл → сразу семантика».

Confidence:
High

---

ID: V-01
Severity: High
Category: Measurement

Target:
proposed-solution.md / §4 T-3

Claim:
Lifecycle: новый `.cs` в fixture `Consumer` → flush → SHA `.csproj` не
изменился, marker жив, **файл компилируется только после**
`reset_workspace`+`load_workspace`.

Evidence:
Причина NETSDK1022 — явный `<Compile Include>` поверх SDK glob, не отсутствие
файла в компиляции. После A glob по-прежнему включает новый `.cs`;
`dotnet build` без reload должен пройти. Reload нужен symbol-тулам /
in-memory `DocumentId`, не MSBuild. T-3 склеивает «нет мутации csproj»,
«нет NETSDK1022» и «семантика после reopen» в один oracle «компилируется
только после reload».

Failure scenario:
1. T-3 требует compile-success только на шаге после reload.
2. Сборка до reload уже зелёная → тест красный при правильном A; исполнитель
   «чинит», снова пуская `AddDocument`, либо пропускает pre-reload build.
3. Ложный fail A или vacuous pass без проверки NETSDK1022.

Suggested change:
Разрезать T-3: (a) SHA `.csproj` / нет нового `<Compile Include>`; (b) сборка
без reload без NETSDK1022; (c) `find_symbol_*` не видит тип до reload и видит
после; (d) hint в ответе тула.

Confidence:
High

---

ID: V-02
Severity: High
Category: Measurement

Target:
proposed-solution.md / §4 T-1, T-2; §1 C-6

Claim:
T-1 — новый unit-тест, который падает на текущем коде: новый `.cs` под
каталогом проекта не увеличивает состав документов. T-2: любой candidate с
добавленным документом → Preflight `false`. C-6: изменение проверяемо тестом,
падающим на текущем коде.

Evidence:
Уже есть `WorkspaceDocumentDiskSyncTests.ApplyAsync_adds_new_cs_under_project_directory`:
`Assert.Equal(1, result.Added)` и документ есть в solution. Это не пробел, а
зафиксированный баг как норма. T-1 на текущем коде красный; после A красный
существующий Fact, если его не инвертировать. T-2 без сужения входа — тот же
глобальный C-1, что C-01.

Failure scenario:
1. Добавляют T-1, оставляют старый Fact; после A CI красный на старом тесте,
   откатывают A как регресс тестов.
2. Либо зеленят T-2 на `Preflight` без контекста операции; write-тулы с
   `AddDocument` начинают падать в integration после включения C.
3. C-6 формально выполнен (T-1 падал), ship ломает другой канон тестов/тулов.

Suggested change:
T-1 = инверсия существующего Fact, не соседний дубль. T-2 не принимать, пока
C-01 не сузит гейт; иначе T-2 обязан различать disk-sync candidate и
намеренный AddDocument.

Confidence:
High
