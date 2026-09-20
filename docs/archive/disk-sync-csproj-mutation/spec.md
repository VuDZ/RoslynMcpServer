# Disk-sync не мутирует `.csproj` — спецификация патча

Дата: 2026-09-20. Статус: **shipped 1.3.30**.
Канон реализации этой темы. Дефект: [README.md](README.md). Варианты и
отклонённые направления: [_archive/proposed-solution.md](_archive/proposed-solution.md).
Process: [_archive/](_archive/README.md).

Базовая версия: **1.3.29**. Ship: **patch 1.3.30** (`Version` /
`AssemblyVersion` / `FileVersion` вместе). `PackageVersion` не менять.

Другой чат реализует **только этот файл**. Не расширять scope. Не включать
вариант C (глобальный `document-set-diff` в `Preflight`). Не парсить project
graph. Не делать overlay документов.

После ship: факты скопировать в `docs/ARCHITECTURE.md` и корневой README;
этот файл пометить shipped и **не** править «под факт».

## 1. Цель

Disk-sync синхронизирует тексты **известных** документов загруженного
`Solution` и не меняет состав документов. `MSBuildWorkspace.TryApplyChanges`
на этом входе не получает `AddDocument` / `RemoveDocument`, поэтому не пишет
явный `<Compile Include>` в SDK-style `.csproj` и не вызывает `NETSDK1022`.

Новый или исчезнувший `.cs` виден семантическим тулам только после повторной
загрузки workspace (`reset_workspace` + `load_workspace`, либо повторный
`load_workspace` при `_projectGraphStale` — cache уже сбрасывается).

`dotnet build` SDK-проекта компилирует новый файл через glob **до** reload.
Это ожидаемо, не регрессия.

## 2. Не цели

- Безопасный `AddDocument` у `extract_interface(createNewFile=true)` и
  `move_type_to_new_file`. Тот же NETSDK1022 там остаётся; follow-up.
- Глобальный гейт состава документов в `WorkspaceWriteBoundary.Preflight`.
- Фильтрация dirty-путей в `QueueDiskPath` (вариант D).
- Overlay неизвестного `.cs` в published `_solution`.
- Парсер glob / `<Compile Include>` / `<Compile Remove>` / конфигураций.
- Auto `reset_workspace`+`load_workspace` из write-тулов.
- Починка уже испорченных чужих `.csproj`.
- `EnableDefaultCompileItems=false` или правка `global.json`.

## 3. Инварианты

| ID | Норма |
|---|---|
| I-1 | Candidate disk-sync, который уходит в `TryApplyChanges`, имеет тот же набор `DocumentId`, что и `workspace.CurrentSolution`. |
| I-2 | `WorkspaceDocumentDiskSync.ApplyAsync` не вызывает `Solution.AddDocument` и `Solution.RemoveDocument`. |
| I-3 | Неизвестный `.cs` под каталогом проекта не увеличивает состав `Solution`. |
| I-4 | Исчезнувший известный `.cs` не удаляется из `Solution` до reload. |
| I-5 | После обнаружения missing-on-disk MCP не делает `File.WriteAllText` по этому пути до reload. |
| I-6 | Смешанная пачка: тексты известных документов применяются; изменение состава только stale + hint. |
| I-7 | `_projectGraphStale` из composition **не** сопровождается текстом «изменился `.csproj`», если graph-file не менялся. |
| I-8 | Hint composition не обещает, что reload *включит* файл; membership решает MSBuild. |
| I-9 | `WorkspaceWriteBoundary` в этом патче не меняется. |
| I-10 | Watcher по-прежнему кладёт любой не-ignore `.cs` в `_dirtySourcePaths`. |

I-1 сужает старый C-1 до **входа disk-sync**. Намеренный `AddDocument`
write-тулов этим патчом не закрывается.

C-5 (явный `<Compile>`) с патча снят: не-`AddDocument` ничего не выкидывает
из `.csproj`. Reload обязателен и для old-style.

## 4. Контракт поведения

### 4.1. `WorkspaceDocumentDiskSync.ApplyAsync`

Расширить результат полем `IReadOnlyList<string> Unrepresentable` (полные
пути). Поля `Added` и `Removed` оставить; после патча с этого API они всегда
`0`.

Для каждого пути из dirty / refreshAll:

| Состояние | Действие |
|---|---|
| Есть `DocumentId`, файл есть | `WithDocumentText`; `Updated` / `Unchanged` как сейчас |
| Есть `DocumentId`, файла нет | **не** `RemoveDocument`; путь → `Unrepresentable` |
| Нет `DocumentId`, файл есть | **не** `AddDocument`; путь → `Unrepresentable` (даже если `FindContainingProject` нашёл каталог) |
| Нет `DocumentId`, файла нет | `Unchanged`; не в `Unrepresentable` |

Состав документов выходного `Solution` равен входному. XML-комментарий класса:
sync texts of known documents; composition changes are reported, not applied.

### 4.2. `FlushDirtyDocumentsUnderLockAsync`

Порядок **обязателен** (иначе stale не взводится из-за early-return):

1. Снять dirty и флаг `refreshAll` в локальные переменные, как сейчас.
2. `ApplyAsync`.
3. Если `Unrepresentable` не пуст **или** этот flush был `refreshAll`
   (watcher error / directory rename): `_projectGraphStale = true`, причина
   `CompositionUnknown`, `LogWarning` со списком путей.
4. Пути из `Unrepresentable`, которых нет на диске, но для которых во
   входном solution был `DocumentId` → внутренний set missing-on-disk
   (живёт до cache-miss load / `ClearWorkspaceAsync`).
5. **Затем** early-return, если `Updated == 0` и `ReferenceEquals` с
   `workspace.CurrentSolution`. Write boundary в этом случае **не** вызывать.
6. Иначе write boundary как сейчас (`persistDocuments: false`), но
   `alreadyOnDisk` только из dirty-путей, у которых `FindDocumentIdForPath != null`
   и `File.Exists`. Новый/непринятый путь в reconciliation не попадает.

Лог `workspace_disk_sync`: писать `unrepresentable=N`; `added` больше не растёт.

`QueueDiskPath` не менять: не ставить stale там вместо Unrepresentable.

### 4.3. Причина stale и тексты hint

Рядом с `_projectGraphStale` — флаги причин (оба могут быть истинны):

- `GraphFile` — как сейчас, FSW на `.csproj` / `.sln` / `Directory.Build.*`.
- `CompositionUnknown` — §4.2 шаг 3 или хук §4.5.

Сброс причин вместе с `_projectGraphStale` в `ClearWorkspaceAsync` и
cache-miss `LoadCoreAsync`.

`GetProjectGraphStaleHint()`:

**Только GraphFile** (сохранить текущий смысл):

```
> **Note:** A `.csproj` / `.sln` / `Directory.Build.props` changed on disk. Saved `.cs` files are synced; package refs and compile globs may be stale. Call `reset_workspace` then `load_workspace` (or `load_workspace` alone — a stale project graph skips the load cache).
```

**Только CompositionUnknown:**

```
> **Note:** A saved `.cs` file appeared or disappeared outside the loaded workspace snapshot. MSBuild decides membership on reload — the file is not guaranteed to enter the workspace. Call `reset_workspace` then `load_workspace` (or `load_workspace` alone — a stale project graph skips the load cache).
```

**Оба:** два абзаца подряд, не одна смешанная причина.

`WithDiskSyncNotes` по-прежнему дописывает hint к телу ответа.

Cache-hit `load_workspace`, **во время которого** flush только что выставил
stale, **не** reopen'ится в том же вызове. Агент видит hint и вызывает load
ещё раз (тогда cache пропускается). Overlay/publication не трогать.

### 4.4. Missing-on-disk

Пока путь в missing-on-disk set, `PersistDocumentChangesAsync` и любой путь
к `File.WriteAllText` через `UpdateDocumentInMemory*` **пропускают** этот путь
и пишут `LogWarning`. Документ остаётся в snapshot (устаревший текст) —
семантика до reload может находить удалённый тип; это контракт, не баг.

### 4.5. Хук для нового файла без FSW

`update_file_content` при `WorkspaceWriteStatus.Skipped` (`not-in-workspace` /
нет workspace) для пути `.cs`: вызвать
`NoteUnrepresentableSourcePath(fullPath)` **до** `SuppressDiskWatchForPath` +
`WriteAllText`. Хук: composition-stale, без `AddDocument`. Может вызываться
без `_workspaceLock` — делать thread-safe (`volatile` / concurrent set).

Иначе 1 с suppress глотает `Created`, Unrepresentable не появится.

### 4.6. Где показывать hint

| Тул | Когда |
|---|---|
| `NavigationTools` | уже через `WithDiskSyncNotes` — оставить |
| `get_test_list` | любой успешный/пустой ответ после flush |
| `run_specific_test` | ответ после построения фильтра / запуска (фильтр из snapshot) |
| `load_workspace` | успешный load, если hint не null |
| `update_file_content` | skip not-in-workspace для `.cs` |

Не размазывать на `run_dotnet_build`. Не менять `WorkspaceWriteBoundary`.

### 4.7. Lifecycle host

`HostResponse` + `Inspect`: `ProjectGraphStale` (`bool`),
`ProjectGraphStaleHint` (`string?` из `GetProjectGraphStaleHint`).
Не переиспользовать `LastRefreshStale` (overlay).

Для T-6: `internal` метод на `SolutionManager` вроде
`RequestRefreshAllDocumentsForTests()` + host op `forceRefreshAll`.

## 5. Тесты (норма)

На 1.3.29 красные тесты — инверсия существующих Facts, не соседние дубли.
**T-2 не писать** (`Preflight` / `document-set-diff`).

| ID | Норма | Уровень |
|---|---|---|
| T-1 | Инвертировать `ApplyAsync_adds_new_cs_under_project_directory`: `Added == 0`, путь в `Unrepresentable`, состав документов не вырос | unit |
| T-1b | Инвертировать `ApplyAsync_removes_document_when_file_deleted`: `Removed == 0`, документ жив, путь в `Unrepresentable` | unit |
| T-1c | Dirty = изменённый `A.cs` + новый `B.cs` → `Updated == 1`, Unrepresentable содержит только `B.cs`, текст A с диска, B не в solution | unit |
| T-3a | Lifecycle Consumer: новый `.cs` → `waitDirty` → `flushFind`/`flushGetter` → SHA `.csproj` как до; нет нового `<Compile Include>` для этого файла | integration |
| T-3b | Тот же fixture: `build` Consumer **до** reload, exit 0, нет `NETSDK1022` | integration |
| T-3c | Тип нового файла не находится до reopen; после `reset`+`load` или второго load при stale — находится (если SDK glob его включает) | integration |
| T-3d / T-5 | Hint composition в ответе host flush/inspect; **нет** фразы что изменился `.csproj`. Прогонять через flush host, не через голый `ApplyAsync` | integration |
| T-6 | `forceRefreshAll` → flush без нового файла → composition-stale | host |
| T-7 | Удалить известный `.cs` → flush → документ жив; `updateDocument` не создаёт файл на диске | integration |

Глобальный ассерт «ни в каком fixture нет `<Compile Include>`» не вводить.

## 6. Docs в том же ship

- Корневой `README.md` «Agent tools by version»: строка **1.3.30**.
- Блок **v1.1.0**: снять «New `.cs` under a project folder are `AddDocument`’d;
  deleted files are removed.» Норма: disk-sync = известные документы;
  новый/удалённый `.cs` → composition-stale + reload.
- `AGENTS.md.sample` File editing: автосинк — правки уже загруженных `.cs`;
  создание/удаление → reload перед symbol / test-discovery.
- `.cursor/rules/roslyn-mcp-overview.mdc` — history 1.3.30.
- `.cursor/rules/roslyn-mcp-pitfalls.mdc` — новый пункт: `TryApplyChanges` +
  `AddDocument` пишет backing `.csproj` → NETSDK1022; disk-sync этого не
  делает; новый файл требует reload; не чинить `EnableDefaultCompileItems` /
  `global.json`.
- `docs/ARCHITECTURE.md` Workspace lifecycle п.3: только тексты известных
  документов; состав — stale + reopen.

## 7. Порядок работ

1. Красные unit T-1 / T-1b / T-1c на текущем коде (доказательство C-6).
2. `WorkspaceDocumentDiskSync` + инверсия Facts.
3. Flush + причины hint + missing-on-disk + хук EditingTools + hint в тулах §4.6.
4. Lifecycle host поля + T-3a…d, T-5, T-6, T-7.
5. Docs §6 + bump 1.3.30.
6. `run_dotnet_test` по затронутым тестам; publish; reload MCP;
   `get_mcp_server_info` = **1.3.30**.

## 8. Закрытие дефектов

| ID | Этот патч |
|---|---|
| D-01 | Закрыт для disk-sync. Refactoring `AddDocument` — follow-up. |
| D-02 | Закрыт. |
| D-03 | Не закрыт (намеренно). |
| D-04 | Закрыт на поверхностях §4.6. |
| D-05 | Самонаведённый graph-file stale от мутации csproj пропадает. Composition-stale — честный. |
| D-06 | T-1, T-3a. |
| D-07 | Состав + I-5. Устаревший символ до reload — контракт. |
| D-08 | Pitfall / 1.3.30. |

## 9. Follow-up (не этот патч)

1. Намеренный `AddDocument` refactoring-тулов: persist `.cs` + stale + reload
   или writer без дубля SDK glob. Пока C нельзя включать.
2. Ручная правка уже испорченных `.csproj` в чужих репо.
3. Overlay-документ (вариант B) — отдельная спека.
