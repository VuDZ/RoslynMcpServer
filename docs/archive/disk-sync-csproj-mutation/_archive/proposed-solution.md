# Disk-sync мутирует `.csproj` — предлагаемое решение

Дата: 2026-09-20. Статус: **record, не норма**. Описание дефекта —
[../README.md](../README.md). Канон реализации — [../spec.md](../spec.md).
Этот файл фиксирует варианты A–E и исходные C-1…C-6. Process:
[review/](review/README.md), [review-astra/](review-astra/README.md).

## 1. Ограничения, которые нельзя нарушать

| # | Ограничение | Источник |
|---|---|---|
| C-1 | `MSBuildWorkspace.TryApplyChanges` не должен получать candidate с **изменившимся составом документов** проекта (add/remove), потому что Roslyn реализует это правкой backing `.csproj` | [../../analyzer-shadow-copy/epoch-2-first-fix-attempt-and-disk-corruption.md](../../analyzer-shadow-copy/epoch-2-first-fix-attempt-and-disk-corruption.md) |
| C-2 | Overlay/проекция никогда не публикуется через `TryApplyChanges`; правило уже действует для analyzer references | [../../analyzer-shadow-copy/epoch-3-inmemory-overlay-fix.md](../../analyzer-shadow-copy/epoch-3-inmemory-overlay-fix.md) |
| C-3 | Новый файл — изменение **project graph**. MSBuildWorkspace представляет его только после ре-эвалютации (`load_workspace`). Watcher не делает `OpenSolutionAsync` | `.cursor/rules/roslyn-mcp-pitfalls.mdc` §20 |
| C-4 | Не заставлять SDK-style проект отключать default items (`EnableDefaultCompileItems=false`) — это правка чужого репозитория | — |
| C-5 | Не терять файл из проекта там, где `<Compile>` задан **явно** (не SDK-style) | открытый вопрос 7.2 |
| C-6 | Изменение должно быть проверяемо тестом, который падает на текущем коде | — |

## 2. Варианты

| ID | Суть | Диск-мутация | Видимость нового файла без reload | Риск | Объём |
|---|---|---|---|---|---|
| **A** | Не добавлять неизвестный `.cs` в `Solution`; помечать project graph stale и возвращать note | нет | нет (нужен reload) | низкий | малый |
| **B** | Добавлять только в **published overlay**, `workspace.CurrentSolution` не трогать | нет | да | высокий | большой |
| **C** | Гейт в `WorkspaceWriteBoundary.Preflight` на diff состава документов (defense in depth) | нет | нет | низкий | малый |
| **D** | Watcher не ставит в dirty пути, которых нет среди документов (контракт «только правки известных документов») | нет | нет | низкий | малый |
| **E** | Снять дамп `.csproj` до и восстановить после `TryApplyChanges` | нет (откат) | нет | высокий | средний |

### Вариант A — «не добавлять, помечать stale, предупреждать» (выбранное направление)

Точечные изменения:

1. `WorkspaceDocumentDiskSync.ApplyAsync`: если для пути нет `DocumentId` —
   **не** вызывать `AddDocument`; вернуть путь в новом списке
   `Unrepresentable` (или `Added` остаётся 0, а путь уходит в отдельный
   результат). Симметрично для `removed` (D-07): не вызывать `RemoveDocument`
   как «изменение состава проекта».
2. `SolutionManager.FlushDirtyDocumentsUnderLockAsync`: при непустом
   `Unrepresentable` — `_projectGraphStale = true`, `LogWarning`
   с путями, и `TryApplyChanges` не выполняется (solution без изменений
   состава → write boundary либо no-op, либо обычный путь).
3. Отдавать агенту note через существующий `WithDiskSyncNotes`:

```1055:1059:Services/SolutionManager.cs
    public string? GetProjectGraphStaleHint()
    {
        if (!_projectGraphStale)
        {
            return null;
        }
```

текст дополнить случаем «новый файл на диске не входит в загруженный project
graph: `reset_workspace` + `load_workspace`».

Плюсы: минимальный дифф; никакой мутации диска; не появляется нового класса
расхождений `workspace.CurrentSolution` ↔ published overlay; согласуется с
C-3 и U-ARB-04-IMPL-1 («no implicit semantic workspace load»).

Минусы: новый сохранённый `.cs` не виден symbol-тулам до reload
(поведенческое изменение — нужно отразить в docs); для проектов с явным
`<Compile>` (C-5) новый файл тоже требует reload — формально корректно, но
надо проверить, что после reload он не потеряется.

### Вариант B — overlay-документ (не рекомендуется без отдельной спеки)

Держать добавленный документ только в published `_solution`, не в
`workspace.CurrentSolution`.

Плюсы: файл сразу виден семантике.

Минусы: сегодня overlay — это **только** analyzer references; расширение на
состав документов ломает инварианты, на которые опираются
`ReconcileSavedTextsUnderLockAsync`, `FindDocumentIdForPath`,
`GetPublishedSolution*` и контракт «raw workspace = база записи» из
`RoslynMcpServer.Tests/AnalyzerLifecycle/Epoch1SemanticInventoryTests.cs`.
Любая последующая запись документа вернёт workspace к состоянию без файла,
и overlay придётся переприменять. Это отдельный дизайн, не патч.

### Вариант C — гейт в write boundary (дополняет A)

Добавить в `WorkspaceWriteBoundary` проверку состава документов — по аналогии
с существующим `RevertAnalyzerReferenceOverlayForApply`:

```
project.DocumentIds (cleaned) != project.DocumentIds (workspaceCurrent)
    → Preflight(false, "document-set-diff", null)
```

Плюсы: закрывает **все** пути к `TryApplyChanges`, а не только FSW
(например, будущие AST-тулы, создающие документы); дешёвый unit-тест; шум в
лог вместо тихой мутации диска.

Минусы: сам по себе даёт «rejected write» вместо понятного «нужен reload» —
поэтому только вместе с A.

### Вариант D — сузить контракт watcher

Watcher перестаёт складывать в dirty пути, которых нет среди документов
загруженного решения. Тогда `WorkspaceDocumentDiskSync` в принципе не видит
add-кейс.

Плюсы: контракт «watcher синхронизирует правки известных документов»
(согласуется с C-3) становится явным; меньше работы в flush.

Минусы: теряется наблюдаемость доставки FSW (`waitDirty`) для новых файлов;
note всё равно нужен. С A пересекается — можно рассматривать как её
упрощённую редакцию.

### Вариант E — откат `.csproj` после `TryApplyChanges` (отклонён)

Снять байты/`LastWriteTime` `.csproj` проекта, применить, обнаружить
мутацию, восстановить. Отклоняется: гонка с реальными правками проекта,
скрывает корневую причину, оставляет `_projectGraphStale` шум, конфликтует с
легальным флоу `add_package_reference` (`Services/ProjectFileHelper.cs`).

## 3. Выбранное направление

**A**: disk-sync синхронизирует тексты известных документов, но не добавляет и
не удаляет документы в загруженном `Solution`; изменение состава требует
повторной загрузки workspace. Как чинить — [../spec.md](../spec.md).

Глобальный гейт **C** и фильтрация watcher **D** в этот патч **не входят**.
Кандидаты тестов ниже — исходные; нормативные T-* задаёт план (T-2 отвергнут).

## 4. Исходные кандидаты для тестов (требуют пересмотра по review)

| # | Тест | Уровень |
|---|---|---|
| T-1 | Новый `.cs` на диске под каталогом проекта → `WorkspaceDocumentDiskSync.ApplyAsync` не увеличивает состав документов; solution неизменен | unit |
| T-2 | Candidate с добавленным документом → `WorkspaceWriteBoundary.Preflight` → `false`, reason `document-set-diff`; candidate без diff → `Accepted` | unit |
| T-3 | Lifecycle host: создать новый `.cs` в fixture `Consumer` → flush → `snapshotCsproj` SHA не изменился, marker жив, файл компилируется только после `reset_workspace`+`load_workspace` | integration |
| T-4 | Обобщить `Epoch1HostOps.AssertProjectFilesUnchanged`: ассерт «в fixture нет `<Compile Include="`» для любых сценариев, включая add | integration |
| T-5 | `_projectGraphStale` взводится при `Unrepresentable`, и hint виден в ответе тула | unit |

## 5. Влияние на docs/версию (когда будет ship)

- patch bump по `.cursor/rules/roslyn-mcp-version-bump.mdc`;
- `README.md` → «Agent tools by version»;
- `.cursor/rules/roslyn-mcp-overview.mdc` → version history;
- `.cursor/rules/roslyn-mcp-pitfalls.mdc` → новый pitfall: «`TryApplyChanges`
  с `AddDocument` правит backing `.csproj` → NETSDK1022; новый файл требует
  `reset_workspace`+`load_workspace`»;
- `.cursor/rules/roslyn-mcp-tools.mdc` — если добавляется note в ответы тулов;
- `AGENTS.md.sample` — только если меняется session policy («создал новый
  файл → reload перед семантическими тулами»);
- `docs/ARCHITECTURE.md` — workspace lifecycle / write boundary.

## 6. Временная мера для оператора (до фикса)

- Инжектированную строку `<Compile Include="…" />` можно удалять вручную —
  это безопасно.
- В той же сессии MCP она может появиться снова при следующем создании файла
  и flush. Надёжнее: `reset_workspace` + `load_workspace` после создания
  новых файлов, либо создавать файлы вне загруженного workspace.
- Не считать это причиной для правки `global.json` / `EnableDefaultCompileItems`.

## 7. Оставшиеся решения до реализации

| # | Вопрос | Варианты |
|---|---|---|
| 7.1 | Видимость нового файла без reload | **Выбрано:** `нет` (A); для семантики нужен reload |
| 7.2 | Проекты с явным `<Compile>` | reload обязателен · отдельная ветка «файл уже в project graph, но Roslyn его не знает» |
| 7.3 | Где ставить гейт | **Выбрано для этого исправления:** только disk-sync (A); глобальный C — отдельное решение |
| 7.4 | Судьба add/remove ветки в `WorkspaceDocumentDiskSync` | оставить для тестов · удалить (D) |
| 7.5 | Формат note агенту | строка в `GetProjectGraphStaleHint` · отдельный structured block |
| 7.6 | `removed`/rename `.cs` (D-07) | симметрично A · отдельное поведение |
| 7.7 | Само-триггер reload в MCP-write-тулах | нет · только note · auto `reset`+`load` |

## 8. Явно вне рамок

- Исправление уже испорченных `.csproj` в чужих репозиториях (например
  `MobsAdmin.Web.Tests.csproj`) — разовая ручная правка, MCP их не сканирует.
- Изменение поведения `find_symbol_definition` для source-generated
  документов (см. epoch 3, non-goals).
- Переход на другой способ представления новых файлов в MSBuildWorkspace.
