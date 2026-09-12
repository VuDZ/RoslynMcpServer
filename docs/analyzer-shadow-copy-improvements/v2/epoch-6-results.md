# Эпоха 6 — результаты аудита

Дата: 2026-09-12. Статус: **аудит документации завершён**
([epoch-6-acceptance.md](epoch-6-acceptance.md)). S1–S4 приняты; серия не завершена.
Норматив: [epoch-6-contract-and-documentation.md](epoch-6-contract-and-documentation.md) E6-S1–S4.
Версия shipped-поведения: **v1.3.8**, commit
`f54aec15f48f942ef9ac077c752bf03ae018bf31`.
Окружение фактов эпох 1–4: Windows 10 (build 26200), .NET SDK 10.0.204,
Roslyn 5.9.0, x64 `C:\Program Files\dotnet`, `DOTNET_MULTILEVEL_LOOKUP=0`.

Runtime в этой эпохе не запускался. Команды повторения — из принятых
[epoch-1-results.md](epoch-1-results.md), [epoch-2-results.md](epoch-2-results.md),
[epoch-3-results.md](epoch-3-results.md), [epoch-4-results.md](epoch-4-results.md).
Эпоха 6 не реализует mapping, write workflow, matcher или loader.

## Статусы (различаются)

| Статус | Что означает здесь |
| --- | --- |
| Исследование завершено | Эпоха 1: красный baseline принят |
| Реализация принята | Эпохи 2 / 3 / 4: v1.3.6 / v1.3.7 / v1.3.8 |
| Аудит завершён | Эта эпоха: S1–S4 сверены; A6-08/11/12 закрыты. A6-13 не блокер |
| Серия завершена | **нет** — эпоха 5 принята (v1.3.12); U-ARB-04/05 открыты |

Эпоха 3 документируется как **выбранный restart-required / main-only**, не как
исследовательский failure и не как in-process V2. Эпоха 5 — **принята**
(v1.3.12), не deferred.

## E6-S1. DOC-EARLY

Три targets совпадают с кодом v1.3.8 (правка 2026-09-11, F-03 / A2-04):

| Target | Текущее утверждение | Код |
| --- | --- | --- |
| `docs/ARCHITECTURE.md` lifecycle | getter возвращает `_solution` / fallback; prepare на load/enable/refresh; edit/flush/post-apply — `PublishInMemorySolution` / mapping reapply | `GetCurrentSolution`, `ShadowCopyInSolutionAnalyzerReferencesAsync`, `PublishInMemorySolution` |
| `docs/analyzer-shadow-copy/README.md` | блок «Current shipped behavior (v1.3.8)»; timestamp + recopy-on-edit = история v1.3.5 | то же |
| remarks `ShadowCopyInSolutionAnalyzerReferencesAsync` | prepare на load/refresh; reapply без analyzer I/O; поколения не удаляются на clear | `SolutionManager` remarks |

Исторический changelog README v1.3.5 («re-derives on every read») оставлен как
запись того релиза и снабжён оговоркой, что getter файлы не копировал.

## E6-S2. Сверка LC-S1 с реализацией

Колонка «v1.3.5» в [LIFECYCLE-v2.md](LIFECYCLE-v2.md) — арбитражный baseline,
не текущий shipped. Ниже — факт v1.3.8. Матрица не свёрнута в «load/reload».

| Действие/ветка | Факт v1.3.8 | Проверка |
| --- | --- | --- |
| Первый load/cache miss, false или omitted | `LoadCoreAsync` открывает граф; overlay не включается (`WorkspaceTools` вызывает prepare только при `true`) | E1-S2 first load / broken-path |
| Первый load/cache miss, true | Отдельный `ShadowCopyInSolutionAnalyzerReferencesAsync`: immutable `v2-main-only`, mapping сессии, rewrite до lazy load | E1/E2/E3 load+oracle |
| Cached load, true | Граф тот же; prepare вызывается снова; same-identity V2 → `RestartRequired`, overlay снимается | E3 V1→V2 cached |
| Cached load, ранее true, затем false/omitted | Prepare не вызывается; активный overlay/mapping сохраняются (sticky, U-ARB-05) | E1 true→false / omitted |
| Cached load, false→true | Prepare без reopen графа; mapping этой сессии | E1/E2 false→true |
| Reset+load | `ClearWorkspaceAsync` снимает workspace/overlay/session; loader и CLR те же; opt-in только новым `true` | E3 reset+load |
| Process restart+load | Новый процесс и loader; exact V2 при opt-in | E3 process restart |
| Graph-stale reopen | `LoadCoreAsync` dispose + новые session/mapping; старые `ProjectId` не переносятся | E1 graph-stale |
| Другое решение A→B | Новая сессия B; mapping A не применяется; same identity → gate, не маркер A | E3 A→B |
| `GetCurrentSolution()` | `_solution ?? workspace.CurrentSolution`; без prepare, hash, flush, load | E1-S4 inventory |
| Text edit | Write boundary: persist + apply + `SetPublishedSolution` / mapping.Apply; **не** artifact refresh | E2/E4 write-path |
| Overlay-derived apply | Preflight → exact inverse → persist → recon → publish mapping | E4-S4 |
| Under-lock apply | `ApplyWorkspaceWriteUnderLockAsync` без повторного semaphore | E4 A4-01 |
| Доставка FSW | Dirty paths; `_solution` не обновляется до flush | E1-S3 |
| Production watcher flush | Тексты + mapping reapply; prepare/I/O не растут | E2/E4 FSW |
| Полный/частичный prepare failure | Per-ref result; file-fail держит stale mapping; restart-required снимает overlay | E2 failed refresh; E3 RestartRequired |
| Apply failure / partial / recon | `WorkspaceWriteStatus` + `SavedPaths` + `Reason`; не атомарное сохранение | E4-S2/S4 |
| Clear/dispose | Overlay/session сняты; поколения на диске остаются; loader/CLR живы | E2-S4; E3 reset ≠ unload |

`_solution = overlay` в `SetPublishedSolution` — публикация ссылки на immutable
снимок, не атомарность нескольких файлов и workspace.

## E6-S2. G/S/M/A/L/D/R (LC-S2)

Обозначения как в матрице. «Совпадает» = код и принятые тесты не противоречат
строке. Открытые неблокеры A2-09 / A4-09…A4-12 не меняют строку, но сужают
доказательство.

| Переход | Вердикт | Замечание аудита |
| --- | --- | --- |
| Первый load false/omitted | совпадает | G новая сессия; S raw; M/A нет |
| Первый load true, prepare успешна | совпадает | S overlay; M новый; A подготовленные; L lazy; D не flush |
| Cached true, успешная подготовка | совпадает | Refresh файлов ≠ execution; hash на обещанном refresh |
| Cached true, неподдержанное in-process обновление | совпадает | RestartRequired; overlay не публикуется как успех V2 |
| Cached true→false/omitted | совпадает | Sticky; не disable; U-ARB-05 |
| Cached false→true | совпадает | Prepare на той же сессии |
| Reset+load | совпадает | M не переносится; L прежний |
| Process restart+load | совпадает | Новый L; exact V2 отдельно |
| Graph-stale reopen | совпадает | Новая база; старый mapping отвергается |
| A→B load | совпадает | Mapping A не на B; identity gate |
| Getter | совпадает | Не flush; не load |
| Text edit, полный успех | совпадает | M совместимый; не refresh |
| Overlay apply, полный успех | совпадает | Inverse + publish mapping |
| Under-lock apply, полный успех | совпадает | Тот же workflow |
| FSW delivery | совпадает | D += dirty; S прежний |
| Flush, полный успех | совпадает | Freshness только завершённого flush (A4-11 не проверяет `WriteStatus`) |
| Prepare failure без старого mapping | совпадает | Нет полного refresh/execution success |
| Failed refresh со старым mapping | совпадает | File-fail: stale V1; restart-required: overlay снят |
| Preflight rejection | совпадает | Нет серверных записей операции |
| Partial persistence→recon success | совпадает | Partial, не FullSuccess |
| Reconciliation failure/отмена | совпадает | Candidate не published целиком; A4-10: cancel после записи → `ReconciliationSucceeded` + reason `cancelled` |
| Clear/dispose | совпадает | Файлы поколений остаются; CLR не unload |

Инвентаризация semantic readers: [epoch-1-semantic-entry-points.md](epoch-1-semantic-entry-points.md),
тест `Epoch1SemanticInventoryTests`. Целостность снимка, freshness после flush и
одна база операции разделены. `RenameSymbol` / `FindSymbolReferences` делают
flush, затем повторный getter — гарантии той же базы после symbol нет (E1-S4).

## E6-S3. Пользовательский контракт (зафиксировано в docs)

- Upgrade: v1.3.5 recopy-on-edit мог подхватить новые bytes; v1.3.6+ edit/flush
  только reapply. Обновление генератора: build → **restart MCP** →
  `load_workspace` `shadowCopyInSolutionAnalyzers=true`. Reset ≠ unload.
- Sticky `false`/omitted на cached load до U-ARB-05; отключение — reset, затем
  load без флага.
- CodeAction / rename: unknown/stale analyzer diff → `PreflightRejected` до
  записей; адаптер отдаёт Status, Reason, известные `SavedPaths`. Partial ≠
  полный успех запроса.
- Layout `v2-main-only`; `dependency-set` — другой namespace, не in-place
  миграция timestamp-каталогов. Clear не удаляет поколения.
- Path resolution и anti-lock независимы. Поиск generated declarations по имени
  — ограничение `SymbolFinder`, не отсутствие генерации.
- `mapping.Apply` — чистое преобразование готового mapping; copier/publisher — нет.
- Новые MCP параметры и номер релиза этой эпохой не назначаются.

## Ограничения, которые аудит не закрывает

- Эпоха 5 / U-ARB-01: capture + confirmed-only приняты в v1.3.12;
  inaccessible не выбран.
- U-ARB-04: load boundary не выбрана; измерение persistence эпохи 4 не есть
  anti-lock гарантия.
- U-ARB-05: desired-state флага не выбран.
- A2-09, A4-09…A4-12 открыты, не блокеры принятых эпох.
- Историческая порча `.csproj` (v1.3.4) автоматически не чистится.
- A6-13: `docs/analyzer-shadow-copy/README.md` § v1.3.5 history всё ещё
  present-tense `RevertAnalyzerReferenceOverlayForApply`.
