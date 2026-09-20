# Disk-sync `.csproj` mutation — черновик плана

Дата: 2026-09-20. Статус: **superseded**. Канон реализации —
[../spec.md](../spec.md). Дефект — [../README.md](../README.md).
Варианты — [proposed-solution.md](proposed-solution.md).
Учтены [review](review/README.md) и [review-astra](review-astra/README.md).

Базовая версия на момент черновика: **1.3.29**. Ship: **patch 1.3.30**.

## 0. Цель патча

Disk-sync больше не меняет состав документов загруженного `Solution`.
`MSBuildWorkspace.TryApplyChanges` не получает `AddDocument` / `RemoveDocument`
с этого входа. SDK-style `.csproj` перестаёт получать явный `<Compile Include>`
и `NETSDK1022`. Новый или исчезнувший `.cs` становится видимым семантике только
после повторной загрузки workspace.

Не цель: безопасный `AddDocument` у `extract_interface` / `move_type_to_new_file`
(остаётся известный дефект той же природы; отдельный follow-up).

## 1. Закрытые решения (бывший §7)

| # | Решение |
|---|---|
| 7.1 Видимость без reload | **Нет.** Семантика видит новый файл только после reopen. `dotnet build` SDK-проекта компилирует его сразу через glob — это не регрессия. |
| 7.2 Явный `<Compile>` | **Reload обязателен.** Парсера membership нет. C-5 с этого патча снят (review C-04): не-`AddDocument` ничего не удаляет из `.csproj`. После reload в snapshot попадает только то, что реально эвалюирует MSBuild. |
| 7.3 Где гейт | **Только disk-sync.** Глобальный вариант C / T-2 **не входят** (review C-01). `WorkspaceWriteBoundary.Preflight` не сравнивает `DocumentIds`. |
| 7.4 Судьба add/remove веток | **Оставить как Unrepresentable**, не удалять ради D. Вариант D **не брать**: без stale в `QueueDiskPath` пропадает hint (review C-03). |
| 7.5 Формат note | **Два текста, один флаг `_projectGraphStale` + причина.** Не конкатенировать «изменился `.csproj`» к composition-кейсу (review C-05). |
| 7.6 `removed` / rename | **Симметрично A** плюс защита от воссоздания файла (review-astra §2). Не `RemoveDocument`. |
| 7.7 Auto-reload в write-тулах | **Нет.** Только note. `update_file_content` нового `.cs` сразу помечает composition-stale (не ждёт FSW). |

Дополнительно, из ревью, без новых вариантов:

- Смешанная пачка: тексты известных документов синхронизируются; состав — только stale (review-astra §3).
- Overflow watcher (`_refreshAllDocuments`): composition-stale, даже если Unrepresentable пуст (review-astra §5). Hint не обещает, что reload *включит* файл (review-astra §4).

## 2. Норма поведения

1. `WorkspaceDocumentDiskSync` обновляет текст документа, у которого уже есть `DocumentId`.
2. Путь без `DocumentId` (файл есть) или `DocumentId` без файла на диске → **не** `AddDocument` / **не** `RemoveDocument`. Путь в `Unrepresentable`. `Solution` по составу документов совпадает с входом.
3. Flush обрабатывает `Unrepresentable` **до** early-return (review C-02): `_projectGraphStale = true`, причина composition, `LogWarning` со списком путей, write boundary **не** вызывается, если candidate — тот же экземпляр / нет `Updated`.
4. Если в той же пачке есть `Updated > 0`: `TryApplyChanges` только для `WithDocumentText`. `alreadyOnDisk` — только dirty-пути, у которых **есть** `DocumentId` и файл существует. Непринятый новый путь в reconciliation не попадает.
5. Удалённый известный `.cs`: документ остаётся в snapshot (устаревший текст). Путь запоминается как missing-on-disk. Последующий `PersistDocumentChangesAsync` / `UpdateDocumentInMemory*` **не** делает `WriteAllText` по этому пути до reload (иначе MCP воссоздаст файл).
6. Ошибка FSW / directory rename, которые ставят `_refreshAllDocuments`: composition-stale на следующем flush, даже если новых путей нет.
7. `update_file_content` для `.cs` с `WorkspaceWriteStatus.Skipped` (`not-in-workspace`): сразу composition-stale + hint в ответе. `SuppressDiskWatchForPath` иначе глотает `Created` на 1 с, и Unrepresentable может не появиться.
8. Следующий `load_workspace` при `_projectGraphStale` уже сбрасывает load-cache — reopen подхватывает glob. Тот cache-hit, *во время которого* flush только что выставил stale, **не** reopen'ится в том же вызове (не трогать overlay/publication). Агент видит hint и вызывает load ещё раз.

## 3. Изменения в коде

Минимальный дифф. Без overlay документов, без парсера csproj, без правки `WorkspaceWriteBoundary`.

### 3.1. `Services/WorkspaceDocumentDiskSync.cs`

- Расширить `WorkspaceDocumentDiskSyncResult`: `IReadOnlyList<string> Unrepresentable` (полные пути). `Added` и `Removed` после патча всегда 0 с этого API; поля оставить, чтобы логи/тесты не гадали по имени.
- Ветку «нет DocumentId, файл есть» и «есть DocumentId, файла нет»: не мутировать `current`; добавить путь в Unrepresentable; `unchanged` для «нет id и нет файла» оставить.
- XML-комментарий класса: «sync texts of known documents; composition changes are reported, not applied».

### 3.2. `Services/SolutionManager.cs`

- Причина stale: флаг/enum рядом с `_projectGraphStale` (`GraphFile` / `CompositionUnknown`; можно оба). Сброс в `ClearWorkspaceAsync` и cache-miss `LoadCoreAsync` вместе со stale.
- `GetProjectGraphStaleHint()`:
  - только graph-file — текущий текст про `.csproj` / `.sln` / `Directory.Build.*`;
  - только composition — отдельный абзац: обнаружено появление или исчезновение `.cs` вне загруженного снимка; membership решает MSBuild при reload, файл **не гарантирован**; `reset_workspace` + `load_workspace` или повторный `load_workspace` (stale сбрасывает cache);
  - оба — два абзаца, не одна ложная причина.
- `FlushDirtyDocumentsUnderLockAsync`:
  1. `ApplyAsync`;
  2. если `Unrepresentable.Count > 0` **или** этот flush был `refreshAll` после watcher error/dir-rename → stale + composition + warning;
  3. missing-on-disk пути (были в snapshot, файла нет) → внутренний set до reload;
  4. **затем** существующий early-return, если нет `Updated` и тот же `Solution`;
  5. иначе write boundary как сейчас, но `alreadyOnDisk` фильтровать по `FindDocumentIdForPath != null`.
- Лог `workspace_disk_sync`: `added` больше не растёт; писать `unrepresentable=N`.
- `PersistDocumentChangesAsync`: если путь в missing-on-disk set — пропустить запись, залогировать warning (не воссоздавать файл).
- Публичный/internal хук для EditingTools, например `NoteUnrepresentableSourcePath(string fullPath)`: composition-stale без AddDocument.
- Не ставить stale в `QueueDiskPath` вместо Unrepresentable (это было бы D). Watcher по-прежнему кладёт любой `.cs` в dirty.

### 3.3. Подсказки в ответах тулов (D-04)

`WithDiskSyncNotes` уже есть, но висит только на `NavigationTools`. Инциденты шли через `run_specific_test` / `load_workspace` / `get_test_list`.

Добавить `WithDiskSyncNotes` (или тот же hint) в ответы:

- `Tools/TestTools.cs` — `get_test_list`, `run_specific_test` (фильтр строится из snapshot; новый тест-файл его не увидит);
- `Tools/WorkspaceTools.cs` — `load_workspace` после успешного load, если hint не null (в т.ч. cache-hit, который только что пометил composition);
- `Tools/EditingTools.cs` — `update_file_content`, когда write skipped как not-in-workspace для `.cs`.

Не размазывать по всем тулам. `run_dotnet_build` не обязан: после A glob собирает файл без мутации csproj.

### 3.4. Lifecycle host (для T-3/T-5)

`HostResponse` + `Inspect`: `ProjectGraphStale`, `ProjectGraphStaleHint` (строка из `GetProjectGraphStaleHint`). Не переиспользовать `LastRefreshStale` (это overlay).

## 4. Тесты

C-6: на текущем коде красные тесты — инверсия существующих Facts, не соседние дубли (review V-02). T-2 **не писать**.

| ID | Что | Уровень | Красный на 1.3.29? |
|---|---|---|---|
| T-1 | Инвертировать `ApplyAsync_adds_new_cs_under_project_directory`: `Added=0`, Unrepresentable содержит путь, состав документов не вырос, исходный `Solution` не содержит новый `DocumentId` | unit | да |
| T-1b | Инвертировать `ApplyAsync_removes_document_when_file_deleted`: `Removed=0`, документ остаётся, путь в Unrepresentable | unit | да |
| T-1c | Смешанная пачка: dirty = изменённый `A.cs` + новый `B.cs` → `Updated=1`, Unrepresentable=`B.cs`, текст A с диска, B не в solution | unit | да (сейчас B добавляется) |
| T-3a | Lifecycle: новый `.cs` в Consumer → `waitDirty` → `flushFind`/`flushGetter` → SHA всех `.csproj` как до; нет нового `<Compile Include>` | integration | да (csproj меняется) |
| T-3b | Тот же fixture: `build` Consumer **до** reload, exit 0, в логе нет `NETSDK1022` | integration | да (сейчас 1022) |
| T-3c | `find`/`flushFind` не видит тип нового файла до reopen; после `reset`+`load` (или второго load при stale) — видит | integration | поведение меняется намеренно |
| T-3d | Ответ flush/inspect содержит composition-hint, **без** фразы что изменился `.csproj` | integration | да (сейчас stale от самозаписи csproj, текст про csproj) |
| T-5 | T-3d через flush host, не через голый `ApplyAsync` (review C-02) | integration | — |
| T-6 | Watcher-error путь: выставить refreshAll (host inject или прямой вызов) → flush → composition-stale даже без нового файла | unit/host | нет на 1.3.29; защита Astra §5 |
| T-7 | Удалить известный `.cs` → flush → документ жив; `updateDocument` на этот путь не создаёт файл на диске | integration | нет на 1.3.29; защита Astra §2 |

`Epoch1HostOps.AssertProjectFilesUnchanged` уже SHA. Отдельный «нет `<Compile Include>` во всех сценариях» (бывший T-4) **не** делать глобальным: легко ложно сработает на легитимный XML. Достаточно T-3a.

Не добавлять T-2 на `Preflight` / `document-set-diff`.

## 5. Docs и версия (в том же ship)

Patch **1.3.29 → 1.3.30** (`Version` / `AssemblyVersion` / `FileVersion`). `PackageVersion` не трогать.

- Корневой `README.md` «Agent tools by version»: строка **1.3.30**; в блоке **v1.1.0** снять обещание «New `.cs` … are `AddDocument`’d; deleted files are removed» (review C-06). Норма: disk-sync = известные документы; новый/удалённый `.cs` → composition-stale + reload.
- `AGENTS.md.sample`: в File editing уточнить, что автосинк — правки **уже загруженных** `.cs`; создание/удаление файла → `reset_workspace` + `load_workspace` (или повторный load при hint) **перед** symbol/test-discovery.
- `.cursor/rules/roslyn-mcp-overview.mdc` — history 1.3.30.
- `.cursor/rules/roslyn-mcp-pitfalls.mdc` — новый пункт: `TryApplyChanges` + `AddDocument` пишет backing `.csproj` → `NETSDK1022`; disk-sync этого не делает; новый файл требует reload. Не чинить через `EnableDefaultCompileItems=false` / `global.json`.
- `docs/ARCHITECTURE.md` § Workspace lifecycle: пункт 3 — только тексты известных документов; состав — stale + reopen.
- После ship: факты в ARCHITECTURE/README; этот план и proposed-solution пометить shipped, не переписывать «под факт».

## 6. Порядок работ

1. ~~Перенос `review/` + `review-astra/` → `_archive/`~~ — сделано.
2. Красные unit T-1 / T-1b / T-1c на текущем коде (подтвердить C-6).
3. `WorkspaceDocumentDiskSync` + инверсия Facts.
4. Flush + hint + missing-on-disk + EditingTools hook.
5. Lifecycle T-3a…d, T-5, T-7; T-6.
6. Docs + bump 1.3.30.
7. `run_dotnet_test` по затронутым тестам; publish + reload; `get_mcp_server_info` = 1.3.30.

Не смешивать с вариантом C, overlay документов, парсером glob/`Compile Remove`, auto-reload refactoring-тулов.

## 7. Что патч закрывает и что нет

| ID | После патча |
|---|---|
| D-01 | Закрыт для **disk-sync**. `extract_interface(createNewFile=true)` / `move_type_to_new_file` всё ещё могут писать `<Compile Include>` — follow-up. |
| D-02 | Закрыт: неизвестный `.cs` не добавляется в Solution. |
| D-03 | **Не закрыт** (намеренно: глобальный C ломает write-тулы). |
| D-04 | Закрыт на navigation + test list/specific + load + update_file_content нового `.cs`. |
| D-05 | Закрыт для этого механизма: csproj больше не мутируется disk-sync, самонаведённый graph-file stale пропадает. Composition-stale — честный, не шум. |
| D-06 | Закрыт тестами T-1 / T-3a. |
| D-07 | Закрыт по составу + no-recreate. Семантика до reload всё ещё видит удалённый тип — это контракт A, не баг. |
| D-08 | Документируется в 1.3.30 / pitfalls. |

## 8. Follow-up (не этот патч)

1. Намеренный `AddDocument` в refactoring-тулах: persist `.cs` + stale + reload **или** безопасный writer, который не дублирует SDK glob. Пока C нельзя включать.
2. Испорченные чужие `.csproj` (MobsAdmin и т.п.) — ручная правка, MCP не сканирует.
3. Вариант B (overlay-документ) — отдельная спека.
